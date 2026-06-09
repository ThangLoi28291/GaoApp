namespace GaoApp.Domain.Enums;

/// <summary>
/// Lý do điều chỉnh kho.
/// </summary>
public enum InventoryAdjustmentReasonType
{
    Other = 0,
    StockTakingDifference = 1,
    Damaged = 2,
    Expired = 3,
    Lost = 4,
    Found = 5,
    OpeningCorrection = 6,
    CostCorrection = 7
}