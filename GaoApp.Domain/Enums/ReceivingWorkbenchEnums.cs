namespace GaoApp.Domain.Enums;

public enum ReceivingSessionState
{
    None = 0,
    Active = 1,
    Frozen = 2,
    Closed = 3
}

public enum ReceiptAllocationKind
{
    Direct = 0,
    PurchaseOrder = 1,
    OutsidePo = 2
}

public enum OutsidePoDecisionStatus
{
    NotApplicable = 0,
    Pending = 1,
    Accepted = 2,
    Rejected = 3
}

public enum PurchaseReceivingActionType
{
    Add = 1,
    Accumulate = 2,
    Bulk = 3,
    Edit = 4,
    Remove = 5,
    Undo = 6,
    ProvisionalCapture = 7,
    ProvisionalAccumulate = 8,
    ProvisionalEdit = 9,
    ProvisionalRemove = 10,
    ProvisionalLinkExisting = 11,
    ProvisionalQuickCreate = 12
}
