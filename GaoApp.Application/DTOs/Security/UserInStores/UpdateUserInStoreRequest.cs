using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Security.UserInStores;

/// <summary>
/// Request cập nhật mapping user-store-role.
/// </summary>
public class UpdateUserInStoreRequest
{
    [Required]
    public int Id { get; set; }

    [Required(ErrorMessage = "Vai trò là bắt buộc.")]
    public int RoleId { get; set; }

    public bool IsActive { get; set; }
    public string? PhoneNumber { get; set; }

    public string? PositionName { get; set; }

    public DateTime? JoinedDate { get; set; }

    public string? Note { get; set; }
}