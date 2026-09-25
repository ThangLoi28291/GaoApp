namespace GaoApp.Application.DTOs.Auth;

/// <summary>
/// Kết quả đăng nhập thành công.
/// Trả về user + store + terminal để controller gắn claims.
/// </summary>
public class LoginResponse
{
    // Internal authentication material: never serialize this DTO field into an API response.
    [System.Text.Json.Serialization.JsonIgnore]
    public string SessionStamp { get; set; } = string.Empty;

    public int UserId { get; set; }
    public string UserName { get; set; } = default!;
    public string? FullName { get; set; }

    public int RoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;

    public int StoreId { get; set; }

    public int TerminalId { get; set; }
    public string TerminalCode { get; set; } = default!;
    public string TerminalName { get; set; } = default!;

    public string? ClientIp { get; set; }

    /// <summary>
    /// DeviceKey đã dùng để nhận diện/ghép thiết bị.
    /// Controller sẽ ghi vào cookie.
    /// </summary>
    public string? DeviceKey { get; set; }

    /// <summary>
    /// True nếu lần đăng nhập này vừa ghép thiết bị mới.
    /// </summary>
    public bool DevicePaired { get; set; }
}
