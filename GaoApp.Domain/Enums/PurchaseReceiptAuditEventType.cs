namespace GaoApp.Domain.Enums;

public enum PurchaseReceiptAuditEventType
{
    ReceiptCreated = 1,
    PhysicalHeaderChanged = 2,
    PhysicalLineAdded = 3,
    PhysicalLineChanged = 4,
    PhysicalLineDeleted = 5,
    SubmittedForApproval = 6,
    RevisionRequested = 7,
    RevisionReturnedForEditing = 8,
    RevisionRequestDismissed = 9,
    ReceiptRejected = 10,
    CommercialApprovalConfirmed = 11,
    GenericReceiptConfirmed = 12
}
