using GaoApp.Application.Services.Purchases;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptExactConversionPolicyTests
{
    [Fact]
    public void Same_unit_factor_one_preserves_quantity_exactly()
        => Assert.Equal(3.125m, PurchaseReceiptQuantityConversionPolicy.ToCanonical(3.125m, 1m));

    [Fact]
    public void Alternate_integer_factor_converts_to_base_quantity()
        => Assert.Equal(36m, PurchaseReceiptQuantityConversionPolicy.ToCanonical(3m, 12m));

    [Fact]
    public void Fractional_factor_uses_decimal_arithmetic()
        => Assert.Equal(0.375m, PurchaseReceiptQuantityConversionPolicy.ToCanonical(1.5m, 0.25m));

    [Fact]
    public void Canonical_quantity_rounds_once_to_three_decimals_away_from_zero()
        => Assert.Equal(0.667m, PurchaseReceiptQuantityConversionPolicy.ToCanonical(0.333m, 2.002m));

    [Fact]
    public void Ordered_equivalent_uses_order_snapshot_factor()
        => Assert.Equal(3m, PurchaseReceiptQuantityConversionPolicy.ToOrderedEquivalent(36m, 12m));

    [Fact]
    public void Ordered_equivalent_rejects_quantity_that_would_create_canonical_drift()
        => Assert.Throws<PurchaseReceiptQuantityException>(() =>
            PurchaseReceiptQuantityConversionPolicy.ToOrderedEquivalent(1m, 12m));

    [Fact]
    public void Maximum_receipt_quantity_rounds_down_and_cannot_over_allocate()
        => Assert.Equal(3.333m, PurchaseReceiptQuantityConversionPolicy.MaximumReceiptQuantity(10m, 3m));

    [Fact]
    public void Maximum_receipt_quantity_is_representable_in_ordered_unit()
        => Assert.Equal(36m,
            PurchaseReceiptQuantityConversionPolicy.MaximumReceiptQuantity(36m, 1m, 12m));

    [Fact]
    public void Maximum_receipt_quantity_searches_the_complete_decimal_lattice()
        => Assert.Equal(31_640.936m,
            PurchaseReceiptQuantityConversionPolicy.MaximumReceiptQuantity(
                100_000m, 3.1415m, 100_000m));

    [Fact]
    public void Adversarial_full_domain_factors_terminate_with_a_valid_exact_maximum()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var maximum = PurchaseReceiptQuantityConversionPolicy.MaximumReceiptQuantity(
            PurchaseReceiptQuantityConversionPolicy.MaximumStoredQuantity,
            10_000_000_000.0001m,
            10_000_000_000.0003m);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Exact maximum calculation took {stopwatch.Elapsed}.");
        Assert.True(maximum > 0m);
        var canonical = PurchaseReceiptQuantityConversionPolicy.ToCanonical(
            maximum, 10_000_000_000.0001m);
        Assert.True(canonical <= PurchaseReceiptQuantityConversionPolicy.MaximumStoredQuantity);
        _ = PurchaseReceiptQuantityConversionPolicy.ToOrderedEquivalent(
            canonical, 10_000_000_000.0003m);
    }

    [Fact]
    public void Low_coprime_factors_with_high_availability_are_fast_for_many_unit_choices()
    {
        const int unitChoices = 32;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (var index = 0; index < unitChoices; index++)
        {
            var maximum = PurchaseReceiptQuantityConversionPolicy.MaximumReceiptQuantity(
                PurchaseReceiptQuantityConversionPolicy.MaximumStoredQuantity,
                0.0001m,
                0.0003m);
            Assert.Equal(PurchaseReceiptQuantityConversionPolicy.MaximumStoredQuantity, maximum);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Calculating {unitChoices} low-factor unit choices took {stopwatch.Elapsed}.");
    }

    [Theory]
    [InlineData(1.000, 0.7, 1.3)]
    [InlineData(0.250, 3.1415, 2.5)]
    [InlineData(2.000, 12, 7)]
    [InlineData(0.075, 0.125, 0.375)]
    public void Number_theoretic_maximum_matches_exhaustive_small_domain(
        decimal available,
        decimal receiptFactor,
        decimal orderedFactor)
    {
        var expected = 0m;
        var upper = PurchaseReceiptQuantityConversionPolicy.MaximumReceiptQuantity(
            available, receiptFactor);
        var upperUnits = (int)(upper * 1000m);
        for (var units = 1; units <= upperUnits; units++)
        {
            var candidate = units / 1000m;
            try
            {
                var canonical = PurchaseReceiptQuantityConversionPolicy.ToCanonical(
                    candidate, receiptFactor);
                if (canonical > available) continue;
                _ = PurchaseReceiptQuantityConversionPolicy.ToOrderedEquivalent(
                    canonical, orderedFactor);
                expected = candidate;
            }
            catch (PurchaseReceiptQuantityException)
            {
                // This persisted receipt quantity is not on the common exact lattice.
            }
        }

        Assert.Equal(expected,
            PurchaseReceiptQuantityConversionPolicy.MaximumReceiptQuantity(
                available, receiptFactor, orderedFactor));
    }

    [Fact]
    public void Modular_predecessor_matches_exhaustive_cross_product()
    {
        decimal[] availabilities = [0.025m, 0.075m, 0.250m];
        decimal[] factors =
            [0.125m, 0.375m, 0.7m, 1m, 1.0001m, 1.0003m, 2.5m, 3.1415m, 7m, 12m];

        foreach (var available in availabilities)
        foreach (var receiptFactor in factors)
        foreach (var orderedFactor in factors)
        {
            var expected = 0m;
            var upper = PurchaseReceiptQuantityConversionPolicy.MaximumReceiptQuantity(
                available, receiptFactor);
            for (var units = 1; units <= (int)(upper * 1000m); units++)
            {
                var candidate = units / 1000m;
                try
                {
                    var canonical = PurchaseReceiptQuantityConversionPolicy.ToCanonical(
                        candidate, receiptFactor);
                    if (canonical > available) continue;
                    _ = PurchaseReceiptQuantityConversionPolicy.ToOrderedEquivalent(
                        canonical, orderedFactor);
                    expected = candidate;
                }
                catch (PurchaseReceiptQuantityException)
                {
                    // This input is not on the common exact lattice.
                }
            }

            Assert.Equal(expected,
                PurchaseReceiptQuantityConversionPolicy.MaximumReceiptQuantity(
                    available, receiptFactor, orderedFactor));
        }
    }

    [Fact]
    public void Cumulative_ordered_invariant_rejects_fractional_rounding_drift()
    {
        var canonical = PurchaseReceiptQuantityConversionPolicy.ToCanonical(0.001m, 1.5m);
        var ordered = PurchaseReceiptQuantityConversionPolicy.ToOrderedEquivalent(canonical, 1.5m);

        Assert.Equal(0.002m, canonical);
        Assert.Throws<PurchaseReceiptQuantityException>(() =>
            PurchaseReceiptQuantityConversionPolicy.EnsureCumulativeOrderedInvariant(
                0.001m, ordered, canonical, 1.5m));
    }

    [Fact]
    public void Cumulative_ordered_invariant_accepts_exact_total()
        => Assert.Equal(3m,
            PurchaseReceiptQuantityConversionPolicy.EnsureCumulativeOrderedInvariant(
                1m, 2m, 24m, 12m));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Zero_or_negative_receipt_quantity_is_rejected(decimal quantity)
        => Assert.Throws<PurchaseReceiptQuantityException>(() =>
            PurchaseReceiptQuantityConversionPolicy.ToCanonical(quantity, 1m));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Zero_or_negative_factor_is_rejected(decimal factor)
        => Assert.Throws<PurchaseReceiptQuantityException>(() =>
            PurchaseReceiptQuantityConversionPolicy.ToCanonical(1m, factor));

    [Fact]
    public void Factor_beyond_four_decimal_places_is_rejected()
        => Assert.Throws<PurchaseReceiptQuantityException>(() =>
            PurchaseReceiptQuantityConversionPolicy.ToCanonical(1m, 1.00005m));

    [Fact]
    public void Maximum_decimal_18_4_factor_is_accepted()
        => Assert.Equal(
            PurchaseReceiptQuantityConversionPolicy.MaximumStoredFactor,
            PurchaseReceiptQuantityConversionPolicy.ValidateFactor(
                PurchaseReceiptQuantityConversionPolicy.MaximumStoredFactor));

    [Fact]
    public void Factor_above_decimal_18_4_magnitude_is_rejected()
        => Assert.Throws<PurchaseReceiptQuantityException>(() =>
            PurchaseReceiptQuantityConversionPolicy.ValidateFactor(100_000_000_000_000m));

    [Fact]
    public void Decimal_overflow_is_reported_as_safe_conversion_failure()
        => Assert.Throws<PurchaseReceiptQuantityException>(() =>
            PurchaseReceiptQuantityConversionPolicy.ToCanonical(decimal.MaxValue, 2m));

    [Fact]
    public void Positive_quantity_rounded_to_zero_is_rejected()
        => Assert.Throws<PurchaseReceiptQuantityException>(() =>
            PurchaseReceiptQuantityConversionPolicy.ToCanonical(0.0004m, 1m));

    [Fact]
    public void Repeated_calculation_is_deterministic_and_has_no_binary_float_drift()
    {
        var first = PurchaseReceiptQuantityConversionPolicy.ToCanonical(0.1m, 3m);
        for (var i = 0; i < 100; i++)
            Assert.Equal(first, PurchaseReceiptQuantityConversionPolicy.ToCanonical(0.1m, 3m));
        Assert.Equal(0.300m, first);
    }
}
