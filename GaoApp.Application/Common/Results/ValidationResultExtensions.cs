using FluentValidation.Results;

namespace GaoApp.Application.Common.Results;

/// <summary>
/// Helper convert FluentValidation result sang Result.
/// </summary>
public static class ValidationResultExtensions
{
    public static Result ToFailureResult(this ValidationResult validationResult)
    {
        if (validationResult.IsValid)
        {
            throw new InvalidOperationException("ValidationResult hợp lệ thì không thể convert sang failure result.");
        }

        var errors = validationResult.Errors
            .Select(x => new ValidationError(
                x.PropertyName ?? string.Empty,
                x.ErrorMessage))
            .ToList();

        return Result.ValidationFailure(errors);
    }

    public static Result<T> ToFailureResult<T>(this ValidationResult validationResult)
    {
        if (validationResult.IsValid)
        {
            throw new InvalidOperationException("ValidationResult hợp lệ thì không thể convert sang failure result.");
        }

        var errors = validationResult.Errors
            .Select(x => new ValidationError(
                x.PropertyName ?? string.Empty,
                x.ErrorMessage))
            .ToList();

        return Result.ValidationFailure<T>(errors);
    }

}