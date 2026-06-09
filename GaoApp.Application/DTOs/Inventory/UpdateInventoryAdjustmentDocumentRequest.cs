using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Request sửa phiếu điều chỉnh kho.
/// Chỉ cho sửa khi phiếu còn Draft hoặc bị Rejected nếu sau này cho phép sửa lại.
/// </summary>
public class UpdateInventoryAdjustmentDocumentRequest
{
    public int Id { get; set; }

    public int WarehouseId { get; set; }

    public DateTime? DocumentDate { get; set; }

    public InventoryTransactionType AdjustmentType { get; set; }

    public InventoryAdjustmentReasonType ReasonType { get; set; }
        = InventoryAdjustmentReasonType.Other;

    [StringLength(1000)]
    public string? Note { get; set; }

    public List<InventoryAdjustmentLineRequestDto> Lines { get; set; } = new();
}