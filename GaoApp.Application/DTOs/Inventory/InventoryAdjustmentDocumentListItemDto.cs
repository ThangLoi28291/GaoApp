using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// DTO dùng cho màn danh sách phiếu điều chỉnh.
/// </summary>
public class InventoryAdjustmentDocumentListItemDto
{
    public int Id { get; set; }

    public string DocumentNo { get; set; } = string.Empty;

    public DateTime DocumentDate { get; set; }

    public int WarehouseId { get; set; }

    public string WarehouseName { get; set; } = string.Empty;

    public InventoryTransactionType AdjustmentType { get; set; }

    public string AdjustmentTypeText { get; set; } = string.Empty;

    public InventoryAdjustmentDocumentStatus Status { get; set; }

    public string StatusText { get; set; } = string.Empty;

    public InventoryAdjustmentReasonType ReasonType { get; set; }

    public string ReasonTypeText { get; set; } = string.Empty;

    public int LineCount { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }
}