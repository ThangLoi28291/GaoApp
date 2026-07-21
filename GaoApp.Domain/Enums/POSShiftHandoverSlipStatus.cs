namespace GaoApp.Domain.Enums;

/// <summary>
/// Trạng thái phiếu bàn giao / nhận ca POS.
/// </summary>
public enum POSShiftHandoverSlipStatus
{
    /// <summary>
    /// Phiếu mới tạo, chưa được nhân viên nhận ca sử dụng.
    /// </summary>
    Draft = 1,

    /// <summary>
    /// Phiếu đã in ra giấy.
    /// </summary>
    Printed = 2,

    /// <summary>
    /// Phiếu đã được dùng để mở ca.
    /// </summary>
    Used = 3,

    /// <summary>
    /// Phiếu đã hủy, không còn hiệu lực.
    /// </summary>
    Cancelled = 4
}