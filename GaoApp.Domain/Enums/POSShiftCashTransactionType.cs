namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại giao dịch tiền mặt trong ca POS
/// </summary>
public enum POSShiftCashTransactionType
{
    /// <summary>
    /// Thu thêm vào két
    /// </summary>
    CashIn = 1,

    /// <summary>
    /// Chi / rút khỏi két
    /// </summary>
    CashOut = 2
}