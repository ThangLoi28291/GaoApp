using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>Append-only customer debt journal. Positive increases debt; negative settles debt.</summary>
public sealed class CustomerReceivableEntry : BaseStoreEntity, IAuditTrackedEntity
{
    public int CustomerId { get; set; }
    public int OrderId { get; set; }
    public int? ReceiptId { get; set; }
    public CustomerDebtReceipt? Receipt { get; set; }
    public int? SalesReturnId { get; set; }
    [MaxLength(20)] public string Kind { get; set; } = "Sale";
    public decimal Amount { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}
