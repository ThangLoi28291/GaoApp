using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Invoices;

public sealed class AutoInvoiceRepositoryScopeTests
{
    // 11/10/2026 midnight through 12/10/2026 midnight in UTC+7.
    private static readonly DateTime FromUtc = new(2026, 10, 10, 17, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-1, true, false)]
    [InlineData(0, false, true)]
    [InlineData(30, true, true)]
    [InlineData(1439, false, true)]
    [InlineData(1440, true, false)]
    public async Task Completed_sale_time_controls_window_even_when_invoice_date_disagrees(
        int completedMinutes, bool invoiceDateInWindow, bool expected)
    {
        await using var db = CreateContext();
        var invoice = Draft(1, FromUtc.AddMinutes(completedMinutes), invoiceDateInWindow);
        db.InvoiceHeads.Add(invoice);
        await db.SaveChangesAsync();

        var candidates = await new AutoInvoiceRepository(db).GetCandidateInvoicesAsync(1, FromUtc, FromUtc.AddDays(1));

        Assert.Equal(expected ? new[] { invoice.Id } : Array.Empty<int>(), candidates.Select(x => x.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invoice_date_fallback_is_retained_only_when_completed_time_is_missing(bool inWindow)
    {
        await using var db = CreateContext();
        var invoice = Draft(1, null, inWindow);
        db.InvoiceHeads.Add(invoice);
        await db.SaveChangesAsync();

        var candidates = await new AutoInvoiceRepository(db).GetCandidateInvoicesAsync(1, FromUtc, FromUtc.AddDays(1));

        Assert.Equal(inWindow ? new[] { invoice.Id } : Array.Empty<int>(), candidates.Select(x => x.Id));
    }

    private static InvoiceHead Draft(int id, DateTime? completedAtUtc, bool invoiceDateInWindow)
        => new()
        {
            Id = id,
            StoreId = 1,
            OrderId = id,
            Order = new Order
            {
                Id = id, StoreId = 1, CompletedAtUtc = completedAtUtc,
                InvoiceIssuanceRoute = InvoiceIssuanceRoute.Automatic
            },
            InvoiceDate = DateTime.SpecifyKind(
                invoiceDateInWindow ? FromUtc.AddHours(1) : FromUtc.AddDays(2), DateTimeKind.Unspecified),
            GrandTotal = 125_000m
        };

    private static InMemoryAppDbContext CreateContext()
    {
        var tenant = new TenantContext();
        tenant.SetStore(1, "test");
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new InMemoryAppDbContext(options, tenant, new TestUser());
        db.Stores.Add(new Store { Id = 1, Name = "Test", SubDomain = "test", SubDomainNormalized = "TEST" });
        return db;
    }

    private sealed class TestUser : ICurrentUser
    {
        public int? UserId => null;
        public string? UserName => "auto-invoice-scope-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => false;
    }
}
