using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("SalesReturns")]
public class SalesReturn : BaseStoreEntity, IAuditTrackedEntity
{
    [Required]
    [StringLength(30)]
    public string ReturnNumber { get; set; } = default!;

    /// <summary>
    /// Đơn bán gốc.
    /// Không sửa lịch sử đơn này, chỉ liên kết để truy vết.
    /// </summary>
    public int OrderId { get; set; }

    public Order Order { get; set; } = default!;

    /// <summary>
    /// Ca POS phát sinh thao tác return/refund.
    /// Dùng để khớp báo cáo ca.
    /// </summary>
    public int POSShiftId { get; set; }

    public POSShift POSShift { get; set; } = default!;

    public SalesReturnType Type { get; set; }

    public SalesReturnStatus Status { get; set; }
        = SalesReturnStatus.Draft;

    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = default!;

    [StringLength(1000)]
    public string? Note { get; set; }

    /// <summary>
    /// Tổng tiền quy đổi từ các dòng hàng trả.
    /// Đây là subtotal nghiệp vụ hàng.
    /// </summary>
    public decimal ReturnSubtotal { get; set; }

    /// <summary>
    /// Tổng tiền refund thực tế đã hoàn ra.
    /// </summary>
    public decimal RefundTotal { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "decimal(18,2)")] public decimal DepositRestoredTotal { get; set; }

    public int CreatedByUserId { get; set; }

    public int? CompletedByUserId { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public ICollection<SalesReturnLine> Lines { get; set; }
        = new List<SalesReturnLine>();

    public ICollection<SalesReturnPayment> Payments { get; set; }
        = new List<SalesReturnPayment>();
}