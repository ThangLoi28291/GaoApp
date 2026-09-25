namespace GaoApp.Domain.Enums;

public enum InputInvoiceSupplierResolutionEventType
{
    AutoResolved = 1,
    Reevaluated = 2,
    ManualCandidateSelected = 3,
    ReceiptAligned = 4,
    CanonicalCorrected = 5
}
