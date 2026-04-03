namespace GaoApp.Domain.Enums;

/// <summary>
/// Phương thức thanh toán của đơn hàng
/// </summary>
public enum PaymentMethod
{
    /// <summary>
    /// Tiền mặt
    /// </summary>
    Cash = 0,

    /// <summary>
    /// Chuyển khoản ngân hàng
    /// Ví dụ: ACB, Vietcombank, QR Banking
    /// </summary>
    BankTransfer = 1,

    /// <summary>
    /// Thanh toán bằng thẻ
    /// Ví dụ: Visa, MasterCard, POS machine
    /// </summary>
    Card = 2,

    /// <summary>
    /// Phương thức khác
    /// Ví dụ: ví điện tử, ghi nợ, voucher...
    EWallet = 3,
    /// </summary>
    Other = 99
}