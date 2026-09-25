namespace GaoApp.Domain.Enums;

public enum InputInvoiceDetailReconciliationState
{
    Matched = 0,
    QuantityMismatch = 1,
    AmountMismatch = 2,
    VatMismatch = 3,
    CombinedMismatch = 4,
    Unmatched = 5,
    Ignored = 6,
    NeedsReview = 7,
    Incomplete = 8
}
