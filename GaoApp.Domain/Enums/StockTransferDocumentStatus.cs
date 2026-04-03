namespace GaoApp.Domain.Enums;

/// <summary>
/// Trạng thái phiếu chuyển kho.
/// 
/// Luồng chuẩn:
/// Draft -> PendingApproval -> Confirmed
///                           -> Rejected
/// 
/// Ghi chú nghiệp vụ phase hiện tại:
/// - Draft: phiếu mới tạo, chỉnh sửa được
/// - PendingApproval: đã submit, hiện tại vẫn tạm cho sửa
///   (sau này sẽ nâng cấp phân quyền ai được sửa)
/// - Rejected: phiếu bị từ chối, cho sửa lại để submit tiếp
/// - Confirmed: đã xác nhận, khóa toàn bộ, không cho sửa/xóa
/// </summary>
public enum StockTransferDocumentStatus
{
    /// <summary>
    /// Phiếu nháp, mới tạo.
    /// </summary>
    Draft = 0,

    /// <summary>
    /// Đã submit chờ duyệt/xác nhận.
    /// 
    /// Giai đoạn hiện tại:
    /// vẫn tạm cho sửa theo rule nghiệp vụ đã chốt.
    /// Sau này sẽ bổ sung phân quyền cụ thể.
    /// </summary>
    PendingApproval = 1,

    /// <summary>
    /// Phiếu bị từ chối.
    /// Có thể sửa lại rồi submit lại.
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// Phiếu đã xác nhận.
    /// Khi ở trạng thái này:
    /// - đã sinh movement kho
    /// - không được sửa
    /// - không được xóa
    /// </summary>
    Confirmed = 3
}