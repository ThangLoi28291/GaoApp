using GaoApp.Application.DTOs.Orders;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Enums;
using GaoApp.Web.Areas.Admin.ViewModels.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/inventory-issues")]
[Authorize]
public class InventoryIssueManagementController : Controller
{
    private readonly IOrderInventoryIssueService _issueService;

    public InventoryIssueManagementController(IOrderInventoryIssueService issueService)
    {
        _issueService = issueService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        InventoryResolutionStatus? status,
        bool? onlyOverdue,
        string? keyword,
        CancellationToken ct)
    {
        var issues = await _issueService.GetListAsync(status, onlyOverdue, ct);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLowerInvariant();

            issues = issues.Where(x =>
                    (!string.IsNullOrWhiteSpace(x.Code) && x.Code.ToLower().Contains(kw)) ||
                    (x.Order != null && !string.IsNullOrWhiteSpace(x.Order.OrderNumber) && x.Order.OrderNumber.ToLower().Contains(kw)) ||
                    x.OrderId.ToString().Contains(kw))
                .ToList();
        }

        var items = issues.Select(x => new InventoryIssueListItemVm
        {
            Id = x.Id,
            Code = x.Code ?? $"ISSUE-{x.Id}",
            OrderId = x.OrderId,
            OrderCode = x.Order?.OrderNumber ?? $"#{x.OrderId}",
            ReasonType = x.ReasonType.ToString(),
            Status = x.Status,
            Severity = x.Severity,
            IsOverdue = x.IsOverdue,
            OpenedAtUtc = x.OpenedAtUtc,
            DueAtUtc = x.DueAtUtc,
            TotalLines = x.Lines?.Count(l => !l.IsDeleted) ?? 0,
            ResolvedLines = x.Lines?.Count(l => !l.IsDeleted && l.IsResolved) ?? 0,
            UnresolvedLines = x.Lines?.Count(l => !l.IsDeleted && !l.IsResolved) ?? 0,
            InternalNotePreview = string.IsNullOrWhiteSpace(x.InternalNote)
                ? null
                : (x.InternalNote.Length <= 140
                    ? x.InternalNote
                    : x.InternalNote.Substring(0, 140) + "...")
        }).ToList();

        var vm = new InventoryIssueIndexVm
        {
            Status = status,
            OnlyOverdue = onlyOverdue,
            Keyword = keyword,

            TotalCount = items.Count,
            PendingCount = items.Count(x => x.Status == InventoryResolutionStatus.PendingResolution),
            ReadyCount = items.Count(x => x.Status == InventoryResolutionStatus.ReadyForApproval),
            ApprovedCount = items.Count(x => x.Status == InventoryResolutionStatus.Approved),
            RejectedCount = items.Count(x => x.Status == InventoryResolutionStatus.Rejected),
            OverdueCount = items.Count(x => x.IsOverdue),

            Items = items
        };

        return View(vm);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        // QUAN TRỌNG:
        // Không được gọi RefreshAutoResolutionAsync trực tiếp trong màn detail nữa.
        // Vì nhiều issue có thể tranh cùng 1 inbound transaction.
        // Nếu refresh riêng 1 issue ở đây sẽ bypass global pool và gây double allocation.
        //
        // Thay vào đó, gọi RefreshIssueAsync.
        // Service sẽ tự quyết định dùng batch/global refresh đúng chuẩn.
        await _issueService.RefreshIssueAsync(id, ct);

        var issue = await _issueService.GetDetailAsync(id, ct);
        if (issue == null) return NotFound();

