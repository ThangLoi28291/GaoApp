using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Services.Banking;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05SchemaTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Fresh_payment_schema_matches_actual_manifest_with_no_menu_store_role_grants_or_money_seed()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(); await using var db = sql.CreateHostContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
        foreach (var type in new[] { typeof(StorefrontQrProcessing), typeof(StorefrontBankObservation), typeof(StorefrontUnattributedBankReceipt) })
        {
            var entity = db.Model.FindEntityType(type); Assert.NotNull(entity);
            var store = Assert.Single(entity.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(Store)); Assert.Equal("StoreId", Assert.Single(store.Properties).Name);
            Assert.NotNull(entity.FindNavigation("Store")); Assert.Same(store, entity.FindNavigation("Store")!.ForeignKey);
        }
        await db.Database.OpenConnectionAsync(); var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray(); var expected = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
        var live = await new SqlServerSchemaSnapshotReader(db).ReadAsync(migrations); var comparison = DatabaseSchemaComparer.Compare(expected, live);
        var folder = "schema/" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Web05Support.Artifact(folder + "/expected.json"), JsonSerializer.Serialize(expected)); await File.WriteAllTextAsync(Web05Support.Artifact(folder + "/live.json"), JsonSerializer.Serialize(live));
        await File.WriteAllTextAsync(Web05Support.Artifact(folder + "/comparison.json"), JsonSerializer.Serialize(comparison));
        var checks = await db.Database.SqlQueryRaw<string>("SELECT (SELECT OBJECT_NAME(parent_object_id) AS TableName,name,definition,is_disabled,is_not_trusted FROM sys.check_constraints ORDER BY parent_object_id,name FOR JSON PATH) AS [Value]").SingleAsync();
        var triggers = await db.Database.SqlQueryRaw<string>("SELECT (SELECT OBJECT_NAME(t.parent_id) AS TableName,t.name,t.is_disabled,m.definition FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id=t.object_id WHERE t.parent_class=1 ORDER BY t.parent_id,t.name FOR JSON PATH) AS [Value]").SingleAsync();
        await File.WriteAllTextAsync(Web05Support.Artifact(folder + "/raw-checks.json"), checks); await File.WriteAllTextAsync(Web05Support.Artifact(folder + "/raw-triggers.json"), triggers);
        Assert.True(comparison.IsMatch, JsonSerializer.Serialize(comparison.Mismatches));
        var tables = await db.Database.SqlQueryRaw<string>("SELECT name AS [Value] FROM sys.tables").ToListAsync();
        foreach (var table in new[] { "BankPaymentReferences", "BankVerifiedReceipts", "BankReceiptConflicts", "BankReceiptConsumptions", "StorefrontQrProcessing", "StorefrontBankObservations", "StorefrontUnattributedBankReceipts" }) Assert.Contains(table, tables);
        Assert.Empty(await db.Stores.ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync()); Assert.Empty(await db.RolePermissions.ToListAsync());
        Assert.Empty(await db.Set<BankVerifiedReceipt>().ToListAsync()); Assert.Empty(await db.Set<BankReceiptConsumption>().ToListAsync()); Assert.Empty(await db.StorefrontOrders.ToListAsync());
        var sequence = await sql.ExecuteScalarAsync<string>("SELECT CONCAT(CONVERT(nvarchar(40),maximum_value),N'|',is_cycling) FROM sys.sequences WHERE name=N'StorefrontBankCorrelationSequence'"); Assert.Equal("999999999999|0", sequence);
    }

    [Theory]
    [InlineData("receipt-amount")][InlineData("receipt-time")][InlineData("receipt-raw")][InlineData("receipt-identity")]
    [InlineData("receipt-delete")][InlineData("reference-rebind")][InlineData("intent-binding")][InlineData("claim-delete")][InlineData("processing-trace")]
    public async Task Direct_SQL_cannot_rewrite_or_delete_durable_money_claim_or_sealed_bank_binding(string mutation)
    {
        var p = await Web05Prepared.CreateAsync(fixture); p.Bank.Pay(p.Reference, 45_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank); var before = await BankState(p.Case);
        await using var db = p.Case.Context(); var sql = mutation switch
        {
            "receipt-amount" => "UPDATE BankVerifiedReceipts SET Amount=45001 WHERE OriginStoreId={0}",
            "receipt-time" => "UPDATE BankVerifiedReceipts SET RawBankTime=N'2026-01-01' WHERE OriginStoreId={0}",
            "receipt-raw" => "UPDATE BankVerifiedReceipts SET ProtectedEvidence=N'tampered' WHERE OriginStoreId={0}",
            "receipt-identity" => "UPDATE BankVerifiedReceipts SET AccountIdentity=REPLICATE(N'F',64) WHERE OriginStoreId={0}",
            "receipt-delete" => "DELETE BankVerifiedReceipts WHERE OriginStoreId={0}",
            "reference-rebind" => "UPDATE BankPaymentReferences SET Reference=N'W999999999998' WHERE OriginStoreId={0}",
            "intent-binding" => "UPDATE StorefrontPaymentIntents SET BindingSnapshotJson=N'{{}}' WHERE StoreId={0}",
            "claim-delete" => "DELETE BankReceiptConsumptions WHERE OriginStoreId={0}",
            _ => "UPDATE StorefrontQrProcessing SET TraceNumber=N'different-request-trace' WHERE StoreId={0}"
        };
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, p.Case.StoreId)); Assert.True(error.Number == 547 || error.Number >= 51000, "Expected an integrity guard, SQL number=" + error.Number);
        Assert.Equal(before, await BankState(p.Case));
    }

    [Fact]
    public async Task Raw_SQL_cannot_promote_service_derived_ambiguous_receipt_into_a_certified_bank_asset()
    {
        var p = await Web05Prepared.CreateAsync(fixture); p.Bank.Pay(p.Reference, 45_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        int secondaryId; const string raw = "Synthetic authenticated secondary45k without transaction identity mapping";
        await using (var db = p.Case.Context())
        {
            var bank = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == p.Case.Seed.BankAccountId);
            var input = new BankVerifiedInput(GlobalBankReceiptLedger.AccountSnapshot(bank), "PHONE-UNMAPPED", BankPaymentChannel.Unknown,
                "PHONE:FT" + Guid.NewGuid().ToString("N"), 45_000m, "COMPLETED", p.Case.Web.Clock.Utc.ToString("O"), raw,
                BankExternalIdentityKind.BankNotification, false, true);
            secondaryId = (await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(p.Case.AdminKeys), p.Case.Web.Clock).RecordVerifiedAsync(input)).Id;
            var original = await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.Id == secondaryId);
            Assert.Equal(BankReceiptAssetConfidence.TransactionIdentityAmbiguous, original.AssetConfidence); Assert.True(original.NeedsReview); Assert.Equal(45_000m, original.Amount);
            Assert.Equal(raw, Web05Support.VerifiedRaw(Web05Support.AdminProtocol(p.Case.AdminKeys), p.Case.StoreId, original.ProtectedEvidence));
        }
        await Web05CashFlowTests.SnapshotIsolation(p.Case.Seed); var day = p.Case.Web.Clock.Utc.AddHours(7).Date;
        var beforeFlow = await Web05CashFlowTests.Flow(p.Case.Seed, day); Assert.Equal(45_000m, beforeFlow.Summary.Single(x => x.Fund == "bank").Inflow);
        var before = await BankState(p.Case);
        await using (var db = p.Case.Context())
        {
            Assert.Single(await db.Set<BankReceiptConsumption>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync());
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE BankVerifiedReceipts SET AssetConfidence={(int)BankReceiptAssetConfidence.CertifiedDistinct} WHERE Id={secondaryId} AND OriginStoreId={p.Case.StoreId}"));
            Assert.Equal(51163, error.Number);
        }
        Assert.Equal(before, await BankState(p.Case));
        var afterFlow = await Web05CashFlowTests.Flow(p.Case.Seed, day); Assert.Equal(45_000m, afterFlow.Summary.Single(x => x.Fund == "bank").Inflow);
        Assert.NotNull(afterFlow.BankReceiptReviews); Assert.Contains(afterFlow.BankReceiptReviews, x => x.Amount == 45_000m);
        await using var check = p.Case.Context(); var retained = await check.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.Id == secondaryId);
        Assert.Equal(BankReceiptAssetConfidence.TransactionIdentityAmbiguous, retained.AssetConfidence); Assert.Equal(45_000m, retained.Amount); Assert.True(retained.NeedsReview);
        Assert.Single(await check.Set<BankReceiptConsumption>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync()); Assert.Single(await check.Set<StorefrontPaymentAllocation>().ToListAsync());
    }

    [Fact]
    public async Task Global_SQL_unique_constraint_prevents_bypassing_service_to_duplicate_physical_transfer()
    {
        var p = await Web05Prepared.CreateAsync(fixture); p.Bank.Pay(p.Reference, 45_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank); await using var db = p.Case.Context();
        var row = await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.OriginStoreId == p.Case.StoreId);
        db.Add(new BankVerifiedReceipt { Provider = row.Provider, AccountIdentity = row.AccountIdentity, ExternalTransactionId = row.ExternalTransactionId, OriginStoreId = row.OriginStoreId,
            BankAccountId = row.BankAccountId, ProviderReference = row.ProviderReference, Channel = row.Channel, Amount = row.Amount, Status = row.Status, Fingerprint = row.Fingerprint,
            ProtectedEvidence = row.ProtectedEvidence, RawBankTime = row.RawBankTime, TimePrecision = row.TimePrecision, BankBusinessDate = row.BankBusinessDate, OccurredAtUtc = row.OccurredAtUtc, VerifiedAtUtc = row.VerifiedAtUtc });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.IsType<SqlException>(error.InnerException); Assert.Contains(((SqlException)error.InnerException!).Number, new[] { 2601, 2627 });
        await using var check = p.Case.Context(); Assert.Single(await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync()); Assert.Single(await check.Set<StorefrontPaymentAllocation>().ToListAsync());
    }

    [Fact]
    public async Task Correlation_sequence_last_value_is_13_characters_then_fails_closed_without_wrapping_or_leaving_partial_order_and_holds()
    {
        var isolated = new Web01Fixture(); await isolated.InitializeAsync();
        try
        {
            var c = await isolated.Web05Async(); await c.Seed.Database.ExecuteAsync("ALTER SEQUENCE dbo.StorefrontBankCorrelationSequence RESTART WITH 999999999999");
            var firstRequest = Web04Case.PlaceRequest(await c.QuoteAsync()); var first = await c.PlaceAsync(firstRequest); Assert.Equal(GaoApp.Application.Storefront.StorefrontPlacementStatus.Placed, first.Status);
            var next = Web04Case.PlaceRequest(await c.QuoteAsync()); await Assert.ThrowsAsync<ValidationAppException>(() => c.PlaceAsync(next));
            Assert.Equal(first.Order, (await c.PlaceAsync(firstRequest)).Order); await using var db = c.Context(); Assert.Single(await db.StorefrontOrders.ToListAsync()); Assert.Single(await db.InventoryReservations.ToListAsync());
            Assert.Equal("W999999999999", (await db.Set<StorefrontPaymentIntent>().SingleAsync()).ProviderReference); Assert.Equal(1, (await db.InventoryBalances.SingleAsync()).ReservedQty);
            Assert.Single(await db.Set<BankPaymentReference>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Fact]
    public async Task W_reference_sequence_has_explicit_positive_minimum_and_fixed_noncycling_13_character_limit_in_model_and_SQL()
    {
        await using var db = fixture.Database.CreateHostContext();
        const string query = "SELECT CONCAT(CONVERT(varchar(40),minimum_value),N'|',CONVERT(varchar(40),maximum_value),N'|',CONVERT(varchar(40),start_value),N'|',CONVERT(varchar(40),increment),N'|',is_cycling) AS [Value] FROM sys.sequences WHERE name=N'StorefrontBankCorrelationSequence' AND SCHEMA_NAME(schema_id)=N'dbo'";
        Assert.Equal("1|999999999999|1|1|0", await db.Database.SqlQueryRaw<string>(query).SingleAsync());
        var sequence = Assert.Single(new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest().Sequences, x => x.Identity.Name == "storefrontbankcorrelationsequence");
        Assert.Equal(1L, sequence.MinimumValue); Assert.Equal(999999999999L, sequence.MaximumValue); Assert.Equal(1L, sequence.StartValue); Assert.Equal(1, sequence.IncrementBy); Assert.False(sequence.IsCyclic);
    }

    private static async Task<string> BankState(Web05Case c)
    {
        await using var db = c.Context(); return await db.Database.SqlQueryRaw<string>("SELECT CONCAT((SELECT * FROM BankVerifiedReceipts WHERE OriginStoreId={0} ORDER BY Id FOR JSON PATH),(SELECT * FROM BankReceiptConsumptions WHERE OriginStoreId={0} ORDER BY Id FOR JSON PATH),(SELECT * FROM BankPaymentReferences WHERE OriginStoreId={0} ORDER BY Id FOR JSON PATH),(SELECT * FROM StorefrontPaymentIntents WHERE StoreId={0} ORDER BY Id FOR JSON PATH),(SELECT * FROM StorefrontQrProcessing WHERE StoreId={0} ORDER BY Id FOR JSON PATH)) AS [Value]", c.StoreId).SingleAsync();
    }
}
