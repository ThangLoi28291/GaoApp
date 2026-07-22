namespace GaoApp.Application.Common.Results;

/// <summary>
/// Kết quả có dữ liệu trả về.
/// </summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value)
        : base(true, Error.None)
    {
        _value = value;
    }

    private Result(Error error)
        : base(false, error)
    {
        _value = default;
    }

    private Result(IReadOnlyList<ValidationError> validationErrors)
        : base(false, new Error("ValidationError", "Dữ liệu đầu vào không hợp lệ."), validationErrors)
    {
        _value = default;
    }

    /// <summary>
    /// Chỉ đọc Value khi IsSuccess = true.
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Không thể lấy Value từ Result thất bại.");

    public static Result<T> Success(T value) => new(value);

    public static new Result<T> Failure(Error error) => new(error);

    /// <summary>
    /// Factory validation failure có dữ liệu trả về.
    /// Chủ ý che factory không generic của Result để giữ kiểu trả về Result&lt;T&gt;.
    /// </summary>
    public static new Result<T> ValidationFailure(
        IEnumerable<ValidationError> validationErrors)
    {
        var errors = validationErrors?.ToList()
            ?? new List<ValidationError>();

        if (errors.Count == 0)
        {
            throw new ArgumentException(
                "ValidationFailure phải có ít nhất 1 lỗi.",
                nameof(validationErrors));
        }

        return new(errors);
    }
}