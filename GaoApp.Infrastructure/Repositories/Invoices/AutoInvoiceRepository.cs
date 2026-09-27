using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public sealed class AutoInvoiceRepository : IAutoInvoiceRepository
{
    private readonly AppDbContext _db;

    public AutoInvoiceRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<AutoInvoiceSettings?> GetSettingsAsync(
        int storeId,
        CancellationToken ct = default)
    {
        return _db.AutoInvoiceSettings
            .FirstOrDefaultAsync(x => x.StoreId == storeId && !x.IsDeleted, ct);
    }

    public Task AddSettingsAsync(
        AutoInvoiceSettings settings,
        CancellationToken ct = default)
        => _db.AutoInvoiceSettings.AddAsync(settings, ct).AsTask();

    public Task<List<int>> GetActiveStoreIdsAsync(CancellationToken ct = default)
    {
        return _db.Stores
            .AsNoTracking()
            .Where(x => x.IsActive && !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(ct);
    }

    public Task<AutoInvoiceWorkerState?> GetWorkerStateAsync(
        int storeId,
        string workerName,
        CancellationToken ct = default)
    {
        return _db.AutoInvoiceWorkerStates
            .FirstOrDefaultAsync(
                x => x.StoreId == storeId &&
                     x.WorkerName == workerName,
                ct);
    }

    public Task<List<InvoiceHead>> GetCandidateInvoicesAsync(
        int storeId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        var issuedStatuses = new[]
        {
            InvoiceProviderStatus.Issued,
            InvoiceProviderStatus.PdfDownloaded,
            InvoiceProviderStatus.ZipDownloaded,
            InvoiceProviderStatus.EmailSent
        };

        // First restrict by the sale window and collect only keys.  Loading all
        // navigations in the same SQL statement made the dashboard/worker query
        // unnecessarily wide and could time out on a store with a large history.
        // The second query is deliberately key-bounded and split into separate
        // commands for reference/collection navigations.
        return GetCandidateInvoicesCoreAsync(storeId, fromUtc, toUtc, issuedStatuses, ct);
    }

    public Task<int> ClearRecoverableCredentialErrorsAsync(
        int storeId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        var issuedStatuses = new[]
        {
            InvoiceProviderStatus.Issued,
            InvoiceProviderStatus.PdfDownloaded,
            InvoiceProviderStatus.ZipDownloaded,
            InvoiceProviderStatus.EmailSent
        };

        return _db.InvoiceHeads
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                !x.LegacyReadOnly &&
                !x.IsAutoInvoiceGroup &&
                x.OriginalInvoiceHeadId == null &&
                x.CorrectionType == null &&
                !issuedStatuses.Contains(x.ProviderStatus) &&
                x.LastErrorCode == "InvoiceProvider.CredentialKeyUnavailable" &&
                ((x.Order != null &&
                  x.Order.CompletedAtUtc.HasValue &&
                  x.Order.CompletedAtUtc.Value >= fromUtc &&
                  x.Order.CompletedAtUtc.Value < toUtc) ||
                 (x.InvoiceDate >= fromUtc && x.InvoiceDate < toUtc)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.LastErrorCode, (string?)null)
                    .SetProperty(x => x.LastErrorMessage, (string?)null),
                ct);
    }

    public async Task MarkInvoiceErrorsAsync(
        int storeId,
        IReadOnlyCollection<int> invoiceHeadIds,
        string errorCode,
        string? errorMessage,
        CancellationToken ct = default)
    {
        var ids = invoiceHeadIds
            .Where(x => x > 0)
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
            return;

        await _db.InvoiceHeads
            .Where(x => x.StoreId == storeId && ids.Contains(x.Id) && !x.IsDeleted)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.LastErrorCode, errorCode)
                    .SetProperty(x => x.LastErrorMessage, errorMessage)
                    .SetProperty(x => x.LastSyncedAtUtc, DateTime.UtcNow),
                ct);
    }
    public async Task ClearInvoiceErrorsAsync(
    int storeId,
    IReadOnlyCollection<int> invoiceHeadIds,
    CancellationToken ct = default)
    {
        var ids = invoiceHeadIds
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
            return;

        await _db.InvoiceHeads
            .Where(x =>
                x.StoreId == storeId &&
                ids.Contains(x.Id) &&
                !x.IsDeleted)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        x => x.LastErrorCode,
                        (string?)null)
                    .SetProperty(
                        x => x.LastErrorMessage,
                        (string?)null)
                    .SetProperty(
                        x => x.LastSyncedAtUtc,
                        DateTime.UtcNow),
                ct);
    }
    public async Task<int> RepairMissingUnitNamesAsync(
    int storeId,
    int invoiceHeadId,
    CancellationToken ct = default)
    {
        var details = await _db.InvoiceDetails
            .AsSplitQuery()
            .Include(x => x.OrderLine)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x!.Product)
                    .ThenInclude(x => x.BaseUnit)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x!.UnitConversions)
                    .ThenInclude(x => x.Unit)
            .Where(x =>
                x.StoreId == storeId &&
                x.InvoiceHeadId == invoiceHeadId &&
                !x.IsDeleted)
            .ToListAsync(ct);

        var changed = 0;

        foreach (var detail in details)
        {
            if (!string.IsNullOrWhiteSpace(
                    detail.UnitName))
            {
                continue;
            }

            string? unitName = null;

            // Ưu tiên snapshot đúng thời điểm bán.
            if (!string.IsNullOrWhiteSpace(
                    detail.OrderLine?.UnitName))
            {
                unitName =
                    detail.OrderLine.UnitName;
            }
            else if (!string.IsNullOrWhiteSpace(
                         detail.OrderLine?.SellingUnitName))
            {
                unitName =
                    detail.OrderLine.SellingUnitName;
            }

            // Nếu snapshot cũ thiếu, thử conversion hiện tại.
            if (string.IsNullOrWhiteSpace(unitName) &&
                detail.ProductVariant != null &&
                detail.OrderLine?.ProductUnitConversionId
                    is int conversionId)
            {
                unitName =
                    detail.ProductVariant.UnitConversions
                        .Where(x =>
                            !x.IsDeleted &&
                            x.Id == conversionId)
                        .Select(x => x.Unit?.Name)
                        .FirstOrDefault(x =>
                            !string.IsNullOrWhiteSpace(x));
            }

            if (string.IsNullOrWhiteSpace(unitName) &&
                detail.ProductVariant != null &&
                detail.OrderLine?.SellingUnitId
                    is int sellingUnitId)
            {
                unitName =
                    detail.ProductVariant.UnitConversions
                        .Where(x =>
                            !x.IsDeleted &&
                            x.UnitId == sellingUnitId)
                        .Select(x => x.Unit?.Name)
                        .FirstOrDefault(x =>
                            !string.IsNullOrWhiteSpace(x));
            }

            if (string.IsNullOrWhiteSpace(unitName) &&
                !string.IsNullOrWhiteSpace(
                    detail.OrderLine?.BaseUnitName))
            {
                unitName =
                    detail.OrderLine.BaseUnitName;
            }

            // Cuối cùng fallback đơn vị gốc hiện tại của Product.
            if (string.IsNullOrWhiteSpace(unitName))
            {
                unitName =
                    detail.ProductVariant?
                        .Product?
                        .BaseUnit?
                        .Name;
            }

            if (string.IsNullOrWhiteSpace(unitName))
                continue;

            var normalized =
                unitName.Trim();

            if (normalized.Length > 100)
            {
                normalized =
                    normalized[..100];
            }

            detail.UnitName = normalized;
            changed++;
        }

        return changed;
    }

    private async Task<List<InvoiceHead>> GetCandidateInvoicesCoreAsync(
        int storeId,
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyCollection<InvoiceProviderStatus> issuedStatuses,
        CancellationToken ct)
    {
        var succeededSourceIds =
    await _db.AutoInvoiceOperationSources
        .AsNoTracking()
        .Where(x =>
            x.StoreId == storeId &&
            !x.IsDeleted &&
            x.Status ==
                AutoInvoiceSourceStatus.Succeeded)
        .Select(x => x.InvoiceHeadId)
        .Distinct()
        .ToListAsync(ct);
        var orderBackedIds = await _db.InvoiceHeads
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                !x.LegacyReadOnly &&
                !x.IsAutoInvoiceGroup &&
                x.OriginalInvoiceHeadId == null &&
                x.CorrectionType == null &&
                x.Order != null &&
x.Order.InvoiceIssuanceRoute ==
    InvoiceIssuanceRoute.Automatic &&
    !succeededSourceIds.Contains(x.Id) &&
                !issuedStatuses.Contains(x.ProviderStatus) &&
                ((x.Order.CompletedAtUtc.HasValue &&
                  x.Order.CompletedAtUtc.Value >= fromUtc &&
                  x.Order.CompletedAtUtc.Value < toUtc) ||
                 (x.InvoiceDate >= fromUtc && x.InvoiceDate < toUtc)))
            .Select(x => x.Id)
            .ToListAsync(ct);

        var legacyIds = await _db.InvoiceHeads
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                !x.LegacyReadOnly &&
                !x.IsAutoInvoiceGroup &&
                x.OriginalInvoiceHeadId == null &&
                x.CorrectionType == null &&
                !issuedStatuses.Contains(x.ProviderStatus) &&
                x.Order == null &&
                x.InvoiceDate >= fromUtc &&
                x.InvoiceDate < toUtc)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var ids = orderBackedIds
    .Distinct()
    .ToArray();
        if (ids.Length == 0)
            return [];

        return await _db.InvoiceHeads
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(x => x.Store)
            .Include(x => x.Order)
                .ThenInclude(x => x!.Payments)
            .Include(x => x.LegalEntity)
            .Include(x => x.InvoiceProviderSetting)
            .Include(x => x.Details)
                .ThenInclude(x => x.OrderLegalEntityAllocation)
            .Where(x => ids.Contains(x.Id))
            .OrderBy(x => x.Order != null && x.Order.CompletedAtUtc.HasValue
                ? x.Order.CompletedAtUtc.Value
                : x.InvoiceDate)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    public Task<List<AutoInvoiceOperation>> GetActiveOperationsAsync(
        int storeId,
        CancellationToken ct = default)
    {
        return _db.AutoInvoiceOperations
            .Include(x => x.Sources)
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                (x.Status == AutoInvoiceOperationStatus.Pending ||
                 x.Status == AutoInvoiceOperationStatus.Processing ||
                 x.Status == AutoInvoiceOperationStatus.Unknown))
            .ToListAsync(ct);
    }

    public Task<InvoiceHead?> GetInvoiceHeadForAutomaticIssueAsync(
        int storeId,
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        return _db.InvoiceHeads
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.Id == invoiceHeadId &&
                !x.IsDeleted,
                ct);
    }
    public Task<InvoiceHead?> GetInvoiceHeadForClaimAsync(
        int storeId,
        int invoiceHeadId,
        CancellationToken ct = default)
        => GetInvoiceHeadForClaimCoreAsync(storeId, invoiceHeadId, includeCorrections: false, ct);

    public Task<InvoiceHead?> GetInvoiceHeadForManualClaimAsync(
        int storeId,
        int invoiceHeadId,
        CancellationToken ct = default)
        => GetInvoiceHeadForClaimCoreAsync(storeId, invoiceHeadId, includeCorrections: true, ct);

    private Task<InvoiceHead?> GetInvoiceHeadForClaimCoreAsync(
        int storeId,
        int invoiceHeadId,
        bool includeCorrections,
        CancellationToken ct)
    {
        return _db.InvoiceHeads
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(x => x.Order)
                .ThenInclude(x => x!.Payments)
            .Include(x => x.OriginalInvoiceHead)
            .Include(x => x.InvoiceProviderSetting)
            .Include(x => x.Details)
                .ThenInclude(x =>
                    x.OrderLegalEntityAllocation)
            .FirstOrDefaultAsync(
                x =>
                    x.StoreId == storeId &&
                    x.Id == invoiceHeadId &&
                    !x.IsDeleted &&
                    !x.LegacyReadOnly &&
                    !x.IsAutoInvoiceGroup &&
                    (includeCorrections ||
                        (x.OriginalInvoiceHeadId == null && x.CorrectionType == null)),
                ct);
    }

    public async Task<List<InvoiceHead>>
        GetInvoiceHeadsForClaimAsync(
            int storeId,
            IReadOnlyCollection<int> invoiceHeadIds,
            CancellationToken ct = default)
    {
        var ids = invoiceHeadIds
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
            return [];

        return await _db.InvoiceHeads
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(x => x.Order)
                .ThenInclude(x => x!.Payments)
            .Include(x => x.InvoiceProviderSetting)
            .Include(x => x.Details)
                .ThenInclude(x =>
                    x.OrderLegalEntityAllocation)
            .Where(x =>
                x.StoreId == storeId &&
                ids.Contains(x.Id) &&
                !x.IsDeleted &&
                !x.LegacyReadOnly &&
                !x.IsAutoInvoiceGroup &&
                x.OriginalInvoiceHeadId == null &&
                x.CorrectionType == null)
            .OrderBy(x =>
                x.Order != null &&
                x.Order.CompletedAtUtc.HasValue
                    ? x.Order.CompletedAtUtc.Value
                    : x.InvoiceDate)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    public Task<bool> HasSuccessfulSourceAsync(
        int storeId,
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        return _db.AutoInvoiceOperationSources
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.StoreId == storeId &&
                    x.InvoiceHeadId == invoiceHeadId &&
                    !x.IsDeleted &&
                    x.Status ==
                        AutoInvoiceSourceStatus.Succeeded,
                ct);
    }
    public Task<List<AutoInvoiceOperation>> GetOperationsAsync(
        int storeId,
        int take,
        CancellationToken ct = default)
    {
        return _db.AutoInvoiceOperations
            .AsNoTracking()
            .Include(x => x.Sources)
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
    }

    public Task<AutoInvoiceOperation?> GetOperationAsync(
        int storeId,
        int operationId,
        CancellationToken ct = default)
    {
        return _db.AutoInvoiceOperations
            .Include(x => x.Sources)
            .FirstOrDefaultAsync(
                x => x.StoreId == storeId &&
                     x.Id == operationId &&
                     !x.IsDeleted,
                ct);
    }

    public Task<bool> HasActiveSourceAsync(
        int storeId,
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        return _db.AutoInvoiceOperationSources.AnyAsync(
            x => x.StoreId == storeId &&
                 x.InvoiceHeadId == invoiceHeadId &&
                 x.IsActive &&
                 !x.IsDeleted,
            ct);
    }

    public Task AddWorkerStateAsync(
        AutoInvoiceWorkerState state,
        CancellationToken ct = default)
        => _db.AutoInvoiceWorkerStates.AddAsync(state, ct).AsTask();

    public Task AddOperationAsync(
        AutoInvoiceOperation operation,
        CancellationToken ct = default)
    {
        return _db.AutoInvoiceOperations.AddAsync(operation, ct).AsTask();
    }

    public Task AddInvoiceHeadAsync(
        InvoiceHead invoice,
        CancellationToken ct = default)
    {
        return _db.InvoiceHeads.AddAsync(invoice, ct).AsTask();
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public void DiscardFailedAutoInvoiceChanges(bool includeWorkerState = false)
    {
        // Do not call DetectChanges as the only cleanup step here. A failed
        // group insert can contain a required relationship which EF represents
        // as a conceptual null; DetectChanges itself may throw before the
        // rolled-back graph can be detached.
        try
        {
            _db.ChangeTracker.DetectChanges();
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Auto-invoice cleanup DetectChanges failed with {0}; continuing best-effort detachment.",
                ex.GetType().Name);

            // The entries are still available through ChangeTracker.Entries;
            // detach them in dependency order below.
        }

        static int CleanupOrder(object entity) => entity switch
        {
            InvoiceDetail => 0,
            AutoInvoiceOperationSource => 1,
            AutoInvoiceOperation => 2,
            InvoiceHead => 3,
            _ => 99
        };

        var entries = _db.ChangeTracker.Entries()
            .Where(entry =>
                (entry.Entity is AutoInvoiceOperation or
                 AutoInvoiceOperationSource or
                 InvoiceHead or
                 InvoiceDetail) ||
                (includeWorkerState && entry.Entity is AutoInvoiceWorkerState))
            .OrderBy(entry => CleanupOrder(entry.Entity))
            .ToList();

        foreach (var entry in entries)
        {
            try
            {
                entry.State = EntityState.Detached;
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "Auto-invoice cleanup could not detach {0}; failure type {1}; continuing best-effort cleanup.",
                    entry.Entity.GetType().Name,
                    ex.GetType().Name);

                // The cleanup is best-effort. Keep the heartbeat entities
                // trackable; the next scope/cycle will reload stale claims.
            }
        }
    }
}
