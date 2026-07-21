using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

public class ApprovalActionRequest
{
    [StringLength(1000)]
    public string? Note { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
