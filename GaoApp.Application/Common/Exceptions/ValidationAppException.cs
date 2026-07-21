namespace GaoApp.Application.Common.Exceptions;

/// <summary>
/// Dùng cho lỗi validate nghiệp vụ.
/// Middleware sẽ map ra HTTP 400.
/// </summary>
public class ValidationAppException : AppException
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationAppException(string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationAppException(string message, IDictionary<string, string[]> errors)
        : base(message)
    {
        Errors = errors;
    }
}