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

public sealed class ReceiptDocumentActionsService(AppDbContext db, ITenantContext tenant, ICurrentUser user)
    : IReceiptDocumentActionsService
{
    private int StoreId => tenant.StoreId is > 0 ? tenant.StoreId.Value
        : throw new BusinessRuleException("Vui lòng chọn cửa hàng.");
    private static bool NeverSubmitted(StockDocument document) => document.Status == StockDocumentStatus.Draft &&
        document.SubmittedAtUtc == null && document.SubmittedByUserId == null &&
        document.ApprovedAtUtc == null && document.ConfirmedAtUtc == null;

    public async Task<ReceiptDocumentActionsDto> GetAsync(int id, CancellationToken ct)
    {
        var document = await LoadAsync(id, false, ct);
        return new(document.Id, document.DocumentNo, document.DocumentTitle, document.WarehouseId, document.ReceiptSource,
            document.Status, Convert.ToBase64String(document.RowVersion), NeverSubmitted(document), await ProposalAsync(id, ct));
    }

    public async Task RenameAsync(int id, ReceiptTitleRequest request, bool manager, CancellationToken ct)
    {
        var document = await LoadAsync(id, true, ct);
        Version(document, request.RowVersion);
        if (!manager && !NeverSubmitted(document))
            throw new BusinessRuleException("Phiếu đã gửi duyệt. Vui lòng gửi yêu cầu đổi tên cho quản lý.");
        var title = Title(request.Title);
        var proposal = await ProposalAsync(id, ct);
        AddEvent(document, PurchaseReceiptAuditEventType.ReceiptTitleChanged, document.DocumentTitle, title,
            proposal?.Id, "Đổi tên phiếu nhập");
        document.DocumentTitle = title;
        await SaveAsync(document, ct);
    }

    public async Task RequestTitleAsync(int id, ReceiptTitleRequest request, CancellationToken ct)
    {
        var document = await LoadAsync(id, true, ct);
        Version(document, request.RowVersion);
        if (document.Status != StockDocumentStatus.PendingApproval)
            throw new BusinessRuleException("Chỉ phiếu đang chờ duyệt mới được gửi yêu cầu đổi tên.");
        if (await ProposalAsync(id, ct) != null)
            throw new BusinessRuleException("Đã có yêu cầu đổi tên đang chờ quản lý xử lý.");
        var title = Title(request.Title);
        if (title == document.DocumentTitle) throw new BusinessRuleException("Tên mới đang trùng với tên hiện tại.");
        AddEvent(document, PurchaseReceiptAuditEventType.ReceiptTitleChangeRequested, document.DocumentTitle, title,
            null, "Yêu cầu đổi tên phiếu nhập");
        await SaveAsync(document, ct);
    }

    public async Task ReviewTitleAsync(int id, ReviewReceiptTitleRequest request, CancellationToken ct)
    {
        var document = await LoadAsync(id, true, ct);
        Version(document, request.RowVersion);
        var proposal = await ProposalAsync(id, ct);
        if (proposal == null || proposal.Id != request.RequestId)
            throw new BusinessRuleException("Yêu cầu đổi tên đã được xử lý hoặc thay đổi. Vui lòng tải lại.");
        AddEvent(document, request.Approve ? PurchaseReceiptAuditEventType.ReceiptTitleChangeApproved :
            PurchaseReceiptAuditEventType.ReceiptTitleChangeDeclined, document.DocumentTitle, proposal.Title,
            proposal.Id, request.Approve ? "Duyệt đổi tên phiếu nhập" : "Từ chối đổi tên phiếu nhập");
        if (request.Approve) document.DocumentTitle = proposal.Title;
        await SaveAsync(document, ct);
    }

    public async Task DeleteDraftAsync(int id, ReceiptVersionRequest request, CancellationToken ct)
    {
        var document = await LoadAsync(id, true, ct);
        Version(document, request.RowVersion);
        if (!NeverSubmitted(document)) throw new BusinessRuleException("Chỉ được xóa phiếu nháp chưa từng gửi duyệt.");
        if (document.ReceivingLeaseExpiresAtUtc > DateTime.UtcNow && document.ReceivingOwnerUserId != user.UserId)
            throw new BusinessRuleException("Nhân viên khác đang nhập phiếu này. Vui lòng chờ kết thúc phiên nhập.");
        AddEvent(document, PurchaseReceiptAuditEventType.ReceiptDraftDeleted, document.DocumentTitle,
            document.DocumentTitle, null, "Xóa phiếu nhập nháp chưa gửi duyệt");
        // Existing DbContext soft-delete preserves the receipt and its business audit evidence.
        db.StockDocuments.Remove(document);
        await SaveAsync(null, ct);
    }

    private async Task<StockDocument> LoadAsync(int id, bool tracked, CancellationToken ct)
    {
        var query = db.StockDocuments.Where(x => x.Id == id && x.StoreId == StoreId &&
            !x.IsDeleted && x.Type == StockDocumentType.Receipt && x.Status != StockDocumentStatus.Cancelled);
        return await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập hoặc phiếu đã bị xóa/hủy.");
    }

    private async Task<ReceiptTitleProposalDto?> ProposalAsync(int id, CancellationToken ct)
    {
        var latest = await db.PurchaseReceiptAuditEvents.AsNoTracking().Where(x => x.StoreId == StoreId &&
            x.StockDocumentId == id && x.IsSuccess && (x.EventType == PurchaseReceiptAuditEventType.ReceiptTitleChanged ||
            x.EventType == PurchaseReceiptAuditEventType.ReceiptTitleChangeRequested ||
            x.EventType == PurchaseReceiptAuditEventType.ReceiptTitleChangeApproved ||
            x.EventType == PurchaseReceiptAuditEventType.ReceiptTitleChangeDeclined))
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (latest?.EventType != PurchaseReceiptAuditEventType.ReceiptTitleChangeRequested) return null;
        using var values = JsonDocument.Parse(latest.NewValuesJson);
        return new(latest.Id, values.RootElement.GetProperty("DocumentTitle").GetString()!, latest.ActorUserName, latest.OccurredAtUtc);
    }

    private void AddEvent(StockDocument document, PurchaseReceiptAuditEventType type, string? before,
        string? title, long? requestId, string reason)
    {
        if (user.UserId is not > 0) throw new BusinessRuleException("Vui lòng đăng nhập lại.");
        db.PurchaseReceiptAuditEvents.Add(new()
        {
            StoreId = StoreId, StockDocumentId = document.Id, EventType = type,
            ActorUserId = user.UserId.Value, ActorUserName = user.UserName, OccurredAtUtc = DateTime.UtcNow,
            IsSuccess = true, Reason = reason,
            ChangedFieldsJson = type == PurchaseReceiptAuditEventType.ReceiptDraftDeleted ? "[\"IsDeleted\"]" : "[\"DocumentTitle\"]",
            OldValuesJson = type == PurchaseReceiptAuditEventType.ReceiptDraftDeleted
                ? JsonSerializer.Serialize(new { IsDeleted = false }) : JsonSerializer.Serialize(new { DocumentTitle = before }),
            NewValuesJson = type == PurchaseReceiptAuditEventType.ReceiptDraftDeleted
                ? JsonSerializer.Serialize(new { IsDeleted = true }) : JsonSerializer.Serialize(new { DocumentTitle = title, RequestId = requestId })
        });
    }

    private async Task SaveAsync(StockDocument? document, CancellationToken ct)
    {
        // Even a request/decline advances the receipt version, serializing competing decisions.
        if (document != null) db.Entry(document).Property(x => x.UpdatedAtUtc).IsModified = true;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new BusinessRuleException("Phiếu đã thay đổi ở phiên khác. Vui lòng tải lại trước khi thao tác."); }
    }
    private static void Version(StockDocument document, string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version != Convert.ToBase64String(document.RowVersion))
            throw new BusinessRuleException("Phiếu đã thay đổi ở phiên khác. Vui lòng tải lại trước khi thao tác.");
    }
    private static string Title(string value)
    {
        var title = value?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 255)
            throw new BusinessRuleException("Nhập tên phiếu từ 1 đến 255 ký tự.");
        return title;
    }
}
