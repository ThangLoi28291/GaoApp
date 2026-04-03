namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại chứng từ hậu mãi sau bán.
/// </summary>
public enum SalesReturnType
{
    RefundOnly = 1,       // chỉ hoàn tiền, không trả hàng
    ReturnOnly = 2,       // chỉ trả hàng, chưa hoàn tiền
    ReturnAndRefund = 3,  // vừa trả hàng vừa hoàn tiền
    Exchange = 4          // để dành phase đổi hàng
}