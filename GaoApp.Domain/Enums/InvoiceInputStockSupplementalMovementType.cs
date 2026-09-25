namespace GaoApp.Domain.Enums;

/// <summary>
/// Nguồn lịch sử bổ sung chỉ phục vụ sổ tồn hóa đơn đầu vào.
/// Không đại diện cho tồn kho vật lý, FIFO, valuation hoặc cost.
/// </summary>
public enum InvoiceInputStockSupplementalMovementType
{
    LegacyOpening = 1,
    LegacyInbound = 2,
    LegacyReconciliationAdjustment = 3,
    LegacyOutbound = 4
}
