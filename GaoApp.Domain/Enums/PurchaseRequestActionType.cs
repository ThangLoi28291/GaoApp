namespace GaoApp.Domain.Enums;

public enum PurchaseRequestActionType
{
    Created = 1,
    Updated = 2,
    Submitted = 3,
    ReturnedForRevision = 4,
    Rejected = 5,
    Approved = 6,
    ConvertedToPurchaseOrder = 7,
    Cancelled = 8,
    PurchaseOrderCancelled = 9,
    PurchaseOrderRejected = 10
}
