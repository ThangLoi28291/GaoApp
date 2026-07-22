using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

public class InvoiceCorrectionCase : BaseStoreEntity
{
    public int OriginalInvoiceHeadId { get; set; }

    public InvoiceHead OriginalInvoiceHead { get; set; } = default!;

    public int? NewInvoiceHeadId { get; set; }

    public InvoiceHead? NewInvoiceHead { get; set; }

    public InvoiceCorrectionType Type { get; set; }

    public InvoiceCorrectionStatus Status { get; set; }
        = InvoiceCorrectionStatus.Draft;

    /// <summary>
    /// Lý do sai sót. Gửi sang Viettel ở adjustedNote.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Số văn bản thỏa thuận hoặc biên bản thỏa thuận.
    /// Gửi sang Viettel ở additionalReferenceDesc.
    /// </summary>
    public string AgreementDocumentNo { get; set; } = string.Empty;

    /// <summary>
    /// Ngày văn bản thỏa thuận.
    /// Gửi sang Viettel ở additionalReferenceDate.
    /// </summary>
    public DateTime AgreementDateUtc { get; set; }

    public string? Note { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime? IssuedAtUtc { get; set; }

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }
}