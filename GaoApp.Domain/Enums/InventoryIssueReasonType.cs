namespace GaoApp.Domain.Enums;

/// <summary>
/// Nguyên nhân gốc của issue tồn âm.
/// </summary>
public enum InventoryIssueReasonType
{
    Unknown = 0,
    MissingPurchaseEntry = 1,
    StockCountMismatch = 2,
    BarcodeOrUnitMappingError = 3,
    OperationalLoss = 4,
    ManualAdjustmentNeeded = 5,
    Other = 99
}