using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("AutoInvoiceWorkerStates")]
public sealed class AutoInvoiceWorkerState : BaseStoreEntity
{
    [Required, StringLength(100)]
    public string WorkerName { get; set; } = "auto-invoice-worker";

    [StringLength(100)]
    public string? WorkerInstanceId { get; set; }

    public bool IsRunning { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? StoppedAtUtc { get; set; }

    public DateTime? LastHeartbeatAtUtc { get; set; }

    public DateTime? LastScanAtUtc { get; set; }

    public int? CurrentOperationId { get; set; }

    public int? CurrentInvoiceHeadId { get; set; }

    public DateTime? NextRunAtUtc { get; set; }

    [StringLength(100)]
    public string? LastErrorCode { get; set; }

    [StringLength(2000)]
    public string? LastErrorMessage { get; set; }

    [StringLength(1000)]
    public string? LastResult { get; set; }
}
