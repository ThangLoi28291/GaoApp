using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("AutoInvoiceOperationSources")]
public sealed class AutoInvoiceOperationSource : BaseStoreEntity
{
    public int AutoInvoiceOperationId { get; set; }

    public AutoInvoiceOperation AutoInvoiceOperation { get; set; } = default!;

    public int InvoiceHeadId { get; set; }

    public int? OrderId { get; set; }

    public int? InvoiceDetailId { get; set; }

    public int? OrderLineId { get; set; }

    public AutoInvoiceSourceStatus Status { get; set; } = AutoInvoiceSourceStatus.Pending;

    public bool IsActive { get; set; } = true;

    [StringLength(2000)]
    public string? SourceSnapshotJson { get; set; }

    [StringLength(100)]
    public string? ErrorCode { get; set; }

    [StringLength(2000)]
    public string? ErrorMessage { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}
