using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Invoices;

public sealed class AutoInvoiceOrderingPolicyTests
{
    private static readonly DateTime Today =
        new(
            2026,
            9,
            24,
            12,
            0,
            0,
            DateTimeKind.Unspecified);

    [Fact]
    public void Waiting_group_does_not_block_newer_single_ready_invoice()
    {
        var result =
            AutoInvoiceOrderingPolicy.Select(
            [
                Candidate(
                    1,
                    "2026-09-24",
                    20_000m,
                    "group-a",
                    8),

                Candidate(
                    2,
                    "2026-09-24",
                    200_000m,
                    "group-b",
                    9)
            ],
            separateAmountThreshold: 100_000m,
            groupTargetAmount: 100_000m,
            closingTimeLocal: new(23, 0, 0),
            issueOldDayRemainder: true,
            nowLocal: Today);

        Assert.NotNull(result);

        Assert.Equal(
            AutoInvoiceOperationKind.Single,
            result!.Kind);

        Assert.Equal(
            [2],
            result.InvoiceHeadIds);
    }

    [Fact]
    public void Older_ready_group_is_selected_before_newer_single()
    {
        var result =
            AutoInvoiceOrderingPolicy.Select(
            [
                Candidate(
                    1,
                    "2026-09-24",
                    60_000m,
                    "group-a",
                    8),

                Candidate(
                    2,
                    "2026-09-24",
                    60_000m,
                    "group-a",
                    9),

                Candidate(
                    3,
                    "2026-09-24",
                    200_000m,
                    "group-b",
                    10)
            ],
            separateAmountThreshold: 100_000m,
            groupTargetAmount: 100_000m,
            closingTimeLocal: new(23, 0, 0),
            issueOldDayRemainder: true,
            nowLocal: Today);

        Assert.NotNull(result);

        Assert.Equal(
            AutoInvoiceOperationKind.Group,
            result!.Kind);

        Assert.Equal(
            [1, 2],
            result.InvoiceHeadIds);
    }

    [Fact]
    public void Older_single_is_selected_before_newer_ready_group()
    {
        var result =
            AutoInvoiceOrderingPolicy.Select(
            [
                Candidate(
                    1,
                    "2026-09-24",
                    200_000m,
                    "single",
                    8),

                Candidate(
                    2,
                    "2026-09-24",
                    60_000m,
                    "group-a",
                    9),

                Candidate(
                    3,
                    "2026-09-24",
                    60_000m,
                    "group-a",
                    10)
            ],
            separateAmountThreshold: 100_000m,
            groupTargetAmount: 100_000m,
            closingTimeLocal: new(23, 0, 0),
            issueOldDayRemainder: true,
            nowLocal: Today);

        Assert.NotNull(result);

        Assert.Equal(
            AutoInvoiceOperationKind.Single,
            result!.Kind);

        Assert.Equal(
            [1],
            result.InvoiceHeadIds);
    }

    [Fact]
    public void Groups_only_same_group_key_until_target_is_reached()
    {
        var result =
            AutoInvoiceOrderingPolicy.Select(
            [
                Candidate(
                    1,
                    "2026-09-23",
                    40_000m,
                    "2026-09-23|1|2|3|10",
                    8),

                Candidate(
                    2,
                    "2026-09-23",
                    70_000m,
                    "2026-09-23|1|2|3|10",
                    9),

                Candidate(
                    3,
                    "2026-09-23",
                    70_000m,
                    "different-group",
                    10)
            ],
            separateAmountThreshold: 100_000m,
            groupTargetAmount: 100_000m,
            closingTimeLocal: new(23, 0, 0),
            issueOldDayRemainder: false,
            nowLocal: Today);

        Assert.NotNull(result);

        Assert.Equal(
            AutoInvoiceOperationKind.Group,
            result!.Kind);

        Assert.Equal(
            [1, 2],
            result.InvoiceHeadIds);
    }

    [Fact]
    public void Today_group_waits_before_closing_when_target_not_reached()
    {
        var result =
            AutoInvoiceOrderingPolicy.Select(
            [
                Candidate(
                    1,
                    "2026-09-24",
                    20_000m,
                    "today",
                    8)
            ],
            separateAmountThreshold: 100_000m,
            groupTargetAmount: 100_000m,
            closingTimeLocal: new(23, 0, 0),
            issueOldDayRemainder: true,
            nowLocal: Today);

        Assert.Null(result);
    }

    [Fact]
    public void Old_day_remainder_is_ready_when_enabled()
    {
        var result =
            AutoInvoiceOrderingPolicy.Select(
            [
                Candidate(
                    1,
                    "2026-09-23",
                    20_000m,
                    "old-day",
                    8)
            ],
            separateAmountThreshold: 100_000m,
            groupTargetAmount: 100_000m,
            closingTimeLocal: new(23, 0, 0),
            issueOldDayRemainder: true,
            nowLocal: Today);

        Assert.NotNull(result);

        Assert.Equal(
            AutoInvoiceOperationKind.Group,
            result!.Kind);

        Assert.Equal(
            [1],
            result.InvoiceHeadIds);
    }

    [Fact]
    public void Defensive_non_consumer_candidate_remains_single()
    {
        var result =
            AutoInvoiceOrderingPolicy.Select(
            [
                new AutoInvoiceCandidate(
                    1,
                    new DateTime(
                        2026,
                        9,
                        24,
                        8,
                        0,
                        0,
                        DateTimeKind.Utc),
                    new DateTime(
                        2026,
                        9,
                        24,
                        8,
                        0,
                        0),
                    20_000m,
                    IsConsumer: false,
                    GroupKey: "unexpected-non-consumer")
            ],
            separateAmountThreshold: 100_000m,
            groupTargetAmount: 100_000m,
            closingTimeLocal: new(23, 0, 0),
            issueOldDayRemainder: true,
            nowLocal: Today);

        Assert.NotNull(result);

        Assert.Equal(
            AutoInvoiceOperationKind.Single,
            result!.Kind);

        Assert.Equal(
            [1],
            result.InvoiceHeadIds);
    }

    private static AutoInvoiceCandidate Candidate(
        int id,
        string date,
        decimal amount,
        string groupKey,
        int hour)
    {
        var saleDate =
            DateTime.Parse(
                date,
                System.Globalization.CultureInfo.InvariantCulture);

        return new AutoInvoiceCandidate(
            id,
            new DateTime(
                saleDate.Year,
                saleDate.Month,
                saleDate.Day,
                hour,
                0,
                0,
                DateTimeKind.Utc),
            new DateTime(
                saleDate.Year,
                saleDate.Month,
                saleDate.Day,
                hour,
                0,
                0),
            amount,
            IsConsumer: true,
            groupKey);
    }
}