namespace GaoApp.Application.DTOs.Orders;

/// <summary>
/// Kết quả kiểm tra toàn bộ case trước khi mark ready / approve.
/// </summary>
public sealed class InventoryIssueApprovalCheckResultDto
{
    public int IssueId { get; set; }
    public string IssueCode { get; set; } = string.Empty;

    public bool CanApprove { get; set; }

    public int TotalLines { get; set; }
    public int ResolvedLines { get; set; }
    public int UnresolvedLines { get; set; }

    public List<InventoryIssueLineResolutionDto> Lines { get; set; } = new();

    public string ToUserMessage()
    {
        if (CanApprove)
            return "Case đã đủ điều kiện approve.";

        var unresolved = Lines.Where(x => !x.IsResolved).ToList();
        if (unresolved.Count == 0)
            return "Case chưa đủ điều kiện approve.";

        var parts = unresolved.Select(x =>
        {
            var code = string.IsNullOrWhiteSpace(x.VariantSku)
                ? $"VariantId={x.ProductVariantId}"
                : x.VariantSku;

            var reason = x.Reasons.Count == 0
                ? "chưa xác định"
                : string.Join("; ", x.Reasons);

            return $"- {x.ProductName} ({code}): {reason}";
        });

        return "Không thể approve case vì còn line chưa resolve:\n" + string.Join("\n", parts);
    }
}