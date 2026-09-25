using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Common.Exceptions;

/// <summary>A fail-closed buyer-owner decision with bounded, audit-safe evidence.</summary>
public sealed class InputInvoiceOwnerGuardException : BusinessRuleException
{
    public InputInvoiceOwnerGuardException(
        string reasonCode,
        string message,
        InputInvoiceBuyerOwnerResolutionStatus resolutionStatus,
        int? receiptOwnerLegalEntityId = null,
        int? invoiceOwnerLegalEntityId = null,
        InputInvoiceHead? attemptedInvoice = null,
        string? normalizedBuyerTaxCode = null,
        int? durableInputInvoiceHeadId = null)
        : base(message)
    {
        ReasonCode = reasonCode;
        ResolutionStatus = resolutionStatus;
        ReceiptOwnerLegalEntityId = receiptOwnerLegalEntityId;
        InvoiceOwnerLegalEntityId = invoiceOwnerLegalEntityId;
        InputInvoiceHeadId = durableInputInvoiceHeadId is > 0
            ? durableInputInvoiceHeadId
            : null;
        BuyerTaxCode = attemptedInvoice?.BuyerTaxCode;
        NormalizedBuyerTaxCode = normalizedBuyerTaxCode;
        NormalizedSellerTaxCode = attemptedInvoice?.NormalizedSellerTaxCode;
        NormalizedInvoiceSeries = attemptedInvoice?.NormalizedInvoiceSeries;
        NormalizedInvoiceNumber = attemptedInvoice?.NormalizedInvoiceNumber;
        InvoiceIdentityDate = attemptedInvoice?.InvoiceIdentityDate;
    }

    public string ReasonCode { get; }
    public InputInvoiceBuyerOwnerResolutionStatus ResolutionStatus { get; }
    public int? ReceiptOwnerLegalEntityId { get; }
    public int? InvoiceOwnerLegalEntityId { get; }
    public int? InputInvoiceHeadId { get; }
    public string? BuyerTaxCode { get; }
    public string? NormalizedBuyerTaxCode { get; }
    public string? NormalizedSellerTaxCode { get; }
    public string? NormalizedInvoiceSeries { get; }
    public string? NormalizedInvoiceNumber { get; }
    public DateTime? InvoiceIdentityDate { get; }
}
