namespace GaoApp.Application.Common.Exceptions.Pos;

/// <summary>
/// Danh sách mã lỗi chuẩn dùng riêng cho POS.
/// 
/// Quy ước:
/// POS_<NHOM>_<TEN_LOI>
/// 
/// Ví dụ:
/// - POS_SHIFT_NOT_OPEN
/// - POS_SHIFT_OPENED_BY_ANOTHER_USER
/// - POS_CART_NEW_BLOCKED_BY_ACTIVE_CART
/// 
/// Lợi ích:
/// - Backend và Frontend cùng hiểu một lỗi theo một mã duy nhất
/// - Không phải check theo text tiếng Việt
/// - Dễ switch-case ở JS/UI
/// - Dễ log / tra cứu / thống kê lỗi production
/// </summary>
public static class PosErrorCodes
{
    // =========================================================
    // CONTEXT / SESSION / USER
    // =========================================================

    /// <summary>
    /// Không resolve được terminal hiện tại từ POS context / claim / runtime.
    /// </summary>
    public const string ContextTerminalNotResolved = "POS_CONTEXT_TERMINAL_NOT_RESOLVED";

    /// <summary>
    /// Không resolve được user hiện tại.
    /// </summary>
    public const string ContextUserNotResolved = "POS_CONTEXT_USER_NOT_RESOLVED";

    /// <summary>
    /// Phiên đăng nhập / phiên thao tác không còn hợp lệ.
    /// </summary>
    public const string ContextSessionInvalid = "POS_CONTEXT_SESSION_INVALID";


    // =========================================================
    // SHIFT - MỞ CA / ĐÓNG CA / SỞ HỮU CA
    // =========================================================

    /// <summary>
    /// Terminal hiện tại chưa mở ca POS.
    /// </summary>
    public const string ShiftNotOpen = "POS_SHIFT_NOT_OPEN";

    /// <summary>
    /// Terminal hiện tại đã có ca POS đang mở.
    /// Dùng như mã tổng quát nếu chưa tách rõ thuộc về ai.
    /// </summary>
    public const string ShiftAlreadyOpen = "POS_SHIFT_ALREADY_OPEN";

    /// <summary>
    /// Terminal đã có ca mở và ca đó thuộc chính user hiện tại.
    /// UX nên cho "vào lại ca hiện tại", không báo lỗi cứng.
    /// </summary>
    public const string ShiftOpenOwnedByCurrentUser = "POS_SHIFT_OPEN_OWNED_BY_CURRENT_USER";

    /// <summary>
    /// Terminal đã có ca mở của người khác.
    /// </summary>
    public const string ShiftOpenedByAnotherUser = "POS_SHIFT_OPENED_BY_ANOTHER_USER";

    /// <summary>
    /// Khi thao tác POS nhưng ca hiện tại thuộc người khác.
    /// </summary>
    public const string ShiftOwnedByAnotherUser = "POS_SHIFT_OWNED_BY_ANOTHER_USER";

    /// <summary>
    /// Không có quyền tiếp quản terminal / ca hiện tại.
    /// </summary>
    public const string ShiftTakeoverNotAllowed = "POS_SHIFT_TAKEOVER_NOT_ALLOWED";

    /// <summary>
    /// Mở ca nhưng chưa chọn kho xuất bán.
    /// </summary>
    public const string ShiftOpenWarehouseRequired = "POS_SHIFT_OPEN_WAREHOUSE_REQUIRED";

    /// <summary>
    /// Mở ca nhưng kho được chọn không tồn tại.
    /// </summary>
    public const string ShiftOpenWarehouseNotFound = "POS_SHIFT_OPEN_WAREHOUSE_NOT_FOUND";

    /// <summary>
    /// Mở ca nhưng kho được chọn đã ngưng hoạt động.
    /// </summary>
    public const string ShiftOpenWarehouseInactive = "POS_SHIFT_OPEN_WAREHOUSE_INACTIVE";

    /// <summary>
    /// Đóng ca bị chặn vì còn đơn giữ của ca hiện tại.
    /// </summary>
    public const string ShiftCloseBlockedHeldOrders = "POS_SHIFT_CLOSE_BLOCKED_HELD_ORDERS";

    /// <summary>
    /// Đóng ca bị chặn vì còn draft / giỏ có dữ liệu của ca hiện tại.
    /// </summary>
    public const string ShiftCloseBlockedActiveDrafts = "POS_SHIFT_CLOSE_BLOCKED_ACTIVE_DRAFTS";

    /// <summary>
    /// Trạng thái ca đã thay đổi ở nơi khác, cần refresh lại.
    /// </summary>
    public const string ShiftCloseStateConflict = "POS_SHIFT_CLOSE_STATE_CONFLICT";


    // =========================================================
    // CART / DRAFT / HOLD / RESUME
    // =========================================================

    /// <summary>
    /// Không tìm thấy giỏ hiện tại hợp lệ.
    /// </summary>
    public const string CartCurrentNotFound = "POS_CART_CURRENT_NOT_FOUND";

    /// <summary>
    /// Giỏ hiện tại không còn ở trạng thái hợp lệ để chỉnh sửa.
    /// Ví dụ: chỉ Draft mới được làm current cart.
    /// </summary>
    public const string CartCurrentInvalidStatus = "POS_CART_CURRENT_INVALID_STATUS";

    /// <summary>
    /// Đơn / giỏ không thuộc ca POS hiện tại.
    /// </summary>
    public const string CartNotInCurrentShift = "POS_CART_NOT_IN_CURRENT_SHIFT";

