using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// DTO chi tiết phiếu kiểm kê.
/// Dùng cho màn hình detail phiếu kiểm kê.
/// </summary>
public class StockCountDocumentDto
{
    public int Id { get; set; }

    /// <summary>
    /// Số phiếu kiểm kê.
    /// </summary>
    public string DocumentNo { get; set; } = string.Empty;
    public string? DocumentName { get; set; }

    /// <summary>
    /// Ngày chứng từ.
    /// </summary>
    public DateTime DocumentDate { get; set; }

    /// <summary>
    /// Kho kiểm kê.
    /// </summary>
    public int WarehouseId { get; set; }

    public string WarehouseName { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái phiếu kiểm kê.
    /// </summary>
    public StockCountDocumentStatus Status { get; set; }

    /// <summary>
    /// Ghi chú chung của phiếu.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Thời điểm confirm phiếu.
    /// </summary>
    public DateTime? ConfirmedAtUtc { get; set; }

    /// <summary>
    /// Người confirm phiếu.
    /// Phase hiện tại chưa map tên user, mới giữ user id.
    /// </summary>
    public int? ConfirmedByUserId { get; set; }

    /// <summary>
    /// Danh sách line chi tiết của phiếu kiểm kê.
    /// </summary>
    public List<StockCountLineDto> Lines { get; set; } = new();

    // =========================================================
    // SUMMARY PHẦN 11
    // =========================================================

    /// <summary>
    /// Tổng số line của phiếu kiểm kê.
    /// </summary>
    public int TotalLines { get; set; }

    /// <summary>
    /// Số line có chênh lệch kiểm kê.
    /// Điều kiện: DifferenceQtyBase != 0
    /// </summary>
    public int DifferenceLineCount { get; set; }

    /// <summary>
    /// Số line tăng tồn sau kiểm kê.
    /// Điều kiện: DifferenceQtyBase > 0
    /// </summary>
    public int GainLineCount { get; set; }

    /// <summary>
    /// Số line giảm tồn sau kiểm kê.
    /// Điều kiện: DifferenceQtyBase < 0
    /// </summary>
    public int LossLineCount { get; set; }

    /// <summary>
    /// Tổng số lượng tăng theo đơn vị gốc.
    /// Chỉ cộng các line có DifferenceQtyBase > 0.
    /// </summary>
    public decimal TotalGainQtyBase { get; set; }

    /// <summary>
    /// Tổng số lượng giảm theo đơn vị gốc.
    /// Trả về số dương để dễ đọc báo cáo.
    /// Ví dụ line = -5 thì cộng vào tổng là 5.
    /// </summary>
    public decimal TotalLossQtyBase { get; set; }
}