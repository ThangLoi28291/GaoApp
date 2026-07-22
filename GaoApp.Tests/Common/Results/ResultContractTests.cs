using GaoApp.Application.Common.Results;

namespace GaoApp.Tests.Common.Results;

public sealed class ResultContractTests
{
    [Fact]
    public void Generic_validation_failure_should_preserve_failure_contract()
    {
        var validationError = new ValidationError(
            "Name",
            "Tên là bắt buộc.");

        var result = Result<int>.ValidationFailure(
            new[] { validationError });

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.True(result.HasValidationErrors);

        var storedError = Assert.Single(result.ValidationErrors);
        Assert.Equal(validationError, storedError);

        Assert.Throws<InvalidOperationException>(
            () => _ = result.Value);
    }

    [Fact]
    public void Generic_validation_failure_should_reject_empty_error_list()
    {
        Assert.Throws<ArgumentException>(
            () => Result<int>.ValidationFailure(
                Array.Empty<ValidationError>()));
    }

    [Fact]
    public void Result_generic_forwarder_should_return_generic_failure()
    {
        var validationError = new ValidationError(
            "Code",
            "Mã không hợp lệ.");

        var result = Result.ValidationFailure<int>(
            new[] { validationError });

        Assert.IsType<Result<int>>(result);
        Assert.True(result.IsFailure);
        Assert.True(result.HasValidationErrors);

        var storedError = Assert.Single(result.ValidationErrors);
        Assert.Equal(validationError, storedError);
    }
}