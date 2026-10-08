using GaoApp.Domain.Delivery;

namespace GaoApp.Tests.Delivery;

[Trait("Category", "DeliveryD07")]
public sealed class DeliveryD07PolicyTests
{
    [Fact]
    public void P01_Quantity_and_whole_dong_strings_preserve_bounds_and_reject_rounding_or_aliases()
    {
        Assert.Equal(.0001m, DeliveryOutcomePolicy.ParseQuantity("0.0001"));
        Assert.Equal(99999999999999.9999m, DeliveryOutcomePolicy.ParseQuantity("99999999999999.9999"));
        foreach (var value in new[] { "0.00001", "-1", "1e3", "01", "1.0", "100000000000000" })
            Error("QUANTITY_INVALID", () => DeliveryOutcomePolicy.ParseQuantity(value));
        Assert.Equal(9999999999999999m, DeliveryOutcomePolicy.ParseMoney("9999999999999999"));
        Assert.Equal(0m, DeliveryOutcomePolicy.ParseMoney("0"));
        foreach (var value in new[] { "0.01", "-1", "01", "1,000", "1e3", "10000000000000000" })
            Error("MONEY_INVALID", () => DeliveryOutcomePolicy.ParseMoney(value));
    }

    [Fact]
    public void P02_Complete_partial_and_failed_results_are_exact_cumulative_declarations()
    {
        Assert.Null(DeliveryOutcomePolicy.Validate("Full", null, [new(1, 4, 0, 4, 4, 0)], 0, 80000));
        Assert.Equal("Khách nhận một phần", DeliveryOutcomePolicy.Validate("Partial", "  Khách nhận một phần  ",
            [new(1, 4, .5m, 3.5m, 2.5m, 1.5m)], 50000, 0));
        foreach (var outcome in new[] { "NotDelivered", "Refused", "Unreachable", "Cancelled" })
            Assert.Equal("Tiền đã báo cần đối soát", DeliveryOutcomePolicy.Validate(outcome, "Tiền đã báo cần đối soát",
                [new(1, 4, 1, 3, 0, 4)], 120000, 35000));
    }

    [Fact]
    public void P03_Accepted_returns_cannot_be_delivered_again_and_unaccounted_or_corrupt_custody_is_rejected()
    {
        Error("OUTCOME_QUANTITY_INVALID", () => DeliveryOutcomePolicy.Validate("Partial", "Thiếu",
            [new(1, 4, 1, 3, 3.5m, .5m)], 0, 0));
        Error("OUTCOME_QUANTITY_INVALID", () => DeliveryOutcomePolicy.Validate("Partial", "Thiếu",
            [new(1, 4, 0, 4, 2, 1)], 0, 0));
        Error("CUSTODY_PROOF_INVALID", () => DeliveryOutcomePolicy.Validate("Partial", "Thiếu",
            [new(1, 4, 1, 4, 2, 2)], 0, 0));
    }

    [Fact]
    public void P04_Nonfull_outcome_needs_a_real_reason_and_cannot_hide_positive_delivery()
    {
        Error("REASON_REQUIRED", () => DeliveryOutcomePolicy.Validate("NotDelivered", " ", [new(1, 4, 0, 4, 0, 4)], 0, 0));
        Error("REASON_INVALID", () => DeliveryOutcomePolicy.Validate("Partial", new string('x', 1001), [new(1, 4, 0, 4, 2, 2)], 0, 0));
        Error("OUTCOME_QUANTITY_INVALID", () => DeliveryOutcomePolicy.Validate("Refused", "Từ chối", [new(1, 4, 0, 4, 1, 3)], 0, 0));
        Error("OUTCOME_QUANTITY_INVALID", () => DeliveryOutcomePolicy.Validate("Full", null, [new(1, 4, 0, 4, 3, 1)], 0, 0));
        Error("OUTCOME_INVALID", () => DeliveryOutcomePolicy.Validate("AutomaticSuccess", null, [new(1, 4, 0, 4, 4, 0)], 0, 0));
        Error("LINE_INVALID", () => DeliveryOutcomePolicy.Validate("Full", null, [new(1, 4, 0, 4, 4, 0), new(1, 4, 0, 4, 4, 0)], 0, 0));
    }

    [Fact]
    public void P05_Pending_recall_can_be_reported_without_bypassing_future_financial_locks()
    {
        var actor = D01Data.Actor(DeliveryCapability.ReportOnBehalf, 20);
        var report = DeliveryReportPolicy.Record(actor, D01Data.Origin, DeliveryState.CancellationPending,
            44, 44, DeliveryReportSource.AtStore, D01Data.Departed, D01Data.Departed.AddMinutes(25));
        Assert.Equal(20, report.ActorUserId); Assert.Equal(44, report.CourierUserId);
        foreach (var state in new[] { DeliveryState.ReadyToSettle, DeliveryState.Settled, DeliveryState.Cancelled })
            Error("REPORT_LOCKED", () => DeliveryReportPolicy.Record(actor, D01Data.Origin, state,
                44, 44, DeliveryReportSource.AtStore, D01Data.Departed, D01Data.Departed.AddMinutes(25)));
    }

    private static void Error(string code, Action action) => Assert.Equal(code, Assert.Throws<DeliveryRuleException>(action).Code);
}
