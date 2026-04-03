namespace GaoApp.Application.DTOs.Auth;

/// <summary>
/// Kết quả đăng nhập thành công.
/// Trả về đầy đủ user + store + terminal để controller gắn claims.
/// </summary>
public class LoginResponse
{
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

}