    /// <summary>
    /// Đơn / giỏ không thuộc terminal POS hiện tại.
    /// </summary>
    public const string CartNotInCurrentTerminal = "POS_CART_NOT_IN_CURRENT_TERMINAL";

    /// <summary>
    /// Không thể giữ giỏ trống.
    /// </summary>
    public const string CartEmptyCannotHold = "POS_CART_EMPTY_CANNOT_HOLD";

    /// <summary>
    /// Không thể giữ đơn vì một dòng hàng không đủ tồn khả dụng tại kho xuất bán legacy.
    /// </summary>
    public const string CartHoldInsufficientInventory =
        "POS_CART_HOLD_INSUFFICIENT_INVENTORY";

    /// <summary>
    /// Không thể tạo giỏ mới vì giỏ hiện tại đang có dữ liệu.
    /// </summary>
    public const string CartNewBlockedByActiveCart = "POS_CART_NEW_BLOCKED_BY_ACTIVE_CART";

    /// <summary>
    /// Resume đơn nhưng đơn không còn ở trạng thái đang giữ.
    /// </summary>
    public const string CartResumeNotHeld = "POS_CART_RESUME_NOT_HELD";

    /// <summary>
    /// Đơn giữ không thuộc cửa hàng hiện tại.
    /// </summary>
    public const string CartResumeStoreMismatch = "POS_CART_RESUME_STORE_MISMATCH";


    // =========================================================
    // CHECKOUT / FINALIZE / KHO XUẤT BÁN
    // =========================================================

    /// <summary>
    /// Không thể chốt đơn vì giỏ chưa có sản phẩm.
    /// </summary>
    public const string CheckoutOrderEmpty = "POS_CHECKOUT_ORDER_EMPTY";

    /// <summary>
    /// Không thể chốt đơn vì tổng tiền không hợp lệ.
    /// </summary>
    public const string CheckoutTotalInvalid = "POS_CHECKOUT_TOTAL_INVALID";

    /// <summary>
    /// Không thể chốt đơn vì khách thanh toán chưa đủ.
    /// </summary>
    public const string CheckoutPaymentNotEnough = "POS_CHECKOUT_PAYMENT_NOT_ENOUGH";

    /// <summary>
    /// Không tìm thấy ca POS gắn với đơn hàng.
    /// </summary>
    public const string CheckoutShiftNotFound = "POS_CHECKOUT_SHIFT_NOT_FOUND";

    /// <summary>
    /// Không thể chốt đơn vì ca POS của đơn đã đóng.
    /// </summary>
    public const string CheckoutShiftClosed = "POS_CHECKOUT_SHIFT_CLOSED";

    /// <summary>
    /// Ca POS chưa cấu hình kho xuất bán.
    /// </summary>
    public const string CheckoutShiftWarehouseNotConfigured = "POS_CHECKOUT_SHIFT_WAREHOUSE_NOT_CONFIGURED";

    /// <summary>
    /// Không tìm thấy kho xuất bán của ca POS.
    /// </summary>
    public const string CheckoutWarehouseNotFound = "POS_CHECKOUT_WAREHOUSE_NOT_FOUND";

    /// <summary>
    /// Kho xuất bán của ca POS đã ngưng hoạt động.
    /// </summary>
    public const string CheckoutWarehouseInactive = "POS_CHECKOUT_WAREHOUSE_INACTIVE";

    /// <summary>
    /// Feature đã bật nhưng cấu hình HKD/kho/activation không đạt preflight.
    /// </summary>
    public const string CheckoutLegalEntityConfigurationInvalid =
        "POS_CHECKOUT_LEGAL_ENTITY_CONFIGURATION_INVALID";

    /// <summary>
    /// Tổng tồn khả dụng của các kho HKD không đủ cho đơn.
    /// </summary>
    public const string CheckoutLegalEntityInsufficientInventory =
        "POS_CHECKOUT_LEGAL_ENTITY_INSUFFICIENT_INVENTORY";

    /// <summary>
    /// Đơn đã có allocation, thường do finalize lặp hoặc dữ liệu không nhất quán.
    /// </summary>
    public const string CheckoutLegalEntityAllocationAlreadyExists =
        "POS_CHECKOUT_LEGAL_ENTITY_ALLOCATION_ALREADY_EXISTS";

    /// <summary>
    /// Movement của fragment allocation bị trùng/không được tạo.
    /// </summary>
    public const string CheckoutLegalEntityMovementConflict =
        "POS_CHECKOUT_LEGAL_ENTITY_MOVEMENT_CONFLICT";

    /// <summary>
    /// Void/refund của đơn allocation bị khóa đến Phase 22.6.
    /// </summary>
    public const string LegalEntityReversalPending =
        "POS_LEGAL_ENTITY_REVERSAL_PENDING";


    // =========================================================
    // CASH TRANSACTION (nộp/rút/điều chỉnh tiền mặt)
    // =========================================================

    /// <summary>
    /// Số tiền cash transaction không hợp lệ.
    /// </summary>
    public const string CashAmountInvalid = "POS_CASH_AMOUNT_INVALID";

    /// <summary>
    /// Chưa nhập lý do cho cash transaction.
    /// </summary>
    public const string CashReasonRequired = "POS_CASH_REASON_REQUIRED";


    // =========================================================
    // AUTH / PERMISSION
    // =========================================================

    /// <summary>
    /// Chưa đăng nhập hoặc phiên đăng nhập đã hết hạn.
    /// </summary>
    public const string AuthUnauthorized = "POS_AUTH_UNAUTHORIZED";

    /// <summary>
    /// Không có quyền thực hiện thao tác.
    /// </summary>
    public const string AuthForbidden = "POS_AUTH_FORBIDDEN";
}
