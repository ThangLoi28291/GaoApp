using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("InvoiceIntegrationLogs")]
public class InvoiceIntegrationLog : BaseStoreEntity
{
    public int InvoiceHeadId { get; set; }

    public InvoiceHead InvoiceHead { get; set; } = default!;

    public int? AutoInvoiceOperationId { get; set; }

    public InvoiceIntegrationActionType ActionType { get; set; }

    [StringLength(500)]
    public string? RequestUrl { get; set; }

    public string? RequestBody { get; set; }

    public string? ResponseBody { get; set; }

    public bool IsSuccess { get; set; }

    [StringLength(100)]
    public string? ErrorCode { get; set; }

    [StringLength(1000)]
    public string? ErrorMessage { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAtUtc { get; set; }

    public long? DurationMs { get; set; }
}
