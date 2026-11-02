using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.POSShiftClosingSlips;
using GaoApp.Application.Services.POSShifts;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.POSShiftHandoverSlips;
using GaoApp.Infrastructure.Repositories.Users;
using GaoApp.Infrastructure.Services.Reports;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

internal sealed record Web07RecordedDebit(StorefrontRefundPayoutSource Source, int? TerminalId, string ExternalReference,
    DateTimeOffset ActualExecutionAt)
{
    internal static async Task<Web07RecordedDebit> CashAsync(Web07Case c, decimal amount, string? external = null, string? recordedReason = null)
    {
        var physical = c.Physical ?? throw new InvalidOperationException("Actual physical-capable principal graph required");
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(physical);
        await using (var db = c.Context())
        {
            var service = CashService(c, db, terminal);
            await service.AddCashTransactionAsync(new CreatePosShiftCashTransactionRequest
            {
                Type = POSShiftCashTransactionType.CashIn, Amount = amount,
                Reason = "WEB07 actual controlled counter float", Note = "Separate fixture funding, not customer refund or QR receipt"
            });
        }
        int id;
        await using (var db = c.Context()) id = (await CashService(c, db, terminal).AddCashTransactionAsync(new()
        {
            Type = POSShiftCashTransactionType.CashOut, Amount = amount,
            Reason = recordedReason ?? "WEB07 actual recorded customer payout", Note = "Controlled synthetic actual-disbursement input; no bank transport"
        })).Id;
        await using var read = c.Context(); var source = await read.POSShiftCashTransactions.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(POSShiftCashTransactionType.CashOut, source.Type); Assert.Equal(amount, source.Amount); Assert.Equal(8, source.RowVersion.Length);
        var at = new DateTimeOffset(DateTime.SpecifyKind(source.CreatedAtUtc, DateTimeKind.Utc));
        c.Checkout.Clock.Utc = DateTime.UtcNow;
        return new(new(StorefrontRefundPayoutSourceKind.CashOut, id, "Actual owned CashOut", "cash", amount,
            Convert.ToBase64String(source.RowVersion), at.UtcDateTime), terminal, external ?? "W07-CASH-" + Guid.NewGuid().ToString("N"), at);
    }
    internal static async Task<Web07RecordedDebit> BankAsync(Web07Case c, decimal amount, string? external = null)
    {
        var reference = external ?? "W07-BANK-" + Guid.NewGuid().ToString("N"); int id;
        await using (var db = c.Context()) id = await new TreasuryService(db).CreateAsync(new TreasuryWriteDto
        {
            ClientRequestId = Guid.NewGuid(), Fund = "bank", Amount = -amount, Date = DateTime.UtcNow.AddHours(7).Date,
            Name = "WEB07 actual manually recorded bank payout", Reference = reference,
            Note = "Controlled synthetic prior debit; account/owner/actual time attested only at authorized native execution"
        }, default);
        await using var read = c.Context(); var source = await read.TreasuryEntries.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(-amount, source.Amount); Assert.Null(source.TargetFund); Assert.False(source.IsVoucherLink); Assert.Equal(8, source.RowVersion.Length);
        var at = DateTimeOffset.UtcNow; c.Checkout.Clock.Utc = DateTime.UtcNow;
        return new(new(StorefrontRefundPayoutSourceKind.TreasuryDebit, id, "Actual owned Treasury debit", "bank", amount,
            Convert.ToBase64String(source.RowVersion), DateTime.SpecifyKind(source.OccurredAtUtc, DateTimeKind.Utc)), null, reference, at);
    }
    internal static async Task<StorefrontRefundPayoutSource> CashVoucherAsync(Web07Case c, Web07RecordedDebit cash, bool transfer = false)
    {
        int id; await using (var db = c.Context()) id = await new TreasuryService(db).CreateAsync(new()
        {
            ClientRequestId = Guid.NewGuid(), Fund = "cash", TargetFund = transfer ? "bank" : null,
            Amount = -cash.Source.Amount, Date = cash.ActualExecutionAt.ToOffset(TimeSpan.FromHours(7)).Date,
            Name = "WEB07 voucher for the same actual CashOut", Reference = cash.ExternalReference,
            POSShiftCashTransactionId = cash.Source.Id
        }, default);
        await using var read = c.Context(); var source = await read.TreasuryEntries.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.True(source.IsVoucherLink); Assert.Equal(cash.Source.Id, source.POSShiftCashTransactionId);
        return new(StorefrontRefundPayoutSourceKind.TreasuryDebit, source.Id, "Same CashOut voucher identity", "cash", -source.Amount,
            Convert.ToBase64String(source.RowVersion), DateTime.SpecifyKind(source.OccurredAtUtc, DateTimeKind.Utc));
    }
    private static POSShiftService CashService(Web07Case c, GaoApp.Infrastructure.Data.AppDbContext db, int terminal)
        => new(new POSShiftRepository(db), new OrderRepository(db), new WarehouseRepository(db),
            new SelectedStore(c.StoreId), new Web01Actor(c.Seed.UserId, terminalId: terminal), new UserRepository(db),
            Web06FinanceSupport.NoExternal<IAuditLogService>(), new POSShiftHandoverSlipRepository(db),
            Web06FinanceSupport.NoExternal<IPOSShiftClosingSlipService>());
    private sealed class SelectedStore(int id) : ICurrentStore { public int StoreId => id; }
}
