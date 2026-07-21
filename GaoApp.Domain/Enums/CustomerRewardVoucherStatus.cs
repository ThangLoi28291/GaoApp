namespace GaoApp.Domain.Enums;

/// <summary>
/// Trạng thái phiếu đổi điểm.
/// </summary>
public enum CustomerRewardVoucherStatus
{
    Available = 1,
    Used = 2,
    Cancelled = 3,
    Expired = 4,
    Locked = 5
}