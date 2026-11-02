using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Only FullApplicationFixture's disposable GUID database, never configured store data.
internal static class PosShiftMobileBrowserFixture
{
    internal sealed record Seed(FullApplicationFixture.Account Limited, FullApplicationFixture.Account Other);
    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
        => new(await app.AddAccountAsync(store, PermissionCodes.Pos.Shift.View), await app.AddAccountAsync(app.Stores[1], "*"));
    internal static Task<string> FingerprintAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
        => PosCheckoutMobileBrowserFixture.FingerprintAsync(app, store);
    internal static async Task<object> VerifyAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var shift = await db.POSShifts.SingleAsync();
        var counts = await db.POSShiftCashDenominations.Where(x => x.POSShiftId == shift.Id).ToListAsync();
        var transactions = await db.POSShiftCashTransactions.Where(x => x.POSShiftId == shift.Id).ToListAsync();
        var closing = counts.Where(x => x.EntryType == POSShiftCashDenominationEntryType.Closing).ToList();
        if (shift.Status != POSShiftStatus.Closed || shift.OpeningCash != 200000 || shift.CashInTotal != 50000 || shift.CashOutTotal != 20000
            || shift.ClosingCashExpected != 230000 || shift.ClosingCashActual != 225000 || shift.CashDifference != -5000
            || shift.CloseNote != "UI07a — kiểm đếm giữ đúng ca" || transactions.Count != 2 || closing.Count != 3 || closing.Sum(x => x.Amount) != 225000
            || counts.Where(x => x.EntryType == POSShiftCashDenominationEntryType.Opening).Sum(x => x.Amount) != 200000
            || await db.POSShiftClosingSlips.CountAsync(x => x.POSShiftId == shift.Id) != 1 || await db.OrderPayments.AnyAsync()
            || await db.InventoryBalances.AnyAsync(x => x.OnHandQty != 100))
            throw new InvalidOperationException("Phone shift UI must open/close one shift, preserve exact counts/difference and never post sales or stock.");
        return new { passed = true, shift.Id, shift.Status, shift.OpeningCash, shift.CashInTotal, shift.CashOutTotal,
            shift.ClosingCashExpected, shift.ClosingCashActual, shift.CashDifference, shift.CloseNote,
            closingDenominations = closing.Select(x => new { x.DenominationValue, x.Quantity, x.Amount }),
            transactionCount = transactions.Count, closingSlipCount = 1, noPaymentsOrInventoryChanges = true, storeMenuSettingsUnchanged = true };
    }
}
