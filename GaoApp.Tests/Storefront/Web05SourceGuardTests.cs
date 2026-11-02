using System.Text.Json.Nodes;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05SourceGuardTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("bankAccountId", true)][InlineData("bankAccountId", false)]
    [InlineData("receivingLegalEntityId", true)][InlineData("receivingLegalEntityId", false)]
    public async Task Required_sealed_JSON_identifiers_cannot_pass_SQL_guard_as_missing_or_nonnumeric_UNKNOWN(string field, bool missing)
    {
        var p = await Web05Prepared.CreateAsync(fixture); await using var db = p.Case.Context(); var source = await db.Set<StorefrontPaymentIntent>().AsNoTracking().SingleAsync();
        var json = JsonNode.Parse(source.BindingSnapshotJson)!; if (missing) json.AsObject().Remove(field); else json[field] = "not-a-numeric-identifier";
        var binding = json.ToJsonString(); var sequence = await p.Case.Seed.Database.ExecuteScalarAsync<long>("SELECT NEXT VALUE FOR dbo.StorefrontBankCorrelationSequence"); var reference = "W" + sequence.ToString("D12");
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT StorefrontPaymentIntents(StoreId,StorefrontOrderId,IsActive,Provider,ProviderReference,BankAccountId,ReceivingLegalEntityId,AmountDue,ExpiresAtUtc,BankTerminalCode,BankUserCode,BindingSnapshotJson,CreatedAtUtc,IsDeleted) SELECT StoreId,StorefrontOrderId,0,Provider,{reference},BankAccountId,ReceivingLegalEntityId,AmountDue,ExpiresAtUtc,BankTerminalCode,BankUserCode,{binding},SYSUTCDATETIME(),0 FROM StorefrontPaymentIntents WHERE Id={source.Id}"));
        Assert.Equal(51168, error.Number); Assert.Single(await db.Set<StorefrontPaymentIntent>().ToListAsync()); Assert.Empty(await db.Set<StorefrontPaymentAllocation>().ToListAsync());
    }

    [Theory]
    [InlineData("soft-delete")][InlineData("store-retarget")][InlineData("merchant")]
    public async Task Active_QR_bank_settings_identity_and_liveness_cannot_be_changed_by_raw_SQL(string field)
    {
        var p = await Web05Prepared.CreateAsync(fixture); var foreign = await fixture.CaseAsync(); await using var db = p.Case.Context();
        var before = await db.Database.SqlQueryRaw<string>("SELECT (SELECT * FROM StoreAcbSettings WHERE StoreId={0} FOR JSON PATH) AS [Value]", p.Case.StoreId).SingleAsync();
        var command = field == "soft-delete" ? "UPDATE StoreAcbSettings SET IsDeleted=1 WHERE StoreId={0}" : field == "merchant" ? "UPDATE StoreAcbSettings SET MerchantId=N'OTHER' WHERE StoreId={0}" : "UPDATE StoreAcbSettings SET StoreId={1} WHERE StoreId={0}";
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(command, p.Case.StoreId, foreign.StoreId)); Assert.Equal(51150, error.Number);
        Assert.Equal(before, await db.Database.SqlQueryRaw<string>("SELECT (SELECT * FROM StoreAcbSettings WHERE StoreId={0} FOR JSON PATH) AS [Value]", p.Case.StoreId).SingleAsync());
    }

    [Theory]
    [InlineData("missing-settlement")][InlineData("wrong-amount")][InlineData("wrong-store")]
    public async Task Raw_web_receipt_cannot_become_sale_payment_without_valid_current_settlement_source(string invalid)
    {
        var p = await Web05Prepared.CreateAsync(fixture); p.Bank.Pay(p.Reference, 45_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        var target = invalid == "wrong-store" ? await fixture.Web05Async() : p.Case; await using var pos = await Web05PosCase.CreateAsync(target);
        await using var db = target.Context(); int receipt;
        await using (var origin = p.Case.Context()) receipt = (await origin.Set<BankVerifiedReceipt>().SingleAsync(x => x.OriginStoreId == p.Case.StoreId)).Id;
        var amount = invalid == "wrong-amount" ? 45_001m : 45_000m;
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT OrderPayments(StoreId,OrderId,Method,Amount,IsDebtCollection,ReferenceCode,Provider,PaidAtUtc,BankVerifiedReceiptId,CreatedAtUtc,IsDeleted) VALUES({target.StoreId},{pos.Order.Id},{(int)PaymentMethod.BankTransfer},{amount},0,{p.Reference},N'ACB',SYSUTCDATETIME(),{receipt},SYSUTCDATETIME(),0)"));
        var sharedBudgetDenied = invalid == "wrong-amount" && error.Errors.Cast<SqlError>().Any(x => x.Number == 51200 && x.Message == "Goods and service mirrors share the one actual bank receipt budget.");
        Assert.True(error.Number is 547 or 51165 || sharedBudgetDenied, "Expected a real source/tenant/amount guard, SQL number=" + error.Number);
        Assert.Empty(await db.OrderPayments.ToListAsync()); await using var check = p.Case.Context(); Assert.Single(await check.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Single(await check.Set<BankReceiptConsumption>().Where(x => x.BankVerifiedReceiptId == receipt).ToListAsync());
    }
}
