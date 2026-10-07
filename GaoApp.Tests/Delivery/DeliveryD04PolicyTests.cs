using System.Globalization;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;

namespace GaoApp.Tests.Delivery;

[Trait("Category", "DeliveryD04")]
public sealed class DeliveryD04PolicyTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1 ")]
    [InlineData("1\n")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1e2")]
    [InlineData("1,25")]
    [InlineData("1.00001")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("100000000000000")]
    [InlineData("99999999999999999999")]
    public void P01_Quantity_grammar_rejects_ambiguous_or_unpersistable_text(string text)
        => Error("QUANTITY_INVALID", () => DeliveryPickingPolicy.ParseQuantity(text));

    [Theory]
    [InlineData("0")]
    [InlineData("0.0001")]
    [InlineData("12.3456")]
    [InlineData("90071992547409.1234")]
    [InlineData("99999999999999.9999")]
    public void P01_Quantity_preserves_all_persisted_digits_above_browser_safe_integer(string text)
    {
        var expected = decimal.Parse(text, CultureInfo.InvariantCulture);
        Assert.Equal(expected, DeliveryPickingPolicy.ParseQuantity(text));
        Assert.Equal(expected, decimal.Parse(DeliveryPickingPolicy.FormatQuantity(expected), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void P01_Quantity_request_requires_a_JSON_string()
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DeliveryPickingReportRequest>(
            "{\"clientRequestId\":\"b96f9396-c218-4a2c-9d0a-0a8fa3f81351\",\"expectedVersion\":\"AAAAAAAAAAA=\",\"lines\":[{\"lineId\":1,\"pickedQuantityText\":1,\"shortageReason\":null}]}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    [Theory]
    [InlineData("0.0001", "0.000001")]
    [InlineData("1", "0.000001")]
    [InlineData("99999999999999.9999", "2")]
    public void P02_Base_conversion_rejects_residual_or_storage_overflow(string quantity, string multiplier)
        => Error("BASE_QUANTITY_INVALID", () => DeliveryPickingPolicy.ToBase(D(quantity), D(multiplier)));

    [Theory]
    [InlineData("100000", "0.123456", "12345.6")]
    [InlineData("0.0001", "1", "0.0001")]
    [InlineData("1", "6", "6")]
    [InlineData("99999999999999.9999", "1", "99999999999999.9999")]
    public void P02_Base_conversion_retains_exact_four_digits_and_six_digit_factor(string quantity, string multiplier, string expected)
        => Assert.Equal(D(expected), DeliveryPickingPolicy.ToBase(D(quantity), D(multiplier)));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.1234567")]
    [InlineData("1000000000000")]
    public void P02_Invalid_multiplier_is_rejected_before_conversion(string multiplier)
        => Error("MULTIPLIER_INVALID", () => DeliveryPickingPolicy.ToBase(1, D(multiplier)));

    [Theory]
    [InlineData("1", "12", "6", "1")]
    [InlineData("13", "26", "1", "1")]
    [InlineData("9999999999999999", "99999999999998", "49999999999999", "5000000000000000")]
    [InlineData("9999999999999999", "99999999999999.9999", "99999999999999.9999", "9999999999999999")]
    public void P03_Frozen_net_proration_rounds_exact_midpoint_without_decimal_intermediate(string net, string ordered, string allowed, string expected)
        => Assert.Equal(D(expected), DeliveryPickingPolicy.Prorate(D(net), D(ordered), D(allowed)));

    [Fact]
    public void P04_Original_discount_stays_frozen_when_replacement_is_added()
    {
        var original = DeliveryPickingPolicy.Prorate(800, 10, 8);
        var replacement = DeliveryPickingPolicy.QuotedNet(2, 120);
        Assert.Equal(640, original);
        Assert.Equal(240, replacement);
        Assert.Equal(880, original + replacement);
    }

    [Fact]
    public void P05_Partial_replacement_prorates_frozen_net_instead_of_repricing()
    {
        var quote = DeliveryPickingPolicy.QuotedNet(2, 0.26m);
        Assert.Equal(1, quote);
        Assert.Equal(1, DeliveryPickingPolicy.Prorate(quote, 2, 1));
        Assert.Equal(0, DeliveryPickingPolicy.QuotedNet(1, 0.26m));
    }

    [Fact]
    public void P03_Allowed_quantity_cannot_exceed_immutable_quote()
        => Error("QUANTITY_EXCEEDS_SOURCE", () => DeliveryPickingPolicy.Prorate(20, 1, 1.0001m));

    [Fact]
    public void P03_Money_overflow_is_a_domain_error_with_large_valid_quantity()
        => Error("MONEY_INVALID", () => DeliveryPickingPolicy.QuotedNet(99999999999999m, 9999999999999999m));

    [Fact]
    public void P06_Coverage_is_explicit_in_original_units_and_never_inferred_from_replacement_ratio()
    {
        DeliveryPickingPolicy.RequireRootCoverage(10, 0, [3]);
        DeliveryPickingPolicy.RequireRootCoverage(10, 8, [1.25m, 0.75m]);
        DeliveryPickingPolicy.RequireRootCoverage(10, 0, [0]);
        Error("ROOT_COVERAGE_EXCEEDED", () => DeliveryPickingPolicy.RequireRootCoverage(10, 8, [2.0001m]));
    }

    [Fact]
    public void P06_Large_multiple_replacement_coverage_cannot_overflow_or_overdraw_root()
        => Error("ROOT_COVERAGE_EXCEEDED", () => DeliveryPickingPolicy.RequireRootCoverage(
            99999999999999.9999m, 99999999999999.9999m, [99999999999999.9999m, 99999999999999.9999m]));

    [Fact]
    public void P07_Unreported_is_distinct_from_explicit_zero_shortage()
    {
        Error("PICKING_INCOMPLETE", () => DeliveryPickingPolicy.RequireReported(2, null, "Chưa đếm"));
        Error("SHORTAGE_REASON_REQUIRED", () => DeliveryPickingPolicy.RequireReported(2, 0, null));
        DeliveryPickingPolicy.RequireReported(2, 0, "Hết hàng");
    }

    [Fact]
    public void P07_Partial_and_removed_reports_need_reason_while_full_report_does_not()
    {
        DeliveryPickingPolicy.RequireReported(2, 2, null);
        Error("SHORTAGE_REASON_REQUIRED", () => DeliveryPickingPolicy.RequireReported(2, 1, " "));
        Error("SHORTAGE_REASON_REQUIRED", () => DeliveryPickingPolicy.RequireReported(0, 0, null));
        DeliveryPickingPolicy.RequireReported(0, 0, "Khách bỏ dòng này");
        Error("QUANTITY_EXCEEDS_SOURCE", () => DeliveryPickingPolicy.RequireReported(2, 3, "Không hợp lệ"));
    }

    [Theory]
    [InlineData(DeliveryState.HandedOver)]
    [InlineData(DeliveryState.Delivering)]
    [InlineData(DeliveryState.AwaitingReconciliation)]
    [InlineData(DeliveryState.ReadyToSettle)]
    [InlineData(DeliveryState.CancellationPending)]
    [InlineData(DeliveryState.Settled)]
    [InlineData(DeliveryState.Cancelled)]
    public void H11_Every_new_picking_operation_is_denied_after_handover_or_cancellation(DeliveryState state)
    {
        foreach (var operation in new[] { "claim", "report", "submit", "plan", "approve", "reopen", "reassign" })
            Error("STATE_CONFLICT", () => DeliveryPickingPolicy.EnsureState(state, operation));
    }

    private static decimal D(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
    private static void Error(string code, Action action)
        => Assert.Equal(code, Assert.Throws<DeliveryRuleException>(action).Code);
}
