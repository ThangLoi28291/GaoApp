using System.Text.Json;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Interceptors.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GaoApp.Infrastructure.Interceptors;

/// <summary>
/// Interceptor ghi audit trail nghiệp vụ vào bảng AuditLogs.
/// Lưu ý:
/// - KHÔNG set CreatedAtUtc / UpdatedAtUtc / IsDeleted ở đây
/// - vì AppDbContext đã xử lý phần audit fields + soft delete + tenant rules
/// - interceptor này chỉ chuyên snapshot thay đổi và ghi AuditLog
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IAuditExecutionContextAccessor _auditExecutionContextAccessor;
    private readonly IDbContextFactory<AuditLogDbContext> _auditLogDbContextFactory;
    private readonly ILogger<AuditSaveChangesInterceptor> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    /// <summary>
    /// Các field hệ thống không cần đưa vào changed columns.
    /// </summary>
    private readonly HashSet<string> _ignoreProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "CreatedAtUtc",
        "UpdatedAtUtc",
        "CreatedBy",
        "UpdatedBy",
        "RowVersion"
    };

    /// <summary>
    /// Pending audits theo từng DbContext instance.
    /// Key = DbContext hiện tại
    /// Value = danh sách audit snapshot đã chụp trước save
    /// </summary>
    private readonly Dictionary<DbContext, List<PendingAuditLogItem>> _pendingAudits = new();

    public AuditSaveChangesInterceptor(
        IAuditExecutionContextAccessor auditExecutionContextAccessor,
        IDbContextFactory<AuditLogDbContext> auditLogDbContextFactory,
        ILogger<AuditSaveChangesInterceptor> logger)
    {
        _auditExecutionContextAccessor = auditExecutionContextAccessor;
        _auditLogDbContextFactory = auditLogDbContextFactory;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        CapturePendingAudits(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CapturePendingAudits(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        FlushPendingAudits(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await FlushPendingAuditsAsync(eventData.Context, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ClearPendingAudits(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ClearPendingAudits(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    /// <summary>
    /// Chụp snapshot thay đổi trước khi EF save xuống DB.
    /// Đây là bước quan trọng nhất để không bị mất state.
    /// </summary>
    private void CapturePendingAudits(DbContext? context)
    {
        if (context == null)
            return;

        // Nếu context này đang ở phase flush audit thì không capture lại
        if (_pendingAudits.ContainsKey(context))
            return;

        var executionCtx = _auditExecutionContextAccessor.GetCurrent();

        // Không có execution context => bỏ qua audit.
        // Tránh lỗi ở startup / migrate / seed / background flow không có HTTP request.
        if (executionCtx == null)
            return;

        var pendingItems = new List<PendingAuditLogItem>();

        var entries = context.ChangeTracker.Entries()
            .Where(e =>
                e.Entity is IAuditTrackedEntity &&
                e.Entity is not AuditLog &&
                (e.State == EntityState.Added ||
                 e.State == EntityState.Modified ||
                 e.State == EntityState.Deleted))
            .ToList();

        foreach (var entry in entries)
        {
            var pending = BuildPendingAudit(entry, executionCtx);
            if (pending != null)
            {
                pendingItems.Add(pending);
            }
        }

        // Chống duplicate trong cùng một lần flush
        // Trường hợp hiếm EF / flow nghiệp vụ tạo nhiều audit item giống hệt nhau.
        if (pendingItems.Count > 1)
        {
            pendingItems = pendingItems
                .GroupBy(x => new
                {
                    x.StoreId,
                    x.ActorUserId,
                    x.Module,
                    x.ActionType,
                    x.EntityName,
                    x.EntityId,
                    x.Summary,
                    x.OldValuesJson,
                    x.NewValuesJson,
                    x.ChangedColumnsJson,
                    x.TraceId,
                    x.Path
                })
                .Select(g => g.First())
                .ToList();
        }

        if (pendingItems.Count > 0)
        {
            _pendingAudits[context] = pendingItems;
        }
    }

    /// <summary>
    /// Sau khi SaveChanges thành công, ghi pending audits vào bảng AuditLogs.
    /// Dùng AuditLogDbContext riêng để tránh recursion / circular dependency.
    /// </summary>
    private void FlushPendingAudits(DbContext? context)
    {
        if (context == null)
            return;

        if (!_pendingAudits.TryGetValue(context, out var pendingItems) || pendingItems.Count == 0)
        {
            _pendingAudits.Remove(context);
            return;
        }

        try
        {
            var logs = pendingItems.Select(ToAuditLog).ToList();

            using var auditDbContext = _auditLogDbContextFactory.CreateDbContext();

            auditDbContext.AuditLogs.AddRange(logs);
            auditDbContext.SaveChanges();
        }
        catch (Exception ex)
        {
            // Primary SaveChanges has already succeeded. Audit persistence is
            // best-effort so it must not turn a committed write into a failure.
            _logger.LogError(
                "Audit persistence failed after primary SaveChanges; primary data remains committed. AuditCount={AuditCount}; TraceId={TraceId}; ExceptionType={ExceptionType}",
                pendingItems.Count,
                pendingItems[0].TraceId,
                ex.GetType().Name);
        }
        finally
        {
            _pendingAudits.Remove(context);
        }
    }

    private async Task FlushPendingAuditsAsync(
        DbContext? context,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
            return;

        if (!_pendingAudits.TryGetValue(context, out var pendingItems) || pendingItems.Count == 0)
        {
            _pendingAudits.Remove(context);
            return;
        }

        try
        {
            var logs = pendingItems.Select(ToAuditLog).ToList();

            await using var auditDbContext =
                await _auditLogDbContextFactory.CreateDbContextAsync(cancellationToken);

            auditDbContext.AuditLogs.AddRange(logs);
            await auditDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Primary SaveChanges has already succeeded. Audit persistence is
            // best-effort so it must not turn a committed write into a failure.
            _logger.LogError(
                "Audit persistence failed after primary SaveChanges; primary data remains committed. AuditCount={AuditCount}; TraceId={TraceId}; ExceptionType={ExceptionType}",
                pendingItems.Count,
                pendingItems[0].TraceId,
                ex.GetType().Name);
        }
        finally
        {
            _pendingAudits.Remove(context);
        }
    }

    /// <summary>
    /// Nếu SaveChanges fail thì phải xóa pending audits để tránh log rác.
    /// </summary>
    private void ClearPendingAudits(DbContext? context)
    {
        if (context == null)
            return;

        _pendingAudits.Remove(context);
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
                return null;

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

        // Không có StoreId => bỏ qua audit thay vì làm chết app.
        // Rất hữu ích cho startup / seed / system flow.
        if (!storeId.HasValue)
        {
            return null;
        }

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
        return new AuditLog
        {
            StoreId = x.StoreId,
            ActorUserId = x.ActorUserId,
            ActorUserName = x.ActorUserName,

            Module = x.Module,
            ActionType = x.ActionType,

            EntityName = x.EntityName,
            EntityId = x.EntityId ?? GetPrimaryKeyValue(x.Entry!),
            EntityDisplay = x.EntityDisplay,
            Summary = x.Summary,

            OldValuesJson = x.OldValuesJson,
            NewValuesJson = x.NewValuesJson,
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
