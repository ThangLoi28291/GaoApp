using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Invoices;

public sealed partial class AutoInvoiceServiceTests
{
    private static readonly DateTime QueueEligibilityNow = new(2026, 10, 10, 18, 18, 14, DateTimeKind.Utc);

    [Fact]
    public async Task Worker_issues_the_same_ready_today_invoices_shown_by_cockpit_after_local_midnight()
    {
        var repo = QueueEligibilityRepository();
        var oldFirst = QueueEligibilityInvoice(104580, QueueEligibilityNow.Date.AddHours(11), 0m);
        var oldSecond = QueueEligibilityInvoice(104581, QueueEligibilityNow.Date.AddHours(11).AddMinutes(1), 0m);
        oldFirst.Details.Clear();
        oldSecond.Details.Clear();
        var first = QueueEligibilityInvoice(104582, QueueEligibilityNow.AddMinutes(-5), 125_000m);
        var second = QueueEligibilityInvoice(104583, QueueEligibilityNow.AddMinutes(-3), 200_000m);
        repo.Candidates.AddRange([oldFirst, oldSecond, first, second]);
        var issue = new FakeIssueService();
        var service = CreateService(repo, issue, nowUtc: QueueEligibilityNow);

        var dashboard = await service.GetDashboardAsync(new() { Workspace = "single" });
        Assert.Equal(new[] { first.Id, second.Id }, dashboard.Queue.Where(x => x.IsReady).Select(x => x.InvoiceHeadId));

        await service.RunOnceAsync();
        Assert.Equal(new[] { first.Id }, issue.InvoiceIds);

        // The real repository excludes sources that have already succeeded.
        repo.Candidates.Remove(first);
        await service.RunOnceAsync(force: true);

        Assert.Equal(new[] { first.Id, second.Id }, issue.InvoiceIds);
        Assert.All(repo.Operations, x =>
        {
            Assert.Equal(AutoInvoiceOperationKind.Single, x.Kind);
            Assert.Equal(AutoInvoiceOperationStatus.Succeeded, x.Status);
        });
        Assert.DoesNotContain(repo.Operations.SelectMany(x => x.Sources), x => x.InvoiceHeadId == oldFirst.Id || x.InvoiceHeadId == oldSecond.Id);
    }

    [Fact]
    public async Task Today_worker_excludes_previous_day_sale_even_when_local_invoice_date_looks_inside_utc_window()
    {
        var repo = QueueEligibilityRepository();
        var old = QueueEligibilityInvoice(601, QueueEligibilityNow.Date.AddHours(11), 200_000m);
        var today = QueueEligibilityInvoice(602, QueueEligibilityNow.AddMinutes(-5), 125_000m);
        repo.Candidates.AddRange([old, today]);
        var issue = new FakeIssueService();
        var service = CreateService(repo, issue, nowUtc: QueueEligibilityNow);

        Assert.Equal(today.Id, Assert.Single((await service.GetDashboardAsync(new())).Queue).InvoiceHeadId);
        await service.RunOnceAsync();

        Assert.Equal(new[] { today.Id }, issue.InvoiceIds);
        Assert.Equal(today.Id, Assert.Single(repo.Operations).InvoiceHeadId);
    }

    [Theory]
    [InlineData("zero-total")]
    [InlineData("negative-total")]
    [InlineData("no-details")]
    [InlineData("deleted-detail")]
    [InlineData("zero-quantity")]
    [InlineData("zero-detail-total")]
    public async Task Empty_draft_in_scope_does_not_block_newer_ready_single(string emptyReason)
    {
        var repo = QueueEligibilityRepository();
        repo.Settings.ScopeMode = AutoInvoiceScopeMode.Month;
        repo.Settings.ScopeStartDateLocal = new DateTime(2026, 10, 1);
        var empty = QueueEligibilityInvoice(611, QueueEligibilityNow.AddDays(-1), 60_000m);
        switch (emptyReason)
        {
            case "zero-total": empty.GrandTotal = 0m; break;
            case "negative-total": empty.GrandTotal = -50_000m; break;
            case "no-details": empty.Details.Clear(); break;
            case "deleted-detail": empty.Details.Single().IsDeleted = true; break;
            case "zero-quantity": empty.Details.Single().Quantity = 0m; break;
            case "zero-detail-total": empty.Details.Single().TotalAmount = 0m; break;
        }
        var ready = QueueEligibilityInvoice(612, QueueEligibilityNow.AddMinutes(-5), 125_000m);
        repo.Candidates.AddRange([empty, ready]);
        var issue = new FakeIssueService();
        var service = CreateService(repo, issue, nowUtc: QueueEligibilityNow);
        var dashboard = await service.GetDashboardAsync(new());
        Assert.Equal(ready.Id, Assert.Single(dashboard.Queue, x => x.IsReady).InvoiceHeadId);

        await service.RunOnceAsync();

        Assert.Equal(new[] { ready.Id }, issue.InvoiceIds);
        Assert.Equal(ready.Id, Assert.Single(repo.Operations).InvoiceHeadId);
    }

    [Fact]
    public async Task Month_worker_still_issues_valid_old_day_remainder_in_selected_scope()
    {
        var repo = QueueEligibilityRepository();
        repo.Settings.ScopeMode = AutoInvoiceScopeMode.Month;
        repo.Settings.ScopeStartDateLocal = new DateTime(2026, 10, 1);
        var old = QueueEligibilityInvoice(621, QueueEligibilityNow.AddDays(-1), 60_000m);
        var today = QueueEligibilityInvoice(622, QueueEligibilityNow.AddMinutes(-5), 125_000m);
        repo.Candidates.AddRange([old, today]);
        var issue = new FakeIssueService();

        await CreateService(repo, issue, nowUtc: QueueEligibilityNow).RunOnceAsync();

        var operation = Assert.Single(repo.Operations);
        Assert.Equal(AutoInvoiceOperationKind.Group, operation.Kind);
        Assert.Equal(old.Id, Assert.Single(operation.Sources).InvoiceHeadId);
        Assert.Equal(AutoInvoiceOperationStatus.Succeeded, operation.Status);
        Assert.Equal(1, issue.Calls);
    }

    private static FakeAutoInvoiceRepository QueueEligibilityRepository()
    {
        var repo = CreateRepository(enabled: true);
        repo.Settings.TimeZoneId = "SE Asia Standard Time";
        repo.Settings.MinimumAgeMinutes = 1;
        repo.Settings.SeparateAmountThreshold = 80_000m;
        repo.Settings.GroupTargetAmount = 150_000m;
        return repo;
    }

    private static InvoiceHead QueueEligibilityInvoice(int id, DateTime saleAtUtc, decimal total)
    {
        var invoice = Invoice(id, saleAtUtc);
        SetInvoiceTotal(invoice, total);
        invoice.Order!.GrandTotal = total;
        invoice.Order.Payments.Single().Amount = total;
        invoice.InvoiceDate = DateTime.SpecifyKind(saleAtUtc.AddHours(7), DateTimeKind.Unspecified);
        return invoice;
    }
}
