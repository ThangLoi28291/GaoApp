using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

// Shared lock protocol without staff/stock-posting dependencies in the customer host.
internal static class StorefrontOrderProofLocks
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static IQueryable<BankVerifiedReceipt> BankReceiptsForOrder(AppDbContext context, int store, int orderId)
    {
        var intents = context.Set<StorefrontPaymentIntent>().Where(x => x.StoreId == store && x.StorefrontOrderId == orderId);
        return context.Set<BankVerifiedReceipt>().Where(receipt => receipt.OriginStoreId == store && intents.Any(intent =>
            EF.Functions.Collate(intent.ProviderReference, "Latin1_General_100_BIN2") == receipt.ProviderReference &&
            EF.Functions.DataLength(intent.ProviderReference) == EF.Functions.DataLength(receipt.ProviderReference)));
    }
    internal static Task CashAsync(AppDbContext db, int store, int cashId, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [POSShiftCashTransactions] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={store} AND [Id]={cashId}; SELECT [Id] FROM [TreasuryEntries] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={store} AND [POSShiftCashTransactionId]={cashId}; SELECT [Id] FROM [POSCashAdjustmentRequests] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={store} AND [TransactionId]={cashId};", ct);
    internal static async Task LockAsync(AppDbContext db, int store, int id, CancellationToken ct, int? incomingGlobalReceiptId = null, int? incomingCashId = null, int? incomingExpenseId = null,
        IReadOnlyCollection<string>? additionalAccounts = null, IReadOnlyCollection<int>? additionalTreasuryIds = null)
    {
        var intents = await db.Set<StorefrontPaymentIntent>().AsNoTracking().Where(x => x.StoreId == store && x.StorefrontOrderId == id).ToListAsync(ct);
        var matchedReceipts = BankReceiptsForOrder(db, store, id).Select(x => x.Id);
        var claimedIds = await db.Set<StorefrontCodReceipt>().AsNoTracking().Where(x => x.StoreId == store && x.StorefrontOrderId == id && x.GlobalBankReceiptId != null).Select(x => x.GlobalBankReceiptId!.Value).ToListAsync(ct);
        if (incomingGlobalReceiptId.HasValue) claimedIds.Add(incomingGlobalReceiptId.Value);
        var rows = await db.Set<BankVerifiedReceipt>().AsNoTracking().Where(x => x.OriginStoreId == store && (matchedReceipts.Contains(x.Id) || claimedIds.Contains(x.Id))).ToListAsync(ct);
        var accounts = intents.Select(x => JsonSerializer.Deserialize<StorefrontBankBinding>(x.BindingSnapshotJson, Json)!.AccountIdentity)
            .Concat(rows.Select(x => x.AccountIdentity)).Concat(additionalAccounts ?? []).Distinct().Order(StringComparer.Ordinal).ToList();
        foreach (var account in accounts) await GlobalBankReceiptLedger.LockAsync(db, "GaoApp:BankAccountEvidence:" + account, ct);
        foreach (var receipt in rows.OrderBy(x => GlobalBankReceiptLedger.Hash(x.Provider + "|" + x.AccountIdentity + "|" + x.ExternalTransactionId), StringComparer.Ordinal))
            await GlobalBankReceiptLedger.LockAsync(db, "GaoApp:BankReceipt:" + GlobalBankReceiptLedger.Hash(receipt.Provider + "|" + receipt.AccountIdentity + "|" + receipt.ExternalTransactionId), ct);
        foreach (var accountId in rows.Select(x => x.BankAccountId).Distinct().Order())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StoreBankAccounts] WITH(HOLDLOCK) WHERE [StoreId]={store} AND [Id]={accountId}; SELECT [Id] FROM [StorefrontBankCollectionAuthorities] WITH(HOLDLOCK) WHERE [StoreId]={store} AND [BankAccountId]={accountId};", ct);
        foreach (var intent in intents.OrderBy(x => x.Id))
        {
            // Unattributed observations deliberately do not masquerade as bank credits. Hold
            // their actual range through the decision, including the empty-range insertion case.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [dbo].[StorefrontUnattributedBankReceipts] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={store} AND [PaymentIntentId]={intent.Id};", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StorefrontBankReviewRestrictions] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={store} AND [PaymentIntentId]={intent.Id}; SELECT [Id] FROM [AcbQrNotificationItems] WITH(HOLDLOCK) WHERE [StoreId]={store} AND [ProviderOrderId]={intent.ProviderReference};", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StorefrontBankScanAttempts] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={store} AND [PaymentIntentId]={intent.Id}; SELECT [Id] FROM [StorefrontBankScanCompletions] WITH(HOLDLOCK) WHERE [StoreId]={store} AND [PaymentIntentId]={intent.Id};", ct);
        }
        // Range-lock reversal children before cash/linked-original rows. Generic treasury
        // reversal inserts its child before changing the original; keep the same SQL order.
        foreach (var treasuryId in (additionalTreasuryIds ?? []).Distinct().Order())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [TreasuryEntries] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={store} AND [ReversalOfId]={treasuryId};", ct);
        var cashSources = await db.Set<StorefrontCodReceipt>().AsNoTracking().Where(x => x.StoreId == store && x.StorefrontOrderId == id && x.POSShiftCashTransactionId != null).ToListAsync(ct);
        var cashIds = cashSources.Select(x => x.POSShiftCashTransactionId!.Value).ToList();
        if (incomingCashId.HasValue) cashIds.Add(incomingCashId.Value);
        foreach (var cashId in cashIds.Distinct().Order()) await CashAsync(db, store, cashId, ct);
        var expenseIds = await db.Set<StorefrontCarrierWithholding>().AsNoTracking().Where(x => x.StoreId == store && x.StorefrontOrderId == id).Select(x => x.OperatingExpenseId).ToListAsync(ct);
        if (incomingExpenseId.HasValue) expenseIds.Add(incomingExpenseId.Value);
        foreach (var expenseId in expenseIds.Distinct().Order()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [OperatingExpenses] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={store} AND [Id]={expenseId}; SELECT [Id] FROM [TreasuryEntries] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={store} AND [OperatingExpenseId]={expenseId};", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StorefrontServiceRevenueSettings] WITH(HOLDLOCK) WHERE [StoreId]={store}; SELECT t.Id FROM [Taxes] t WITH(HOLDLOCK) JOIN [StorefrontServiceRevenueSettings] s ON s.StoreId=t.StoreId AND s.TaxId=t.Id WHERE s.StoreId={store};", ct);
    }
}
