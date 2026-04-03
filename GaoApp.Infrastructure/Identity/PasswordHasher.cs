using GaoApp.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace GaoApp.Infrastructure.Identity;

/// <summary>
/// Wrapper cho ASP.NET Identity PasswordHasher.
/// 
/// Mục tiêu:
/// - Thống nhất chuẩn hash/verify password toàn hệ thống
/// - Tương thích với PasswordHasherHelper cũ đang dùng để seed dữ liệu
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password)
    {
        return _hasher.HashPassword(null!, password);
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash))
            return false;

        var result = _hasher.VerifyHashedPassword(null!, hash, password);

        return result == PasswordVerificationResult.Success
            || result == PasswordVerificationResult.SuccessRehashNeeded;
    }
}