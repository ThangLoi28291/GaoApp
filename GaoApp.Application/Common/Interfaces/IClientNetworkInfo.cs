namespace GaoApp.Application.Common.Interfaces;

/// <summary>
/// Service đọc thông tin mạng phía client hiện tại.
/// Tách riêng để không nhúng HttpContext vào service nghiệp vụ.
/// </summary>
public interface IClientNetworkInfo
{
    string? GetClientIp();
}