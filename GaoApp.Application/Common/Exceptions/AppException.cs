namespace GaoApp.Application.Common.Exceptions;

/// <summary>
/// Base exception cho lỗi nghiệp vụ có chủ đích.
/// Dùng để phân biệt với lỗi hệ thống không mong muốn.
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string message)
        : base(message)
    {
    }
}