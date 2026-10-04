using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Inventory;

/// <summary>Explicit manager confirmation for historical receipts; never reposts inventory or payables.</summary>
public sealed class ReceiptInvoiceBackfillService(AppDbContext db, ITenantContext tenant, ICurrentUser user,
    IInputInvoiceReconciliationService reconciliationService) : IReceiptInvoiceBackfillService
{
    private int StoreId => tenant.StoreId is > 0 ? tenant.StoreId.Value
        : throw new BusinessRuleException("Vui lòng chọn cửa hàng.");

    public static (DateTime FromUtc, DateTime? ToUtcExclusive) GetUtcRange(ReceiptInvoiceBackfillQuery query)
    {
        if (query.LinkedFromDate.Year < 1900 || query.LinkedFromDate.Year >= 9999 ||
            query.LinkedToDate?.Year < 1900 || query.LinkedToDate?.Year >= 9999 ||
            query.LinkedToDate?.Date < query.LinkedFromDate.Date || query.Page is < 1 or > 1000000 || query.PageSize is < 1 or > 50)
            throw new BusinessRuleException("Khoảng ngày gắn XML hoặc số dòng không hợp lệ.");
        // Business dates are Vietnam dates, independent of the server's timezone.
        return (DateTime.SpecifyKind(query.LinkedFromDate.Date.AddHours(-7), DateTimeKind.Utc),
            query.LinkedToDate.HasValue ? DateTime.SpecifyKind(query.LinkedToDate.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc) : null);
    }

    private IQueryable<StockDocumentInputInvoiceMap> Candidates(ReceiptInvoiceBackfillQuery query, bool direct, bool purchaseOrder)
    {
        var (fromUtc, toUtc) = GetUtcRange(query);
        var storeId = StoreId;
        var eligible = db.StockDocumentInputInvoiceMaps.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == storeId && !x.IsDeleted &&
            x.StockDocument.StoreId == storeId && !x.StockDocument.IsDeleted && x.StockDocument.Type == StockDocumentType.Receipt &&
            x.StockDocument.Status == StockDocumentStatus.Confirmed &&
            x.InputInvoiceHead.StoreId == storeId && !x.InputInvoiceHead.IsDeleted &&
            ((direct && x.StockDocument.ReceiptSource != PurchaseReceiptSource.PurchaseOrder) ||
             (purchaseOrder && x.StockDocument.ReceiptSource == PurchaseReceiptSource.PurchaseOrder)));
        // Restoring a previously deleted association can retain its original CreatedAtUtc.
        // In that case the successful link/relink event is the actual attachment date.
        return from map in eligible
            let auditedAt = db.PurchaseReceiptAuditEvents.Where(x => x.StoreId == storeId && x.StockDocumentId == map.StockDocumentId &&
                x.IsSuccess && (x.EventType == PurchaseReceiptAuditEventType.InputInvoiceLinked || x.EventType == PurchaseReceiptAuditEventType.InputInvoiceRelinked))
                .OrderByDescending(x => x.Id).Select(x => (DateTime?)x.OccurredAtUtc).FirstOrDefault()
            let linkedAt = auditedAt.HasValue && auditedAt.Value > map.CreatedAtUtc ? auditedAt.Value : map.CreatedAtUtc
            where linkedAt >= fromUtc && (!toUtc.HasValue || linkedAt < toUtc.Value)
            select map;
    }

    public async Task<ReceiptInvoiceBackfillPage> PreviewAsync(ReceiptInvoiceBackfillQuery query,
        bool canApproveDirect, bool canApprovePurchaseOrder, CancellationToken ct)
    {
        var candidates = Candidates(query, canApproveDirect, canApprovePurchaseOrder);
        var total = await candidates.CountAsync(ct);
        var ids = await candidates.OrderBy(x => x.StockDocumentId).Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize).Select(x => x.StockDocumentId).ToListAsync(ct);
        var rows = new List<ReceiptInvoiceBackfillRow>();
        foreach (var id in ids)
        {
            var candidate = await LoadAsync(id, query, canApproveDirect, canApprovePurchaseOrder, ct);
            if (candidate != null) rows.Add(candidate.Row);
            db.ChangeTracker.Clear();
        }
        return new(query.Page, query.PageSize, total, rows);
    }

    public async Task<IReadOnlyList<ReceiptInvoiceBackfillResult>> ConfirmAsync(ReceiptInvoiceBackfillRequest request,
        bool canApproveDirect, bool canApprovePurchaseOrder, CancellationToken ct)
    {
        GetUtcRange(request);
        var reason = request.Reason?.Trim();
        if (user.UserId is not > 0 || !user.IsAuthenticated) throw new BusinessRuleException("Vui lòng đăng nhập lại.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500 || request.Items is not { Count: > 0 and <= 50 } ||
            request.Items.Any(x => x.ReceiptId <= 0 || x.SnapshotHash?.Length != 64) ||
            request.Items.Select(x => x.ReceiptId).Distinct().Count() != request.Items.Count)
            throw new BusinessRuleException("Chọn từ 1 đến 50 phiếu và nhập ghi chú xác nhận.");
        var results = new List<ReceiptInvoiceBackfillResult>();
        foreach (var item in request.Items)
        {
            ct.ThrowIfCancellationRequested();
            try { results.Add(await ConfirmOneAsync(item, request, reason, canApproveDirect, canApprovePurchaseOrder, ct)); }
            catch (BusinessRuleException error) { results.Add(new(item.ReceiptId, "Blocked", error.SafeMessage)); }
            catch (DbUpdateConcurrencyException) { results.Add(new(item.ReceiptId, "Blocked", "Phiếu đã thay đổi. Vui lòng rà soát lại.")); }
            finally { db.ChangeTracker.Clear(); }
        }
        return results;
    }

    private async Task<ReceiptInvoiceBackfillResult> ConfirmOneAsync(ReceiptInvoiceBackfillItem item,
        ReceiptInvoiceBackfillQuery query, string reason, bool direct, bool purchaseOrder, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var storeId = StoreId;
        var receipt = await db.StockDocuments.FromSqlInterpolated($"SELECT * FROM [StockDocument] WITH (UPDLOCK,HOLDLOCK) WHERE [Id] = {item.ReceiptId} AND [StoreId] = {storeId}")
            .SingleOrDefaultAsync(ct);
        if (receipt == null) throw new BusinessRuleException("Không tìm thấy phiếu trong phạm vi được phép xử lý.");
        await db.Entry(receipt).ReloadAsync(ct);
        var candidate = await LoadAsync(item.ReceiptId, query, direct, purchaseOrder, ct)
            ?? throw new BusinessRuleException("Phiếu không còn thuộc khoảng ngày gắn XML hoặc phạm vi được phép xử lý.");
        if (candidate.Row.Status == "Complete")
        {
            await transaction.CommitAsync(ct);
            return new(item.ReceiptId, "Skipped", "Phiếu đã được tính đủ; không ghi xác nhận trùng.");
        }
        if (candidate.Row.Status != "Ready") throw new BusinessRuleException(candidate.Row.Message);
        if (candidate.Row.SnapshotHash != item.SnapshotHash)
            throw new BusinessRuleException("Phiếu, liên kết XML hoặc số liệu đã thay đổi sau khi rà soát. Vui lòng rà soát lại.");

        var evidence = await reconciliationService.RefreshWithinTransactionAsync(storeId, item.ReceiptId, ct);
        if (evidence.InputInvoiceHeadId != candidate.InvoiceId || evidence.EvidenceFingerprint != candidate.Fingerprint)
            throw new BusinessRuleException("Kết quả đối chiếu đã thay đổi. Vui lòng rà soát lại trước khi xác nhận.");
        db.PurchaseReceiptAuditEvents.Add(new()
        {
            StoreId = storeId, StockDocumentId = item.ReceiptId,
            EventType = PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed,
            ActorUserId = user.UserId!.Value, ActorUserName = user.UserName, OccurredAtUtc = DateTime.UtcNow,
            IsSuccess = true, Reason = reason, Note = "Bổ sung tồn hóa đơn cho phiếu đã duyệt theo ngày gắn XML.",
            ChangedFieldsJson = "[\"InvoiceFollowUp\"]",
            OldValuesJson = JsonSerializer.Serialize(new { candidate.Row.ProductCountNeedingConfirmation }),
            NewValuesJson = JsonSerializer.Serialize(new { InvoiceFollowUp = "Reviewed", MapId = candidate.Row.MapId,
                EvidenceFingerprint = evidence.EvidenceFingerprint, BatchConfirmation = true, query.LinkedFromDate,
                query.LinkedToDate, candidate.Row.SnapshotHash })
        });
        await db.SaveChangesAsync(ct);
        var verification = await LoadAsync(item.ReceiptId, query, direct, purchaseOrder, ct);
        if (verification?.Row.Status != "Complete")
            throw new BusinessRuleException("Chưa thể ghi nhận đầy đủ tồn hóa đơn cho phiếu này; xác nhận đã được hủy.");
        await transaction.CommitAsync(ct);
        return new(item.ReceiptId, "Confirmed", "Đã xác nhận và ghi nhận đủ số lượng, giá vốn theo giao dịch nhập kho.");
    }

    private async Task<Candidate?> LoadAsync(int id, ReceiptInvoiceBackfillQuery query, bool direct, bool purchaseOrder, CancellationToken ct)
    {
        var map = await Candidates(query, direct, purchaseOrder).Where(x => x.StockDocumentId == id)
            .Include(x => x.StockDocument).ThenInclude(x => x.Warehouse)
            .Include(x => x.InputInvoiceHead).SingleOrDefaultAsync(ct);
        if (map == null) return null;
        var doc = map.StockDocument;
        var row = new ReceiptInvoiceBackfillRow { ReceiptId = id, DocumentNo = doc.DocumentNo, ReceiptSource = doc.ReceiptSource,
            MapId = map.Id, XmlNumber = map.InputInvoiceHead.InvoiceNumber ?? "", WarehouseName = doc.Warehouse.Name,
            LinkedAtUtc = DateTime.SpecifyKind(map.CreatedAtUtc, DateTimeKind.Utc) };
        var auditedLinkAt = await db.PurchaseReceiptAuditEvents.AsNoTracking().Where(x => x.StoreId == StoreId && x.StockDocumentId == id &&
            x.IsSuccess && (x.EventType == PurchaseReceiptAuditEventType.InputInvoiceLinked || x.EventType == PurchaseReceiptAuditEventType.InputInvoiceRelinked))
            .OrderByDescending(x => x.Id).Select(x => (DateTime?)x.OccurredAtUtc).FirstOrDefaultAsync(ct);
        if (auditedLinkAt > row.LinkedAtUtc) row.LinkedAtUtc = DateTime.SpecifyKind(auditedLinkAt.Value, DateTimeKind.Utc);
        var fingerprint = "";
        try
        {
            if (doc.Warehouse.IsDeleted || doc.Warehouse.StoreId != StoreId ||
                (doc.ConfirmedLegalEntityId.HasValue && doc.ConfirmedLegalEntityId != doc.Warehouse.LegalEntityId) ||
                (map.InputInvoiceHead.ResolvedBuyerLegalEntityId.HasValue && map.InputInvoiceHead.ResolvedBuyerLegalEntityId != doc.Warehouse.LegalEntityId))
                throw new BusinessRuleException("Kho hoặc pháp nhân trên phiếu và XML không khớp; cần kiểm tra riêng.");
            var reference = id.ToString(CultureInfo.InvariantCulture);
            var posted = await db.InventoryTransactions.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == StoreId && !x.IsDeleted &&
                x.ReferenceType == InventoryReferenceType.StockDocument && x.TransactionType == InventoryTransactionType.PurchaseReceipt &&
                x.ReferenceId == reference && x.QuantityChange > 0)
                .Select(x => new { x.Id, x.ProductVariantId, x.WarehouseId, x.QuantityChange, x.UnitCostSnapshot, x.TotalCost,
                    VariantStoreId = x.ProductVariant.StoreId, VariantDeleted = x.ProductVariant.IsDeleted }).ToListAsync(ct);
            row.PostedTransactionCount = posted.Count;
            if (posted.Count == 0) throw new BusinessRuleException("Phiếu chưa có giao dịch nhập kho đã ghi sổ; không bổ sung tự động.");
            if (posted.Any(x => x.WarehouseId != doc.WarehouseId || x.VariantStoreId != StoreId || x.VariantDeleted))
                throw new BusinessRuleException("Giao dịch nhập có kho hoặc sản phẩm không hợp lệ; cần kiểm tra riêng.");
            var movements = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(StoreId,
                posted.Select(x => x.ProductVariantId).Distinct().ToArray(), [doc.WarehouseId], ct);
            var actual = movements.Where(x => x.StockDocumentId == id && x.Kind == "increase").OrderBy(x => x.Key).ToList();
            var expected = posted.GroupBy(x => x.ProductVariantId).ToDictionary(x => x.Key,
                x => new { Quantity = x.Sum(t => t.QuantityChange), Cost = x.Sum(t => t.TotalCost) });
            row.ProductCount = expected.Count;
            if (actual.Any(x => !expected.ContainsKey(x.ProductVariantId)) || expected.Any(x => actual.Where(m => m.ProductVariantId == x.Key).Sum(m => m.Change) > x.Value.Quantity))
                throw new BusinessRuleException("Tồn hóa đơn hiện ghi nhận vượt giao dịch nhập kho; cần kiểm tra riêng.");
            row.ProductCountNeedingConfirmation = expected.Count(x =>
                actual.Where(m => m.ProductVariantId == x.Key).Sum(m => m.Change) != x.Value.Quantity ||
                actual.Where(m => m.ProductVariantId == x.Key).Any(m => m.TotalCost == null) ||
                actual.Where(m => m.ProductVariantId == x.Key).Sum(m => m.TotalCost ?? 0) != x.Value.Cost);
            if (row.ProductCountNeedingConfirmation == 0)
            {
                row.Status = "Complete"; row.Message = "Đã được tính đủ số lượng và giá vốn.";
                return new(row, map.InputInvoiceHeadId, fingerprint);
            }
            var reconciliations = await db.StockDocumentInputInvoiceReconciliations.AsNoTracking()
                .Where(x => x.StoreId == StoreId && x.StockDocumentId == id && !x.IsDeleted).ToListAsync(ct);
            if (reconciliations.Count > 1 || reconciliations.Any(x => x.StockDocumentInputInvoiceMapId != map.Id || x.InputInvoiceHeadId != map.InputInvoiceHeadId))
                throw new BusinessRuleException("Dữ liệu đối chiếu không khớp liên kết XML hiện tại; cần kiểm tra riêng.");
            var reconciliation = reconciliations.SingleOrDefault();
            fingerprint = reconciliation?.EvidenceFingerprint ?? "";
            if (string.IsNullOrWhiteSpace(fingerprint))
                fingerprint = InputInvoiceReconciliationPolicy.Fingerprint(await reconciliationService.CalculateAsync(StoreId, id, ct));
            var latestDecision = await db.PurchaseReceiptAuditEvents.AsNoTracking().Where(x => x.StoreId == StoreId && x.StockDocumentId == id)
                .Select(x => (long?)x.Id).MaxAsync(ct);
            row.SnapshotHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                StoreId, ReceiptId = id, doc.RowVersion, MapId = map.Id, MapRowVersion = map.RowVersion, map.CreatedAtUtc, row.LinkedAtUtc,
                InvoiceRowVersion = map.InputInvoiceHead.RowVersion, doc.ConfirmedLegalEntityId,
                WarehouseRowVersion = doc.Warehouse.RowVersion, Fingerprint = fingerprint,
                ReconciliationRowVersion = reconciliation?.RowVersion, LatestDecision = latestDecision,
                Posted = posted.OrderBy(x => x.Id), Actual = actual.Select(x => new { x.Key, x.ProductVariantId, x.Change, x.UnitCost, x.TotalCost })
            })));
            row.Status = "Ready"; row.Message = "Cần xác nhận để tính đủ các giao dịch nhập kho vào tồn hóa đơn.";
        }
        catch (BusinessRuleException error) { row.Status = "Blocked"; row.Message = error.SafeMessage; }
        return new(row, map.InputInvoiceHeadId, fingerprint);
    }

    private sealed record Candidate(ReceiptInvoiceBackfillRow Row, int InvoiceId, string Fingerprint);
}
