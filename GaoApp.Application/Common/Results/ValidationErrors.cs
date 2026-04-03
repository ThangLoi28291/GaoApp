using FluentValidation.Results;

namespace GaoApp.Application.Common.Results;

/// <summary>
/// Helper xử lý danh sách lỗi validation.
/// </summary>
public static class ValidationErrors
{
    /// <summary>
    /// Convert danh sách lỗi FluentValidation thành Error tổng.
    /// Dùng khi cần 1 lỗi tổng quát.
    /// </summary>
    public static Error FromFluentValidation(IEnumerable<ValidationFailure> failures)
    {
        var messages = failures
            .Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage))
            .Select(x => x.ErrorMessage.Trim())
            .Distinct()
            .ToList();

        var message = messages.Count == 0
            ? "Dữ liệu không hợp lệ."
            : string.Join(" | ", messages);

        return new Error("Validation.Invalid", message);
    }

    /// <summary>
    /// Convert danh sách lỗi FluentValidation thành list ValidationError chi tiết theo field.
    /// </summary>
    public static List<ValidationError> ToValidationErrorList(IEnumerable<ValidationFailure> failures)
    {
        return failures
            .Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage))
            .Select(x => new ValidationError(
                x.PropertyName ?? string.Empty,
                x.ErrorMessage.Trim()))
            .ToList();
    }
}