using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("CustomerRewardVouchers")]
public class CustomerRewardVoucher : BaseStoreEntity
{
    public int CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public Customer? Customer { get; set; }

    [Required, StringLength(50)]
    public string VoucherCode { get; set; } = default!;

    /// <summary>
    /// Giá trị phiếu, ví dụ 30.000đ.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Value { get; set; }

    /// <summary>
    /// Số tiền tích lũy đã bị trừ để đổi phiếu.
    /// Ví dụ 65.000 * 30 = 1.950.000đ.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal RequiredAmount { get; set; }

    public CustomerRewardVoucherStatus Status { get; set; } =
        CustomerRewardVoucherStatus.Available;

    public DateTime IssuedAtUtc { get; set; }

    public DateTime? UsedAtUtc { get; set; }

    public int? UsedOrderId { get; set; }

    [ForeignKey(nameof(UsedOrderId))]
    public Order? UsedOrder { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Mã tham chiếu hệ thống cũ, ví dụ UserPoint.ID khi import.
    /// </summary>
    [StringLength(100)]
    public string? ReferenceCode { get; set; }
    public ICollection<OrderRewardVoucher> Orders { get; set; } = new List<OrderRewardVoucher>();
}