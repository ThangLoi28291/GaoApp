using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

public sealed class CustomerDebtReceipt : BaseStoreEntity, IAuditTrackedEntity
{
    public Guid ClientRequestId { get; set; }
    public int CustomerId { get; set; }
    public int POSShiftId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public int? StoreBankAccountId { get; set; }
    [MaxLength(100)] public string? Reference { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    /// <summary>Canonical request, retained to reject reuse of a request ID with different allocation.</summary>
    public string RequestJson { get; set; } = "";
    public ICollection<CustomerReceivableEntry> Entries { get; set; } = new List<CustomerReceivableEntry>();
}
