using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Request từ chối phiếu điều chỉnh kho.
/// </summary>
public class RejectInventoryAdjustmentDocumentRequest
{
    public int Id { get; set; }

    [StringLength(1000)]
    public string? ApprovalNote { get; set; }
}