using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Phiếu bàn giao cuối ca POS.
/// Tự sinh sau khi đóng ca thành công.
/// Lưu snapshot số liệu ca để sau này in lại không bị thay đổi.
/// </summary>
[Table("POSShiftClosingSlips")]
public class POSShiftClosingSlip : BaseStoreEntity, IAuditTrackedEntity
{
    public int POSShiftId { get; set; }
    public POSShift POSShift { get; set; } = default!;

    [Required]
    [StringLength(50)]
    public string SlipCode { get; set; } = default!;

    [Required]
    [StringLength(100)]
    public string BarcodeValue { get; set; } = default!;

    public POSShiftClosingSlipStatus Status { get; set; } = POSShiftClosingSlipStatus.Created;

    public int OpenedByUserId { get; set; }
    public int ClosedByUserId { get; set; }

    public DateTime OpenedAtUtc { get; set; }
    public DateTime ClosedAtUtc { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal OpeningCash { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CashSalesTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal NonCashSalesTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CashRefundTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal NonCashRefundTotal { get; set; }

    public int RefundCount { get; set; }
    public int VoidCount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CashInTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CashOutTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ClosingCashExpected { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ClosingCashActual { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CashDifference { get; set; }

    [StringLength(500)]
    public string? CloseNote { get; set; }

    public DateTime? PrintedAtUtc { get; set; }

    public ICollection<POSShiftClosingSlipDenomination> Denominations { get; set; } = new List<POSShiftClosingSlipDenomination>();

    public void MarkPrinted()
    {
        if (Status == POSShiftClosingSlipStatus.Cancelled)
            throw new InvalidOperationException("Phiếu đã hủy, không thể in.");

        Status = POSShiftClosingSlipStatus.Printed;
        PrintedAtUtc ??= DateTime.UtcNow;
    }
}