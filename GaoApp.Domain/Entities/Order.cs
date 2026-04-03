using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("Orders")]
public class Order : BaseStoreEntity, IAuditTrackedEntity
{
    /// <summary>
    /// Số đơn hàng sinh khi finalize.
    /// Draft có thể chưa có số đơn.
    /// </summary>
    [StringLength(30)]
    public string? OrderNumber { get; set; }

    /// <summary>
    /// Trạng thái nghiệp vụ bán hàng.
    /// Chỉ phản ánh vòng đời bán hàng, không phản ánh tồn kho hậu kiểm.
    /// </summary>
    public OrderStatus Status { get; set; } = OrderStatus.Draft;

    /// <summary>
    /// Trạng thái thanh toán của đơn.
    /// </summary>
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    /// <summary>
    /// Khách hàng gắn với đơn, có thể null nếu bán lẻ không chọn khách.
    /// </summary>
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // =========================
    // TỔNG TIỀN
    // =========================

    /// <summary>
    /// Tổng tiền hàng trước giảm giá.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    /// <summary>
    /// Tổng giảm giá của các dòng hàng.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountTotal { get; set; }

    /// <summary>
    /// Giảm giá cấp đơn.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal OrderDiscount { get; set; }

    /// <summary>
    /// Tổng tiền cuối cùng cần thanh toán.
    /// = Subtotal - DiscountTotal - OrderDiscount
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal GrandTotal { get; set; }

    // =========================
    // THANH TOÁN / CÔNG NỢ
    // =========================

    /// <summary>
    /// Tổng số tiền khách đã thanh toán.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal PaidTotal { get; set; }

    /// <summary>
    /// Công nợ còn lại của đơn.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal BalanceDue { get; set; }

    /// <summary>
    /// Tiền thối lại khách nếu khách đưa dư.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal ChangeDue { get; set; }

    // =========================
    // GHI CHÚ
    // =========================

    [StringLength(500)]
    public string? Note { get; set; }

    // =========================
    // POS SHIFT
    // =========================

    /// <summary>
    /// Mỗi order POS bắt buộc thuộc một ca bán hàng.
    /// </summary>
    public int POSShiftId { get; set; }
    public POSShift POSShift { get; set; } = default!;

    // =========================
    // THỜI GIAN NGHIỆP VỤ
    // =========================

    /// <summary>
    /// Thời điểm finalize đơn.
    /// Chỉ có khi Status = Completed.
    /// </summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// Thời điểm đưa đơn vào giữ chỗ / hold.
    /// </summary>
    public DateTime? HeldAtUtc { get; set; }

    /// <summary>
    /// Ghi chú khi giữ đơn.
    /// </summary>
    [StringLength(500)]
    public string? HoldNote { get; set; }

    /// <summary>
    /// Mã ngắn hỗ trợ tìm nhanh đơn hold.
    /// </summary>
    [StringLength(50)]
    public string? HoldCode { get; set; }

    /// <summary>
    /// Cờ cho biết đơn có reservation hay không.
    /// </summary>
    public bool HasReservation { get; set; }

    /// <summary>
    /// Thời điểm bắt đầu reservation.
    /// </summary>
    public DateTime? ReservedAtUtc { get; set; }

    // =========================
    // INVENTORY ISSUE MIRROR
    // =========================

    /// <summary>
    /// Cờ mirror nhanh cho biết order này có issue inventory hậu kiểm hay không.
    /// Ví dụ:
    /// - âm kho sau khi finalize
    /// - line bán đang dùng provisional cost
    /// 
    /// Nguồn sự thật vẫn là bảng OrderInventoryIssue.
    /// Field này chỉ để query/list/filter nhanh.
    /// </summary>
    public bool HasInventoryIssue { get; set; }

    /// <summary>
    /// Trạng thái mirror nhanh của issue inventory.
    /// Không thay thế bảng OrderInventoryIssue, chỉ phục vụ đọc nhanh.
    /// </summary>
    public InventoryResolutionStatus InventoryResolutionStatus { get; set; } = InventoryResolutionStatus.None;

    /// <summary>
    /// Thời điểm issue inventory hiện tại / gần nhất được mở ra.
    /// </summary>
    public DateTime? InventoryIssueOpenedAtUtc { get; set; }

    /// <summary>
    /// Thời điểm quản lý duyệt issue inventory.
    /// </summary>
    public DateTime? InventoryIssueApprovedAtUtc { get; set; }

    // =========================
    // NAVIGATION
    // =========================

    /// <summary>
    /// Thông tin issue inventory hậu kiểm của order.
    /// Một order tối đa 1 issue đang hoạt động theo hướng thiết kế hiện tại.
    /// </summary>
    public virtual OrderInventoryIssue? InventoryIssue { get; set; }

    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
    public ICollection<OrderPayment> Payments { get; set; } = new List<OrderPayment>();
    public ICollection<SalesReturn> SalesReturns { get; set; } = new List<SalesReturn>();
}