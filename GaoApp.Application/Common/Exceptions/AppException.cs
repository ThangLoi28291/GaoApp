namespace GaoApp.Application.Common.Exceptions;

/// <summary>
/// Base exception cho lỗi nghiệp vụ có chủ đích.
/// Dùng để phân biệt với lỗi hệ thống không mong muốn.
/// </summary>
public abstract class AppException : Exception
{
    /// <summary>
    /// Thông điệp do application exception chủ động công bố cho public boundary.
    /// Technical exceptions không kế thừa contract này.
    /// </summary>
    public string SafeMessage => Message;

    protected AppException(string message)
        : base(message)
    {
    }

    protected AppException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
