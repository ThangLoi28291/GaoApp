namespace GaoApp.Domain.Enums;

public enum InputInvoiceBuyerOwnerResolutionStatus
{
    NotEvaluated = 0,
    MissingBuyerTaxCode = 1,
    NotFound = 2,
    Ambiguous = 3,
    Resolved = 4
}
