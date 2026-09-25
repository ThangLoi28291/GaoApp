namespace GaoApp.Domain.Enums;

public enum InputInvoiceReconciliationState
{
    NotApplicable = 0,
    Incomplete = 1,
    Matched = 2,
    Mismatch = 3,
    AcceptedMismatch = 4
}
