namespace GaoApp.Application.Common.Exceptions;

/// <summary>
/// Dùng khi dữ liệu không tồn tại.
/// Middleware sẽ map ra HTTP 404.
/// </summary>
public class NotFoundAppException : AppException
{
    public NotFoundAppException(string message)
        : base(message)
    {
    }
}