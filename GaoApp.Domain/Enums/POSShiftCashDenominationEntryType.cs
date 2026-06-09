namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại bảng kê mệnh giá tiền trong ca POS.
/// </summary>
public enum POSShiftCashDenominationEntryType
{
    /// <summary>
    /// Tiền đầu ca khi nhân viên nhận ca.
    /// </summary>
    Opening = 1,

    /// <summary>
    /// Tiền cuối ca khi nhân viên đóng/bàn giao ca.
    /// </summary>
    Closing = 2
}