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
    public decimal OnHandQty { get; set; }
    public bool IsNegativeStock { get; set; }

    /// <summary>
    /// Ảnh đầy đủ để preview / modal.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Ảnh thumbnail nhỏ cho autocomplete / cart.
    /// Nếu chưa có resize riêng thì có thể tạm dùng cùng ImageUrl.
    /// </summary>
    public string? ImageThumbUrl { get; set; }

    /// <summary>
    /// Alt text hiển thị cho ảnh.
    /// </summary>
    public string? ImageAlt { get; set; }

    /// <summary>
    /// Có ảnh để render nhanh phía UI.
    /// </summary>
    public bool HasImage { get; set; }

    public List<POSProductSearchUnitOptionDto> UnitOptions { get; set; } = new();
}