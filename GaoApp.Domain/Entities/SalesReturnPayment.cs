using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("SalesReturnPayments")]
public class SalesReturnPayment : BaseStoreEntity
{
    public int SalesReturnId { get; set; }
    public SalesReturn SalesReturn { get; set; } = default!;

    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }

    [StringLength(100)]
    public string? ReferenceCode { get; set; }

    [StringLength(50)]
    public string? Provider { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;
}