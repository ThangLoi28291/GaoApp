using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("ProductVariant")]
public class ProductVariant : BaseStoreEntity, IAuditTrackedEntity
{
    [Range(1, int.MaxValue, ErrorMessage = "ProductId không hợp lệ.")]
    public int ProductId { get; set; }

    public Product Product { get; set; } = default!;

    [Required(ErrorMessage = "Vui lòng nhập SKU.")]
    [StringLength(60)]
    public string Sku { get; set; } = default!;
    /// <summary>
    /// Tên hiển thị theo từng variant cho POS / bán hàng / tìm kiếm nhanh.
    /// Ví dụ:
    /// sữa tươi th size l vị cam hương bưởi
    /// </summary>
    [StringLength(255)]
    public string? ProductVariantName { get; set; }

    /// <summary>
    /// Tên chuẩn hóa để tìm kiếm nhanh, bỏ dấu + lower-case.
    /// Ví dụ:
    /// sua tuoi th size l vi cam huong buoi
    /// </summary>
    [StringLength(255)]
    public string? ProductVariantNameNormalized { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Giá vốn không hợp lệ.")]
    public decimal CostPrice { get; set; } = 0;

    [Range(0, double.MaxValue, ErrorMessage = "Giá bán không hợp lệ.")]
    public decimal? Price { get; set; }

    public bool IsActive { get; set; } = true;

    public int? PrimaryProductImageId { get; set; }
    public ProductImage? PrimaryProductImage { get; set; }
    

    public ICollection<ProductVariantAttributeValue> AttributeValues { get; set; } = new List<ProductVariantAttributeValue>();

    /// <summary>
    /// Số dư tồn kho hiện tại của biến thể này theo từng kho.
    /// </summary>
    public ICollection<InventoryBalance> InventoryBalances { get; set; } = new List<InventoryBalance>();

    /// <summary>
    /// Lịch sử giao dịch kho của biến thể này.
    /// </summary>
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();

    /// <summary>
    /// Danh sách quy đổi đơn vị bán của biến thể.
    /// </summary>
    public ICollection<ProductUnitConversion> UnitConversions { get; set; } = new List<ProductUnitConversion>();

    /// <summary>
    /// Các dòng kiểm kê có tham chiếu đến variant này.
    /// </summary>
    public ICollection<StockCountLine> StockCountLines { get; set; } = new List<StockCountLine>();
}