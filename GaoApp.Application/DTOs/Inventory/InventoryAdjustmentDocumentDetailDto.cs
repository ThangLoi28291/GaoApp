using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// DTO chi tiết phiếu điều chỉnh kho.
/// </summary>
public class InventoryAdjustmentDocumentDetailDto
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

    public string? Note { get; set; }

    public string? ApprovalNote { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    public int? SubmittedByUserId { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public int? ApprovedByUserId { get; set; }

    public DateTime? RejectedAtUtc { get; set; }

    public int? RejectedByUserId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public int? CancelledByUserId { get; set; }

    public List<InventoryAdjustmentLineDto> Lines { get; set; } = new();
}