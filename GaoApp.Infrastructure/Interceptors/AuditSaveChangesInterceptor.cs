using System.Text.Json;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Interceptors.Auditing;
using GaoApp.Infrastructure.Services.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Interceptors;

/// <summary>Persist audit evidence on the primary connection before its transaction can commit.</summary>
public sealed class AuditSaveChangesInterceptor(IAuditExecutionContextAccessor auditExecutionContextAccessor) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly HashSet<string> _ignoreProperties = new(StringComparer.OrdinalIgnoreCase)
    { "CreatedAtUtc", "UpdatedAtUtc", "CreatedBy", "UpdatedBy", "RowVersion" };
    private readonly Dictionary<DbContext, SaveState> _pendingAudits = new();

    private sealed class SaveState(List<PendingAuditLogItem> items, IDbContextTransaction transaction, bool ownsTransaction)
    {
        public List<PendingAuditLogItem> Items { get; } = items;
        public IDbContextTransaction Transaction { get; } = transaction;
        public bool OwnsTransaction { get; } = ownsTransaction;
        public string? Savepoint { get; set; }
        public bool PrimarySaved { get; set; }
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        var context = eventData.Context;
        var items = CapturePendingAudits(context);
        if (context is null || items.Count == 0) return result;
        if (!context.Database.IsRelational())
        {
            context.Set<AuditOutboxMessage>().AddRange(items.Select(x => AuditOutboxEvidence.Create(ToAuditLog(x))));
            return result;
        }
        var existing = context.Database.CurrentTransaction;
        var state = new SaveState(items, existing ?? context.Database.BeginTransaction(), existing is null);
        _pendingAudits.Add(context, state);
        if (!state.OwnsTransaction && state.Transaction.SupportsSavepoints)
        {
            state.Savepoint = Guid.NewGuid().ToString("N");
            try { state.Transaction.CreateSavepoint(state.Savepoint); }
            catch { Release(context); throw; }
        }
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        var items = CapturePendingAudits(context);
        if (context is null || items.Count == 0) return result;
        if (!context.Database.IsRelational())
        {
            context.Set<AuditOutboxMessage>().AddRange(items.Select(x => AuditOutboxEvidence.Create(ToAuditLog(x))));
            return result;
        }
        var existing = context.Database.CurrentTransaction;
        var state = new SaveState(items, existing ?? await context.Database.BeginTransactionAsync(cancellationToken), existing is null);
        _pendingAudits.Add(context, state);
        if (!state.OwnsTransaction && state.Transaction.SupportsSavepoints)
        {
            state.Savepoint = Guid.NewGuid().ToString("N");
            try { await state.Transaction.CreateSavepointAsync(state.Savepoint, cancellationToken); }
            catch { await ReleaseAsync(context); throw; }
        }
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        var context = eventData.Context;
        if (context is null || !_pendingAudits.TryGetValue(context, out var state)) return result;
        state.PrimarySaved = true;
        try
        {
            using var audit = CreateEnlistedContext(context);
            audit.Database.UseTransaction(state.Transaction.GetDbTransaction());
            audit.AuditOutbox.AddRange(state.Items.Select(x => AuditOutboxEvidence.Create(ToAuditLog(x))));
            audit.SaveChanges();
            if (state.OwnsTransaction) state.Transaction.Commit();
            else if (state.Savepoint is not null) state.Transaction.ReleaseSavepoint(state.Savepoint);
        }
        catch
        {
            Abort(context);
            throw new InvalidOperationException("Không lưu được bằng chứng kiểm toán. Giao dịch chưa hoàn tất.");
        }
        finally { Release(context); }
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null || !_pendingAudits.TryGetValue(context, out var state)) return result;
        state.PrimarySaved = true;
        try
        {
            await using var audit = CreateEnlistedContext(context);
            await audit.Database.UseTransactionAsync(state.Transaction.GetDbTransaction(), cancellationToken);
            audit.AuditOutbox.AddRange(state.Items.Select(x => AuditOutboxEvidence.Create(ToAuditLog(x))));
            await audit.SaveChangesAsync(cancellationToken);
            if (state.OwnsTransaction) await state.Transaction.CommitAsync(cancellationToken);
            else if (state.Savepoint is not null) await state.Transaction.ReleaseSavepointAsync(state.Savepoint, cancellationToken);
        }
        catch
        {
            await AbortAsync(context);
            throw new InvalidOperationException("Không lưu được bằng chứng kiểm toán. Giao dịch chưa hoàn tất.");
        }
        finally { await ReleaseAsync(context); }
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Abort(eventData.Context);
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        => AbortAsync(eventData.Context);
    public override void SaveChangesCanceled(DbContextEventData eventData) => Abort(eventData.Context);
    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
        => AbortAsync(eventData.Context);

    private static AuditLogDbContext CreateEnlistedContext(DbContext context)
    {
        if (!context.Database.IsSqlServer()) throw new NotSupportedException("Durable audit requires SQL Server.");
        return new AuditLogDbContext(new DbContextOptionsBuilder<AuditLogDbContext>()
            .UseSqlServer(context.Database.GetDbConnection()).Options);
    }

    private void Abort(DbContext? context)
    {
        if (context is null || !_pendingAudits.TryGetValue(context, out var state)) return;
        try
        {
            if (!state.PrimarySaved && state.Savepoint is not null)
            {
                try { state.Transaction.RollbackToSavepoint(state.Savepoint); }
                catch { state.Transaction.Rollback(); } // SQL THROW/XACT_ABORT can make a savepoint unusable.
            }
            else state.Transaction.Rollback(); // Missing audit evidence aborts the entire business transaction.
        }
        finally
        {
            if (state.PrimarySaved) context.ChangeTracker.Clear();
            Release(context);
        }
    }

    private async Task AbortAsync(DbContext? context)
    {
        if (context is null || !_pendingAudits.TryGetValue(context, out var state)) return;
        try
        {
            if (!state.PrimarySaved && state.Savepoint is not null)
            {
                try { await state.Transaction.RollbackToSavepointAsync(state.Savepoint, CancellationToken.None); }
                catch { await state.Transaction.RollbackAsync(CancellationToken.None); }
            }
            else await state.Transaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            if (state.PrimarySaved) context.ChangeTracker.Clear();
            await ReleaseAsync(context);
        }
    }

    private void Release(DbContext context)
    {
        if (_pendingAudits.Remove(context, out var state) && state.OwnsTransaction) state.Transaction.Dispose();
    }
    private async Task ReleaseAsync(DbContext context)
    {
        if (_pendingAudits.Remove(context, out var state) && state.OwnsTransaction) await state.Transaction.DisposeAsync();
    }

    private List<PendingAuditLogItem> CapturePendingAudits(DbContext? context)
    {
        if (context is null) return [];
        if (_pendingAudits.ContainsKey(context)) throw new InvalidOperationException("Reentrant audited SaveChanges is not supported.");
        context.ChangeTracker.DetectChanges();
        var executionCtx = auditExecutionContextAccessor.GetCurrent()
            ?? throw new InvalidOperationException("An audit execution context is required.");
        return context.ChangeTracker.Entries().Where(e => e.Entity is IAuditTrackedEntity && e.Entity is not AuditLog &&
                e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => BuildPendingAudit(e, executionCtx)).OfType<PendingAuditLogItem>().ToList();
    }
    private PendingAuditLogItem? BuildPendingAudit(
        EntityEntry entry,
        AuditExecutionContextDto executionCtx)
    {
        var entityType = entry.Metadata.ClrType.Name;
        string? entityId = null;

        // Nếu là CREATE → chưa lấy ID
        if (entry.State != EntityState.Added)
        {
            entityId = GetPrimaryKeyValue(entry);
        }

        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();
        var changedColumns = new List<string>();

        AuditActionType actionType;
        string summary;

        if (entry.State == EntityState.Added)
        {
            actionType = AuditActionType.Create;
            summary = $"Tạo mới {entityType}";

            foreach (var prop in entry.Properties)
            {
                if (ShouldIgnoreProperty(prop))
                    continue;

                newValues[prop.Metadata.Name] = prop.CurrentValue;
                changedColumns.Add(prop.Metadata.Name);
            }
        }
        else if (entry.State == EntityState.Deleted)
        {
            actionType = AuditActionType.Delete;
            summary = $"Xóa {entityType} #{entityId}";

            foreach (var prop in entry.Properties)
            {
                if (ShouldIgnoreProperty(prop))
                    continue;

                oldValues[prop.Metadata.Name] = prop.OriginalValue;
                changedColumns.Add(prop.Metadata.Name);
            }
        }
        else
        {
            DetectModifiedValues(entry, oldValues, newValues, changedColumns);

            if (changedColumns.Count == 0)
            {
                // Record credential-only changes without storing credential names or values.
                if (!entry.Properties.Any(p => p.IsModified && AuditSensitiveData.IsSensitive(p.Metadata.Name) &&
                    !Equals(p.OriginalValue, p.CurrentValue))) return null;
                changedColumns.Add("CredentialsChanged");
            }

            if (IsRestore(oldValues, newValues))
            {
                actionType = AuditActionType.Restore;
                summary = $"Khôi phục {entityType} #{entityId}";
            }
            else if (IsSoftDelete(oldValues, newValues))
            {
                actionType = AuditActionType.Delete;
                summary = $"Soft delete {entityType} #{entityId}";
            }
            else
            {
                actionType = AuditActionType.Update;
                summary = $"Cập nhật {entityType} #{entityId}";
            }
        }

        var storeId = TryResolveStoreId(entry, executionCtx.StoreId);

        if (storeId is not > 0)
            throw new InvalidOperationException("Không xác định được cửa hàng để lưu bằng chứng kiểm toán.");

        return new PendingAuditLogItem
        {
            StoreId = storeId.Value,
            ActorUserId = executionCtx.UserId,
            ActorUserName = executionCtx.UserName,
            Entry = entry,
            Module = ResolveModule(entityType),
            ActionType = actionType,

            EntityName = entityType,
            EntityId = entityId,
            EntityDisplay = $"{entityType} #{entityId}",
            Summary = summary,

            OldValuesJson = oldValues.Count == 0 ? null : JsonSerializer.Serialize(oldValues, JsonOptions),
            NewValuesJson = newValues.Count == 0 ? null : JsonSerializer.Serialize(newValues, JsonOptions),
            ChangedColumnsJson = changedColumns.Count == 0 ? null : JsonSerializer.Serialize(changedColumns, JsonOptions),

            TraceId = executionCtx.TraceId,
            IpAddress = executionCtx.IpAddress,
            UserAgent = executionCtx.UserAgent,
            Path = executionCtx.Path,

            IsSuccess = true,
            ErrorMessage = null,

            // LUÔN lưu UTC trong DB
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private AuditLog ToAuditLog(PendingAuditLogItem x)
    {
        var entityId = x.EntityId ?? GetPrimaryKeyValue(x.Entry!);
        var newValuesJson = x.NewValuesJson;
        if (x.ActionType == AuditActionType.Create && x.Entry is not null)
            newValuesJson = JsonSerializer.Serialize(x.Entry.Properties.Where(p => !ShouldIgnoreProperty(p))
                .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue), JsonOptions);
        return new AuditLog
        {
            StoreId = x.StoreId,
            ActorUserId = x.ActorUserId,
            ActorUserName = x.ActorUserName,

            Module = x.Module,
            ActionType = x.ActionType,

            EntityName = x.EntityName,
            EntityId = entityId,
            EntityDisplay = $"{x.EntityName} #{entityId}",
            Summary = x.Summary,

            OldValuesJson = x.OldValuesJson,
            NewValuesJson = newValuesJson,
            ChangedColumnsJson = x.ChangedColumnsJson,

            TraceId = x.TraceId,
            IpAddress = x.IpAddress,
            UserAgent = x.UserAgent,
            Path = x.Path,

            IsSuccess = x.IsSuccess,
            ErrorMessage = x.ErrorMessage,

            // DB vẫn lưu UTC
            CreatedAtUtc = x.CreatedAtUtc
        };
    }


    private void DetectModifiedValues(
        EntityEntry entry,
        Dictionary<string, object?> oldValues,
        Dictionary<string, object?> newValues,
        List<string> changedColumns)
    {
        foreach (var prop in entry.Properties)
        {
            if (ShouldIgnoreProperty(prop))
                continue;

            if (prop.Metadata.IsPrimaryKey())
                continue;

            if (!prop.IsModified)
                continue;

            var original = prop.OriginalValue;
            var current = prop.CurrentValue;

            if (Equals(original, current))
                continue;

            oldValues[prop.Metadata.Name] = original;
            newValues[prop.Metadata.Name] = current;
            changedColumns.Add(prop.Metadata.Name);
        }
    }

    private bool ShouldIgnoreProperty(PropertyEntry prop)
    {
        if (AuditSensitiveData.IsSensitive(prop.Metadata.Name))
            return true;

        if (_ignoreProperties.Contains(prop.Metadata.Name))
            return true;

        return false;
    }

    private string? GetPrimaryKeyValue(EntityEntry entry)
    {
        var pk = entry.Metadata.FindPrimaryKey();
        if (pk == null)
            return null;

        var firstKey = pk.Properties.FirstOrDefault();
        if (firstKey == null)
            return null;

        var prop = entry.Property(firstKey.Name);
        var value = prop.CurrentValue ?? prop.OriginalValue;
        return value?.ToString();
    }

    private int? TryResolveStoreId(EntityEntry entry, int? fallbackStoreId)
    {
        var storeProp = entry.Properties.FirstOrDefault(x => x.Metadata.Name == "StoreId");

        if (storeProp?.CurrentValue is int currentStoreId)
            return currentStoreId;

        if (storeProp?.OriginalValue is int originalStoreId)
            return originalStoreId;

        return fallbackStoreId;
    }

    private bool IsSoftDelete(
        Dictionary<string, object?> oldValues,
        Dictionary<string, object?> newValues)
    {
        return oldValues.TryGetValue("IsDeleted", out var oldVal) &&
               newValues.TryGetValue("IsDeleted", out var newVal) &&
               oldVal is bool oldBool &&
               newVal is bool newBool &&
               oldBool == false &&
               newBool == true;
    }

    private bool IsRestore(
        Dictionary<string, object?> oldValues,
        Dictionary<string, object?> newValues)
    {
        return oldValues.TryGetValue("IsDeleted", out var oldVal) &&
               newValues.TryGetValue("IsDeleted", out var newVal) &&
               oldVal is bool oldBool &&
               newVal is bool newBool &&
               oldBool == true &&
               newBool == false;
    }

    private AuditModuleType ResolveModule(string entityType)
    {
        return entityType switch
        {
            "Order" or "OrderLine" => AuditModuleType.Orders,

            "StockDocument" or
            "StockDocumentLine" or
            "InventoryBalance" or
            "InventoryReservation" or
            "InventoryTransaction" or
            "InventoryValuationEntry" or
            "InventoryCostLayer" or
            "InventoryCostLayerAllocation" or
            "StockCountDocument" or
            "StockCountLine" or
            "StockTransferDocument" or
            "StockTransferLine" or
            "PurchaseOrder" or
            "PurchaseOrderLine" or
            "PurchaseOrderAction" or
            "PurchaseRequest" or
            "PurchaseRequestLine" or
            "PurchaseRequestAction"
                => AuditModuleType.Inventory,

            "Product" or
            "ProductVariant" or
            "Category" or
            "Brand" or
            "Supplier" or
            "ProductAttribute" or
            "AttributeValue" or
            "Unit" or
            "Tax"
                => AuditModuleType.Catalog,

            "POSShift" or "POSTerminal" or "OrderPayment"
                => AuditModuleType.POS,

            "User" or "Role" or "UserInStore" or "Permission" or "RolePermission"
                => AuditModuleType.UserManagement,

            _ => AuditModuleType.System
        };
    }
}
