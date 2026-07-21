using GaoApp.Application.DTOs.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;


namespace GaoApp.Application.Interfaces.Services.Orders;

public interface IOrderInventoryIssueService
{
    Task<OrderInventoryIssue?> GetDetailAsync(int issueId, CancellationToken ct = default);

    Task<List<OrderInventoryIssue>> GetListAsync(
        InventoryResolutionStatus? status = null,
        bool? onlyOverdue = null,
        CancellationToken ct = default);

    Task AddNoteAsync(int issueId, string note, int? actorUserId, CancellationToken ct = default);

    Task AppendNegativeDetectedAsync(
        int issueId,
        int? actorUserId,
        string? note = null,
        CancellationToken ct = default);

    Task<InventoryIssueApprovalCheckResultDto> ValidateBeforeApproveAsync(
        int issueId,
        CancellationToken ct = default);

 

    Task ApproveAsync(
        int issueId,
        int approvedByUserId,
        string? note = null,
        CancellationToken ct = default);

    Task RejectAsync(
        int issueId,
        int rejectedByUserId,
        string note,
        CancellationToken ct = default);

    Task LinkReceiptAsync(
        int issueId,
        int issueLineId,
        int receiptId,
        int actorUserId,
        string? note = null,
        CancellationToken ct = default);

    Task LinkAdjustmentAsync(
        int issueId,
        int issueLineId,
        int adjustmentId,
        int actorUserId,
        string? note = null,
        CancellationToken ct = default);

    Task ReopenAsync(
        int issueId,
        int actorUserId,
        string note,
        int? issueLineId = null,
        CancellationToken ct = default);

    Task EscalateAsync(
        int issueId,
        int actorUserId,
        string note,
        int? issueLineId = null,
        CancellationToken ct = default);

    Task UpdateOverdueFlagAsync(int issueId, CancellationToken ct = default);

    Task<int> UpdateOverdueFlagsAsync(CancellationToken ct = default);
    Task RefreshResolutionStateAsync(int issueId, CancellationToken ct = default);
    Task<List<InventoryIssueDocumentOptionDto>> GetReceiptOptionsForIssueLineAsync(
    int issueLineId,
    CancellationToken ct = default);

    Task<List<InventoryIssueDocumentOptionDto>> GetAdjustmentOptionsForIssueLineAsync(
        int issueLineId,
        CancellationToken ct = default);
    Task RefreshAutoResolutionAsync(int issueId, CancellationToken ct = default);
    Task RefreshIssueAsync(int issueId, CancellationToken ct = default);

    Task<InventoryIssueBatchRefreshResultDto> RefreshOpenIssuesAsync(CancellationToken ct = default);

}