namespace GaoApp.Domain.Enums;

/// <summary>
/// Phân loại barcode để quản lý nguồn gốc / mục đích sử dụng.
/// </summary>
public enum BarcodeType
{
    /// <summary>
    /// Barcode nội bộ hệ thống tự sinh.
    /// </summary>
    Internal = 0,

    /// <summary>
    /// Barcode từ nhà sản xuất / thị trường.
    /// </summary>
    External = 1,

    /// <summary>
    /// Barcode từ nhà cung cấp.
    /// </summary>
    Supplier = 2,

    /// <summary>
    /// Barcode dùng cho đơn vị đóng gói.
    /// </summary>
    Packaging = 3,

    /// <summary>
    /// Barcode cũ từ hệ thống cũ.
    /// </summary>
    Legacy = 4
}