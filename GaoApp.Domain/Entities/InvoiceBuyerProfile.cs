using GaoApp.Domain.Common;
using GaoApp.Domain.Constants;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Hồ sơ người mua dùng cho hóa đơn điện tử.
/// Đây là "sổ địa chỉ xuất hóa đơn", tách riêng khỏi Customer POS.
/// </summary>
[Table("InvoiceBuyerProfiles")]
public class InvoiceBuyerProfile : BaseStoreEntity
{
    /// <summary>
    /// Liên kết khách POS nếu có.
    /// Nullable vì có người mua vãng lai chỉ xuất hóa đơn 1 lần.
    /// </summary>
    public int? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    [Required, StringLength(30)]
    public string BuyerType { get; set; } = InvoiceBuyerTypes.Business;

    /// <summary>
    /// MST / mã định danh để lần sau gõ lại tìm nhanh.
    /// Có thể là 10 số, 12 số, 13 số, mã có dấu -, mã nước ngoài.
    /// </summary>
    [StringLength(50)]
    public string? TaxCode { get; set; }

    /// <summary>
    /// Cá nhân: tên khách hàng.
    /// Doanh nghiệp: người liên hệ/người mua nếu có.
    /// </summary>
    [StringLength(300)]
    public string? BuyerName { get; set; }

    /// <summary>
    /// Doanh nghiệp: tên đơn vị/công ty/hộ kinh doanh.
    /// </summary>
    [StringLength(500)]
    public string? BuyerLegalName { get; set; }

    [StringLength(1200)]
    public string? BuyerAddress { get; set; }

    [StringLength(2000)]
    public string? BuyerEmail { get; set; }

    [StringLength(30)]
    public string? BuyerPhone { get; set; }

    /// <summary>
    /// Nguồn dữ liệu:
    /// manual / vietqr / customer / invoice-history
    /// </summary>
    [StringLength(50)]
    public string Source { get; set; } = "manual";

    /// <summary>
    /// true sau khi người dùng đã bấm Lưu/Xác nhận.
    /// </summary>
    public bool IsVerifiedByUser { get; set; }

    public DateTime? LastLookupAtUtc { get; set; }

    public DateTime? LastUsedAtUtc { get; set; }

    public int UseCount { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public bool IsActive { get; set; } = true;
}