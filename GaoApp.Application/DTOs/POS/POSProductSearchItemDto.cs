namespace GaoApp.Application.DTOs.POS;

public class POSProductSearchItemDto
{
    public int VariantId { get; set; }
    public int ProductId { get; set; }

    /// <summary>
    /// Tên sản phẩm cha.
    /// Ví dụ: "Lau sàn gift"
    /// </summary>
    public string ProductName { get; set; } = "";

    /// <summary>
    /// Tên biến thể thực tế để POS hiển thị.
    /// Ví dụ: "buoi", "cam", "1kg", "thùng 24 lon"...
    /// </summary>
    public string ProductVariantName { get; set; } = "";

    /// <summary>
    /// Tên đầy đủ để hiển thị nhanh ngoài UI.
    /// Format: ProductName - ProductVariantName
    /// </summary>
    public string DisplayName { get; set; } = "";

    public string? Sku { get; set; }
    public string? Barcode { get; set; }

    public decimal Price { get; set; }
    public bool IsActive { get; set; }
}