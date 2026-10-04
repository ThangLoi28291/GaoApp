using GaoApp.Domain.Common;
using GaoApp.Domain.Constants;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaoApp.Domain.Entities;

[Table("Customers")]
public class Customer : BaseStoreEntity
{
    [Required, StringLength(200)]
    public string Name { get; set; } = default!;

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    // Mã khách trong GaoApp, ví dụ: KH000001 hoặc Gao20056 khi import hệ thống cũ
    [StringLength(50)]
    public string? Code { get; set; }

    // ID khách bên hệ thống cũ OnlineShop.User.ID
    public long? OldCustomerId { get; set; }

    // Nhóm khách cũ: MEMBER, WHOLESALE, KVL, KHACHORDER
    [StringLength(30)]
    public string CustomerGroup { get; set; } = "MEMBER";

    [StringLength(100)]
    public string? Email { get; set; }

    // Mã số thuế khách hàng
    [StringLength(50)]
    public string? TaxCode { get; set; }

    // Khách có công nợ hay không, lấy từ User.HaveDebt cũ
    public bool HaveDebt { get; set; }
    public bool AskBeforePrintingReceipt { get; set; }

    // Đánh dấu khách được import từ hệ thống cũ
    public bool IsImportedFromOldSystem { get; set; }

    // Chỉ lưu số dư tích lũy cũ để đối soát.
    // Giai đoạn sau sẽ đưa sang CustomerRewardLedger.
    [Column(TypeName = "decimal(18,2)")]
    public decimal ImportedRewardAmount { get; set; }

    public bool IsActive { get; set; } = true;
    /// <summary>
    /// Nhóm giá áp dụng cho khách hàng.
    /// 
    /// RETAIL:
    /// - Khách lẻ.
    /// - POS lấy giá bán lẻ hiện tại.
    ///
    /// WHOLESALE:
    /// - Khách sỉ.
    /// - POS ưu tiên lấy giá sỉ nếu sản phẩm / đơn vị có cấu hình.
    /// </summary>
    [StringLength(30)]
    public string PriceTier { get; set; } = CustomerPriceTiers.Retail;
}
