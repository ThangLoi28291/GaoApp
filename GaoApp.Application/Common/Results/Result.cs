namespace GaoApp.Application.Common.Results;

/// <summary>
/// Kết quả không có dữ liệu trả về.
/// Dùng cho update/delete/toggle...
/// 
/// Hỗ trợ:
/// - lỗi đơn giản qua Error
/// - nhiều lỗi validation qua ValidationErrors
/// </summary>
public class Result
{
    protected Result(
        bool isSuccess,
        Error error,
        IReadOnlyList<ValidationError>? validationErrors = null)
    {
        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("Result success không được đi kèm lỗi.", nameof(error));
        }

        if (isSuccess && validationErrors is { Count: > 0 })
        {
            throw new ArgumentException("Result success không được có validation errors.", nameof(validationErrors));
        }

        if (!isSuccess && error == Error.None && (validationErrors == null || validationErrors.Count == 0))
        {
            throw new ArgumentException("Result failure phải có Error hoặc ValidationErrors.");
        }

        IsSuccess = isSuccess;
        Error = error;
        ValidationErrors = validationErrors ?? Array.Empty<ValidationError>();
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Lỗi đơn chính.
    /// Với validation nhiều lỗi, Error có thể là ValidationError tổng quát.
    /// </summary>
    public Error Error { get; }

    /// <summary>
    /// Danh sách lỗi validation chi tiết.
    /// </summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; }

    /// <summary>
    /// Có phải lỗi validation nhiều field hay không.
    /// </summary>
    public bool HasValidationErrors => ValidationErrors.Count > 0;

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result ValidationFailure(IEnumerable<ValidationError> validationErrors)
    {
        var errors = validationErrors?.ToList() ?? new List<ValidationError>();

        if (errors.Count == 0)
        {
            throw new ArgumentException("ValidationFailure phải có ít nhất 1 lỗi.", nameof(validationErrors));
        }

        return new(false, new Error("ValidationError", "Dữ liệu đầu vào không hợp lệ."), errors);
    }

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);

    public static Result<T> ValidationFailure<T>(IEnumerable<ValidationError> validationErrors)
        => Result<T>.ValidationFailure(validationErrors);
}