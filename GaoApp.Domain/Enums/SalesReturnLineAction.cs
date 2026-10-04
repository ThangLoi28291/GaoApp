namespace GaoApp.Domain.Enums;

/// <summary>
/// Cách xử lý hàng trả của từng dòng.
/// </summary>
public enum SalesReturnLineAction
{
    NoRestock = 0, // không nhập kho lại
    Restock = 1,   // nhập kho lại
    PendingRestock = 2 // đã nhận hàng, chờ xác định giá vốn và nhập kho
}
