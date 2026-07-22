using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.DTOs.POSShifts;

public class POSShiftCashTransactionReasonContractTests
{
    [Fact]
    public void Request_with_valid_reason_should_pass_validation()
    {
        var request = CreateValidRequest();
        request.Reason = "Bổ sung tiền lẻ cho ca bán hàng";

        var validationResults = Validate(request);

        Assert.Empty(validationResults);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("     ")]
    public void Request_with_empty_or_whitespace_reason_should_fail_validation(
        string reason)
    {
        var request = CreateValidRequest();
        request.Reason = reason;

        var validationResults = Validate(request);

        Assert.Contains(
            validationResults,
            result => result.MemberNames.Contains(
                nameof(CreatePosShiftCashTransactionRequest.Reason)));
    }

    [Fact]
    public void Request_with_null_reason_should_fail_validation()
    {
        var request = CreateValidRequest();

        // Mô phỏng dữ liệu JSON/client truyền null vào property non-nullable.
        var reasonProperty =
            typeof(CreatePosShiftCashTransactionRequest).GetProperty(
                nameof(CreatePosShiftCashTransactionRequest.Reason));

        if (reasonProperty is null)
        {
            throw new InvalidOperationException(
                "Không tìm thấy property Reason trên request.");
        }

        reasonProperty.SetValue(request, null);

        var validationResults = Validate(request);

        Assert.Contains(
            validationResults,
            result => result.MemberNames.Contains(
                nameof(CreatePosShiftCashTransactionRequest.Reason)));
    }

    [Fact]
    public void Request_with_reason_over_300_characters_should_fail_validation()
    {
        var request = CreateValidRequest();
        request.Reason = new string('A', 301);

        var validationResults = Validate(request);

        Assert.Contains(
            validationResults,
            result => result.MemberNames.Contains(
                nameof(CreatePosShiftCashTransactionRequest.Reason)));
    }

    [Fact]
    public void Request_default_reason_should_be_empty_but_not_null()
    {
        var request = new CreatePosShiftCashTransactionRequest();

        Assert.NotNull(request.Reason);
        Assert.Equal(string.Empty, request.Reason);

        var validationResults = Validate(request);

        Assert.Contains(
            validationResults,
            result => result.MemberNames.Contains(
                nameof(CreatePosShiftCashTransactionRequest.Reason)));
    }

    [Fact]
    public void Response_dto_should_serialize_with_non_null_reason()
    {
        var dto = new POSShiftCashTransactionDto();

        Assert.NotNull(dto.Reason);
        Assert.Equal(string.Empty, dto.Reason);

        var json = JsonSerializer.Serialize(dto);

        Assert.Contains(
            "\"Reason\":\"\"",
            json,
            StringComparison.Ordinal);
    }

    private static CreatePosShiftCashTransactionRequest CreateValidRequest()
        => new()
        {
            Type = POSShiftCashTransactionType.CashIn,
            Amount = 100_000m,
            Reason = "Bổ sung tiền lẻ"
        };

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            instance,
            new ValidationContext(instance),
            results,
            validateAllProperties: true);

        return results;
    }
}