using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.Services.Reports;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Services.Banking;
using GaoApp.Infrastructure.Services.Reports;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05CashFlowTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Delayed_authenticated_web_receipt_is_bank_cashflow_on_actual_bank_day_before_any_order_finalization()
    {
        var seed = await fixture.CaseAsync(); await SnapshotIsolation(seed); var day = new DateTime(2026, 9, 8);
        await Receipt(seed, 230_000m, "2026-09-08");
        var original = await Flow(seed, day); Assert.Equal(230_000m, original.Summary.Single(x => x.Fund == "bank").Inflow);
        var row = Assert.Single(original.Items); Assert.Equal("bank", row.Fund); Assert.Equal(230_000m, row.Amount); Assert.Equal(day.AddHours(-7), row.AtUtc);
        var processingDay = DateTime.UtcNow.AddHours(7).Date; Assert.NotEqual(day, processingDay);
        Assert.Equal(0, (await Flow(seed, processingDay)).Summary.Single(x => x.Fund == "bank").Inflow);
        await using var db = seed.Context(); Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync()); Assert.Empty(await db.POSShifts.ToListAsync());
    }

    [Fact]
    public async Task Receipt_allocation_and_order_confirmation_do_not_add_a_second_bank_inflow()
    {
        var p = await Web05Prepared.CreateAsync(fixture); await SnapshotIsolation(p.Case.Seed); p.Bank.Pay(p.Reference, 45_000m);
        await p.Case.ProcessAsync(p.IntentId, p.Bank); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        var report = await Flow(p.Case.Seed, p.Case.Web.Clock.Utc.AddHours(7).Date);
        Assert.Equal(45_000m, report.Summary.Single(x => x.Fund == "bank").Inflow); Assert.Equal(45_000m, report.Items.Sum(x => x.Amount)); Assert.Single(report.Items);
        Assert.Equal(0, report.Summary.Single(x => x.Fund == "cash").Inflow); Assert.Equal(0, report.ClosingShiftDifference);
        await using var db = p.Case.Context(); Assert.Single(await db.Set<GaoApp.Domain.Storefront.StorefrontPaymentAllocation>().ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
    }

    [Fact]
    public async Task Review_under_and_over_receipts_remain_full_actual_cashflow_without_becoming_sales_or_partial_allocations()
    {
        var p = await Web05Prepared.CreateAsync(fixture); await SnapshotIsolation(p.Case.Seed);
        p.Bank.Pay(p.Reference, 10_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank); p.Bank.Pay(p.Reference, 60_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        var report = await Flow(p.Case.Seed, p.Case.Web.Clock.Utc.AddHours(7).Date);
        Assert.Equal(70_000m, report.Summary.Single(x => x.Fund == "bank").Inflow); Assert.Equal(70_000m, report.Items.Sum(x => x.Amount)); Assert.Equal(2, report.Items.Count);
        await using var db = p.Case.Context(); Assert.Empty(await db.Set<GaoApp.Domain.Storefront.StorefrontPaymentAllocation>().ToListAsync()); Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.InventoryTransactions.ToListAsync());
    }

    [Fact]
    public async Task Bank_cashflow_opening_and_closing_conserve_prior_and_current_receipts_by_bank_day()
    {
        var seed = await fixture.CaseAsync(); await SnapshotIsolation(seed); var baseline = new DateTime(2026, 9, 8);
        await using (var db = seed.Context()) { db.Add(new TreasuryOpeningBalance { StoreId = seed.StoreId, Fund = "bank", AsOfDate = baseline, Amount = 1_000m, Note = "Synthetic verified opening" }); await db.SaveChangesAsync(); }
        await Receipt(seed, 3_300m, "2026-09-08"); await Receipt(seed, 4_400m, "2026-09-09");
        var report = await Flow(seed, baseline.AddDays(1)); var bank = report.Summary.Single(x => x.Fund == "bank");
        Assert.Equal(4_300m, bank.Opening); Assert.Equal(4_400m, bank.Inflow); Assert.Equal(0, bank.Outflow); Assert.Equal(8_700m, bank.Closing);
        Assert.Equal(bank.Opening + bank.Inflow - bank.Outflow, bank.Closing); Assert.Equal(4_400m, Assert.Single(report.Items).Amount);
    }

    [Fact]
    public async Task Missing_bank_date_cannot_be_reported_as_ingestion_day_income()
    {
        var seed = await fixture.CaseAsync(); await SnapshotIsolation(seed); await Receipt(seed, 230_000m, "");
        var report = await Flow(seed, DateTime.UtcNow.AddHours(7).Date); Assert.Equal(0, report.Summary.Single(x => x.Fund == "bank").Inflow);
        await using var db = seed.Context(); var receipt = await db.Set<BankVerifiedReceipt>().SingleAsync(x => x.OriginStoreId == seed.StoreId);
        Assert.True(receipt.NeedsReview); Assert.Equal(230_000m, receipt.Amount); Assert.Null(receipt.OccurredAtUtc);
    }

    [Fact]
    public async Task Real_POS_provider_receipt_payment_FK_is_one_bank_inflow_and_still_consumes_remaining_cash_tender_budget()
    {
        var c = await fixture.Web05Async(); await SnapshotIsolation(c.Seed); await using var pos = await Web05PosCase.CreateAsync(c);
        await pos.RetrievePaid("W05-REPORT-" + Guid.NewGuid().ToString("N")); await pos.Payments.CompleteAsync(pos.QrId, default);
        var bank = Assert.Single(await pos.Db.OrderPayments.ToListAsync()); Assert.NotNull(bank.BankVerifiedReceiptId); Assert.Equal(45_000m, bank.Amount);
        // Report-only, valid POS financial snapshot. The provider payment above
        // was created by real CompleteAsync; this does not claim stock finalize.
        pos.Db.OrderPayments.Add(new() { StoreId = c.StoreId, OrderId = pos.Order.Id, Method = PaymentMethod.Cash, Amount = 100_000m, PaidAtUtc = c.Web.Clock.Utc });
        pos.Order.Status = OrderStatus.Completed; pos.Order.CompletedAtUtc = c.Web.Clock.Utc; pos.Order.PaidTotal = 145_000m;
        pos.Order.BalanceDue = 0; pos.Order.ChangeDue = 45_000m; pos.Order.PaymentStatus = PaymentStatus.Paid; await pos.Db.SaveChangesAsync();
        var report = await Flow(c.Seed, c.Web.Clock.Utc.AddHours(7).Date);
        Assert.Equal(45_000m, report.Summary.Single(x => x.Fund == "bank").Inflow); Assert.Equal(55_000m, report.Summary.Single(x => x.Fund == "cash").Inflow);
        Assert.Equal(100_000m, report.Items.Sum(x => x.Amount)); Assert.Equal(2, report.Items.Count); Assert.Equal(45_000m, Assert.Single(report.Items, x => x.Fund == "bank").Amount);
        Assert.Equal(55_000m, Assert.Single(report.Items, x => x.Fund == "cash").Amount);
    }

    [Fact]
    public async Task Bank_day_receipt_is_never_plotted_as_if_an_actual_midnight_payment_hour_was_known()
    {
        var seed = await fixture.CaseAsync(); await SnapshotIsolation(seed); var day = new DateTime(2026, 9, 8); await Receipt(seed, 230_000m, "2026-09-08");
        var report = await Flow(seed, day); Assert.Equal(230_000m, report.Summary.Single(x => x.Fund == "bank").Inflow);
        Assert.DoesNotContain(report.Trend, x => (x.Inflow != 0 || x.Outflow != 0) && System.Text.RegularExpressions.Regex.IsMatch(x.Label, @"\b\d{1,2}:\d{2}\b"));
        var row = Assert.Single(report.Items); Assert.Equal(BankTimePrecision.BankDay, row.TimePrecision); Assert.NotNull(row.TimeText); Assert.DoesNotContain(":", row.TimeText);
    }

    [Fact]
    public async Task Unknown_bank_day_observed_after_reported_period_prevents_exact_bank_closing_without_invalidating_independent_cash_balance()
    {
        var seed = await fixture.CaseAsync(); await SnapshotIsolation(seed); var baseline = new DateTime(2026, 9, 1);
        await using (var db = seed.Context())
        {
            db.AddRange(new TreasuryOpeningBalance { StoreId = seed.StoreId, Fund = "bank", AsOfDate = baseline, Amount = 1_000m, Note = "Synthetic bank opening" },
                new TreasuryOpeningBalance { StoreId = seed.StoreId, Fund = "cash", AsOfDate = baseline, Amount = 500m, Note = "Synthetic independent cash opening" }); await db.SaveChangesAsync();
        }
        await Receipt(seed, 230_000m, ""); var report = await Flow(seed, new DateTime(2026, 9, 8)); var bank = report.Summary.Single(x => x.Fund == "bank");
        Assert.Null(bank.Opening); Assert.Null(bank.Closing); var cash = report.Summary.Single(x => x.Fund == "cash"); Assert.Equal(500m, cash.Opening); Assert.Equal(500m, cash.Closing);
        Assert.Equal(0, bank.Inflow); Assert.Equal(0, bank.Outflow); Assert.NotEmpty(report.Warnings);
    }

    [Fact]
    public async Task Unresolved_bank_reversal_keeps_original_credit_history_but_cannot_certify_bank_position_or_invent_outgoing_refund()
    {
        var p = await Web05Prepared.CreateAsync(fixture); await SnapshotIsolation(p.Case.Seed); await Baselines(p.Case.Seed, p.Case.Web.Clock.Utc.AddHours(7).Date);
        p.Bank.Pay(p.Reference, 45_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        var bankOrder = p.Bank.Orders[p.Reference]; lock (bankOrder.Transfers) bankOrder.Transfers[0] = bankOrder.Transfers[0] with { Status = "REVERSED" };
        await p.Case.ProcessAsync(p.IntentId, p.Bank); var report = await Flow(p.Case.Seed, p.Case.Web.Clock.Utc.AddHours(7).Date); var bank = report.Summary.Single(x => x.Fund == "bank");
        Assert.Equal(45_000m, bank.Inflow); Assert.Equal(0, bank.Outflow); Assert.Null(bank.Closing); Assert.Null(bank.Opening);
        var cash = report.Summary.Single(x => x.Fund == "cash"); Assert.Equal(500m, cash.Opening); Assert.Equal(500m, cash.Closing);
        await using var db = p.Case.Context(); Assert.Equal(45_000m, (await db.Set<BankVerifiedReceipt>().SingleAsync(x => x.OriginStoreId == p.Case.StoreId)).Amount); Assert.Empty(await db.TreasuryEntries.ToListAsync());
    }

    [Fact]
    public async Task Known_underpayment_review_does_not_erase_proven_bank_assets_or_unnecessarily_invalidate_a_verified_bank_opening()
    {
        var p = await Web05Prepared.CreateAsync(fixture); await SnapshotIsolation(p.Case.Seed); var day = p.Case.Web.Clock.Utc.AddHours(7).Date; await Baselines(p.Case.Seed, day);
        p.Bank.Pay(p.Reference, 10_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank); var report = await Flow(p.Case.Seed, day); var bank = report.Summary.Single(x => x.Fund == "bank");
        Assert.Equal(1_000m, bank.Opening); Assert.Equal(10_000m, bank.Inflow); Assert.Equal(11_000m, bank.Closing); Assert.Equal(0, bank.Outflow);
    }

    internal static async Task SnapshotIsolation(Web01Case seed)
    {
        var database = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(seed.Database.ConnectionString).InitialCatalog;
        Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", database);
        await seed.Database.ExecuteAsync("ALTER DATABASE [" + database + "] SET ALLOW_SNAPSHOT_ISOLATION ON");
    }
    internal static async Task<CashFlowReportDto> Flow(Web01Case seed, DateTime day)
    {
        await using var db = seed.Context(); return await new OperationsReportService(db, new SalesReportingPeriodPolicy()).CashFlowAsync(new() { FromDate = day, ToDate = day }, default);
    }
    internal static async Task Receipt(Web01Case seed, decimal amount, string time)
    {
        var directory = Web05Support.RuntimePath("bank-report-evidence"); await using var db = seed.Context(); var bank = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == seed.BankAccountId);
        await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(new(GlobalBankReceiptLedger.AccountSnapshot(bank), "W000000000001", BankPaymentChannel.Storefront,
            "W05-TX-" + Guid.NewGuid().ToString("N"), amount, "COMPLETED", time, "Synthetic report evidence"));
    }
    private static async Task Baselines(Web01Case seed, DateTime day)
    {
        await using var db = seed.Context(); db.AddRange(new TreasuryOpeningBalance { StoreId = seed.StoreId, Fund = "bank", AsOfDate = day, Amount = 1_000m, Note = "Synthetic verified bank opening" },
            new TreasuryOpeningBalance { StoreId = seed.StoreId, Fund = "cash", AsOfDate = day, Amount = 500m, Note = "Synthetic independent cash opening" }); await db.SaveChangesAsync();
    }
}
