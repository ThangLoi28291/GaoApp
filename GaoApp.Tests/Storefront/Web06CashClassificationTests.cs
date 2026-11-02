using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Reports;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06CashClassificationTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Same_actual_cash_in_through_direct_or_treasury_identity_is_one_claim_and_one_cash_inflow(bool treasuryFirst)
    {
        var (c, _, cash) = await Ready(); int treasury;
        await using (var db = c.Context()) treasury = await new TreasuryService(db).CreateAsync(Voucher(cash), default);
        var request = await c.RequestAsync<StorefrontCodCashReceiptRequest>();
        if (treasuryFirst) request.TreasuryEntryId = treasury; else request.POSShiftCashTransactionId = cash;
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db).ReceiveCashRemittanceAsync(c.OrderId, request);
        var duplicate = await c.RequestAsync<StorefrontCodCashReceiptRequest>();
        if (treasuryFirst) duplicate.POSShiftCashTransactionId = cash; else duplicate.TreasuryEntryId = treasury;
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => Web06FinanceSupport.Settlement(c, db).ReceiveCashRemittanceAsync(c.OrderId, duplicate));
        await using var check = c.Context(); Assert.Single(await check.Set<StorefrontCodReceipt>().ToListAsync()); Assert.Equal(200_000m, (await check.POSShifts.SingleAsync()).CashInTotal);
        Assert.Equal(200_000m, (await Web06FinanceSupport.Settlement(c, check).GetAsync(c.OrderId)).ActualStoreReceived);
        await Web05CashFlowTests.SnapshotIsolation(c.Seed); var flow = await Web05CashFlowTests.Flow(c.Seed, DateTime.UtcNow.AddHours(7).Date);
        Assert.Equal(200_000m, flow.Summary.Single(x => x.Fund == "cash").Inflow); Assert.Equal(0m, flow.Summary.Single(x => x.Fund == "bank").Inflow); Assert.Equal(0m, flow.Summary.Single(x => x.Fund == "bank").Outflow);
    }
    [Fact]
    public async Task Actual_internal_bank_to_cash_transfer_cannot_be_claimed_as_external_customer_COD_remittance()
    {
        var (c, _, cash) = await Ready(); int treasury; await using (var db = c.Context()) treasury = await new TreasuryService(db).CreateAsync(Voucher(cash, internalTransfer: true), default);
        var request = await c.RequestAsync<StorefrontCodCashReceiptRequest>(); request.POSShiftCashTransactionId = cash;
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => Web06FinanceSupport.Settlement(c, db).ReceiveCashRemittanceAsync(c.OrderId, request));
        request = await c.RequestAsync<StorefrontCodCashReceiptRequest>(); request.TreasuryEntryId = treasury;
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => Web06FinanceSupport.Settlement(c, db).ReceiveCashRemittanceAsync(c.OrderId, request));
        await using var check = c.Context(); Assert.Empty(await check.Set<StorefrontCodReceipt>().ToListAsync()); Assert.Equal(200_000m, (await Web06FinanceSupport.Settlement(c, check).GetAsync(c.OrderId)).CollectorOutstanding);
        Assert.Equal(200_000m, (await check.POSShifts.SingleAsync()).CashInTotal); Assert.Equal("cash", (await check.TreasuryEntries.SingleAsync()).TargetFund);
    }
    [Fact]
    public async Task Claimed_external_cash_cannot_later_be_reclassified_as_internal_transfer_or_reversed_independently()
    {
        var (c, _, cash) = await Ready(); await Web06FinanceSupport.ReceiveCashAsync(c, cash);
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => new TreasuryService(db).CreateAsync(Voucher(cash, internalTransfer: true), default));
        int treasury; await using (var db = c.Context()) treasury = await new TreasuryService(db).CreateAsync(Voucher(cash), default);
        await using (var db = c.Context())
        {
            var original = await db.TreasuryEntries.SingleAsync(x => x.Id == treasury);
            await Assert.ThrowsAsync<ConflictAppException>(() => new TreasuryService(db).ReverseAsync(treasury, new() { ClientRequestId = Guid.NewGuid(), RowVersion = Convert.ToBase64String(original.RowVersion), Date = DateTime.UtcNow.AddHours(7).Date, Reason = "Independent reversal would erase a claimed COD source" }, default));
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE TreasuryEntries SET TargetFund=N'cash' WHERE StoreId={0} AND Id={1}", c.StoreId, treasury));
            Assert.True(error.Number == 547 || error.Number >= 51000);
            error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE POSShiftCashTransactions SET Amount=1 WHERE StoreId={0} AND Id={1}", c.StoreId, cash)); Assert.True(error.Number == 547 || error.Number >= 51000);
        }
        await using var check = c.Context(); Assert.Equal(200_000m, (await check.POSShiftCashTransactions.SingleAsync()).Amount); var retained = await check.TreasuryEntries.SingleAsync(); Assert.Null(retained.TargetFund); Assert.Null(retained.ReversedAtUtc);
        Assert.Single(await check.Set<StorefrontCodReceipt>().ToListAsync()); Assert.Equal(200_000m, (await check.POSShifts.SingleAsync()).CashInTotal);
        await Web05CashFlowTests.SnapshotIsolation(c.Seed); var flow = await Web05CashFlowTests.Flow(c.Seed, DateTime.UtcNow.AddHours(7).Date); Assert.Equal(200_000m, flow.Summary.Single(x => x.Fund == "cash").Inflow); Assert.Equal(0m, flow.Summary.Single(x => x.Fund == "bank").Outflow);
    }
    private async Task<(Web06Case Case, int Terminal, int Cash)> Ready()
    {
        var c = await Web06Case.CreateAsync(fixture); await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); return (c, terminal, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 200_000m));
    }
    private static TreasuryWriteDto Voucher(int cash, bool internalTransfer = false) => new() { ClientRequestId = Guid.NewGuid(), Fund = "cash", TargetFund = internalTransfer ? "cash" : null,
        Amount = 200_000m, Date = DateTime.UtcNow.AddHours(7).Date, Name = internalTransfer ? "WEB06 documented bank withdrawal to counter" : "WEB06 mirror of actual external courier cash receipt", POSShiftCashTransactionId = cash };
}
