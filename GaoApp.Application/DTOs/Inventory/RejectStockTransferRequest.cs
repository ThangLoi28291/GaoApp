using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Request reject phiếu chuyển kho.
/// </summary>
public class RejectStockTransferRequest
{
    [StringLength(500)]
    public string? Reason { get; set; }
}