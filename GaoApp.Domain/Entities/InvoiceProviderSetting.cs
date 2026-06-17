using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaoApp.Domain.Entities;

[Table("InvoiceProviderSettings")]
public class InvoiceProviderSetting : BaseStoreEntity
{
    /// <summary>
    /// Tên nhà cung cấp: Viettel, Misa, VNPT...
    /// Giai đoạn này dùng Viettel.
    /// </summary>
    [Required]
    [StringLength(50)]
    public string ProviderCode { get; set; } = "VIETTEL";

    /// <summary>
    /// Môi trường test hoặc production.
    /// </summary>
    public bool IsProduction { get; set; }

    /// <summary>
    /// Base URL API.
    /// Ví dụ test/production tùy cấu hình Viettel cấp.
    /// </summary>
    [Required]
    [StringLength(500)]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Tài khoản API.
    /// </summary>
    [Required]
    [StringLength(150)]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Mật khẩu API.
    /// Giai đoạn đầu lưu plain để test nội bộ.
    /// Sau đó nên mã hóa.
    /// </summary>
    [Required]
    [StringLength(500)]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// MST phát hành hóa đơn.
    /// supplierTaxCode trong tài liệu Viettel.
    /// </summary>
    [Required]
    [StringLength(20)]
    public string SupplierTaxCode { get; set; } = string.Empty;

    /// <summary>
    /// Loại hóa đơn.
    /// TT78 thường dùng: 1, 2, 3, 4, 5, 6.
    /// </summary>
    [Required]
    [StringLength(20)]
    public string InvoiceType { get; set; } = "1";

    /// <summary>
    /// Mẫu hóa đơn.
    /// Ví dụ: 1/001.
    /// </summary>
    [Required]
    [StringLength(20)]
    public string TemplateCode { get; set; } = string.Empty;

    /// <summary>
    /// Ký hiệu hóa đơn.
    /// Ví dụ: C24AAA, K24TAA...
    /// </summary>
    [Required]
    [StringLength(25)]
    public string InvoiceSeries { get; set; } = string.Empty;

    /// <summary>
    /// Tiền tệ.
    /// Mặc định VND.
    /// </summary>
    [Required]
    [StringLength(3)]
    public string CurrencyCode { get; set; } = "VND";

    /// <summary>
    /// Tỷ giá.
    /// Nếu VND thì để 1.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal ExchangeRate { get; set; } = 1m;

    /// <summary>
    /// Tên phương thức thanh toán gửi sang Viettel.
    /// Ví dụ: TM, CK, TM/CK, Tiền mặt, Chuyển khoản.
    /// </summary>
    [StringLength(50)]
    public string PaymentMethodName { get; set; } = "TM";

    /// <summary>
    /// Có cho người mua tra cứu hóa đơn hay không.
    /// </summary>
    public bool CusGetInvoiceRight { get; set; } = true;

    /// <summary>
    /// Hóa đơn đã thanh toán hay chưa.
    /// POS thường là true sau khi finalize.
    /// </summary>
    public bool DefaultPaymentStatus { get; set; } = true;

    /// <summary>
    /// Cấu hình đang hoạt động.
    /// </summary>
    public bool IsActive { get; set; } = true;

    [StringLength(500)]
    public string? Note { get; set; }
    /// <summary>
    /// Kiểu xác thực API.
    /// Với tài khoản Viettel hiện tại, dùng BasicAuth.
    /// </summary>
    public InvoiceProviderAuthMode AuthMode { get; set; } = InvoiceProviderAuthMode.BasicAuth;
}