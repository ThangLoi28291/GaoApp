using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Text.Json;

namespace GaoApp.Infrastructure.Repositories.Invoices;

/// <summary>One documentary projection shared by the management view and issuance preflight.</summary>
public sealed class InvoiceInputStockReadRepository(AppDbContext db, IMemoryCache? cache = null) : IInvoiceInputStockReadRepository
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> SnapshotLocks = new();

    public async Task<IReadOnlyList<InvoiceInputStockMovement>> GetMovementsAsync(int storeId, CancellationToken ct = default)
    {
        if (cache is null || storeId <= 0)
            return await LoadMovementsAsync(storeId, null, null, ct);

        // The revision changes when a receipt is linked/reviewed/accepted or
        // when a physical inventory transaction is posted. This keeps the
        // fast snapshot while preventing a stale result after approval.
        var latestAuditId = await db.PurchaseReceiptAuditEvents.AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .Select(x => (long?)x.Id)
            .MaxAsync(ct) ?? 0L;
        var latestInventoryId = await db.InventoryTransactions.AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .Select(x => (int?)x.Id)
            .MaxAsync(ct) ?? 0;
        var key = $"invoice-input-stock:{storeId}:{latestAuditId}:{latestInventoryId}";
        if (cache.TryGetValue(key, out IReadOnlyList<InvoiceInputStockMovement>? snapshot) && snapshot is not null)
            return snapshot;

        var gate = SnapshotLocks.GetOrAdd(storeId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(key, out snapshot) && snapshot is not null)
                return snapshot;

            snapshot = await LoadMovementsAsync(storeId, null, null, ct);
            cache.Set(key, snapshot, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30),
                Size = Math.Max(1, snapshot.Count)
            });
            return snapshot;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Reads only the movement slice relevant to one invoice preflight.  The
    /// unfiltered overload remains available for the management/read screens.
    /// </summary>
    public async Task<IReadOnlyList<InvoiceInputStockMovement>> GetMovementsAsync(
        int storeId,
        IReadOnlyCollection<int>? productVariantIds,
        IReadOnlyCollection<int>? warehouseIds,
        CancellationToken ct = default)
        => productVariantIds is null && warehouseIds is null
            ? await GetMovementsAsync(storeId, ct)
            : await LoadMovementsAsync(storeId, productVariantIds, warehouseIds, ct);

    private async Task<IReadOnlyList<InvoiceInputStockMovement>> LoadMovementsAsync(
        int storeId,
        IReadOnlyCollection<int>? productVariantIds,
        IReadOnlyCollection<int>? warehouseIds,
        CancellationToken ct)
    {
        if (storeId <= 0) return [];

        var variantFilter = productVariantIds?
            .Where(x => x > 0)
            .Distinct()
            .ToArray();
        var warehouseFilter = warehouseIds?
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        // XML reconciliation is documentary support. After the manager has
        // explicitly reviewed the linked invoice, or accepted a discrepancy
        // before approving the receipt, the physical receipt ledger becomes
        // the source of truth for invoice-input stock. A later unlink/relink
        // event invalidates an older manager decision. The decision must also
        // reference the current invoice link and reconciliation evidence.
        var followUpEventTypes = new[]
        {
            PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed,
            PurchaseReceiptAuditEventType.InputInvoiceReconciliationAccepted,
            PurchaseReceiptAuditEventType.InputInvoiceReconciliationAcceptanceInvalidated,
            PurchaseReceiptAuditEventType.InputInvoiceLinked,
            PurchaseReceiptAuditEventType.InputInvoiceUnlinked,
            PurchaseReceiptAuditEventType.InputInvoiceRelinked
        };
        var latestFollowUpIds = db.PurchaseReceiptAuditEvents.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.IsSuccess && followUpEventTypes.Contains(x.EventType))
            .GroupBy(x => x.StockDocumentId)
            .Select(group => new { StockDocumentId = group.Key, EventId = group.Max(x => x.Id) });
        var managerDecisions = await (
            from audit in db.PurchaseReceiptAuditEvents.AsNoTracking()
            join latest in latestFollowUpIds
                on new { audit.StockDocumentId, EventId = audit.Id }
                equals new { latest.StockDocumentId, latest.EventId }
            join document in db.StockDocuments.AsNoTracking()
                on new { audit.StoreId, StockDocumentId = audit.StockDocumentId }
                equals new { document.StoreId, StockDocumentId = document.Id }
            join association in db.StockDocumentInputInvoiceMaps.AsNoTracking()
                on new { document.StoreId, StockDocumentId = document.Id }
                equals new { association.StoreId, association.StockDocumentId }
            join reconciliation in db.StockDocumentInputInvoiceReconciliations.AsNoTracking()
                on new { association.StoreId, MapId = association.Id }
                equals new { reconciliation.StoreId, MapId = reconciliation.StockDocumentInputInvoiceMapId }
            join invoice in db.InputInvoiceHeads.AsNoTracking()
                on new { association.StoreId, association.InputInvoiceHeadId }
                equals new { invoice.StoreId, InputInvoiceHeadId = invoice.Id }
            where (audit.EventType == PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed
                    || audit.EventType == PurchaseReceiptAuditEventType.InputInvoiceReconciliationAccepted)
                && document.Status == StockDocumentStatus.Confirmed && document.Type == StockDocumentType.Receipt
                && !document.IsDeleted && !association.IsDeleted && !reconciliation.IsDeleted && !invoice.IsDeleted
                && reconciliation.StockDocumentId == document.Id
                && reconciliation.InputInvoiceHeadId == association.InputInvoiceHeadId
            select new
            {
                audit.StockDocumentId, audit.OccurredAtUtc, audit.EventType, audit.NewValuesJson,
                MapId = association.Id, association.InputInvoiceHeadId,
                reconciliation.EvidenceFingerprint, reconciliation.AcceptedEvidenceFingerprint,
                ReconciliationState = reconciliation.OverallState
            }
        ).ToListAsync(ct);
        var currentReviewedReceipts = managerDecisions
            .Where(x => x.EventType == PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed
                ? ReceiptInvoiceFollowUp.IsReviewCurrent(x.NewValuesJson, x.MapId, x.EvidenceFingerprint)
                : x.ReconciliationState == InputInvoiceReconciliationState.AcceptedMismatch
                    && !string.IsNullOrWhiteSpace(x.EvidenceFingerprint)
                    && x.AcceptedEvidenceFingerprint == x.EvidenceFingerprint
                    && IsAcceptanceCurrent(x.NewValuesJson, x.InputInvoiceHeadId, x.EvidenceFingerprint))
            .ToDictionary(x => x.StockDocumentId, x => x.OccurredAtUtc);
        var reviewedReceiptIds = currentReviewedReceipts.Keys.ToHashSet();
        var reviewedReceiptReferences = reviewedReceiptIds
            .Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        var receipts = await (
            from evidence in db.StockDocumentInputInvoiceDetailReconciliations.AsNoTracking()
            join reconciliation in db.StockDocumentInputInvoiceReconciliations.AsNoTracking()
                on evidence.StockDocumentInputInvoiceReconciliationId equals reconciliation.Id
            join association in db.StockDocumentInputInvoiceMaps.AsNoTracking()
                on reconciliation.StockDocumentInputInvoiceMapId equals association.Id
            join document in db.StockDocuments.AsNoTracking() on association.StockDocumentId equals document.Id
            join xml in db.InputInvoiceDetails.AsNoTracking() on evidence.InputInvoiceDetailId equals xml.Id
            where evidence.StoreId == storeId && reconciliation.StoreId == storeId && association.StoreId == storeId
                && document.StoreId == storeId && xml.InputInvoiceHead.StoreId == storeId
                && !evidence.IsDeleted && !reconciliation.IsDeleted && !association.IsDeleted && !document.IsDeleted
                && !xml.IsDeleted && !xml.InputInvoiceHead.IsDeleted && !evidence.IsIgnored
                && document.Status == StockDocumentStatus.Confirmed && document.Type == StockDocumentType.Receipt
                && evidence.StockDocumentId == document.Id && reconciliation.StockDocumentId == document.Id
                && reconciliation.InputInvoiceHeadId == association.InputInvoiceHeadId
                && evidence.InputInvoiceHeadId == association.InputInvoiceHeadId && xml.InputInvoiceHeadId == association.InputInvoiceHeadId
                && evidence.ProductVariantId != null && evidence.ConfirmedFactor > 0
                && evidence.DerivedBaseQuantity > 0 && evidence.ReceiptBaseQuantity > 0 && evidence.ConfirmedBaseUnitId.HasValue
                && (variantFilter == null || variantFilter.Contains(evidence.ProductVariantId ?? 0))
                && (warehouseFilter == null || warehouseFilter.Contains(document.WarehouseId))
                && document.Warehouse.StoreId == storeId
                && (!document.ConfirmedLegalEntityId.HasValue || document.ConfirmedLegalEntityId == document.Warehouse.LegalEntityId)
                && (!xml.InputInvoiceHead.ResolvedBuyerLegalEntityId.HasValue || xml.InputInvoiceHead.ResolvedBuyerLegalEntityId == document.Warehouse.LegalEntityId)
                && db.StockDocumentLineInputInvoiceMaps.Any(map => map.StoreId == storeId && !map.IsDeleted
                    && map.StockDocumentId == document.Id && map.UseInputInvoice && map.InputInvoiceDetailId == xml.Id
                    && !map.StockDocumentLine.IsDeleted && map.StockDocumentLine.StockDocumentId == document.Id
                    && map.StockDocumentLine.ProductVariantId == evidence.ProductVariantId)
            select new ReceiptSource
            {
                EvidenceId = evidence.Id, DocumentId = document.Id, XmlDetailId = xml.Id,
                VariantId = evidence.ProductVariantId!.Value, WarehouseId = document.WarehouseId,
                LegalEntityId = document.Warehouse.LegalEntityId, Factor = evidence.ConfirmedFactor!.Value,
                BaseUnitId = evidence.ConfirmedBaseUnitId, XmlBase = evidence.DerivedBaseQuantity,
                ReceiptBase = evidence.ReceiptBaseQuantity, DocumentNo = document.DocumentNo,
                XmlNumber = xml.InputInvoiceHead.InvoiceNumber,
                ConfirmedAt = document.ConfirmedAtUtc ?? document.CreatedAtUtc,
                MappedAt = evidence.CreatedAtUtc, AssociatedAt = association.CreatedAtUtc
            }).ToListAsync(ct);

        // Manager-confirmed receipts are posted below from the same
        // InventoryTransaction rows used by /admin/inventory-ledger. Do not
        // also count their XML evidence rows, otherwise mapped lines would be
        // doubled and unmatched receipt lines would be lost.
        receipts.RemoveAll(x => reviewedReceiptIds.Contains(x.DocumentId));

        var result = new List<InvoiceInputStockMovement>();
        // An XML detail may serve several receipts/warehouses. Allocate once globally, before warehouse filtering.
        foreach (var group in receipts.GroupBy(x => x.XmlDetailId))
        {
            // Conflicting conversion snapshots must be reconciled, never guessed or added together.
            if (group.Select(x => (x.VariantId, x.Factor, x.BaseUnitId, x.XmlBase)).Distinct().Count() != 1) continue;
            var remaining = group.First().XmlBase;
            foreach (var source in group.OrderBy(x => x.RecognizedAt).ThenBy(x => x.DocumentId).ThenBy(x => x.EvidenceId))
            {
                var quantity = Math.Min(remaining, source.ReceiptBase);
                if (quantity <= 0) continue;
                remaining -= quantity;
                result.Add(new InvoiceInputStockMovement
                {
                    Key = $"xml-{source.EvidenceId}", WarehouseId = source.WarehouseId, LegalEntityId = source.LegalEntityId,
                    ProductVariantId = source.VariantId, DateUtc = Utc(source.RecognizedAt), Kind = "increase", Change = quantity,
                    StockDocumentId = source.DocumentId, SourceCode = source.DocumentNo, XmlNumber = source.XmlNumber,
                    Note = $"XML quy đổi: {source.XmlBase:0.####}; dòng nhập được map: {source.ReceiptBase:0.####}. Ghi nhận {quantity:0.####} đơn vị gốc sau khi giới hạn số lượng XML dùng chung."
                });
            }
        }

        if (reviewedReceiptReferences.Length > 0)
        {
            var reviewedInbound = await db.InventoryTransactions.AsNoTracking()
                .Where(x => x.StoreId == storeId && !x.IsDeleted
                    && x.ReferenceType == InventoryReferenceType.StockDocument
                    && x.TransactionType == InventoryTransactionType.PurchaseReceipt
                    && x.QuantityChange > 0
                    && x.ReferenceId != null
                    && reviewedReceiptReferences.Contains(x.ReferenceId)
                    && (variantFilter == null || variantFilter.Contains(x.ProductVariantId))
                    && (warehouseFilter == null || warehouseFilter.Contains(x.WarehouseId)))
                .Select(x => new ReviewedReceiptSource
                {
                    TransactionId = x.Id,
                    ReferenceId = x.ReferenceId!,
                    VariantId = x.ProductVariantId,
                    WarehouseId = x.WarehouseId,
                    LegalEntityId = x.Warehouse.LegalEntityId,
                    Quantity = x.QuantityChange,
                    UnitCost = x.UnitCostSnapshot,
                    TotalCost = x.TotalCost,
                    IsProvisionalCost = x.IsProvisionalCost,
                    OccurredAtUtc = x.OccurredAtUtc
                })
                .ToListAsync(ct);

            var reviewedDocuments = await (
                from document in db.StockDocuments.AsNoTracking()
                join map in db.StockDocumentInputInvoiceMaps.AsNoTracking()
                    on new { document.StoreId, StockDocumentId = document.Id }
                    equals new { map.StoreId, map.StockDocumentId }
                join invoice in db.InputInvoiceHeads.AsNoTracking()
                    on new { map.StoreId, InputInvoiceHeadId = map.InputInvoiceHeadId }
                    equals new { invoice.StoreId, InputInvoiceHeadId = invoice.Id }
                where document.StoreId == storeId && reviewedReceiptIds.Contains(document.Id)
                    && !document.IsDeleted && !map.IsDeleted && !invoice.IsDeleted
                select new ReviewedReceiptDocument
                {
                    DocumentId = document.Id,
                    DocumentNo = document.DocumentNo,
                    // InvoiceIdentityDate is the date-only value normalized
                    // from the XML. Fall back to InvoiceDate for legacy XML.
                    InvoiceDate = invoice.InvoiceIdentityDate ?? invoice.InvoiceDate
                }).ToDictionaryAsync(x => x.DocumentId, ct);

            foreach (var source in reviewedInbound)
            {
                if (!int.TryParse(source.ReferenceId, out var documentId)
                    || !reviewedReceiptIds.Contains(documentId)
                    || !reviewedDocuments.TryGetValue(documentId, out var document))
                    continue;

                result.Add(new InvoiceInputStockMovement
                {
                    Key = $"reviewed-receipt-{source.TransactionId}",
                    WarehouseId = source.WarehouseId,
                    LegalEntityId = source.LegalEntityId,
                    ProductVariantId = source.VariantId,
                    DateUtc = InvoiceDateUtc(document.InvoiceDate, currentReviewedReceipts[documentId]),
                    Kind = "increase",
                    Change = source.Quantity,
                    UnitCost = source.UnitCost,
                    TotalCost = source.TotalCost,
                    IsProvisionalCost = source.IsProvisionalCost,
                    StockDocumentId = documentId,
                    SourceCode = document.DocumentNo,
                    OperationLabel = "Nhập theo phiếu đã xác nhận",
                    Note = $"Quản lý đã xác nhận kiểm tra/đối chiếu hóa đơn. Ghi nhận theo giao dịch nhập kho gốc lúc {Utc(source.OccurredAtUtc):dd/MM/yyyy HH:mm} UTC; XML chỉ hỗ trợ đối chiếu."
                });
            }
        }

        var supplemental = await db.InvoiceInputStockSupplementalMovements.AsNoTracking()
            .Where(x => x.StoreId == storeId
                && x.Warehouse.StoreId == storeId
                && x.ProductVariant.StoreId == storeId)
            .Where(x => variantFilter == null || variantFilter.Contains(x.ProductVariantId))
            .Where(x => warehouseFilter == null || warehouseFilter.Contains(x.WarehouseId))
            .Select(x => new
            {
                x.Id,
                x.WarehouseId,
                x.ProductVariantId,
                x.EffectiveAtUtc,
                x.QuantityChange,
                x.MovementType,
                x.LegacySourceKey,
                x.LegacyOrderId,
                x.LegacyInvoiceNumber,
                x.LegacyInvoiceSymbol,
                LegacyInvoiceHeadId = db.InvoiceHeads.Where(h => h.StoreId == storeId && h.LegacySourceId == x.LegacyOrderId && x.LegacyOrderId != null)
                    .Select(h => (int?)h.Id).FirstOrDefault(),
                x.SourcePeriod,
                x.Note,
                LegalEntityId = x.Warehouse.LegalEntityId
            })
            .ToListAsync(ct);
        foreach (var item in supplemental)
        {
            if (item.QuantityChange == 0m) continue;
            var label = SupplementalLabel(item.MovementType);
            if (label is null) continue;
            var note = string.IsNullOrWhiteSpace(item.Note) ? label : $"{label}: {item.Note}";
            if (!string.IsNullOrWhiteSpace(item.SourcePeriod)) note += $" (kỳ {item.SourcePeriod})";
            result.Add(new InvoiceInputStockMovement
            {
                Key = $"supplemental-{item.Id}",
                WarehouseId = item.WarehouseId,
                LegalEntityId = item.LegalEntityId,
                ProductVariantId = item.ProductVariantId,
                DateUtc = Utc(item.EffectiveAtUtc),
                Kind = item.QuantityChange > 0m ? "increase" : "decrease",
                Change = item.QuantityChange,
                IsOpening = item.MovementType == InvoiceInputStockSupplementalMovementType.LegacyOpening,
                Held = 0m,
                SourceCode = string.Join(" · ", new[]
                {
                    item.LegacyOrderId.HasValue ? $"OrderID {item.LegacyOrderId}" : null,
                    !string.IsNullOrWhiteSpace(item.LegacyInvoiceNumber) ? $"HĐ {item.LegacyInvoiceNumber}" : null,
                    !string.IsNullOrWhiteSpace(item.LegacyInvoiceSymbol) ? $"Ký hiệu {item.LegacyInvoiceSymbol}" : null
                }.Where(x => x != null)) is { Length: > 0 } reference ? reference
                    : item.LegacySourceKey.StartsWith("GSTORE-IIS-V1|", StringComparison.Ordinal) ? label : item.LegacySourceKey,
                LegacySourceKey = item.LegacySourceKey,
                InvoiceHeadId = item.LegacyInvoiceHeadId,
                LegacyOrderId = item.LegacyOrderId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                LegacyInvoiceNumber = item.LegacyInvoiceNumber,
                LegacyInvoiceSymbol = item.LegacyInvoiceSymbol,
                OperationLabel = label,
                Note = note
            });
        }

        // The supplemental rows were already loaded above. Keep the legacy
        // outbound source ids in memory instead of running a correlated
        // string-concatenation subquery for every InvoiceDetail.
        var legacyOutboundSourceIds = supplemental
            .Where(x => x.MovementType == InvoiceInputStockSupplementalMovementType.LegacyOutbound)
            .Select(x => x.LegacySourceKey)
            .Where(x => x.StartsWith("GSTORE-IIS-V1|X|", StringComparison.OrdinalIgnoreCase))
            .Select(x => long.TryParse(x["GSTORE-IIS-V1|X|".Length..], out var id) ? (long?)id : null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToHashSet();

        var invoices = await db.InvoiceDetails.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.InvoiceHead.StoreId == storeId && !x.IsDeleted && !x.InvoiceHead.IsDeleted
                && x.ProductVariantId.HasValue && x.ProductVariant!.StoreId == storeId
                && (variantFilter == null || variantFilter.Contains(x.ProductVariantId.Value))
                && x.InvoiceHead.OriginalInvoiceHeadId == null && x.InvoiceHead.CorrectionType == null
                && (x.InvoiceHead.ProviderStatus >= InvoiceProviderStatus.Issuing || x.InvoiceHead.ProviderInvoiceNo != null || x.InvoiceHead.IssuedAtUtc != null))
            .Select(x => new
            {
                x.Id, x.InvoiceHeadId, x.LegacySourceId, VariantId = x.ProductVariantId!.Value, x.ItemName, x.Quantity,
                x.InvoiceHead.ProviderStatus, x.InvoiceHead.ProviderInvoiceNo, x.InvoiceHead.IssuedAtUtc,
                x.InvoiceHead.LastErrorCode, x.InvoiceHead.LastErrorMessage, x.InvoiceHead.CreatedAtUtc,
                x.InvoiceHead.LastSyncedAtUtc, x.InvoiceHead.LegalEntityId,
                WarehouseId = x.OrderLegalEntityAllocation != null ? x.OrderLegalEntityAllocation.WarehouseId
                    : x.InvoiceHead.LegalEntity != null && x.InvoiceHead.LegalEntity.DefaultWarehouseId.HasValue
                        ? x.InvoiceHead.LegalEntity.DefaultWarehouseId.Value : x.InvoiceHead.Order != null ? x.InvoiceHead.Order.POSShift.WarehouseId : 0,
                BaseQuantity = x.LegacyUnitFactor.HasValue ? x.Quantity * x.LegacyUnitFactor.Value
                    : x.OrderLegalEntityAllocation != null ? x.OrderLegalEntityAllocation.BaseQuantity
                    : x.OrderLine != null && x.OrderLine.Quantity != 0
                        ? x.Quantity * x.OrderLine.BaseQuantity / x.OrderLine.Quantity : x.Quantity
            }).ToListAsync(ct);
        foreach (var item in invoices)
        {
            if (item.LegacySourceId.HasValue && legacyOutboundSourceIds.Contains(item.LegacySourceId.Value))
                continue;
            var issued = !string.IsNullOrWhiteSpace(item.ProviderInvoiceNo) || item.IssuedAtUtc.HasValue ||
                item.ProviderStatus is InvoiceProviderStatus.Issued or InvoiceProviderStatus.IssuedWaitingNumber
                    or InvoiceProviderStatus.PdfDownloaded or InvoiceProviderStatus.ZipDownloaded or InvoiceProviderStatus.EmailSent;
            var held = !issued && (item.ProviderStatus == InvoiceProviderStatus.Issuing ||
                IsUncertainFailure(item.ProviderStatus, item.LastErrorCode, item.LastErrorMessage));
            if (!issued && !held) continue;
            var quantity = Math.Abs(item.BaseQuantity);
            if (quantity == 0) continue;
            result.Add(new InvoiceInputStockMovement
            {
                Key = $"invoice-{item.Id}", WarehouseId = item.WarehouseId, LegalEntityId = item.LegalEntityId,
                ProductVariantId = item.VariantId, ProductName = item.ItemName,
                DateUtc = Utc(item.IssuedAtUtc ?? item.LastSyncedAtUtc ?? item.CreatedAtUtc),
                Kind = issued ? "decrease" : "hold", Change = issued ? -quantity : 0, Held = held ? quantity : 0,
                InvoiceHeadId = item.InvoiceHeadId, SourceCode = item.ProviderInvoiceNo ?? $"HĐ #{item.InvoiceHeadId}",
                Note = held ? "Đang phát hành hoặc chưa xác định kết quả. Số lượng được giữ để tránh phát hành trùng; chưa ghi giảm tồn."
                    : "Đã ghi giảm khi phát hành hóa đơn. Đơn POS hoặc hóa đơn nháp không làm giảm tồn XML."
            });
        }

        var variantIds = result.Select(x => x.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking().Where(x => x.StoreId == storeId && variantIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Sku, Name = x.ProductVariantName ?? x.Product.Name, Unit = x.Product.BaseUnit.Name })
            .ToDictionaryAsync(x => x.Id, ct);
        var warehouses = await db.Warehouses.AsNoTracking().Where(x => x.StoreId == storeId)
            .Select(x => new { x.Id, x.Name, x.LegalEntityId, LegalName = x.LegalEntity.Name }).ToDictionaryAsync(x => x.Id, ct);
        result.RemoveAll(x => !variants.ContainsKey(x.ProductVariantId) || !warehouses.ContainsKey(x.WarehouseId) ||
            (x.LegalEntityId is > 0 && warehouses[x.WarehouseId].LegalEntityId != x.LegalEntityId));
        foreach (var row in result)
        {
            var variant = variants[row.ProductVariantId]; var warehouse = warehouses[row.WarehouseId];
            row.ProductName = string.IsNullOrWhiteSpace(variant.Name) ? row.ProductName : variant.Name;
            row.Code = variant.Sku; row.BaseUnit = variant.Unit ?? "đơn vị gốc";
            row.WarehouseName = warehouse.Name; row.LegalEntityName = warehouse.LegalName ?? "";
            row.LegalEntityId = warehouse.LegalEntityId;
        }
        return result;
    }

    private static bool IsAcceptanceCurrent(string? evidenceJson, int invoiceId, string fingerprint)
    {
        if (string.IsNullOrEmpty(evidenceJson)) return false;
        try
        {
            using var evidence = JsonDocument.Parse(evidenceJson);
            return evidence.RootElement.ValueKind == JsonValueKind.Object
                && evidence.RootElement.TryGetProperty("InputInvoiceHeadId", out var invoice)
                && invoice.ValueKind == JsonValueKind.Number && invoice.TryGetInt32(out var id) && id == invoiceId
                && evidence.RootElement.TryGetProperty("EvidenceFingerprint", out var hash)
                && hash.ValueKind == JsonValueKind.String && hash.GetString() == fingerprint;
        }
        catch (JsonException) { return false; }
    }

    private static string? SupplementalLabel(InvoiceInputStockSupplementalMovementType movementType)
        => movementType switch
        {
            InvoiceInputStockSupplementalMovementType.LegacyOpening => "Tồn đầu kỳ cũ",
            InvoiceInputStockSupplementalMovementType.LegacyInbound => "Nhập HĐĐT lịch sử",
            InvoiceInputStockSupplementalMovementType.LegacyReconciliationAdjustment => "Điều chỉnh lịch sử",
            InvoiceInputStockSupplementalMovementType.LegacyOutbound => "Xuất HĐĐT lịch sử",
            _ => null
        };

    private static bool IsUncertainFailure(InvoiceProviderStatus status, string? code, string? message)
        => status == InvoiceProviderStatus.IssueFailed &&
            (string.Equals(code, "TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
             (code ?? "").StartsWith("HTTP_5", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(code, "VIETTEL_SERVER_500", StringComparison.OrdinalIgnoreCase) ||
             (message ?? "").Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
             (message ?? "").Contains("HTTP 500", StringComparison.OrdinalIgnoreCase));

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime InvoiceDateUtc(DateTime? invoiceDate, DateTime fallbackUtc)
        => invoiceDate.HasValue
            ? DateTime.SpecifyKind(invoiceDate.Value.Date, DateTimeKind.Utc)
            : Utc(fallbackUtc);

    private sealed class ReceiptSource
    {
        public int EvidenceId { get; init; }
        public int DocumentId { get; init; }
        public int XmlDetailId { get; init; }
        public int VariantId { get; init; }
        public int WarehouseId { get; init; }
        public int LegalEntityId { get; init; }
        public int? BaseUnitId { get; init; }
        public decimal Factor { get; init; }
        public decimal XmlBase { get; init; }
        public decimal ReceiptBase { get; init; }
        public string DocumentNo { get; init; } = "";
        public string? XmlNumber { get; init; }
        public DateTime ConfirmedAt { get; init; }
        public DateTime MappedAt { get; init; }
        public DateTime AssociatedAt { get; init; }
        public DateTime RecognizedAt => new[] { ConfirmedAt, MappedAt, AssociatedAt }.Max();
    }

    private sealed class ReviewedReceiptSource
    {
        public int TransactionId { get; init; }
        public string ReferenceId { get; init; } = "";
        public int VariantId { get; init; }
        public int WarehouseId { get; init; }
        public int LegalEntityId { get; init; }
        public decimal Quantity { get; init; }
        public decimal UnitCost { get; init; }
        public decimal TotalCost { get; init; }
        public bool IsProvisionalCost { get; init; }
        public DateTime OccurredAtUtc { get; init; }
    }

    private sealed class ReviewedReceiptDocument
    {
        public int DocumentId { get; init; }
        public string DocumentNo { get; init; } = "";
        public DateTime? InvoiceDate { get; init; }
    }
}
