using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("OrderRewardVouchers")]
public class OrderRewardVoucher : BaseStoreEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;

    public int VoucherId { get; set; }
    public CustomerRewardVoucher Voucher { get; set; } = default!;

    [Column(TypeName = "decimal(18,2)")]
    public decimal VoucherValue { get; set; }
}