namespace GaoApp.Application.Common.Results;

/// <summary>
/// Mô tả 1 lỗi nghiệp vụ / ứng dụng theo dạng chuẩn.
/// </summary>
public sealed class Error
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public Error(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public string Code { get; }
    public string Message { get; }

    public override string ToString() => $"{Code}: {Message}";

    // =========================================================
    // 🔥 ADD THESE METHODS
    // =========================================================

    public static Error NotFound(string message)
        => new("NotFound", message);

    public static Error Failure(string message)
        => new("Failure", message);

    public static Error Conflict(string message)
        => new("Conflict", message);

    public static Error Validation(string code, string message)
        => new(code, message);

    public static Error Validation(string message)
        => new("ValidationError", message);
}