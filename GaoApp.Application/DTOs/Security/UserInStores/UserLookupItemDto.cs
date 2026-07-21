namespace GaoApp.Application.DTOs.Security.UserInStores;

/// <summary>
/// Item lookup người dùng để bind dropdown/autocomplete.
/// </summary>
public class UserLookupItemDto
{
    public int UserId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    /// <summary>
    /// Text gộp sẵn để UI dùng nhanh.
    /// </summary>
    public string DisplayText
        => string.IsNullOrWhiteSpace(Email)
            ? $"{FullName} ({Username})"
            : $"{FullName} ({Username} - {Email})";
}