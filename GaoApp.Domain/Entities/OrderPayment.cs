using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("OrderPayments")]
public class OrderPayment : BaseStoreEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;

    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public bool IsDebtCollection { get; set; }
    // One durable identity per manual collection, retained even after soft deletion.
    public Guid? ClientRequestId { get; set; }

    [StringLength(100)]
    public string? ReferenceCode { get; set; }

    [StringLength(50)]
    public string? Provider { get; set; }

    public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;

    public string? MetadataJson { get; set; } // chứa qrDataUrl base64...
}
