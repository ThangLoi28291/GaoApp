using FluentAssertions;
using GaoApp.Application.Services.Purchases;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptPriceVariancePolicyTests
{
    [Theory]
    [InlineData(10, 1, 10)]
    [InlineData(30, 3, 10)]
    [InlineData(1, 0.1, 10.049)]
    public void Equivalent_persisted_unit_price_has_no_variance(
        decimal current,
        decimal factor,
        decimal previousBase)
    {
        PurchaseReceiptPriceVariancePolicy.Evaluate(
                1, current, factor, previousBase)
            .Should().BeNull();
    }

    [Theory]
    [InlineData(12, 1, 10, 2)]
    [InlineData(8, 1, 10, -2)]
    [InlineData(30.01, 3, 10, 0.01)]
    public void Changed_persisted_unit_price_returns_exact_evidence(
        decimal current,
        decimal factor,
        decimal previousBase,
        decimal expectedDifference)
    {
        var result = PurchaseReceiptPriceVariancePolicy.Evaluate(
            7, current, factor, previousBase);

        result.Should().NotBeNull();
        result!.LineNo.Should().Be(7);
        result.PreviousUnitPriceBeforeVat.Should().Be(previousBase * factor);
        result.CurrentUnitPriceBeforeVat.Should().Be(current);
        result.Difference.Should().Be(expectedDifference);
    }

    [Fact]
    public void Missing_history_has_no_variance()
    {
        PurchaseReceiptPriceVariancePolicy.Evaluate(
                1, 99m, 1m, null)
            .Should().BeNull();
    }

    [Fact]
    public void Positive_history_that_rounds_to_zero_remains_a_variance_boundary()
    {
        var result = PurchaseReceiptPriceVariancePolicy.Evaluate(
            7, 0.01m, 0.0001m, 0.01m);

        result.Should().NotBeNull();
        result!.PreviousUnitPriceBeforeVat.Should().Be(0m);
        result.CurrentUnitPriceBeforeVat.Should().Be(0.01m);
        result.Difference.Should().Be(0.01m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonpositive_history_has_no_variance(decimal previousBase)
    {
        PurchaseReceiptPriceVariancePolicy.Evaluate(
                1, 99m, 1m, previousBase)
            .Should().BeNull();
    }

    [Fact]
    public void Invalid_current_factor_fails_closed()
    {
        var action = () => PurchaseReceiptPriceVariancePolicy.Evaluate(
            1, 10m, 0m, 10m);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Conversion_overflow_fails_closed_without_binary_float_fallback()
    {
        var action = () => PurchaseReceiptPriceVariancePolicy.ToReceiptUnitPrice(
            decimal.MaxValue,
            2m);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Audit_evidence_preserves_every_line_when_summary_would_exceed_note_limit()
    {
        var decisions = Enumerable.Range(1, 80)
            .Select(line => new PurchaseReceiptPriceVarianceDecision(
                line, line * 10m, line * 10m + 1m, 1m))
            .ToArray();

        var evidence = PurchaseReceiptPriceVariancePolicy.BuildAuditEvidence(decisions);

        evidence.Should().HaveCount(160);
        foreach (var decision in decisions)
        {
            evidence[$"PriceVariance.Line.{decision.LineNo}.PreviousUnitPriceBeforeVat"]
                .Should().Be(decision.PreviousUnitPriceBeforeVat);
            evidence[$"PriceVariance.Line.{decision.LineNo}.CurrentUnitPriceBeforeVat"]
                .Should().Be(decision.CurrentUnitPriceBeforeVat);
        }
    }
}
