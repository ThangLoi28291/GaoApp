using GaoApp.Application.DTOs.Orders;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Orders;

/// <summary>
/// Workflow case tồn âm mức 2:
/// - vẫn giữ timeline append-only
/// - không rewrite POS finalize / costing / inventory movement cũ
/// - approve chỉ được khi tất cả line đã resolve đúng điều kiện
/// 
/// Bản này bổ sung chuẩn hóa overdue:
/// - overdue khi case còn mở và nowUtc > DueAtUtc
/// - Rejected / Approved không còn bị xem là overdue
/// - dùng OrderInventoryIssue.RefreshOverdueState(...)
/// - có sẵn hook để bước sau tích hợp Notification + SignalR
/// </summary>
public sealed class OrderInventoryIssueService : IOrderInventoryIssueService
{
    private readonly IOrderInventoryIssueRepository _issueRepository;
    private readonly IOrderRepository _orderRepository;

    public OrderInventoryIssueService(
        IOrderInventoryIssueRepository issueRepository,
        IOrderRepository orderRepository)
    {
        _issueRepository = issueRepository;
        _orderRepository = orderRepository;
    }

    public Task<OrderInventoryIssue?> GetDetailAsync(int issueId, CancellationToken ct = default)
        => _issueRepository.GetDetailByIdAsync(issueId, ct);

    public Task<List<OrderInventoryIssue>> GetListAsync(
        InventoryResolutionStatus? status = null,
        bool? onlyOverdue = null,
        CancellationToken ct = default)
        => _issueRepository.GetListAsync(status, onlyOverdue, ct);

    public async Task AddNoteAsync(
        int issueId,
        string note,
        int? actorUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("Ghi chú không được để trống.", nameof(note));

        var issue = await RequireIssueDetailAsync(issueId, ct);

        await AddActionInternalAsync(
            issue,
            InventoryIssueActionType.NoteAdded,
            actorUserId,
            note.Trim(),
            InventoryIssueReferenceType.None,
            null,
            null,
            ct);

        await _issueRepository.SaveChangesAsync(ct);
    }

    public async Task AppendNegativeDetectedAsync(
        int issueId,
        int? actorUserId,
        string? note = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var issue = await RequireIssueDetailAsync(issueId, ct);

        var wasClosed =
            issue.Status == InventoryResolutionStatus.Approved ||
            issue.Status == InventoryResolutionStatus.Rejected;

        if (wasClosed)
        {
            issue.Status = InventoryResolutionStatus.PendingResolution;
            issue.ApprovedAtUtc = null;
            issue.ApprovedByUserId = null;
            issue.RejectedAtUtc = null;
            issue.RejectedByUserId = null;
            issue.ReadyForApprovalAtUtc = null;

            // Mở lại line unresolved
            foreach (var line in issue.Lines)
            {
                line.IsResolved = false;
                line.ResolvedAtUtc = null;
            }

            RefreshOverdueAndSeverity(issue, now);

            _issueRepository.Update(issue);

            await AddActionInternalAsync(
                issue,
                InventoryIssueActionType.Reopened,
                actorUserId,
                "Case được mở lại do phát sinh inventory issue mới.",
                InventoryIssueReferenceType.Order,
                issue.OrderId,
                null,
                ct);
        }

        await AddActionInternalAsync(
      issue,
      InventoryIssueActionType.NegativeDetected,
      actorUserId,
      BuildNegativeDetectedSummary(issue, note),
      InventoryIssueReferenceType.Order,
      issue.OrderId,
      null,
      ct);

        await RefreshIssueLineResolutionAsync(issue, now, ct);

        _issueRepository.Update(issue);

        await SyncOrderMirrorAsync(issue, ct);
        await _issueRepository.SaveChangesAsync(ct);
        await _orderRepository.SaveChangesAsync(ct);
    }

    public async Task<InventoryIssueApprovalCheckResultDto> ValidateBeforeApproveAsync(
        int issueId,
        CancellationToken ct = default)
    {
        var issue = await RequireIssueDetailAsync(issueId, ct);

        var result = new InventoryIssueApprovalCheckResultDto
        {
            IssueId = issue.Id,
            IssueCode = issue.Code ?? $"ISSUE-{issue.Id}"
        };

        var activeLines = issue.Lines
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Id)
            .ToList();

        foreach (var line in activeLines)
        {
            result.Lines.Add(EvaluateLine(line));
        }

        result.TotalLines = result.Lines.Count;
        result.ResolvedLines = result.Lines.Count(x => x.IsResolved);
        result.UnresolvedLines = result.Lines.Count(x => !x.IsResolved);
        result.CanApprove = result.TotalLines > 0 && result.UnresolvedLines == 0;

