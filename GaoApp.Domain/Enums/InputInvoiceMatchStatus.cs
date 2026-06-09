namespace GaoApp.Domain.Enums;

/// <summary>
/// Trạng thái đối chiếu giữa dòng nhập kho và dòng hóa đơn XML.
/// </summary>
public enum InputInvoiceMatchStatus
{
    /// <summary>
    /// Chưa đối chiếu.
    /// </summary>
    None = 0,

    /// <summary>
    /// Đã map và số lượng/thành tiền khớp tương đối.
    /// </summary>
    Matched = 1,

    /// <summary>
    /// Đã map nhưng số lượng lệch.
    /// </summary>
    QuantityMismatch = 2,

    /// <summary>
    /// Đã map nhưng thành tiền lệch.
    /// </summary>
    AmountMismatch = 3,

    /// <summary>
    /// Đã map nhưng vừa lệch số lượng vừa lệch tiền.
    /// </summary>
    QuantityAndAmountMismatch = 4,

    /// <summary>
    /// User tắt: dòng này không thuộc hóa đơn XML.
    /// </summary>
    Excluded = 5
}