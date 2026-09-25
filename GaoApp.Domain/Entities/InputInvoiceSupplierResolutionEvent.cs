using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Append-only business evidence for canonical Supplier resolution.
/// This deliberately does not inherit a soft-delete entity base.
/// </summary>
[Table("InputInvoiceSupplierResolutionEvent")]
public sealed class InputInvoiceSupplierResolutionEvent
{
    public long Id { get; set; }
    public int StoreId { get; set; }
    public int InputInvoiceHeadId { get; set; }
    public int? StockDocumentId { get; set; }
    public InputInvoiceSupplierResolutionEventType EventType { get; set; }
    public InputInvoiceSupplierResolutionStatus PreviousStatus { get; set; }
    public InputInvoiceSupplierResolutionStatus NewStatus { get; set; }
    public int? OldSupplierId { get; set; }
    public int? NewSupplierId { get; set; }
    public int? CandidateCount { get; set; }

    [StringLength(1000)]
    public string? Reason { get; set; }

    public int ActorUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Store Store { get; set; } = default!;
    public InputInvoiceHead InputInvoiceHead { get; set; } = default!;
    public StockDocument? StockDocument { get; set; }
    public Supplier? OldSupplier { get; set; }
    public Supplier? NewSupplier { get; set; }
}