        return result;
    }

    public async Task ApproveAsync(
        int issueId,
        int approvedByUserId,
        string? note = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var issue = await RequireIssueDetailAsync(issueId, ct);

        if (issue.Status == InventoryResolutionStatus.Approved)
            return;

        if (issue.Status != InventoryResolutionStatus.ReadyForApproval)
            throw new InvalidOperationException("Chỉ được approve case đang ở trạng thái ReadyForApproval.");

        var check = await ValidateBeforeApproveAsync(issueId, ct);
        ApplyResolutionResultToEntity(issue, check, now);

        if (!check.CanApprove)
            throw new InvalidOperationException(check.ToUserMessage());

        issue.Status = InventoryResolutionStatus.Approved;
        issue.ApprovedAtUtc = now;
        issue.ApprovedByUserId = approvedByUserId;

        // Khi approve xong thì case không còn open => overdue phải tự clear.
        RefreshOverdueAndSeverity(issue, now);

        _issueRepository.Update(issue);

        await SyncOrderMirrorAsync(issue, ct);

        await AddActionInternalAsync(
            issue,
            InventoryIssueActionType.Approved,
            approvedByUserId,
            string.IsNullOrWhiteSpace(note)
                ? "Quản lý đã approve case sau khi tất cả issue line đạt điều kiện resolve."
                : note.Trim(),
            InventoryIssueReferenceType.None,
            null,
            null,
            ct);

        await _issueRepository.SaveChangesAsync(ct);
        await _orderRepository.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(
        int issueId,
        int rejectedByUserId,
        string note,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("Reject phải có lý do/ghi chú.", nameof(note));

        var now = DateTime.UtcNow;
        var issue = await RequireIssueDetailAsync(issueId, ct);

        if (issue.Status == InventoryResolutionStatus.Approved)
            throw new InvalidOperationException("Case đã approved, không thể reject.");

        issue.Status = InventoryResolutionStatus.Rejected;
        issue.RejectedAtUtc = now;
        issue.RejectedByUserId = rejectedByUserId;

        // Rejected cũng là closed case => không được còn overdue.
        RefreshOverdueAndSeverity(issue, now);

        _issueRepository.Update(issue);
        await SyncOrderMirrorAsync(issue, ct);

        await AddActionInternalAsync(
            issue,
            InventoryIssueActionType.Rejected,
            rejectedByUserId,
            note.Trim(),
            InventoryIssueReferenceType.None,
            null,
            null,
            ct);

        await _issueRepository.SaveChangesAsync(ct);
        await _orderRepository.SaveChangesAsync(ct);
    }

    public async Task LinkReceiptAsync(
        int issueId,
        int issueLineId,
        int receiptId,
        int actorUserId,
        string? note = null,
        CancellationToken ct = default)
    {
        var issue = await RequireIssueDetailAsync(issueId, ct);
        var line = RequireLine(issue, issueLineId);

        await AddActionInternalAsync(
            issue,
            InventoryIssueActionType.LinkedReceipt,
            actorUserId,
            string.IsNullOrWhiteSpace(note)
                ? $"Liên kết chứng từ nhập kho #{receiptId} cho issue line #{issueLineId}."
                : note.Trim(),
            InventoryIssueReferenceType.GoodsReceipt,
            receiptId,
            line.Id,
            ct);

        RefreshSingleLineResolution(issue, line, DateTime.UtcNow);

        _issueRepository.Update(issue);
        await SyncOrderMirrorAsync(issue, ct);
        await _issueRepository.SaveChangesAsync(ct);
        await _orderRepository.SaveChangesAsync(ct);
    }

    public async Task LinkAdjustmentAsync(
        int issueId,
        int issueLineId,
        int adjustmentId,
        int actorUserId,
        string? note = null,
        CancellationToken ct = default)
    {
        var issue = await RequireIssueDetailAsync(issueId, ct);
        var line = RequireLine(issue, issueLineId);

        await AddActionInternalAsync(
            issue,
            InventoryIssueActionType.LinkedAdjustment,
            actorUserId,
            string.IsNullOrWhiteSpace(note)
                ? $"Liên kết phiếu điều chỉnh #{adjustmentId} cho issue line #{issueLineId}."
                : note.Trim(),
            InventoryIssueReferenceType.InventoryAdjustment,
            adjustmentId,
            line.Id,
            ct);

        RefreshSingleLineResolution(issue, line, DateTime.UtcNow);

        _issueRepository.Update(issue);
        await SyncOrderMirrorAsync(issue, ct);
        await _issueRepository.SaveChangesAsync(ct);
        await _orderRepository.SaveChangesAsync(ct);
    }

    public async Task ReopenAsync(
        int issueId,
        int actorUserId,
        string note,
        int? issueLineId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("Reopen phải có ghi chú.", nameof(note));

        var now = DateTime.UtcNow;
        var issue = await RequireIssueDetailAsync(issueId, ct);

        issue.Status = InventoryResolutionStatus.PendingResolution;
        issue.ApprovedAtUtc = null;
        issue.ApprovedByUserId = null;
        issue.RejectedAtUtc = null;
        issue.RejectedByUserId = null;
        issue.ReadyForApprovalAtUtc = null;

        if (issueLineId.HasValue)
        {
            var line = RequireLine(issue, issueLineId.Value);
            line.IsResolved = false;
            line.ResolvedAtUtc = null;
        }
        else
        {
            foreach (var line in issue.Lines)
            {
                line.IsResolved = false;
                line.ResolvedAtUtc = null;
            }
        }

        RefreshOverdueAndSeverity(issue, now);

        _issueRepository.Update(issue);
        await SyncOrderMirrorAsync(issue, ct);

        await AddActionInternalAsync(
            issue,
            InventoryIssueActionType.Reopened,
            actorUserId,
            note.Trim(),
            InventoryIssueReferenceType.Order,
            issue.OrderId,
            issueLineId,
            ct);

        await _issueRepository.SaveChangesAsync(ct);
        await _orderRepository.SaveChangesAsync(ct);
    }

    public async Task EscalateAsync(
        int issueId,
        int actorUserId,
        string note,
        int? issueLineId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("Escalate phải có ghi chú.", nameof(note));

        var now = DateTime.UtcNow;
        var issue = await RequireIssueDetailAsync(issueId, ct);

        RefreshOverdueAndSeverity(issue, now);

        // Nếu chưa tới mức Warning/Overdue/Critical thì tăng nhẹ lên Warning
        if (issue.Severity == InventoryIssueSeverity.Normal)
            issue.Severity = InventoryIssueSeverity.Warning;

        _issueRepository.Update(issue);
        await SyncOrderMirrorAsync(issue, ct);

        await AddActionInternalAsync(
            issue,
            InventoryIssueActionType.Escalated,
            actorUserId,
            note.Trim(),
            InventoryIssueReferenceType.Order,
            issue.OrderId,
            issueLineId,
            ct);

        await _issueRepository.SaveChangesAsync(ct);
        await _orderRepository.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Refresh overdue cho 1 case.
    /// Dùng khi vào detail hoặc sau các action nghiệp vụ.
    /// 
    /// Lưu ý:
    /// - Chỉ update nếu state thật sự thay đổi
    /// - Hook notification overdue sẽ gắn ở bước kế tiếp
    /// </summary>
    public async Task UpdateOverdueFlagAsync(int issueId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var issue = await RequireIssueDetailAsync(issueId, ct);

        var changed = RefreshOverdueAndSeverity(issue, now);

        if (!changed)
            return;

        _issueRepository.Update(issue);
        await SyncOrderMirrorAsync(issue, ct);

        // TODO bước kế tiếp:
        // nếu issue vừa chuyển sang overdue thì:
        // - tạo Notification
        // - push SignalR tới admin
        // - issue.MarkOverdueNotified(now)

        await _issueRepository.SaveChangesAsync(ct);
        await _orderRepository.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Refresh overdue hàng loạt cho các case còn mở.
    /// Đây là service check chính cho badge/UI/dashboard.
    /// 
    /// Hiện tại:
    /// - chỉ cập nhật overdue + severity
    /// - chưa bắn notification ở bước này vì chưa inject notification service
    /// 
    /// Bước sau sẽ cắm thêm:
    /// - Notifications
    /// - SignalR
    /// - chống spam theo LastOverdueNotifiedAtUtc
    /// </summary>
    public async Task<int> UpdateOverdueFlagsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var issues = await _issueRepository.GetPendingOrReadyIssuesAsync(ct);

        var changed = 0;

        foreach (var head in issues)
        {
            var issue = await RequireIssueDetailAsync(head.Id, ct);

            var stateChanged = RefreshOverdueAndSeverity(issue, now);
            if (!stateChanged)
                continue;

            _issueRepository.Update(issue);
            await SyncOrderMirrorAsync(issue, ct);

            // TODO bước kế tiếp:
            // if (issue.IsOverdue && issue.LastOverdueNotifiedAtUtc == null)
            // {
            //     create notification + push signalr
            //     issue.MarkOverdueNotified(now);
            // }

            changed++;
        }

        if (changed > 0)
        {
            await _issueRepository.SaveChangesAsync(ct);
            await _orderRepository.SaveChangesAsync(ct);
        }

        return changed;
    }

    private async Task<OrderInventoryIssue> RequireIssueDetailAsync(
        int issueId,
        CancellationToken ct = default)
    {
        var issue = await _issueRepository.GetDetailByIdAsync(issueId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy hồ sơ pending inventory issue.");

        issue.Lines = issue.Lines
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Id)
            .ToList();

        issue.Actions = issue.Actions
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.ActionAtUtc)
            .ThenBy(x => x.Id)
            .ToList();

        return issue;
    }

    private static OrderInventoryIssueLine RequireLine(
        OrderInventoryIssue issue,
        int issueLineId)
    {
        var line = issue.Lines.FirstOrDefault(x => x.Id == issueLineId && !x.IsDeleted);

        if (line is null)
            throw new InvalidOperationException("Dòng issue không tồn tại hoặc đã bị thay thế.");

        return line;
    }

    private async Task RefreshIssueLineResolutionAsync(
        OrderInventoryIssue issue,
        DateTime now,
        CancellationToken ct)
    {
        var check = await ValidateBeforeApproveAsync(issue.Id, ct);
        ApplyResolutionResultToEntity(issue, check, now);

        RefreshOverdueAndSeverity(issue, now);
    }

    private static void RefreshSingleLineResolution(
        OrderInventoryIssue issue,
        OrderInventoryIssueLine line,
        DateTime now)
    {
        var evaluation = EvaluateLine(line);

        line.RevaluationAmount = evaluation.RevaluationAmount;
        line.AutoDetectedRevaluationAmount =
            evaluation.RevaluationAmount;

        line.AutoDetectedDocumentResolved =
            evaluation.DocumentResolved;

        line.AutoDetectedCostResolved =
            evaluation.CostResolved;

        line.IsResolved = evaluation.IsResolved;
        line.ResolvedAtUtc = evaluation.IsResolved
            ? now
            : null;

        line.LastAutoResolvedAtUtc = now;

        line.AutoResolveNote = evaluation.IsResolved
            ? $"Đã auto-resolve line. " +
              $"Inbound={line.AutoDetectedInboundQty:N2}/" +
              $"{line.NegativeQty:N2}, " +
              $"Revaluation=" +
              $"{(evaluation.RevaluationAmount?.ToString("N0") ?? "0")}."
            : string.Join(" | ", evaluation.Reasons);

        RefreshOverdueAndSeverity(issue, now);
    }

    private static void ApplyResolutionResultToEntity(
        OrderInventoryIssue issue,
        InventoryIssueApprovalCheckResultDto result,
        DateTime now)
    {
        var activeLines = issue.Lines
            .Where(x => !x.IsDeleted)
            .ToDictionary(x => x.Id);

        foreach (var dto in result.Lines)
        {
            if (!activeLines.TryGetValue(
                dto.IssueLineId,
                out var line))
            {
                continue;
            }

            // Mirror snapshot để DB và UI đồng bộ.
            line.RevaluationAmount =
                dto.RevaluationAmount;

            line.AutoDetectedRevaluationAmount =
                dto.RevaluationAmount;

            line.AutoDetectedDocumentResolved =
                dto.DocumentResolved;

            line.AutoDetectedCostResolved =
                dto.CostResolved;

            // AutoDetectedInboundQty đã được auto engine tính.
            // Không tính lại tại đây để tránh lệch allocation.
            line.IsResolved = dto.IsResolved;

            line.ResolvedAtUtc = dto.IsResolved
                ? now
                : null;

            line.LastAutoResolvedAtUtc ??= now;

            if (dto.IsResolved)
            {
                line.AutoResolveNote =
                    $"Đã auto-resolve line. " +
                    $"Inbound={line.AutoDetectedInboundQty:N2}/" +
                    $"{line.NegativeQty:N2}, " +
                    $"Revaluation=" +
                    $"{(dto.RevaluationAmount?.ToString("N0") ?? "0")}.";
            }
            else
            {
                line.AutoResolveNote =
                    string.Join(" | ", dto.Reasons);
            }
        }

        var resolvedLines = issue.Lines.Count(
            x => !x.IsDeleted && x.IsResolved);

        issue.AutoResolvedLineCount = resolvedLines;
        issue.LastAutoResolvedAtUtc = now;

        RefreshOverdueAndSeverity(issue, now);
    }

    private static InventoryIssueLineResolutionDto EvaluateLine(
      OrderInventoryIssueLine line)
    {
        var dto = new InventoryIssueLineResolutionDto
        {
            IssueLineId = line.Id,
            OrderLineId = line.OrderLineId,
            ProductId = line.ProductId,
            ProductVariantId = line.ProductVariantId,
            ProductName =
                $"ProductVariant #{line.ProductVariantId}",

            OrderedQty = line.OrderedQty,
            StockBefore = line.StockBefore,
            StockAfter = line.StockAfter,
            NegativeQty = line.NegativeQty,

            ProvisionalUnitCost =
                line.ProvisionalUnitCost,

            ProvisionalCostAmount =
                line.ProvisionalCostAmount,

            // Dùng snapshot từ auto engine.
            RevaluationAmount =
                line.AutoDetectedRevaluationAmount,

            CurrentAvailableQty =
                line.AutoDetectedInboundQty,

            HasLinkedReceipt =
                line.AutoDetectedDocumentResolved,

            HasLinkedAdjustment = false,

            QuantityResolved =
                line.AutoDetectedInboundQty >=
                line.NegativeQty,

            DocumentResolved =
                line.AutoDetectedDocumentResolved,

            CostResolved =
                line.AutoDetectedCostResolved
        };

        if (!dto.QuantityResolved)
        {
            dto.Reasons.Add(
                $"thiếu số lượng " +
                $"(auto inbound: " +
                $"{line.AutoDetectedInboundQty:N2}, " +
                $"cần: {line.NegativeQty:N2})");
        }

        if (!dto.DocumentResolved)
        {
            dto.Reasons.Add(
                "chưa có inbound hợp lệ");
        }

        if (!dto.CostResolved)
        {
            dto.Reasons.Add(
                "chưa finalize cost");
        }

        dto.IsResolved =
            dto.QuantityResolved &&
            dto.DocumentResolved &&
            dto.CostResolved;

        return dto;
    }

    /// <summary>
    /// Helper append timeline append-only.
    /// Không sửa action cũ.
    /// </summary>
    private async Task AddActionInternalAsync(
        OrderInventoryIssue issue,
        InventoryIssueActionType actionType,
        int? actorUserId,
        string? note,
        InventoryIssueReferenceType referenceType,
        int? referenceId,
        int? issueLineId,
        CancellationToken ct)
    {
        var action = new OrderInventoryIssueAction
        {
            StoreId = issue.StoreId,
            OrderInventoryIssueId = issue.Id,
            OrderInventoryIssueLineId = issueLineId,
            ActionType = actionType,
            ActorUserId = actorUserId,
            ActionAtUtc = DateTime.UtcNow,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Note = NormalizeActionNote(note)
        };

        await _issueRepository.AddActionAsync(action, ct);
        issue.Actions.Add(action);
    }

    /// <summary>
    /// Refresh lại overdue + severity cho issue.
    /// Trả về true nếu có bất kỳ thay đổi state nào.
    /// 
    /// Quan trọng:
    /// - overdue dùng helper trong entity
    /// - Approved / Rejected không còn bị tính overdue
    /// - severity được tính lại đồng bộ với overdue
    /// </summary>
    private static bool RefreshOverdueAndSeverity(OrderInventoryIssue issue, DateTime nowUtc)
    {
        var oldOverdue = issue.IsOverdue;
        var oldOverdueSinceUtc = issue.OverdueSinceUtc;
        var oldSeverity = issue.Severity;

        issue.RefreshOverdueState(nowUtc);
        issue.Severity = CalculateSeverity(issue, nowUtc);

        return oldOverdue != issue.IsOverdue
            || oldOverdueSinceUtc != issue.OverdueSinceUtc
            || oldSeverity != issue.Severity;
    }

    private static InventoryIssueSeverity CalculateSeverity(OrderInventoryIssue issue, DateTime nowUtc)
    {
        // Case đã đóng thì luôn quay về trạng thái bình thường
        if (!issue.IsOpen())
            return InventoryIssueSeverity.Normal;

        // Overdue > 24h kể từ DueAtUtc => Critical
        if (issue.IsOverdue)
        {
            if (issue.DueAtUtc.AddHours(24) < nowUtc)
                return InventoryIssueSeverity.Critical;

            return InventoryIssueSeverity.Overdue;
        }

        // Chưa overdue nhưng đã dùng nhiều thời gian xử lý => Warning
        var totalHours = (issue.DueAtUtc - issue.OpenedAtUtc).TotalHours;
        var usedHours = (nowUtc - issue.OpenedAtUtc).TotalHours;

        if (totalHours > 0 && usedHours / totalHours >= 0.7d)
            return InventoryIssueSeverity.Warning;

        return InventoryIssueSeverity.Normal;
    }

    private async Task SyncOrderMirrorAsync(OrderInventoryIssue issue, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(issue.OrderId, ct);
        if (order == null)
            throw new InvalidOperationException($"Không tìm thấy Order #{issue.OrderId}.");

        order.HasInventoryIssue = issue.Status != InventoryResolutionStatus.None;
        order.InventoryResolutionStatus = issue.Status;
        order.InventoryIssueOpenedAtUtc ??= issue.OpenedAtUtc;
        order.InventoryIssueApprovedAtUtc =
            issue.Status == InventoryResolutionStatus.Approved
                ? issue.ApprovedAtUtc
                : null;

        _orderRepository.Update(order);
    }

    private static bool HasProvisionalCost(OrderInventoryIssueLine line)
    {
        return line.ProvisionalUnitCost.HasValue || line.ProvisionalCostAmount.HasValue;
    }

    public async Task RefreshResolutionStateAsync(int issueId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var issue = await RequireIssueDetailAsync(issueId, ct);

        var check = await ValidateBeforeApproveAsync(issueId, ct);
        ApplyResolutionResultToEntity(issue, check, now);

        _issueRepository.Update(issue);
        await SyncOrderMirrorAsync(issue, ct);

        await _issueRepository.SaveChangesAsync(ct);
        await _orderRepository.SaveChangesAsync(ct);
    }

    public Task<List<InventoryIssueDocumentOptionDto>> GetReceiptOptionsForIssueLineAsync(
        int issueLineId,
        CancellationToken ct = default)
        => _issueRepository.GetReceiptOptionsForIssueLineAsync(issueLineId, ct);

    public Task<List<InventoryIssueDocumentOptionDto>> GetAdjustmentOptionsForIssueLineAsync(
        int issueLineId,
        CancellationToken ct = default)
        => _issueRepository.GetAdjustmentOptionsForIssueLineAsync(issueLineId, ct);

    public async Task RefreshAutoResolutionAsync(int issueId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var issue = await RequireIssueDetailAsync(issueId, ct);

        if (issue.Status == InventoryResolutionStatus.Approved ||
            issue.Status == InventoryResolutionStatus.Rejected)
        {
            return;
        }

        var inboundCandidates = await _issueRepository.GetInboundCandidatesForIssueAsync(issueId, ct);

        // Load allocation hiện tại
        var existingAllocations = await _issueRepository.GetAllocationsByIssueAsync(issueId, ct);

        // QUAN TRỌNG:
        // RefreshAutoResolutionAsync đang rebuild allocation từ đầu.
        // Vì phía dưới dùng ReplaceAllocationsAsync(issue.Id, newAllocations),
        // nên KHÔNG được trừ allocation cũ đang active.
        var layerStates = inboundCandidates
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.InventoryTransactionId)
            .Select(x => new LayerState
            {
                InventoryTransactionId = x.InventoryTransactionId,
                WarehouseId = x.WarehouseId,
                ProductVariantId = x.ProductVariantId,
                SourceReferenceType = x.SourceReferenceType,
                SourceReferenceId = x.SourceReferenceId,
                SourceReferenceLineId = x.SourceReferenceLineId,
                RemainingQty = x.QuantityChange,
                OccurredAtUtc = x.OccurredAtUtc,
                Note = x.Note
            })
            .Where(x => x.RemainingQty > 0)
            .ToList();

        var newAllocations = new List<OrderInventoryIssueLineAllocation>();

        foreach (var line in issue.Lines
                     .Where(x => !x.IsDeleted)
                     .OrderBy(x => x.Id))
        {
            var costSnapshot = await _issueRepository.GetCostResolutionSnapshotForOrderLineAsync(
                line.OrderId,
                line.OrderLineId,
                ct);

            var needQty = line.NegativeQty;
            var allocatedQty = 0m;

            var matchedLayers = layerStates
                .Where(x =>
                    x.ProductVariantId == line.ProductVariantId &&
                    x.RemainingQty > 0 &&
                    x.OccurredAtUtc >= issue.OpenedAtUtc)
                .OrderBy(x => x.OccurredAtUtc)
                .ThenBy(x => x.InventoryTransactionId)
                .ToList();

            foreach (var layer in matchedLayers)
            {
                if (needQty <= 0) break;

                var allocateQty = Math.Min(needQty, layer.RemainingQty);
                if (allocateQty <= 0) continue;

                newAllocations.Add(new OrderInventoryIssueLineAllocation
                {
                    StoreId = issue.StoreId,
                    OrderInventoryIssueId = issue.Id,
                    OrderInventoryIssueLineId = line.Id,
                    SourceReferenceType = layer.SourceReferenceType,
                    SourceReferenceId = layer.SourceReferenceId,
                    SourceReferenceLineId = layer.SourceReferenceLineId,
                    InventoryTransactionId = layer.InventoryTransactionId,
                    AllocatedQuantity = allocateQty,
                    Note = "AUTO FIFO from layer"
                });

                layer.RemainingQty -= allocateQty;
                needQty -= allocateQty;
                allocatedQty += allocateQty;
            }

            var hasProvisionalCost = HasProvisionalCost(line);
            var quantityResolved = allocatedQty >= line.NegativeQty;
            var documentResolved = allocatedQty > 0;
            var costResolved = !hasProvisionalCost || costSnapshot.HasRevaluationEntry;

            line.AutoDetectedInboundQty = allocatedQty;
            line.AutoDetectedDocumentResolved = documentResolved;
            line.AutoDetectedCostResolved = costResolved;
            line.AutoDetectedRevaluationAmount = costSnapshot.HasRevaluationEntry
                ? costSnapshot.RevaluationAmount
                : null;
            line.RevaluationAmount = line.AutoDetectedRevaluationAmount;
            line.LastAutoResolvedAtUtc = now;

            var notes = new List<string>();

            if (allocatedQty > 0)
                notes.Add($"FIFO allocate {allocatedQty:N2}/{line.NegativeQty:N2}");
            else
                notes.Add("Không có inbound layer phù hợp");

            if (costResolved)
                notes.Add("Cost đã finalize");
            else if (hasProvisionalCost)
                notes.Add("Chưa revaluation");

            line.AutoResolveNote = string.Join(" | ", notes);

            line.IsResolved = quantityResolved && documentResolved && costResolved;
            line.ResolvedAtUtc = line.IsResolved ? now : null;
        }

        await _issueRepository.ReplaceAllocationsAsync(issue.Id, newAllocations, ct);

        var totalLines = issue.Lines.Count(x => !x.IsDeleted);
        var resolvedLines = issue.Lines.Count(x => !x.IsDeleted && x.IsResolved);

        issue.AutoResolvedLineCount = resolvedLines;
        issue.LastAutoResolvedAtUtc = now;

        if (totalLines > 0 && resolvedLines == totalLines)
        {
            issue.Status = InventoryResolutionStatus.ReadyForApproval;
            issue.ReadyForApprovalAtUtc ??= now;
        }
        else
        {
            issue.Status = InventoryResolutionStatus.PendingResolution;
            issue.ReadyForApprovalAtUtc = null;
        }

        RefreshOverdueAndSeverity(issue, now);

        _issueRepository.Update(issue);
        await SyncOrderMirrorAsync(issue, ct);

        await _issueRepository.SaveChangesAsync(ct);
        await _orderRepository.SaveChangesAsync(ct);
    }

    private sealed class LayerState
    {
        public int InventoryTransactionId { get; set; }
        public int WarehouseId { get; set; }
        public int ProductVariantId { get; set; }

        public InventoryReferenceType SourceReferenceType { get; set; }
        public int SourceReferenceId { get; set; }
        public int? SourceReferenceLineId { get; set; }

        /// <summary>
        /// Remaining qty của FIFO layer
        /// </summary>
        public decimal RemainingQty { get; set; }

        public DateTime OccurredAtUtc { get; set; }
        public string? Note { get; set; }
    }

    public async Task RefreshIssueAsync(int issueId, CancellationToken ct = default)
    {
        // Không refresh riêng lẻ 1 issue nữa.
        // Vì nhiều issue có thể tranh cùng 1 inbound transaction.
        // Nếu refresh từng issue độc lập thì inbound sẽ bị dùng lặp.
        // Do đó khi refresh 1 issue bất kỳ, phải refresh toàn bộ open issues
        // bằng global pool theo thứ tự OpenedAtUtc.
        await RefreshOpenIssuesAsync(ct);
    }

    public async Task<InventoryIssueBatchRefreshResultDto> RefreshOpenIssuesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var issueHeads = await _issueRepository.GetPendingOrReadyIssuesAsync(ct);

        var result = new InventoryIssueBatchRefreshResultDto
        {
            TotalRequested = issueHeads.Count
        };

        if (issueHeads.Count == 0)
            return result;

        // 1) Load đầy đủ issue theo đúng thứ tự ưu tiên:
        // issue mở sớm hơn phải được allocate trước
        var orderedHeads = issueHeads
            .OrderBy(x => x.OpenedAtUtc)
            .ThenBy(x => x.Id)
            .ToList();

        var issues = new List<OrderInventoryIssue>();
        var issueInboundMap = new Dictionary<int, List<InventoryIssueInboundCandidateDto>>();

        foreach (var head in orderedHeads)
        {
            var issue = await RequireIssueDetailAsync(head.Id, ct);
            issues.Add(issue);

            issueInboundMap[issue.Id] = await _issueRepository.GetInboundCandidatesForIssueAsync(issue.Id, ct);
        }

        // 2) Build GLOBAL inbound pool
        // - Distinct theo InventoryTransactionId
        // - Mỗi inbound transaction chỉ được dùng 1 lần trên toàn batch
        var globalLayerPool = issueInboundMap.Values
            .SelectMany(x => x)
            .GroupBy(x => x.InventoryTransactionId)
            .Select(g =>
            {
                var first = g
                    .OrderBy(x => x.OccurredAtUtc)
                    .ThenBy(x => x.InventoryTransactionId)
                    .First();

                return new LayerState
                {
                    InventoryTransactionId = first.InventoryTransactionId,
                    WarehouseId = first.WarehouseId,
                    ProductVariantId = first.ProductVariantId,
                    SourceReferenceType = first.SourceReferenceType,
                    SourceReferenceId = first.SourceReferenceId,
                    SourceReferenceLineId = first.SourceReferenceLineId,
                    RemainingQty = first.QuantityChange,
                    OccurredAtUtc = first.OccurredAtUtc,
                    Note = first.Note
                };
            })
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.InventoryTransactionId)
            .ToDictionary(x => x.InventoryTransactionId);

        // 3) Refresh từng issue theo thứ tự mở sớm -> muộn
        // nhưng allocate từ cùng 1 global pool
        foreach (var issue in issues)
        {
            try
            {
                var inboundCandidates = issueInboundMap[issue.Id];

                var eligibleTxIdsByVariant = inboundCandidates
                    .GroupBy(x => x.ProductVariantId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderBy(x => x.OccurredAtUtc)
                              .ThenBy(x => x.InventoryTransactionId)
                              .Select(x => x.InventoryTransactionId)
                              .ToList());

                var newAllocations = new List<OrderInventoryIssueLineAllocation>();

                foreach (var line in issue.Lines
                             .Where(x => !x.IsDeleted)
                             .OrderBy(x => x.Id))
                {
                    var costSnapshot = await _issueRepository.GetCostResolutionSnapshotForOrderLineAsync(
                        line.OrderId,
                        line.OrderLineId,
                        ct);

                    var needQty = line.NegativeQty;
                    var allocatedQty = 0m;

                    if (eligibleTxIdsByVariant.TryGetValue(line.ProductVariantId, out var eligibleTxIds))
                    {
                        foreach (var txId in eligibleTxIds)
                        {
                            if (needQty <= 0)
                                break;

                            if (!globalLayerPool.TryGetValue(txId, out var layer))
                                continue;

                            if (layer.RemainingQty <= 0)
                                continue;

                            var allocateQty = Math.Min(needQty, layer.RemainingQty);
                            if (allocateQty <= 0)
                                continue;

                            newAllocations.Add(new OrderInventoryIssueLineAllocation
                            {
                                StoreId = issue.StoreId,
                                OrderInventoryIssueId = issue.Id,
                                OrderInventoryIssueLineId = line.Id,
                                SourceReferenceType = layer.SourceReferenceType,
                                SourceReferenceId = layer.SourceReferenceId,
                                SourceReferenceLineId = layer.SourceReferenceLineId,
                                InventoryTransactionId = layer.InventoryTransactionId,
                                AllocatedQuantity = allocateQty,
                                Note = "AUTO FIFO from global layer pool"
                            });

                            // Trừ trực tiếp khỏi GLOBAL pool
                            layer.RemainingQty -= allocateQty;
                            needQty -= allocateQty;
                            allocatedQty += allocateQty;
                        }
                    }

                    var hasProvisionalCost = HasProvisionalCost(line);
                    var quantityResolved = allocatedQty >= line.NegativeQty;
                    var documentResolved = allocatedQty > 0;
                    var costResolved = !hasProvisionalCost || costSnapshot.HasRevaluationEntry;

                    line.AutoDetectedInboundQty = allocatedQty;
                    line.AutoDetectedDocumentResolved = documentResolved;
                    line.AutoDetectedCostResolved = costResolved;
                    line.AutoDetectedRevaluationAmount = costSnapshot.HasRevaluationEntry
                        ? costSnapshot.RevaluationAmount
                        : null;
                    line.RevaluationAmount = line.AutoDetectedRevaluationAmount;
                    line.LastAutoResolvedAtUtc = now;

                    var notes = new List<string>();

                    if (allocatedQty > 0)
                        notes.Add($"FIFO allocate {allocatedQty:N2}/{line.NegativeQty:N2}");
                    else
                        notes.Add("Không có inbound layer phù hợp");

                    if (costResolved)
                        notes.Add("Cost đã finalize");
                    else if (hasProvisionalCost)
                        notes.Add("Chưa revaluation");

                    line.AutoResolveNote = string.Join(" | ", notes);

                    line.IsResolved = quantityResolved && documentResolved && costResolved;
                    line.ResolvedAtUtc = line.IsResolved ? now : null;
                }

                await _issueRepository.ReplaceAllocationsAsync(issue.Id, newAllocations, ct);

                var totalLines = issue.Lines.Count(x => !x.IsDeleted);
                var resolvedLines = issue.Lines.Count(x => !x.IsDeleted && x.IsResolved);

                issue.AutoResolvedLineCount = resolvedLines;
                issue.LastAutoResolvedAtUtc = now;

                if (totalLines > 0 && resolvedLines == totalLines)
                {
                    issue.Status = InventoryResolutionStatus.ReadyForApproval;
                    issue.ReadyForApprovalAtUtc ??= now;
                }
                else
                {
                    issue.Status = InventoryResolutionStatus.PendingResolution;
                    issue.ReadyForApprovalAtUtc = null;
                }

                RefreshOverdueAndSeverity(issue, now);

                _issueRepository.Update(issue);
                await SyncOrderMirrorAsync(issue, ct);

                await _issueRepository.SaveChangesAsync(ct);
                await _orderRepository.SaveChangesAsync(ct);

                result.Items.Add(new InventoryIssueBatchRefreshItemDto
                {
                    IssueId = issue.Id,
                    IsSuccess = true
                });

                result.RefreshedCount++;
            }
            catch (Exception ex)
            {
                result.Items.Add(new InventoryIssueBatchRefreshItemDto
                {
                    IssueId = issue.Id,
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                });

                result.FailedCount++;
            }
        }

        return result;
    }
    private static string BuildNegativeDetectedSummary(
    OrderInventoryIssue issue,
    string? sourceNote)
    {
        var activeLines = issue.Lines
            .Where(x => !x.IsDeleted)
            .ToList();

        var totalLines = activeLines.Count;
        var unresolvedLines = activeLines.Count(x => !x.IsResolved);
        var provisionalLines = activeLines.Count(x =>
            x.ProvisionalUnitCost.HasValue || x.ProvisionalCostAmount.HasValue);

        return
            $"Phát hiện tồn âm/provisional cost sau finalize. " +
            $"IssueId={issue.Id}; OrderId={issue.OrderId}; " +
            $"Số dòng issue={totalLines}; Chưa xử lý={unresolvedLines}; " +
            $"Có provisional cost={provisionalLines}. " +
            $"Chi tiết xem tại OrderInventoryIssueLines.";
    }

    private static string? NormalizeActionNote(string? note, int maxLength = 1000)
    {
        if (string.IsNullOrWhiteSpace(note))
            return null;

        var value = note.Trim();

        if (value.Length <= maxLength)
            return value;

        return value[..(maxLength - 30)] + "... [đã rút gọn]";
    }
}