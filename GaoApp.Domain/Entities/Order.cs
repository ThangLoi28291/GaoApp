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
    /// <summary>Loại khách tại lúc chốt đơn; null dành cho dữ liệu trước tính năng báo cáo.</summary>
    [StringLength(20)]
    public string? CustomerPriceTierSnapshot { get; set; }
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

    public int? CustomerDepositId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DepositAmount { get; set; }
    public bool IsCreditSale { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal CreditInitialBalance { get; set; }
    public DateTime? CreditDueDate { get; set; }
    public Guid? CreditRequestId { get; set; }
    [StringLength(500)] public string? CreditNote { get; set; }

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
    // =========================
    // INVOICE ISSUANCE ROUTING
    // =========================

    /// <summary>
    /// Quyết định phát hành hóa đơn của Order.
    /// Unselected: POS đã hoàn tất bán hàng nhưng chưa xác nhận ý định hóa đơn.
    /// Automatic: bán cho người tiêu dùng, AutoInvoice chịu trách nhiệm.
    /// Manual: khách yêu cầu hóa đơn, chờ thông tin/phát hành thủ công.
    /// </summary>
    public InvoiceIssuanceRoute InvoiceIssuanceRoute { get; set; }
        = InvoiceIssuanceRoute.Unselected;

    /// <summary>
    /// Thời điểm route được người dùng chọn/đổi lần cuối.
    /// Backfill historical có thể để null.
    /// </summary>
    public DateTime? InvoiceIssuanceRouteSelectedAtUtc { get; set; }

    /// <summary>
    /// User thực hiện lựa chọn/đổi route.
    /// Backfill historical có thể để null.
    /// </summary>
    public int? InvoiceIssuanceRouteSelectedByUserId { get; set; }

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
    // MULTI LEGAL ENTITY
    // =========================

    /// <summary>
    /// Số LegalEntity thực tế đã cấp hàng khi finalize. Dữ liệu legacy/flag tắt = 0.
    /// </summary>
    public int LegalEntityCount { get; set; }

    /// <summary>
    /// True khi một đơn gộp lấy hàng từ nhiều LegalEntity.
    /// </summary>
    public bool HasMultipleLegalEntities { get; set; }

    /// <summary>
    /// Thời điểm allocation đã được khóa và ghi cùng transaction finalize.
    /// </summary>
    public DateTime? LegalEntityAllocatedAtUtc { get; set; }

    /// <summary>
    /// Snapshot chế độ Multi LegalEntity tại thời điểm tạo order. Kill switch chỉ
    /// tác động order tạo sau khi tắt; order/giỏ đang xử lý giữ nguyên chế độ gốc.
    /// </summary>
    public bool UseMultiLegalEntity { get; set; }

    /// <summary>
    /// Có giá trị nghĩa là order đã được Phase 22.9 chụp chế độ rõ ràng.
    /// Null dành cho dữ liệu cũ và được xử lý theo compatibility policy.
    /// </summary>
    public DateTime? LegalEntityModeCapturedAtUtc { get; set; }

    /// <summary>
    /// Mốc activation được chụp nếu order thuộc cohort Multi LegalEntity.
    /// </summary>
    public DateTime? LegalEntityActivationAtUtcSnapshot { get; set; }

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
    /// <summary>
    /// Tổng tiền giảm bằng voucher tích điểm.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal VoucherDiscountTotal { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal PromotionDiscountTotal { get; set; }
    /// <summary>
    /// Tổng tiền giảm do combo.
    /// Ví dụ: SP1 + SP2 + SP3 giá gốc 33.000, combo còn 30.000
    /// => ComboDiscountTotal = 3.000.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal ComboDiscountTotal { get; set; }

    /// <summary>
    /// PromotionId combo đang áp dụng.
    /// Phase 1 chỉ lưu combo tốt nhất.
    /// </summary>
    public int? ComboPromotionId { get; set; }

    /// <summary>
    /// Tên combo snapshot.
    /// </summary>
    [StringLength(200)]
    public string? ComboPromotionName { get; set; }

    /// <summary>
    /// Ghi chú combo hiển thị POS / bill.
    /// </summary>
    [StringLength(500)]
    public string? ComboPromotionNote { get; set; }

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
    public ICollection<OrderRewardVoucher> RewardVouchers { get; set; } = new List<OrderRewardVoucher>();
    public ICollection<OrderLegalEntityAllocation> LegalEntityAllocations { get; set; } = new List<OrderLegalEntityAllocation>();
}
