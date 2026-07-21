namespace GaoApp.Application.Common.Exceptions;

/// <summary>
/// Dùng cho lỗi xung đột dữ liệu:
/// ví dụ trùng code, trùng tên, version conflict...
/// Middleware sẽ map ra HTTP 409.
/// </summary>
public class ConflictAppException : AppException
{
    public ConflictAppException(string message)
        : base(message)
    {
    }
}