namespace GaoApp.Application.DTOs.Security.UserInStores;

/// <summary>
/// Dữ liệu load cho màn Edit UserInStore.
/// </summary>
public class UserInStoreEditDto
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public int RoleId { get; set; }

    public bool IsActive { get; set; }
    public string? PhoneNumber { get; set; }

    public string? PositionName { get; set; }

    public DateTime? JoinedDate { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Danh sách role của store hiện tại để bind dropdown.
    /// </summary>
    public List<RoleLookupItemDto> AvailableRoles { get; set; } = new();
}