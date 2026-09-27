using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Invoices;

public sealed partial class AutoInvoiceServiceTests
{
    private static readonly DateTime CashRoutingNow = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("cash", true)]
    [InlineData("cash-overpay", true)]
    [InlineData("deleted-bank", true)]
    [InlineData("zero-bank", true)]
    [InlineData("bank", false)]
    [InlineData("mixed", false)]
    [InlineData("card", false)]
    [InlineData("wallet", false)]
    [InlineData("other", false)]
    [InlineData("no-payment", false)]
    [InlineData("deposit", false)]
    [InlineData("partial-cash", false)]
    [InlineData("foreign-store", false)]
    [InlineData("foreign-order", false)]
    [InlineData("bank-debt-collection", false)]
    public async Task Cash_payment_routing_worker_and_cockpit_agree_below_threshold(string payment, bool waitsForGroup)
    {
        var repo = CashRoutingRepository();
        var invoice = CashRoutingInvoice(501, 60_000m, payment);
        repo.Candidates.Add(invoice);
        var issue = new FakeIssueService();
        var service = CashRoutingService(repo, issue);

        var dashboard = await service.GetDashboardAsync(new());
        Assert.Equal(waitsForGroup, Assert.Single(dashboard.Queue).IsGroupedConsumer);
        Assert.Equal(waitsForGroup ? 1 : 0, dashboard.Groups.Count);
        Assert.Equal(waitsForGroup ? 1 : 0, (await service.GetDashboardAsync(new() { Workspace = "groups" })).Queue.Count);
        Assert.Equal(waitsForGroup ? 0 : 1, (await service.GetDashboardAsync(new() { Workspace = "single" })).Queue.Count);

        var result = await service.RunOnceAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(waitsForGroup ? 0 : 1, issue.Calls);
        if (waitsForGroup) Assert.Empty(repo.Operations);
        else
        {
            var operation = Assert.Single(repo.Operations);
            Assert.Equal(AutoInvoiceOperationKind.Single, operation.Kind);
            Assert.Equal(AutoInvoiceOperationStatus.Succeeded, operation.Status);
            Assert.Equal(invoice.Id, operation.InvoiceHeadId);
        }
    }

    [Fact]
    public async Task Cash_payment_routing_cash_at_threshold_is_single()
    {
        var repo = CashRoutingRepository();
        repo.Candidates.Add(CashRoutingInvoice(501, 80_000m));
        var issue = new FakeIssueService();
        var service = CashRoutingService(repo, issue);
        Assert.False(Assert.Single((await service.GetDashboardAsync(new())).Queue).IsGroupedConsumer);
        await service.RunOnceAsync();
        Assert.Equal(AutoInvoiceOperationKind.Single, Assert.Single(repo.Operations).Kind);
        Assert.Equal(1, issue.Calls);
    }

    [Fact]
    public async Task Cash_payment_routing_cash_group_contains_no_mixed_payment_order()
    {
        var repo = CashRoutingRepository();
        var cash = Enumerable.Range(501, 3).Select(id => CashRoutingInvoice(id, 60_000m)).ToList();
        var mixed = CashRoutingInvoice(504, 60_000m, "mixed");
        mixed.Order!.CompletedAtUtc = CashRoutingNow.AddMinutes(-5);
        repo.Candidates.AddRange(cash.Append(mixed));
        var issue = new FakeIssueService();
        var service = CashRoutingService(repo, issue);
        var dashboard = await service.GetDashboardAsync(new());
        Assert.Equal(new[] { 501, 502, 503 }, Assert.Single(dashboard.Groups).InvoiceHeadIds.OrderBy(x => x));

        await service.RunOnceAsync();

        var operation = Assert.Single(repo.Operations);
        Assert.Equal(AutoInvoiceOperationKind.Group, operation.Kind);
        Assert.Equal(new[] { 501, 502, 503 }, operation.Sources.Select(x => x.InvoiceHeadId).OrderBy(x => x));
        Assert.Equal(1, issue.Calls);
    }

    [Fact]
    public async Task Cash_payment_routing_bank_does_not_wait_for_older_cash_group()
    {
        var repo = CashRoutingRepository();
        var cash = CashRoutingInvoice(501, 60_000m);
        var bank = CashRoutingInvoice(502, 10_000m, "bank");
        bank.Order!.CompletedAtUtc = CashRoutingNow.AddMinutes(-5);
        repo.Candidates.AddRange([cash, bank]);
        var issue = new FakeIssueService();
        await CashRoutingService(repo, issue).RunOnceAsync();
        Assert.Equal(bank.Id, Assert.Single(repo.Operations).InvoiceHeadId);
        Assert.Equal(new[] { bank.Id }, issue.InvoiceIds);
    }

    [Fact]
    public async Task Cash_payment_routing_bank_still_waits_for_minimum_age()
    {
        var repo = CashRoutingRepository();
        repo.Settings.MinimumAgeMinutes = 30;
        repo.Candidates.Add(CashRoutingInvoice(501, 60_000m, "bank"));
        var issue = new FakeIssueService();
        var service = CashRoutingService(repo, issue);
        var row = Assert.Single((await service.GetDashboardAsync(new())).Queue);
        Assert.False(row.IsAgeEligible);
        Assert.False(row.IsGroupedConsumer);
        await service.RunOnceAsync();
        Assert.Empty(repo.Operations);
        Assert.Equal(0, issue.Calls);
    }

    [Fact]
    public async Task Cash_payment_routing_group_rechecks_new_bank_payment_under_claim_lock()
    {
        var repo = CashRoutingRepository();
        var invoices = Enumerable.Range(501, 3).Select(id => CashRoutingInvoice(id, 60_000m)).ToList();
        repo.Candidates.AddRange(invoices);
        repo.BeforeGroupClaimRead = () => invoices[0].Order!.Payments.Single().Method = PaymentMethod.BankTransfer;
        var issue = new FakeIssueService();
        var service = CashRoutingService(repo, issue);

        await service.RunOnceAsync();

        Assert.Empty(repo.Operations);
        Assert.Equal(0, issue.Calls);
        repo.BeforeGroupClaimRead = null;
        await service.RunOnceAsync(force: true);
        Assert.Equal(AutoInvoiceOperationKind.Single, Assert.Single(repo.Operations).Kind);
        Assert.Equal(new[] { 501 }, issue.InvoiceIds);
    }

    [Fact]
    public async Task Cash_payment_routing_single_rechecks_change_to_cash_under_claim_lock()
    {
        var repo = CashRoutingRepository();
        var invoice = CashRoutingInvoice(501, 60_000m, "bank");
        repo.Candidates.Add(invoice);
        repo.BeforeSingleClaimRead = () => invoice.Order!.Payments.Single().Method = PaymentMethod.Cash;
        var issue = new FakeIssueService();
        await CashRoutingService(repo, issue).RunOnceAsync();
        Assert.Empty(repo.Operations);
        Assert.Equal(0, issue.Calls);
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("manual")]
    [InlineData("invalid-detail")]
    public async Task Cash_payment_routing_bank_does_not_bypass_existing_guards(string guard)
    {
        var repo = CashRoutingRepository();
        var invoice = CashRoutingInvoice(501, 60_000m, "bank");
        if (guard == "paused") repo.Settings.IsEnabled = false;
        if (guard == "manual") invoice.Order!.InvoiceIssuanceRoute = InvoiceIssuanceRoute.Manual;
        if (guard == "invalid-detail") invoice.Details.Single().UnitName = null;
        repo.Candidates.Add(invoice);
        var issue = new FakeIssueService();
        await CashRoutingService(repo, issue).RunOnceAsync();
        Assert.Equal(0, issue.Calls);
        Assert.DoesNotContain(repo.Operations, x => x.Status == AutoInvoiceOperationStatus.Succeeded);
    }

    private static FakeAutoInvoiceRepository CashRoutingRepository()
    {
        var repo = CreateRepository(enabled: true);
        repo.Settings.SeparateAmountThreshold = 80_000m;
        repo.Settings.GroupTargetAmount = 150_000m;
        repo.Settings.MinimumAgeMinutes = 1;
        repo.Settings.IssueOldDayRemainder = false;
        return repo;
    }

    private static AutoInvoiceService CashRoutingService(FakeAutoInvoiceRepository repo, FakeIssueService issue)
        => new(repo, new SufficientStockRepository(), issue, new FoundLookupService(),
            new NoopUnitOfWork(), new StoreTenant(1), new TestCurrentUser(), new FixedTimeProvider(CashRoutingNow));

    private static InvoiceHead CashRoutingInvoice(int id, decimal total, string payment = "cash")
    {
        var invoice = Invoice(id, CashRoutingNow.AddMinutes(-10));
        SetInvoiceTotal(invoice, total);
        var order = invoice.Order!;
        order.GrandTotal = total;
        var first = order.Payments.Single();
        first.Amount = total;
        switch (payment)
        {
            case "bank": first.Method = PaymentMethod.BankTransfer; break;
            case "card": first.Method = PaymentMethod.Card; break;
            case "wallet": first.Method = PaymentMethod.EWallet; break;
            case "other": first.Method = PaymentMethod.Other; break;
            case "cash-overpay": first.Amount += 10_000m; break;
            case "partial-cash": first.Amount /= 2; break;
            case "deposit": order.DepositAmount = total / 2; break;
            case "foreign-store": first.StoreId = 2; break;
            case "foreign-order": first.OrderId = id + 1; break;
            case "no-payment": order.Payments.Clear(); break;
            case "mixed":
            case "deleted-bank":
            case "zero-bank":
            case "bank-debt-collection":
                var bank = new OrderPayment
                {
                    StoreId = 1, OrderId = id, Method = PaymentMethod.BankTransfer,
                    Amount = payment == "zero-bank" ? 0 : total / 2,
                    IsDeleted = payment == "deleted-bank",
                    IsDebtCollection = payment == "bank-debt-collection"
                };
                if (!bank.IsDeleted) first.Amount -= bank.Amount;
                order.Payments.Add(bank);
                break;
        }
        return invoice;
    }
}
