namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại giao dịch phát sinh biến động tồn kho.
/// Dùng để phân biệt nhập, xuất, điều chỉnh, chuyển kho...
/// </summary>
public enum InventoryTransactionType
{
    /// <summary>
    /// Số dư đầu kỳ / khởi tạo tồn ban đầu.
    /// </summary>
    OpeningBalance = 1,

    // =============================
    // PURCHASE
    // =============================

    /// <summary>
    /// Nhập hàng từ nhà cung cấp.
    /// </summary>
    PurchaseReceipt = 10,

    /// <summary>
    /// Trả hàng cho nhà cung cấp.
    /// </summary>
    PurchaseReturn = 11,

    // =============================
    // SALES / POS
    // =============================

    /// <summary>
    /// Xuất bán hàng.
    /// </summary>
    SaleIssue = 20,

    /// <summary>
    /// Tên alias để code POS cũ / code service mới có thể dùng.
    /// Bản chất giống SaleIssue.
    /// </summary>
    //SaleOut = 20,

    /// <summary>
    /// Nhập lại hàng do bán hàng bị trả lại.
    /// Hiện chưa dùng trực tiếp trong flow POS phase hiện tại,
    /// ưu tiên dùng CustomerReturnIn để diễn đạt rõ nghiệp vụ refund.
    /// </summary>
    SaleReturn = 21,

    /// <summary>
    /// Nhập lại kho do void đơn bán sau khi đã chốt.
    /// </summary>
    SaleVoidIn = 22,

    /// <summary>
    /// Nhập lại kho do khách trả hàng / hoàn tiền.
    /// </summary>
    CustomerReturnIn = 23,

    // =============================
    // MANUAL ADJUSTMENT
    // =============================

    /// <summary>
    /// Điều chỉnh tăng tồn kho thủ công.
    /// </summary>
    AdjustmentIncrease = 30,

    /// <summary>
    /// Điều chỉnh giảm tồn kho thủ công.
    /// </summary>
    AdjustmentDecrease = 31,

    // =============================
    // TRANSFER
    // =============================

    /// <summary>
    /// Nhập kho do chuyển từ kho khác sang.
    /// </summary>
    TransferIn = 40,

    /// <summary>
    /// Xuất kho để chuyển sang kho khác.
    /// </summary>
    TransferOut = 41,

    // =============================
    // STOCK COUNT
    // =============================

    /// <summary>
    /// Chênh lệch tăng sau kiểm kê.
    /// </summary>
    StockCountGain = 50,

    /// <summary>
    /// Chênh lệch giảm sau kiểm kê.
    /// </summary>
    StockCountLoss = 51,

    // =============================
    // RESERVE
    // =============================

    /// <summary>
    /// Giữ hàng / reserve hàng.
    /// Chưa trừ OnHand nhưng tăng Reserved.
    /// </summary>
    Hold = 60,

    /// <summary>
    /// Nhả giữ hàng / bỏ reserve.
    /// </summary>
    ReleaseHold = 61,

    /// <summary>
    /// Điều chỉnh lại giá trị cho provisional cost mà không đổi số lượng.
    /// </summary>
    Revaluation = 70,
}