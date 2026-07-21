using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Chủ thể pháp lý (HKD/doanh nghiệp) vận hành bên trong một Store.
/// Store vẫn là tenant/cửa hàng vận hành; LegalEntity chỉ đại diện cho
/// chủ thể sở hữu tồn, ghi nhận doanh thu và phát hành hóa đơn điện tử.
/// </summary>
public sealed class LegalEntity : BaseStoreEntity
{
    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(300)]
    public string LegalName { get; set; } = string.Empty;

    [StringLength(50)]
    public string? TaxCode { get; set; }

    [StringLength(1200)]
    public string? Address { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(320)]
    public string? Email { get; set; }

    /// <summary>
    /// Kho bán mặc định của LegalEntity. Có thể null trong lúc cấu hình nền;
    /// preflight kích hoạt đa HKD sẽ bắt buộc phải có.
    /// </summary>
    public int? DefaultWarehouseId { get; set; }
    public Warehouse? DefaultWarehouse { get; set; }

    /// <summary>
    /// Cấu hình nhà cung cấp hóa đơn dành riêng cho LegalEntity.
    /// Có thể null khi HKD chưa được cấu hình phát hành hóa đơn.
    /// </summary>
    public int? InvoiceProviderSettingId { get; set; }
    public InvoiceProviderSetting? InvoiceProviderSetting { get; set; }

    /// <summary>
    /// Số nhỏ hơn được ưu tiên xuất bán trước.
    /// </summary>
    public int SalePriority { get; set; } = 1;

    public bool IsDefaultForPurchase { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(1000)]
    public string? Note { get; set; }

    public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
}
