namespace GaoApp.Domain.Enums;

public enum PurchaseOrderActionType
{
    Created = 1,
    Updated = 2,
    Submitted = 3,
    Approved = 4,
    Rejected = 5,
    ReturnedForRevision = 6,
    SentToSupplier = 7,
    ReceiptCreated = 8,
    ReceiptApproved = 9,
    ShortClosed = 10,
    Cancelled = 11
}
