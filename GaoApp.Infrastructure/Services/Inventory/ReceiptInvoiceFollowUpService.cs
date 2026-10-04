using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Inventory;

public sealed class ReceiptInvoiceFollowUpService(AppDbContext db, ITenantContext tenant, ICurrentUser user)
    : IReceiptInvoiceFollowUpService
{
    private int StoreId => tenant.StoreId is > 0 ? tenant.StoreId.Value
        : throw new BusinessRuleException("Vui lòng chọn cửa hàng.");

    private async Task<StockDocument> Load(int id, CancellationToken ct) => await db.StockDocuments
        .Where(x => x.Id == id && x.StoreId == StoreId && !x.IsDeleted && x.Type == StockDocumentType.Receipt)
        .SingleOrDefaultAsync(ct) ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập.");

    public async Task<ReceiptInvoiceFollowUpDto> GetAsync(int id, CancellationToken ct)
    {
        var receipt = await Load(id, ct);
        var map = await db.Set<StockDocumentInputInvoiceMap>().AsNoTracking()
            .Where(x => x.StoreId == StoreId && x.StockDocumentId == id && !x.IsDeleted)
            .Select(x => new { x.Id }).SingleOrDefaultAsync(ct);
        var reconciliation = map == null ? null : await db.Set<StockDocumentInputInvoiceReconciliation>().AsNoTracking()
            .Where(x => x.StoreId == StoreId && x.StockDocumentInputInvoiceMapId == map.Id && !x.IsDeleted)
            .SingleOrDefaultAsync(ct);
        var review = await db.PurchaseReceiptAuditEvents.AsNoTracking()
            .Where(x => x.StoreId == StoreId && x.StockDocumentId == id && x.IsSuccess &&
                (x.EventType == PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed ||
                 x.EventType == PurchaseReceiptAuditEventType.InputInvoiceLinked ||
                 x.EventType == PurchaseReceiptAuditEventType.InputInvoiceUnlinked ||
                 x.EventType == PurchaseReceiptAuditEventType.InputInvoiceRelinked))
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        var reviewed = receipt.Status == StockDocumentStatus.Confirmed && review?.EventType == PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed &&
            ReceiptInvoiceFollowUp.IsReviewCurrent(review?.NewValuesJson, map?.Id, reconciliation?.EvidenceFingerprint);
        var state = ReceiptInvoiceFollowUp.Resolve(receipt.WaitForInputInvoice, map != null, reconciliation?.OverallState, reviewed);
        return new(state, map != null, receipt.Status == StockDocumentStatus.Confirmed,
            Convert.ToBase64String(receipt.RowVersion), state == "Waiting" && receipt.ApprovedAtUtc.HasValue
                ? Math.Max(0, (DateTime.UtcNow.Date - receipt.ApprovedAtUtc.Value.Date).Days) : 0, receipt.ReceiptSource)
        {
            MapId = map?.Id, EvidenceFingerprint = reconciliation?.EvidenceFingerprint,
            ReceiptGoodsTotal = reconciliation?.ReceiptGoodsTotal, XmlPaymentAmount = reconciliation?.XmlPaymentAmount,
            UnmatchedDetailCount = reconciliation?.UnmatchedDetailCount ?? 0,
            ReviewReason = reviewed ? review?.Reason : null, ReviewedBy = reviewed ? review?.ActorUserName : null,
            ReviewedAtUtc = reviewed && review != null ? DateTime.SpecifyKind(review.OccurredAtUtc, DateTimeKind.Utc) : null
        };
    }

    public async Task ReviewAsync(int id, ReviewReceiptInvoiceRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var receipt = await db.StockDocuments.FromSqlInterpolated($"SELECT * FROM [StockDocument] WITH (UPDLOCK,HOLDLOCK) WHERE [Id] = {id} AND [StoreId] = {StoreId}")
            .SingleOrDefaultAsync(x => !x.IsDeleted && x.Type == StockDocumentType.Receipt, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập.");
        if (receipt.Status != StockDocumentStatus.Confirmed)
            throw new BusinessRuleException("Chỉ xác nhận kiểm tra hóa đơn cho phiếu đã ghi sổ.");
        // GetAsync may already have tracked the receipt before acquiring the lock.
        await db.Entry(receipt).ReloadAsync(ct);
        if (receipt.IsDeleted || receipt.Status != StockDocumentStatus.Confirmed || request.RowVersion != Convert.ToBase64String(receipt.RowVersion))
            throw new BusinessRuleException("Phiếu đã thay đổi. Vui lòng tải lại trước khi thao tác.");
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            throw new BusinessRuleException("Nhập ghi chú kiểm tra hóa đơn từ 1 đến 500 ký tự.");
        if (user.UserId is not > 0) throw new BusinessRuleException("Vui lòng đăng nhập lại.");
        var context = await GetAsync(id, ct);
        if (!context.HasLinkedInvoice || context.MapId != request.MapId ||
            string.IsNullOrWhiteSpace(context.EvidenceFingerprint) || context.EvidenceFingerprint != request.EvidenceFingerprint)
            throw new BusinessRuleException("Hóa đơn hoặc số liệu đối chiếu đã thay đổi. Vui lòng tải lại và kiểm tra lại.");
        if (context.State == "Reviewed") { await transaction.CommitAsync(ct); return; }
        if (context.State != "NeedsReview")
            throw new BusinessRuleException("Hóa đơn không còn cần kiểm tra.");
        db.PurchaseReceiptAuditEvents.Add(new()
        {
            StoreId = StoreId, StockDocumentId = id, EventType = PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed,
            ActorUserId = user.UserId.Value, ActorUserName = user.UserName, OccurredAtUtc = DateTime.UtcNow,
            IsSuccess = true, Reason = reason, ChangedFieldsJson = "[\"InvoiceFollowUp\"]",
            OldValuesJson = JsonSerializer.Serialize(new { InvoiceFollowUp = "NeedsReview" }),
            NewValuesJson = JsonSerializer.Serialize(new { InvoiceFollowUp = "Reviewed", request.MapId, request.EvidenceFingerprint })
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task EndWaitingAsync(int id, EndReceiptInvoiceWaitRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var receipt = await db.StockDocuments.FromSqlInterpolated($"SELECT * FROM [StockDocument] WITH (UPDLOCK,HOLDLOCK) WHERE [Id] = {id} AND [StoreId] = {StoreId}")
            .SingleOrDefaultAsync(x => !x.IsDeleted && x.Type == StockDocumentType.Receipt, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập.");
        if (receipt.Status != StockDocumentStatus.Confirmed || receipt.WaitForInputInvoice != true)
            throw new BusinessRuleException("Phiếu không ở trạng thái đã duyệt, chờ hóa đơn.");
        if (request.RowVersion != Convert.ToBase64String(receipt.RowVersion))
            throw new BusinessRuleException("Phiếu đã thay đổi. Vui lòng tải lại trước khi thao tác.");
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            throw new BusinessRuleException("Nhập ghi chú kết thúc chờ hóa đơn từ 1 đến 500 ký tự.");
        if (user.UserId is not > 0) throw new BusinessRuleException("Vui lòng đăng nhập lại.");
        if (await db.Set<StockDocumentInputInvoiceMap>().AnyAsync(x => x.StoreId == StoreId && x.StockDocumentId == id && !x.IsDeleted, ct))
            throw new BusinessRuleException("Phiếu đã có hóa đơn. Vui lòng hoàn tất đối chiếu hóa đơn.");
        receipt.WaitForInputInvoice = false;
        db.PurchaseReceiptAuditEvents.Add(new()
        {
            StoreId = StoreId, StockDocumentId = id, EventType = PurchaseReceiptAuditEventType.InputInvoiceWaitingEnded,
            ActorUserId = user.UserId.Value, ActorUserName = user.UserName, OccurredAtUtc = DateTime.UtcNow,
            IsSuccess = true, Reason = reason, ChangedFieldsJson = "[\"WaitForInputInvoice\"]",
            OldValuesJson = JsonSerializer.Serialize(new { WaitForInputInvoice = true }),
            NewValuesJson = JsonSerializer.Serialize(new { WaitForInputInvoice = false })
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new BusinessRuleException("Phiếu đã thay đổi. Vui lòng tải lại."); }
        await transaction.CommitAsync(ct);
    }
}
