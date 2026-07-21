using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Phiếu kiểm kê kho.
/// 
/// Vai trò:
/// - Người dùng thao tác trên phiếu này
/// - Phiếu lưu trạng thái kiểm kê
/// - Khi Confirmed, hệ thống sẽ sinh InventoryTransaction chênh lệch ở phase sau
/// </summary>
[Table("StockCountDocument")]
public class StockCountDocument : BaseStoreEntity
{
    /// <summary>
    /// Kho được kiểm kê.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "WarehouseId không hợp lệ.")]
    public int WarehouseId { get; set; }

    public Warehouse Warehouse { get; set; } = null!;

    /// <summary>
    /// Số phiếu kiểm kê.
    /// Ví dụ: KK-20260314-0001
    /// </summary>
    [Required]
    [StringLength(50)]
    public string DocumentNo { get; set; } = string.Empty;
    // StockCountDocument.cs
    [StringLength(250)]
    public string? DocumentName { get; set; }

    /// <summary>
    /// Ngày kiểm kê trên chứng từ.
    /// Có thể là ngày người dùng chọn, không bắt buộc phải trùng CreatedAtUtc.
    /// </summary>
    public DateTime DocumentDate { get; set; } = DateTime.Now;

    /// <summary>
    /// Trạng thái phiếu.
    /// </summary>
    public StockCountDocumentStatus Status { get; set; } = StockCountDocumentStatus.Draft;

    /// <summary>
    /// Ghi chú chung của phiếu kiểm kê.
    /// </summary>
    [StringLength(1000)]
    public string? Note { get; set; }

    /// <summary>
    /// Thời điểm xác nhận phiếu kiểm kê.
    /// </summary>
    public DateTime? ConfirmedAtUtc { get; set; }

    /// <summary>
    /// User xác nhận phiếu.
    /// Giai đoạn đầu có thể để nullable, chưa gắn user thực tế.
    /// </summary>
    public int? ConfirmedByUserId { get; set; }

    /// <summary>
    /// Các dòng kiểm kê.
    /// </summary>
    public ICollection<StockCountLine> Lines { get; set; } = new List<StockCountLine>();
}