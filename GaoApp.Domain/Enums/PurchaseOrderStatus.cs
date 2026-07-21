namespace GaoApp.Domain.Enums;

public enum PurchaseOrderStatus
{
    Draft = 1,
    PendingApproval = 2,
    ReturnedForRevision = 3,
    Rejected = 4,
    Approved = 5,
    SentToSupplier = 6,
    PartiallyReceived = 7,
    FullyReceived = 8,
    ShortClosed = 9,
    Cancelled = 10
}
