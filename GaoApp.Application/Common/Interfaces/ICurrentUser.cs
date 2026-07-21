namespace GaoApp.Application.Common.Interfaces;

/// <summary>
/// Đại diện cho user đang đăng nhập ở request hiện tại.
/// Dùng để ghi log / history / audit mà không cần query DB.
/// </summary>
public interface ICurrentUser
{
    int? UserId { get; }
    string? UserName { get; }
    int? TerminalId { get; }
    string? TerminalCode { get; }
    bool IsAuthenticated { get; }

}