using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("AutoInvoiceOperations")]
public sealed class AutoInvoiceOperation : BaseStoreEntity
{
    public AutoInvoiceOperationKind Kind { get; set; }

    public AutoInvoiceOperationStatus Status { get; set; } = AutoInvoiceOperationStatus.Pending;

    public int? InvoiceHeadId { get; set; }

    [StringLength(500)]
    public string? GroupKey { get; set; }

    public DateTime SaleDateLocal { get; set; }

    [StringLength(36)]
    public string? TransactionUuid { get; set; }

    public int AttemptCount { get; set; }

    public bool IsManual { get; set; }

    public int? RequestedByUserId { get; set; }

    [StringLength(200)]
    public string? RequestedByUserName { get; set; }

    [StringLength(100)]
    public string? CorrelationId { get; set; }

    public DateTime? ClaimedAtUtc { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime? NextAttemptAtUtc { get; set; }

    [StringLength(100)]
    public string? ErrorCode { get; set; }

    [StringLength(2000)]
    public string? ErrorMessage { get; set; }

    public ICollection<AutoInvoiceOperationSource> Sources { get; set; } = new List<AutoInvoiceOperationSource>();
}
