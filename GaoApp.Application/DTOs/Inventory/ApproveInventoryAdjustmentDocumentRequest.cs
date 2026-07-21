using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Request duyệt phiếu điều chỉnh kho.
/// Duyệt xong mới phát sinh InventoryMovement.
/// </summary>
public class ApproveInventoryAdjustmentDocumentRequest
{
    public int Id { get; set; }

    [StringLength(1000)]
    public string? ApprovalNote { get; set; }
}