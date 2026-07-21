namespace GaoApp.Application.Common.Exceptions;

/// <summary>
/// Dùng cho lỗi không đủ quyền / sai tenant / forbidden business.
/// Middleware sẽ map ra HTTP 403.
/// </summary>
public class ForbiddenAppException : AppException
{
    public ForbiddenAppException(string message)
        : base(message)
    {
    }
}