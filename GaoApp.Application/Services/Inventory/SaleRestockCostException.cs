namespace GaoApp.Application.Services.Inventory;

/// <summary>An inventory reversal prerequisite, distinct from a technical posting failure.</summary>
public sealed class SaleRestockCostException(int orderId, int orderLineId, SaleValuationCostPolicy.Quality quality, string? reason)
    : InvalidOperationException($"Sale restock cost is unavailable. {reason}")
{
    public int OrderId { get; } = orderId;
    public int OrderLineId { get; } = orderLineId;
    public string ErrorCode => quality is SaleValuationCostPolicy.Quality.Provisional or SaleValuationCostPolicy.Quality.PartiallyFinalized
        ? "POS_RETURN_COST_PENDING" : "POS_RETURN_COST_UNAVAILABLE";
    public string SafeMessage => ErrorCode == "POS_RETURN_COST_PENDING"
        ? $"Dòng hàng #{OrderLineId} chưa thể nhập lại kho vì giá vốn xuất bán còn tạm tính."
        : $"Dòng hàng #{OrderLineId} chưa thể nhập lại kho vì lịch sử giá vốn chưa đủ hoặc chưa khớp.";
    public string ActionHint => "Quản lý cần kiểm tra phiếu nhập, tồn đầu kỳ và đối soát giá vốn của dòng hàng này. Sau khi xử lý, tải lại đơn để trả hàng.";
}
