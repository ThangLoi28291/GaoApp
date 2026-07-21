namespace GaoApp.Domain.Enums;

/// <summary>
/// Cách xử lý hàng trả của từng dòng.
/// </summary>
public enum SalesReturnLineAction
{
    NoRestock = 0, // không nhập kho lại
    Restock = 1    // nhập kho lại
}