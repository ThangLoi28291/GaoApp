using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Hồ sơ case xử lý tồn kho hậu kiểm cho 1 đơn bán đã completed.
/// Mỗi order tối đa có 1 case đang hoạt động.
/// </summary>
public class OrderInventoryIssue : BaseStoreEntity
{
    public int OrderId { get; set; }

    /// <summary>
    /// Mã case hiển thị cho vận hành.
    /// Ví dụ: INVISS-20260324-0001
    /// </summary>
    public string Code { get; set; } = null!;

    /// <summary>
    /// Trạng thái xử lý hiện tại của case.
    /// </summary>
    public InventoryResolutionStatus Status { get; set; } = InventoryResolutionStatus.PendingResolution;

    /// <summary>
    /// Mức độ cảnh báo theo SLA / thời gian pending.
    /// </summary>
    public InventoryIssueSeverity Severity { get; set; } = InventoryIssueSeverity.Normal;

    /// <summary>
    /// Thời điểm mở case.
    /// Đây là mốc gốc để tính SLA pending.
    /// </summary>
    public DateTime OpenedAtUtc { get; set; }

    /// <summary>
    /// Deadline xử lý.
    /// Theo rule hiện tại: overdue khi nowUtc > DueAtUtc.
    /// Ví dụ OpenedAtUtc + 1 ngày.
    /// </summary>
    public DateTime DueAtUtc { get; set; }

    /// <summary>
    /// Thời điểm case được chuyển sang chờ duyệt.
    /// </summary>
    public DateTime? ReadyForApprovalAtUtc { get; set; }

    /// <summary>
    /// Thời điểm quản lý duyệt.
    /// </summary>
    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>
    /// User quản lý duyệt.
    /// </summary>
    public int? ApprovedByUserId { get; set; }

    /// <summary>
    /// Thời điểm quản lý từ chối gần nhất.
    /// </summary>
    public DateTime? RejectedAtUtc { get; set; }

    /// <summary>
    /// User quản lý từ chối gần nhất.
    /// </summary>
    public int? RejectedByUserId { get; set; }

    /// <summary>
    /// Nguyên nhân gốc gần nhất / cuối cùng của case.
    /// </summary>
    public InventoryIssueReasonType ReasonType { get; set; } = InventoryIssueReasonType.Unknown;

    /// <summary>
    /// Ghi chú nội bộ.
    /// </summary>
    public string? InternalNote { get; set; }

    /// <summary>
    /// Cờ tính sẵn để query/dashboard/UI nhanh.
    /// Chỉ true khi case đang mở và đã quá hạn xử lý.
    /// </summary>
    public bool IsOverdue { get; set; }

    /// <summary>
    /// Thời điểm case chính thức bị đánh dấu overdue lần đầu.
    /// Dùng để hiển thị UI, sort, audit nhẹ và tránh phải suy luận lại.
    /// </summary>
    public DateTime? OverdueSinceUtc { get; set; }

    /// <summary>
    /// Thời điểm gần nhất hệ thống đã gửi cảnh báo overdue.
    /// Dùng để chống spam notification/SignalR.
    /// </summary>
    public DateTime? LastOverdueNotifiedAtUtc { get; set; }

    public virtual Order Order { get; set; } = null!;
    public virtual User? ApprovedByUser { get; set; }
    public virtual User? RejectedByUser { get; set; }

    /// <summary>
    /// Lần cuối hệ thống refresh auto-resolution cho case.
    /// </summary>
    public DateTime? LastAutoResolvedAtUtc { get; set; }

    /// <summary>
    /// Tổng số line hệ thống đã auto-resolve được ở lần chạy gần nhất.
    /// </summary>
    public int AutoResolvedLineCount { get; set; }

    public ICollection<OrderInventoryIssueLineAllocation> Allocations { get; set; } = new List<OrderInventoryIssueLineAllocation>();
    public virtual ICollection<OrderInventoryIssueLine> Lines { get; set; } = new List<OrderInventoryIssueLine>();
    public virtual ICollection<OrderInventoryIssueAction> Actions { get; set; } = new List<OrderInventoryIssueAction>();

    /// <summary>
    /// Case còn đang mở để tiếp tục xử lý nghiệp vụ.
    /// Các trạng thái đã duyệt / từ chối thì không còn xem là open.
    /// </summary>
    public bool IsOpen()
    {
        return Status == InventoryResolutionStatus.PendingResolution
            || Status == InventoryResolutionStatus.ReadyForApproval;
    }

    /// <summary>
    /// Kiểm tra case có bị overdue tại thời điểm nowUtc hay không.
    /// Rule hiện tại:
    /// - chỉ case đang mở mới xét overdue
    /// - nowUtc > DueAtUtc thì overdue
    /// </summary>
    public bool ShouldBeOverdue(DateTime nowUtc)
    {
        if (!IsOpen())
        {
            return false;
        }

        return nowUtc > DueAtUtc;
    }

    /// <summary>
    /// Đồng bộ cờ overdue theo thời điểm hiện tại.
    /// Trả về true nếu trạng thái overdue bị thay đổi để service phía trên biết mà lưu DB / bắn notification.
    /// 
    /// Lưu ý:
    /// - Không tự gửi notification ở đây
    /// - Không tự đổi severity ở đây
    /// - Chỉ cập nhật state tối thiểu của overdue
    /// </summary>
    public bool RefreshOverdueState(DateTime nowUtc)
    {
        var shouldBeOverdue = ShouldBeOverdue(nowUtc);

        // Không thay đổi gì
        if (IsOverdue == shouldBeOverdue)
        {
            return false;
        }

        // Vừa chuyển sang overdue
        if (shouldBeOverdue)
        {
            IsOverdue = true;
            OverdueSinceUtc ??= nowUtc;
            return true;
        }

        // Không còn overdue nữa
        // Trường hợp thường gặp:
        // - case đã approve
        // - case đã reject
        // - hoặc deadline được điều chỉnh lại
        IsOverdue = false;
        OverdueSinceUtc = null;
        return true;
    }

    /// <summary>
    /// Đánh dấu vừa gửi cảnh báo overdue.
    /// Tách riêng để service chủ động gọi sau khi tạo Notification / push SignalR thành công.
    /// </summary>
    public void MarkOverdueNotified(DateTime nowUtc)
    {
        LastOverdueNotifiedAtUtc = nowUtc;
    }
}