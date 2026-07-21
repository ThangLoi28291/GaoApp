namespace GaoApp.Domain.Enums;

/// <summary>
/// Kiểu sắp xếp danh sách hóa đơn bán ra.
/// Dùng cho màn hình Admin/Invoice/Index.
/// </summary>
public enum InvoiceListSortMode
{
    /// <summary>
    /// Mặc định: hóa đơn có sản phẩm lên trước, trong mỗi nhóm sắp theo OrderId mới nhất.
    /// Đây là kiểu nên dùng cho màn hình quản lý.
    /// </summary>
    HasLinesThenOrderIdDesc = 0,

    /// <summary>
    /// OrderId mới nhất lên trước.
    /// Không ưu tiên hóa đơn có sản phẩm.
    /// </summary>
    OrderIdDesc = 1,

    /// <summary>
    /// OrderId cũ nhất lên trước.
    /// </summary>
    OrderIdAsc = 2,

    /// <summary>
    /// Ngày hóa đơn mới nhất lên trước.
    /// </summary>
    InvoiceDateDesc = 3,

    /// <summary>
    /// Ngày hóa đơn cũ nhất lên trước.
    /// </summary>
    InvoiceDateAsc = 4,

    /// <summary>
    /// Tổng tiền cao nhất lên trước.
    /// </summary>
    GrandTotalDesc = 5,

    /// <summary>
    /// Tổng tiền thấp nhất lên trước.
    /// </summary>
    GrandTotalAsc = 6,

    /// <summary>
    /// Số lượng nhiều nhất lên trước.
    /// </summary>
    QuantityDesc = 7,

    /// <summary>
    /// Số lượng ít nhất lên trước.
    /// </summary>
    QuantityAsc = 8,

    /// <summary>
    /// Nhiều dòng Auto nhất lên trước.
    /// </summary>
    AutoLineCountDesc = 9,

    /// <summary>
    /// Nhiều dòng Manual nhất lên trước.
    /// </summary>
    ManualLineCountDesc = 10
}