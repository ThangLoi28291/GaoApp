using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Request tạo phiếu điều chỉnh kho.
/// Tạo phiếu chỉ lưu Draft, chưa tác động tồn kho.
/// </summary>
public class CreateInventoryAdjustmentDocumentRequest
{
    public int WarehouseId { get; set; }

    public DateTime? DocumentDate { get; set; }

    public InventoryTransactionType AdjustmentType { get; set; }

    public InventoryAdjustmentReasonType ReasonType { get; set; }
        = InventoryAdjustmentReasonType.Other;

    [StringLength(1000)]
    public string? Note { get; set; }

    public List<InventoryAdjustmentLineRequestDto> Lines { get; set; } = new();
}