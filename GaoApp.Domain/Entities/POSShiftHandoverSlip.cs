using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Phiếu bàn giao / nhận ca POS.
/// 
/// Luồng nghiệp vụ:
/// - Quản lý tạo phiếu trước, nhập số tờ từng mệnh giá.
/// - Hệ thống sinh SlipCode + BarcodeValue.
/// - In phiếu cho nhân viên.
/// - Nhân viên quét BarcodeValue để mở ca.
/// - Khi mở ca thành công, phiếu chuyển Used và gắn với POSShift.
/// </summary>
[Table("POSShiftHandoverSlips")]
public class POSShiftHandoverSlip : BaseStoreEntity, IAuditTrackedEntity
{
    /// <summary>
    /// Mã phiếu dễ đọc.
    /// Ví dụ: HOS-20260604-0001.
    /// </summary>
    [Required]
    [StringLength(50)]
    public string SlipCode { get; set; } = default!;

    /// <summary>
    /// Giá trị barcode in trên phiếu.
    /// Có thể giống SlipCode hoặc thêm prefix.
    /// Ví dụ: POS-HANDOVER:HOS-20260604-0001.
    /// </summary>
    [Required]
    [StringLength(100)]
    public string BarcodeValue { get; set; } = default!;

    public POSShiftHandoverSlipStatus Status { get; set; } = POSShiftHandoverSlipStatus.Draft;

    /// <summary>
    /// Terminal dự kiến nhận ca.
    /// Có thể null nếu phiếu dùng chung, nhưng nên có để kiểm soát tốt.
    /// </summary>
    public int? TerminalId { get; set; }
    public POSTerminal? Terminal { get; set; }

    /// <summary>
    /// Kho xuất bán dự kiến.
    /// </summary>
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = default!;

    /// <summary>
    /// Người quản lý/người lập phiếu.
    /// </summary>
    public int CreatedByUserId { get; set; }

    /// <summary>
    /// Nhân viên dự kiến nhận ca.
    /// Có thể null nếu chưa chỉ định.
    /// </summary>
    public int? AssignedToUserId { get; set; }

    /// <summary>
    /// Tổng tiền đầu ca theo phiếu.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal OpeningCashTotal { get; set; }

    /// <summary>
    /// Ca POS được mở từ phiếu này.
    /// Null nếu phiếu chưa dùng.
    /// </summary>
    public int? UsedPOSShiftId { get; set; }
    public POSShift? UsedPOSShift { get; set; }

    public DateTime? PrintedAtUtc { get; set; }

    public DateTime? UsedAtUtc { get; set; }

    public int? UsedByUserId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public int? CancelledByUserId { get; set; }

    [StringLength(300)]
    public string? CancelReason { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public ICollection<POSShiftHandoverSlipDenomination> Denominations { get; set; } = new List<POSShiftHandoverSlipDenomination>();

    public void RecalcTotal()
    {
        OpeningCashTotal = Denominations
            .Where(x => !x.IsDeleted)
            .Sum(x => x.Amount);

        if (OpeningCashTotal < 0)
            OpeningCashTotal = 0;
    }

    public void MarkPrinted()
    {
        if (Status == POSShiftHandoverSlipStatus.Cancelled)
            throw new InvalidOperationException("Phiếu đã hủy, không thể in.");

        if (Status == POSShiftHandoverSlipStatus.Used)
            throw new InvalidOperationException("Phiếu đã sử dụng, không thể in lại theo luồng in lần đầu.");

        Status = POSShiftHandoverSlipStatus.Printed;
        PrintedAtUtc ??= DateTime.UtcNow;
    }

    public void MarkUsed(int posShiftId, int usedByUserId)
    {
        if (Status == POSShiftHandoverSlipStatus.Cancelled)
            throw new InvalidOperationException("Phiếu đã hủy, không thể sử dụng.");

        if (Status == POSShiftHandoverSlipStatus.Used)
            throw new InvalidOperationException("Phiếu đã được sử dụng.");

        UsedPOSShiftId = posShiftId;
        UsedByUserId = usedByUserId;
        UsedAtUtc = DateTime.UtcNow;
        Status = POSShiftHandoverSlipStatus.Used;
    }

    public void Cancel(int cancelledByUserId, string reason)
    {
        if (Status == POSShiftHandoverSlipStatus.Used)
            throw new InvalidOperationException("Phiếu đã sử dụng, không thể hủy.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Lý do hủy phiếu không được để trống.");

        Status = POSShiftHandoverSlipStatus.Cancelled;
        CancelledByUserId = cancelledByUserId;
        CancelledAtUtc = DateTime.UtcNow;
        CancelReason = reason.Trim();
    }
}