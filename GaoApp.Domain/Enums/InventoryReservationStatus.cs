namespace GaoApp.Domain.Enums;

public enum InventoryReservationStatus
{
    /// <summary>
    /// Đang giữ hàng.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Đã nhả giữ, không còn chiếm ReservedQty.
    /// </summary>
    Released = 2,

    /// <summary>
    /// Reservation đã được dùng để đi tới bước finalize/xuất bán.
    /// Không còn chiếm ReservedQty.
    /// </summary>
    Consumed = 3,

    /// <summary>
    /// Trạng thái hủy logic nếu sau này cần mở rộng.
    /// </summary>
    Cancelled = 4
}