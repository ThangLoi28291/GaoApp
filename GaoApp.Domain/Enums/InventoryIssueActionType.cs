namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại hành động trong timeline xử lý case tồn âm.
/// </summary>
public enum InventoryIssueActionType
{
    CaseCreated = 1,
    NegativeDetected = 2,
    NoteAdded = 3,
    LinkedReceipt = 4,
    LinkedAdjustment = 5,
    MarkedReadyForApproval = 6,
    Approved = 7,
    Rejected = 8,
    Escalated = 9,
    Reopened = 10,
    AutoResolved = 11,
    AutoResolutionRefreshed = 12

}