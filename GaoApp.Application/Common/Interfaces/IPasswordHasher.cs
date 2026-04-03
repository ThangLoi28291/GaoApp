namespace GaoApp.Application.Common.Interfaces;

/// <summary>
/// Abstraction cho hash/verify mật khẩu.
/// Application chỉ biết interface, không phụ thuộc Infrastructure.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}