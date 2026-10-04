using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

// A library/review record, independent of receipt posting and accounting approval.
public sealed class InputInvoiceLibraryEntry : BaseStoreEntity, IAuditTrackedEntity
{
    [MaxLength(64)] public string IdentityHash { get; set; } = "";
    [MaxLength(64)] public string XmlHash { get; set; } = "";
    [MaxLength(350)] public string DocumentKey { get; set; } = "";
    [MaxLength(50)] public string? SourceSupplierTaxCode { get; set; }
    [MaxLength(50)] public string SellerTaxCode { get; set; } = "";
    [MaxLength(300)] public string SellerName { get; set; } = "";
    [MaxLength(50)] public string BuyerTaxCode { get; set; } = "";
    [MaxLength(300)] public string BuyerName { get; set; } = "";
    [MaxLength(50)] public string Series { get; set; } = "";
    [MaxLength(50)] public string Number { get; set; } = "";
    public DateTime InvoiceDate { get; set; }
    public decimal BeforeTax { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    // 0 original, 1 replacement, 2 adjustment, 3 unrecognized relation.
    public int InvoiceKind { get; set; }
    [MaxLength(500)] public string? RelatedInvoice { get; set; }
    public bool HasPdf { get; set; }
    public int ReviewStatus { get; set; }
    [MaxLength(1000)] public string? ReviewNote { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public int? ReviewedBy { get; set; }
    public ICollection<InputInvoiceLibraryReview> Reviews { get; set; } = new List<InputInvoiceLibraryReview>();
}

public sealed class InputInvoiceLibraryReview : BaseStoreEntity
{
    public int InputInvoiceLibraryEntryId { get; set; }
    public InputInvoiceLibraryEntry Entry { get; set; } = null!;
    public int PreviousStatus { get; set; }
    public int Status { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    public int ActorId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
