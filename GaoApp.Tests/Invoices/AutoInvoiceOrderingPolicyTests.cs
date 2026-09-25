using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Invoices;

public sealed class AutoInvoiceOrderingPolicyTests
{
    private static readonly DateTime Today = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public void Selects_the_oldest_sale_before_a_newer_separate_invoice()
    {
        var result = AutoInvoiceOrderingPolicy.Select(
            [
                Candidate(1, "2026-09-24", 20_000m, "group-a", 8),
                new AutoInvoiceCandidate(
                    2,
                    new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 9, 24, 9, 0, 0),
                    200_000m,
                    IsConsumer: false,
                    GroupKey: "buyer-specific")
            ],
            separateAmountThreshold: 100_000m,
            groupTargetAmount: 100_000m,
            closingTimeLocal: new(23, 0, 0),
            issueOldDayRemainder: true,
            nowLocal: Today);

        Assert.Null(result);
    }

    [Fact]
    public void Groups_only_same_day_and_same_group_key_when_target_is_reached()
    {
        var result = AutoInvoiceOrderingPolicy.Select(
            [
                Candidate(1, "2026-09-23", 40_000m, "2026-09-23|1|2|3|10", 8),
                Candidate(2, "2026-09-23", 70_000m, "2026-09-23|1|2|3|10", 9),
                Candidate(3, "2026-09-24", 70_000m, "2026-09-24|1|2|3|10", 10)
            ],
            separateAmountThreshold: 100_000m,
            groupTargetAmount: 100_000m,
            closingTimeLocal: new(23, 0, 0),
            issueOldDayRemainder: false,
            nowLocal: Today);

        Assert.NotNull(result);
        Assert.Equal(AutoInvoiceOperationKind.Group, result!.Kind);
        Assert.Equal([1, 2], result.InvoiceHeadIds);
    }

    [Fact]
    public void Does_not_move_today_group_to_tomorrow_before_closing()
    {
        var result = AutoInvoiceOrderingPolicy.Select(
            [Candidate(1, "2026-09-24", 20_000m, "today", 8)],
            separateAmountThreshold: 100_000m,
            groupTargetAmount: 100_000m,
            closingTimeLocal: new(23, 0, 0),
            issueOldDayRemainder: true,
            nowLocal: Today);

        Assert.Null(result);
    }

    [Fact]
    public void Flushes_old_day_remainder_after_the_old_day_is_exhausted()
    {
        var result = AutoInvoiceOrderingPolicy.Select(
            [Candidate(1, "2026-09-23", 20_000m, "old-day", 8)],
            separateAmountThreshold: 100_000m,
            groupTargetAmount: 100_000m,
            closingTimeLocal: new(23, 0, 0),
            issueOldDayRemainder: true,
            nowLocal: Today);

        Assert.NotNull(result);
        Assert.Equal(AutoInvoiceOperationKind.Group, result!.Kind);
        Assert.Equal([1], result.InvoiceHeadIds);
    }

    [Fact]
    public void Buyer_with_specific_information_is_always_separate()
    {
        var result = AutoInvoiceOrderingPolicy.Select(
            [new AutoInvoiceCandidate(
                1,
                new DateTime(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 24),
                20_000m,
                IsConsumer: false,
                GroupKey: "same-key")],
            100_000m,
            100_000m,
            new(23, 0, 0),
            true,
            Today);

        Assert.NotNull(result);
        Assert.Equal(AutoInvoiceOperationKind.Single, result!.Kind);
        Assert.Equal([1], result.InvoiceHeadIds);
    }

    private static AutoInvoiceCandidate Candidate(
        int id,
        string date,
        decimal amount,
        string groupKey,
        int hour)
    {
        var saleDate = DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
        return new AutoInvoiceCandidate(
            id,
            new DateTime(saleDate.Year, saleDate.Month, saleDate.Day, hour, 0, 0, DateTimeKind.Utc),
            new DateTime(saleDate.Year, saleDate.Month, saleDate.Day, hour, 0, 0),
            amount,
            IsConsumer: true,
            groupKey);
    }
}
