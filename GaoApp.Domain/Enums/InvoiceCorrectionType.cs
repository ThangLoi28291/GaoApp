namespace GaoApp.Domain.Enums;

public enum InvoiceCorrectionType
{
    /// <summary>
    /// Hóa đơn mới thay thế toàn bộ hóa đơn gốc.
    /// Viettel adjustmentType = 3.
    /// </summary>
    Replacement = 1,

    /// <summary>
    /// Hóa đơn điều chỉnh tiền.
    /// Viettel adjustmentType = 5, adjustmentInvoiceType = 1.
    /// </summary>
    AdjustmentAmount = 2,

    /// <summary>
    /// Hóa đơn điều chỉnh thông tin.
    /// Viettel adjustmentType = 5, adjustmentInvoiceType = 2.
    /// </summary>
    AdjustmentInfo = 3
}