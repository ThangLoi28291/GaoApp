using GaoApp.Application.DTOs.Reports.Profit;

namespace GaoApp.Application.Services.Reports;

public static class ProfitCostDiagnosticsPolicy
{
    public static ProfitCostDiagnosticsDto ForPeriod(IReadOnlyCollection<ProfitCostIssueDto> issues,
        IReadOnlyCollection<ProfitDetailRowDto> rows)
    {
        // Diagnostics follow the same eligible revenue events as the report, including returns.
        var affected = rows.Where(x => !x.Summary.Cogs.Available).Select(x => x.OrderId).ToHashSet();
        var selected = issues.Where(x => affected.Contains(x.OrderId))
            .DistinctBy(x => (x.OrderId, x.OrderLineId, x.Code))
            .OrderBy(x => x.OrderNumber).ThenBy(x => x.OrderId).ThenBy(x => x.OrderLineId).ThenBy(x => x.Code).ToList();
        return new() {
            AffectedOrders = selected.Select(x => x.OrderId).Distinct().Count(),
            AffectedLines = selected.Where(x => x.OrderLineId.HasValue).Select(x => (x.OrderId, x.OrderLineId)).Distinct().Count(),
            UnavailableOrders = selected.Where(x => x.Quality == ProfitQuality.Unavailable).Select(x => x.OrderId).Distinct().Count(),
            ConflictingOrders = selected.Where(x => x.Quality == ProfitQuality.DataIntegrityConflict).Select(x => x.OrderId).Distinct().Count(),
            TotalIssues = selected.Count, Issues = selected.Take(20).ToList()
        };
    }

    public static string ValuationReason(string? reason) => reason switch {
        "Invalid original sale valuation." => "Bút toán giá vốn bán gốc có số lượng, đơn giá hoặc thành tiền không hợp lệ.",
        "Original warehouse/variant evidence is missing." => "Thiếu thông tin kho hoặc sản phẩm của bút toán giá vốn gốc.",
        "Original sale transaction is missing." => "Thiếu giao dịch xuất kho của dòng bán gốc.",
        "Original sale transaction linkage does not match." => "Giao dịch xuất kho không khớp đơn, dòng hàng, kho hoặc sản phẩm của bút toán giá vốn.",
        "Original cost allocations are missing." => "Thiếu phân bổ nguồn giá vốn của dòng xuất bán.",
        "Open provisional source cost is unknown." => "Nguồn giá vốn còn tạm tính nhưng chưa có đơn giá nguồn để tính.",
        "Budget sale allocation quantities/amounts do not reconcile." => "Số lượng hoặc thành tiền phân bổ giá vốn không khớp bút toán xuất bán.",
        "Budget sale source/scope is missing or invalid." => "Nguồn giá vốn bán gốc thiếu hoặc không khớp giao dịch xuất kho, kho, sản phẩm và cửa hàng.",
        "Final budget debit proof is incomplete." => "Thiếu chứng từ nguồn để xác nhận giá vốn đã phân bổ.",
        "Budget-source reversal requires the separately scoped source adapter." => "Lịch sử hoàn hoặc hủy hàng dùng nguồn giá vốn chưa được hỗ trợ đối chiếu.",
        "Inventory quantity was over-reversed." => "Số lượng hoàn hoặc hủy kho vượt quá số lượng đã xuất bán.",
        "Normalized sale cost is negative." => "Giá vốn sau đối chiếu bị âm.",
        "Full reversal leaves a sale cost residual." => "Đơn đã hoàn hoặc hủy toàn bộ nhưng còn dư giá vốn.",
        "Historical reversal differs from normalized final cost." => "Giá vốn hoàn hoặc hủy không khớp giá vốn bán đã xác định.",
        "Final cost cannot be represented at movement precision." => "Giá vốn không khớp độ chính xác của giao dịch kho.",
        _ when reason?.Contains("reversal", StringComparison.OrdinalIgnoreCase) == true => "Lịch sử hoàn hoặc hủy hàng và lịch sử xác định giá vốn chưa đối chiếu khớp.",
        _ when reason?.Contains("budget", StringComparison.OrdinalIgnoreCase) == true => "Chứng từ nguồn hoặc phân bổ ngân sách giá vốn chưa đối chiếu khớp.",
        _ when reason?.Contains("resolution", StringComparison.OrdinalIgnoreCase) == true || reason?.Contains("revaluation", StringComparison.OrdinalIgnoreCase) == true => "Lịch sử điều chỉnh hoặc xác định giá vốn thiếu bằng chứng hoặc không khớp nguồn.",
        _ when reason?.Contains("allocation", StringComparison.OrdinalIgnoreCase) == true => "Phân bổ nguồn giá vốn thiếu hoặc không khớp bút toán xuất bán.",
        _ => "Liên kết lịch sử giá vốn không hợp lệ; cần kiểm tra bút toán xuất bán và chứng từ nguồn."
    };
}
