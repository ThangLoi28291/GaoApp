using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05GlobalLedgerTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Authenticated_unmatched_money_is_durable_full_amount_protected_and_survives_new_context_and_key_provider()
    {
        var seed = await fixture.CaseAsync(); var directory = Web05Support.RuntimePath("unmatched-bank-evidence");
        var transaction = "W05-" + Guid.NewGuid().ToString("N"); const string raw = "{\"private\":\"synthetic-bank-evidence-W05\"}";
        var input = await Input(seed, transaction); input = input with { Amount = 5_000_000_000m, RawEvidence = raw };
        BankReceiptRecord recorded;
        await using (var db = seed.Context()) recorded = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(input);
        Assert.True(recorded.Conflict); Assert.False(recorded.Duplicate);
        await using var reopened = seed.Context(); var receipt = await reopened.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.ExternalTransactionId == transaction);
        Assert.Equal(recorded.Id, receipt.Id); Assert.Equal(5_000_000_000m, receipt.Amount);
        Assert.Equal(seed.StoreId, receipt.OriginStoreId); Assert.Equal(seed.BankAccountId, receipt.BankAccountId);
        Assert.Equal(input.RawBankTime, receipt.RawBankTime); Assert.Equal(BankTimePrecision.Timestamp, receipt.TimePrecision);
        Assert.Equal(new DateTime(2026, 10, 30, 17, 0, 1), receipt.OccurredAtUtc);
        Assert.True(receipt.NeedsReview); Assert.DoesNotContain("synthetic-bank-evidence-W05", receipt.ProtectedEvidence);
        Assert.Equal(raw, Web05Support.VerifiedRaw(Web05Support.AdminProtocol(directory), seed.StoreId, receipt.ProtectedEvidence));
        Assert.Equal(BankPaymentChannel.Unknown, receipt.Channel);
        using (var envelope = JsonDocument.Parse(Web05Support.AdminProtocol(directory).Unprotect(seed.StoreId, receipt.ProtectedEvidence)))
        { Assert.Equal((int)BankPaymentChannel.Storefront, envelope.RootElement.GetProperty("ClaimedChannel").GetInt32()); Assert.Equal(input.ProviderReference, envelope.RootElement.GetProperty("ProviderReference").GetString()); }
        Assert.Empty(await reopened.Set<BankReceiptConsumption>().Where(x => x.BankVerifiedReceiptId == receipt.Id).ToListAsync());
        Assert.Empty(await reopened.StorefrontOrders.ToListAsync()); Assert.Empty(await reopened.OrderPayments.ToListAsync());
        Assert.Empty(await reopened.InventoryTransactions.ToListAsync()); Assert.Empty(await reopened.AdminMenuItems.ToListAsync());
    }

    [Fact]
    public async Task Same_evidence_from_new_transport_envelope_is_one_physical_receipt_and_keeps_original_proof()
    {
        var seed = await fixture.CaseAsync(); var directory = Web05Support.RuntimePath("duplicate-bank-evidence");
        var input = await Input(seed, "W05-" + Guid.NewGuid().ToString("N"));
        BankReceiptRecord first; BankReceiptRecord second; string original;
        await using (var db = seed.Context())
        {
            first = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(input);
            original = (await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.Id == first.Id)).ProtectedEvidence;
        }
        await using (var db = seed.Context()) second = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(input with { RawEvidence = "{\"newTransportEnvelope\":true}" });
        Assert.Equal(first.Id, second.Id); Assert.True(second.Duplicate);
        await using var check = seed.Context(); var row = Assert.Single(await check.Set<BankVerifiedReceipt>().AsNoTracking().Where(x => x.ExternalTransactionId == input.ExternalTransactionId).ToListAsync());
        Assert.Equal(input.Amount, row.Amount); Assert.Equal(original, row.ProtectedEvidence);
        Assert.Empty(await check.Set<BankReceiptConflict>().Where(x => x.BankVerifiedReceiptId == row.Id).ToListAsync());
        Assert.Empty(await check.Set<BankReceiptConsumption>().Where(x => x.BankVerifiedReceiptId == row.Id).ToListAsync());
    }

    [Theory]
    [InlineData("amount")][InlineData("reference")][InlineData("channel")][InlineData("bank-time")][InlineData("status")]
    public async Task Changed_facts_for_same_physical_identity_are_retained_as_conflict_without_overwriting_money(string change)
    {
        var seed = await fixture.CaseAsync(); var directory = Web05Support.RuntimePath("conflicting-bank-evidence");
        var input = await Input(seed, "W05-" + Guid.NewGuid().ToString("N"));
        var changed = change switch
        {
            "amount" => input with { Amount = input.Amount + 1 },
            "reference" => input with { ProviderReference = "W999999999999" },
            "channel" => input with { Channel = BankPaymentChannel.Pos },
            "bank-time" => input with { RawBankTime = "2026-11-01" },
            _ => input with { Status = "REVERSED" }
        };
        changed = changed with { RawEvidence = JsonSerializer.Serialize(new { changed.Amount, changed.ProviderReference, changed.Channel, changed.RawBankTime, changed.Status }) };
        BankReceiptRecord first;
        await using (var db = seed.Context()) first = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(input);
        for (var i = 0; i < 2; i++)
        {
            await using var db = seed.Context(); var conflict = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(changed);
            Assert.Equal(first.Id, conflict.Id); Assert.True(conflict.Duplicate); Assert.True(conflict.Conflict);
        }
        await using var check = seed.Context(); var receipt = await check.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.Id == first.Id);
        Assert.Equal(input.Amount, receipt.Amount); Assert.Equal(input.ProviderReference, receipt.ProviderReference); Assert.Equal(input.RawBankTime, receipt.RawBankTime);
        Assert.True(receipt.NeedsReview);
        var evidence = Assert.Single(await check.Set<BankReceiptConflict>().AsNoTracking().Where(x => x.BankVerifiedReceiptId == first.Id).ToListAsync());
        Assert.Equal(seed.StoreId, evidence.AttemptedStoreId);
        Assert.Equal(changed.RawEvidence, Web05Support.VerifiedRaw(Web05Support.AdminProtocol(directory), seed.StoreId, evidence.ProtectedEvidence));
        Assert.Empty(await check.Set<BankReceiptConsumption>().Where(x => x.BankVerifiedReceiptId == first.Id).ToListAsync());
    }

    [Fact]
    public async Task Same_receiving_account_in_two_stores_collides_globally_and_keeps_new_tenant_evidence_separate()
    {
        var a = await fixture.CaseAsync(); var b = await fixture.CaseAsync(); var directory = Web05Support.RuntimePath("cross-store-bank-alias");
        // Both seed accounts deliberately represent the same physical ACB account (000TEST), despite distinct config IDs/stores.
        var input = await Input(a, "W05-" + Guid.NewGuid().ToString("N")); BankReceiptRecord original; BankReceiptRecord alias;
        var aliasAccount = (await Input(b, input.ExternalTransactionId)).Account;
        await using (var db = a.Context()) original = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(input);
        await using (var db = b.Context()) alias = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(input with { Account = aliasAccount, RawEvidence = "tenant-B-private-evidence" });
        Assert.Equal(original.Id, alias.Id); Assert.True(alias.Duplicate); Assert.True(alias.Conflict);
        await using var check = a.Context(); var row = Assert.Single(await check.Set<BankVerifiedReceipt>().AsNoTracking().Where(x => x.ExternalTransactionId == input.ExternalTransactionId).ToListAsync());
        Assert.Equal(a.StoreId, row.OriginStoreId); Assert.Equal(a.BankAccountId, row.BankAccountId); Assert.Equal(input.Amount, row.Amount);
        var conflict = Assert.Single(await check.Set<BankReceiptConflict>().Where(x => x.BankVerifiedReceiptId == row.Id).ToListAsync());
        Assert.Equal(b.StoreId, conflict.AttemptedStoreId);
        Assert.Equal("tenant-B-private-evidence", Web05Support.VerifiedRaw(Web05Support.AdminProtocol(directory), b.StoreId, conflict.ProtectedEvidence));
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => Web05Support.AdminProtocol(directory).Unprotect(b.StoreId, row.ProtectedEvidence));
        Assert.Empty(await check.Set<BankReceiptConsumption>().Where(x => x.BankVerifiedReceiptId == row.Id).ToListAsync());
    }

    [Theory]
    [InlineData("ACB", "000TEST2")][InlineData("ACB", "00TEST")][InlineData("VCB", "000TEST")]
    public async Task Same_transaction_number_on_a_distinct_physical_receiving_account_is_not_falsely_merged(string bankCode, string accountNumber)
    {
        var seed = await fixture.CaseAsync(); var directory = Web05Support.RuntimePath("distinct-receiving-bank-account"); int other;
        var otherSeed = bankCode == "VCB" && accountNumber == "000TEST" ? await fixture.CaseAsync() : seed;
        await using (var db = otherSeed.Context())
        {
            if (otherSeed.StoreId != seed.StoreId)
            {
                var bank = await db.StoreBankAccounts.SingleAsync(x => x.Id == otherSeed.BankAccountId);
                bank.BankCode = bankCode; bank.BankName = "Synthetic distinct bank"; bank.AccountNumber = accountNumber;
                await db.SaveChangesAsync(); other = bank.Id;
            }
            else
            {
                var bank = new StoreBankAccount { StoreId = seed.StoreId, BankCode = bankCode, BankName = "Synthetic distinct bank", AccountNumber = accountNumber, AccountName = "Synthetic owner" };
                db.Add(bank); await db.SaveChangesAsync(); other = bank.Id;
            }
        }
        var input = await Input(seed, "W05-" + Guid.NewGuid().ToString("N")); BankReceiptRecord first; BankReceiptRecord second;
        BankVerifiedAccount otherAccount;
        await using (var db = otherSeed.Context()) otherAccount = GlobalBankReceiptLedger.AccountSnapshot(await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == other));
        await using (var db = seed.Context()) first = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(input);
        await using (var db = otherSeed.Context()) second = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(input with { Account = otherAccount });
        Assert.NotEqual(first.Id, second.Id); Assert.False(second.Duplicate);
        await using var check = seed.Context(); var rows = await check.Set<BankVerifiedReceipt>().AsNoTracking().Where(x => x.ExternalTransactionId == input.ExternalTransactionId).ToListAsync();
        Assert.Equal(2, rows.Count); Assert.Equal(460_000m, rows.Sum(x => x.Amount));
    }

    [Fact]
    public async Task Concurrent_independent_connections_record_one_global_physical_transfer()
    {
        var seed = await fixture.CaseAsync(); var directory = Web05Support.RuntimePath("concurrent-bank-receipts");
        var input = await Input(seed, "W05-" + Guid.NewGuid().ToString("N"));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = seed.Context(); return await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(input);
        }));
        Assert.Single(results.Select(x => x.Id).Distinct()); Assert.Single(results, x => !x.Duplicate);
        await using var check = seed.Context(); var row = Assert.Single(await check.Set<BankVerifiedReceipt>().Where(x => x.ExternalTransactionId == input.ExternalTransactionId).ToListAsync());
        Assert.Equal(230_000m, row.Amount); Assert.Empty(await check.Set<BankReceiptConsumption>().Where(x => x.BankVerifiedReceiptId == row.Id).ToListAsync());
    }

    [Fact]
    public async Task Receipt_boundary_rejects_ambient_business_transaction_instead_of_allowing_later_business_rollback_to_erase_money()
    {
        var seed = await fixture.CaseAsync(); var directory = Web05Support.RuntimePath("receipt-transaction-boundary");
        var input = await Input(seed, "W05-" + Guid.NewGuid().ToString("N"));
        await using (var db = seed.Context())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(input));
        }
        await using var check = seed.Context(); Assert.Empty(await check.Set<BankVerifiedReceipt>().Where(x => x.ExternalTransactionId == input.ExternalTransactionId).ToListAsync());
    }

    [Theory]
    [InlineData("")][InlineData("not-a-bank-date")][InlineData("2026-02-29")]
    [InlineData("0001-01-01")][InlineData("9999-12-31T23:59:59Z")]
    public async Task Invalid_or_unrepresentable_bank_time_retains_raw_money_as_durable_review(string rawTime)
    {
        var seed = await fixture.CaseAsync(); var directory = Web05Support.RuntimePath("unknown-bank-time");
        var input = await Input(seed, "W05-" + Guid.NewGuid().ToString("N")); input = input with { RawBankTime = rawTime };
        var clock = new Web04Clock { Utc = new DateTime(2026, 10, 31, 12, 34, 56, DateTimeKind.Utc) };
        BankReceiptRecord saved;
        await using (var db = seed.Context()) saved = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory), clock).RecordVerifiedAsync(input);
        Assert.Equal(BankTimePrecision.Unknown, saved.TimePrecision); Assert.True(saved.Conflict);
        Assert.Null(saved.BankBusinessDate); Assert.Null(saved.OccurredAtUtc); Assert.Equal(clock.Utc, saved.VerifiedAtUtc);
        await using var check = seed.Context(); var row = await check.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.Id == saved.Id);
        Assert.Equal(230_000m, row.Amount); Assert.Equal(rawTime, row.RawBankTime); Assert.Null(row.OccurredAtUtc); Assert.Null(row.BankBusinessDate);
        Assert.True(row.NeedsReview); Assert.Empty(await check.Set<BankReceiptConsumption>().Where(x => x.BankVerifiedReceiptId == row.Id).ToListAsync());
    }

    [Fact]
    public async Task Date_only_bank_evidence_keeps_its_actual_day_when_verified_on_a_later_day()
    {
        var seed = await fixture.CaseAsync(); var directory = Web05Support.RuntimePath("delayed-bank-day");
        var input = await Input(seed, "W05-" + Guid.NewGuid().ToString("N")); input = input with { RawBankTime = "2026-09-08" };
        var clock = new Web04Clock { Utc = new DateTime(2026, 10, 31, 12, 34, 56, DateTimeKind.Utc) };
        await using var db = seed.Context(); var saved = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory), clock).RecordVerifiedAsync(input);
        Assert.Equal(BankTimePrecision.BankDay, saved.TimePrecision); Assert.Equal(new DateTime(2026, 9, 8), saved.BankBusinessDate);
        Assert.Equal(new DateTime(2026, 9, 7, 17, 0, 0, DateTimeKind.Utc), saved.OccurredAtUtc); Assert.Equal(clock.Utc, saved.VerifiedAtUtc);
        var row = await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.Id == saved.Id);
        Assert.Equal("2026-09-08", row.RawBankTime); Assert.Equal(BankTimePrecision.BankDay, row.TimePrecision);
        Assert.NotEqual(row.VerifiedAtUtc.Date, row.BankBusinessDate); Assert.Equal(input.Amount, row.Amount);
    }

    [Fact]
    public async Task Authenticated_store_cannot_select_foreign_bank_account_id_to_record_money()
    {
        var a = await fixture.CaseAsync(); var b = await fixture.CaseAsync(); var directory = Web05Support.RuntimePath("foreign-bank-rejection");
        var input = await Input(b, "W05-" + Guid.NewGuid().ToString("N"));
        await using (var db = a.Context())
        {
            var ledger = new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory));
            await Assert.ThrowsAsync<GaoApp.Application.Common.Exceptions.ForbiddenAppException>(() => ledger.RecordVerifiedAsync(input));
            await Assert.ThrowsAsync<GaoApp.Application.Common.Exceptions.ValidationAppException>(() => ledger.RecordVerifiedAsync(input with { Account = input.Account with { StoreId = a.StoreId } }));
        }
        await using var check = a.Context(); Assert.Empty(await check.Set<BankVerifiedReceipt>().Where(x => x.ExternalTransactionId == input.ExternalTransactionId).ToListAsync());
    }

    [Theory]
    [InlineData("account-number")][InlineData("bank-code")][InlineData("deactivate")][InlineData("soft-delete")]
    public async Task Account_changed_after_trusted_lookup_keeps_original_physical_money_identity_and_retry_does_not_duplicate(string mutation)
    {
        var seed = await fixture.CaseAsync(); var directory = Web05Support.RuntimePath("immutable-verified-account");
        // Capture the trusted receiving account before an independent admin context changes its mutable configuration.
        var input = await Input(seed, "W05-" + Guid.NewGuid().ToString("N"));
        var oldIdentity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("ACB|000TEST")));
        await using (var admin = seed.Context())
        {
            var account = admin.StoreBankAccounts.Where(x => x.Id == seed.BankAccountId);
            if (mutation == "account-number") await account.ExecuteUpdateAsync(x => x.SetProperty(b => b.AccountNumber, "000OTHER"));
            else if (mutation == "bank-code") await account.ExecuteUpdateAsync(x => x.SetProperty(b => b.BankCode, "VCB"));
            else if (mutation == "deactivate") await account.ExecuteUpdateAsync(x => x.SetProperty(b => b.IsActive, false));
            else await account.ExecuteUpdateAsync(x => x.SetProperty(b => b.IsDeleted, true));
        }
        BankReceiptRecord first; BankReceiptRecord retry;
        await using (var db = seed.Context()) first = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(input);
        await using (var db = seed.Context()) retry = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(directory)).RecordVerifiedAsync(input);
        Assert.True(first.Conflict); Assert.True(retry.Conflict); Assert.True(retry.Duplicate); Assert.Equal(first.Id, retry.Id);
        await using var check = seed.Context(); var receipt = Assert.Single(await check.Set<BankVerifiedReceipt>().AsNoTracking().Where(x => x.ExternalTransactionId == input.ExternalTransactionId).ToListAsync());
        Assert.Equal(oldIdentity, receipt.AccountIdentity); Assert.Equal(seed.BankAccountId, receipt.BankAccountId); Assert.Equal(seed.StoreId, receipt.OriginStoreId);
        Assert.Equal(230_000m, receipt.Amount); Assert.Equal(input.RawBankTime, receipt.RawBankTime); Assert.True(receipt.NeedsReview);
        Assert.Equal(input.RawEvidence, Web05Support.VerifiedRaw(Web05Support.AdminProtocol(directory), seed.StoreId, receipt.ProtectedEvidence));
        Assert.Empty(await check.Set<BankReceiptConsumption>().Where(x => x.BankVerifiedReceiptId == receipt.Id).ToListAsync());
        Assert.Empty(await check.OrderPayments.ToListAsync()); Assert.Empty(await check.StorefrontOrders.ToListAsync());
    }

    private static async Task<BankVerifiedInput> Input(Web01Case seed, string transaction)
    {
        await using var db = seed.Context(); var bank = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == seed.BankAccountId);
        return new(GlobalBankReceiptLedger.AccountSnapshot(bank), "W000000000001", BankPaymentChannel.Storefront,
            transaction, 230_000m, "COMPLETED", "2026-10-31T00:00:01+07:00", "synthetic-private-bank-raw-evidence");
    }
}