        var activeLines = issue.Lines
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Id)
            .ToList();

        var lineVms = activeLines.Select(line =>
        {
            var reasons = !string.IsNullOrWhiteSpace(line.AutoResolveNote)
                ? line.AutoResolveNote.Split(" | ", StringSplitOptions.RemoveEmptyEntries).ToList()
                : new List<string>();

            return new InventoryIssueDetailLineVm
            {
                Id = line.Id,
                OrderLineId = line.OrderLineId,
                ProductId = line.ProductId,
                ProductVariantId = line.ProductVariantId,
                ProductName = line.Product?.Name ?? $"ProductVariant #{line.ProductVariantId}",
                VariantSku = line.ProductVariant?.Sku,

                OrderedQty = line.OrderedQty,
                StockBefore = line.StockBefore,
                StockAfter = line.StockAfter,
                NegativeQty = line.NegativeQty,

                CurrentAvailableQty = line.AutoDetectedInboundQty,

                ProvisionalUnitCost = line.ProvisionalUnitCost,
                ProvisionalCostAmount = line.ProvisionalCostAmount,
                RevaluationAmount = line.AutoDetectedRevaluationAmount ?? line.RevaluationAmount,

                HasLinkedReceipt = line.AutoDetectedDocumentResolved,
                HasLinkedAdjustment = false,

                QuantityResolved = line.AutoDetectedInboundQty >= line.NegativeQty,
                DocumentResolved = line.AutoDetectedDocumentResolved,
                CostResolved = line.AutoDetectedCostResolved,

                IsResolved = line.IsResolved,
                ResolvedAtUtc = line.ResolvedAtUtc,

                ResolveReasons = reasons,

                AutoDetectedInboundQty = line.AutoDetectedInboundQty,
                AutoResolveNote = line.AutoResolveNote
            };
        }).ToList();

        var vm = new InventoryIssueDetailVm
        {
            Id = issue.Id,
            Code = issue.Code ?? $"ISSUE-{issue.Id}",
            OrderId = issue.OrderId,
            OrderCode = issue.Order?.OrderNumber ?? $"ORDER-{issue.OrderId}",

            Status = issue.Status,
            Severity = issue.Severity,
            IsOverdue = issue.IsOverdue,

            ReasonType = issue.ReasonType.ToString(),
            OpenedAtUtc = issue.OpenedAtUtc,
            DueAtUtc = issue.DueAtUtc,
            ReadyForApprovalAtUtc = issue.ReadyForApprovalAtUtc,
            ApprovedAtUtc = issue.ApprovedAtUtc,
            RejectedAtUtc = issue.RejectedAtUtc,

            InternalNote = issue.InternalNote,

            TotalLines = activeLines.Count,
            ResolvedLines = activeLines.Count(x => x.IsResolved),
            UnresolvedLines = activeLines.Count(x => !x.IsResolved),

            Lines = lineVms,

            Actions = issue.Actions
                .OrderByDescending(a => a.ActionAtUtc)
                .Select(a => new InventoryIssueActionVm
                {
                    Id = a.Id,
                    ActionType = a.ActionType,
                    ActorName = a.ActorUser != null
                        ? (a.ActorUser.FullName ?? a.ActorUser.UserName)
                        : (a.ActorUserId.HasValue ? $"User #{a.ActorUserId.Value}" : "-"),
                    ActionAtUtc = a.ActionAtUtc,
                    ReferenceType = a.ReferenceType,
                    ReferenceId = a.ReferenceId,
                    OrderInventoryIssueLineId = a.OrderInventoryIssueLineId,
                    Note = a.Note
                })
                .ToList()
        };

        return View(vm);
    }

    [HttpPost("add-note")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNote(InventoryIssueActionRequestVm model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return RedirectToAction(nameof(Detail), new { id = model.IssueId });

        await _issueService.AddNoteAsync(model.IssueId, model.Note ?? string.Empty, GetCurrentUserId(), ct);

        TempData["Success"] = "Đã thêm ghi chú.";
        return RedirectToAction(nameof(Detail), new { id = model.IssueId });
    }

    [HttpPost("approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(InventoryIssueActionRequestVm model, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Unauthorized();

        try
        {
            await _issueService.ApproveAsync(model.IssueId, userId.Value, model.Note, ct);
            TempData["Success"] = "Đã duyệt case thành công.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id = model.IssueId });
    }

    [HttpPost("reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(InventoryIssueActionRequestVm model, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Unauthorized();

        try
        {
            await _issueService.RejectAsync(model.IssueId, userId.Value, model.Note ?? string.Empty, ct);
            TempData["Success"] = "Đã từ chối case.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id = model.IssueId });
    }

    [HttpPost("reopen")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(ReopenIssueRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Unauthorized();

        try
        {
            await _issueService.ReopenAsync(
                request.IssueId,
                userId.Value,
                request.Note,
                request.IssueLineId,
                ct);

            TempData["Success"] = "Đã mở lại case.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id = request.IssueId });
    }

    [HttpPost("escalate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Escalate(EscalateIssueRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Unauthorized();

        try
        {
            await _issueService.EscalateAsync(
                request.IssueId,
                userId.Value,
                request.Note,
                request.IssueLineId,
                ct);

            TempData["Success"] = "Đã escalate case.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id = request.IssueId });
    }

    private int? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(raw, out var userId))
            return userId;

        return null;
    }

    [HttpPost("{id:int}/refresh")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshOne(int id, CancellationToken ct)
    {
        try
        {
            await _issueService.RefreshIssueAsync(id, ct);
            TempData["Success"] = $"Đã làm mới case #{id}.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("refresh-open")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshOpen(CancellationToken ct)
    {
        try
        {
            var result = await _issueService.RefreshOpenIssuesAsync(ct);

            TempData["Success"] =
                $"Đã làm mới {result.RefreshedCount}/{result.TotalRequested} case đang mở." +
                (result.FailedCount > 0 ? $" Có {result.FailedCount} case lỗi." : string.Empty);
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}