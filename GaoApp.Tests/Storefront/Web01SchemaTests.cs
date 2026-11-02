using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb01"), Trait("Category", "StorefrontWeb01")]
public sealed class Web01SchemaTests(Web01Fixture fixture)
{
    private const string Previous = "20261030150000_AddCustomerServicePermissionCatalog";
    private const string Migration = "20261030170000_AddStorefrontFoundation";
    private static readonly (Type Type, string Table)[] NativeTables =
    [
        (typeof(StorefrontFulfillmentWork), "StorefrontFulfillmentWorks"), (typeof(StorefrontReservationTransfer), "StorefrontReservationTransfers"),
        (typeof(StorefrontFulfillmentEvidence), "StorefrontFulfillmentEvidence"), (typeof(StorefrontCustodyIncident), "StorefrontCustodyIncidents"),
        (typeof(StorefrontServiceRevenueSetting), "StorefrontServiceRevenueSettings"), (typeof(StorefrontServiceRevenue), "StorefrontServiceRevenues"),
        (typeof(StorefrontSaleBinding), "StorefrontSaleBindings"), (typeof(StorefrontServiceFunding), "StorefrontServiceFundings"),
        (typeof(StorefrontInvoiceWork), "StorefrontInvoiceWorks"), (typeof(StorefrontCodCollection), "StorefrontCodCollections"),
        (typeof(StorefrontCodBankRouting), "StorefrontCodBankRoutings"), (typeof(StorefrontCodReceipt), "StorefrontCodReceipts"),
        (typeof(StorefrontCarrierWithholding), "StorefrontCarrierWithholdings"), (typeof(StorefrontNotificationAttestation), "StorefrontNotificationAttestations"),
        (typeof(StorefrontNotificationCertification), "StorefrontNotificationCertifications"), (typeof(StorefrontInventoryIssueSource), "StorefrontInventoryIssueSources")
    ];

    [Fact]
    public async Task Current_model_snapshot_and_live_manifest_agree_after_fresh_migration()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(); await using var db = sql.CreateHostContext();
        Assert.Contains(Migration, await db.Database.GetAppliedMigrationsAsync()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        var model = db.GetService<IDesignTimeModel>().Model;
        var storefront = model.GetEntityTypes().Where(x => x.ClrType.Namespace == "GaoApp.Domain.Storefront").ToArray();
        Assert.Equal(51, storefront.Length);
        foreach (var (type, table) in NativeTables)
            Assert.Equal(table, Assert.Single(storefront, x => x.ClrType == type).GetTableName());
        foreach (var added in new[] { typeof(StorefrontQrProcessing), typeof(StorefrontBankObservation), typeof(StorefrontUnattributedBankReceipt) })
            Assert.Contains(storefront, x => x.ClrType == added);
        Assert.All(storefront, x =>
        {
            Assert.NotNull(x.FindProperty("StoreId")); Assert.True(x.FindProperty("RowVersion")!.IsConcurrencyToken);
            Assert.NotEmpty(x.GetDeclaredQueryFilters());
            Assert.All(x.GetForeignKeys(), fk => Assert.Contains(fk.Properties, p => p.Name == "StoreId"));
        });
        Assert.False((bool)model.FindEntityType(typeof(StorefrontSettings))!.FindProperty("IsEnabled")!.GetDefaultValue()!);
        foreach (var type in new[] { typeof(StorefrontProduct), typeof(StorefrontVariant), typeof(StorefrontSellingUnit) })
            Assert.False((bool)model.FindEntityType(type)!.FindProperty("IsPublished")!.GetDefaultValue()!);
        Assert.Equal(StorefrontShippingFeeTreatment.StoreServiceFee,
            model.FindEntityType(typeof(StorefrontSettings))!.FindProperty("ShippingFeeTreatment")!.GetDefaultValue());
        var manifest = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
        await db.Database.OpenConnectionAsync();
        var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync((await db.Database.GetAppliedMigrationsAsync()).ToArray());
        var comparison = DatabaseSchemaComparer.Compare(manifest, actual);
        if (!comparison.IsMatch)
        {
            var diagnostic = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "../../.artifacts/storefront-web01/tests/manifest-diagnostic"));
            Directory.CreateDirectory(diagnostic);
            await File.WriteAllTextAsync(Path.Combine(diagnostic, "expected.json"), JsonSerializer.Serialize(manifest));
            await File.WriteAllTextAsync(Path.Combine(diagnostic, "actual.json"), JsonSerializer.Serialize(actual));
        }
        Assert.True(comparison.IsMatch, JsonSerializer.Serialize(comparison.Mismatches));
        Assert.Equal(0, await db.Stores.CountAsync()); Assert.Equal(0, await db.StorefrontSettings.CountAsync());
        Assert.Equal(0, await db.StorefrontProducts.CountAsync()); Assert.Equal(0, await db.StorefrontOrders.CountAsync());
        Assert.Equal(0, await db.AdminMenuItems.CountAsync()); Assert.Equal(0, await db.RolePermissions.CountAsync());
    }

    [Fact]
    public void Prefix_manifest_has_foundation_tables_only_after_WEB01()
    {
        using var db = fixture.Database.CreateHostContext(); var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);
        var ids = db.Database.GetMigrations().ToArray(); var position = Array.IndexOf(ids, Migration); Assert.True(position > 0);
        Assert.True(catalog.TryGetManifestForAppliedMigrationPrefix(ids[..position], out var before));
        Assert.True(catalog.TryGetManifestForAppliedMigrationPrefix(ids[..(position + 1)], out var after));
        Assert.DoesNotContain(before.Tables, x => x.Identity == new DatabaseObjectIdentity("dbo", "StorefrontSettings"));
        var settings = Assert.Single(after.Tables, x => x.Identity == new DatabaseObjectIdentity("dbo", "StorefrontSettings"));
        Assert.Contains(settings.Columns, x => x.Name == "rowversion" && x.IsRowVersion);
        foreach (var table in StorefrontFoundationSchemaSqlCatalog.ProtectedTables.Keys)
            Assert.Equal(table == "StorefrontLineSources" ? 2 : 1,
                after.Tables.Single(x => x.Identity == new DatabaseObjectIdentity("dbo", table)).Triggers.Count);
    }

    [Fact]
    public async Task Upgrade_and_reapply_preserve_pos_money_inventory_menu_order_and_role_grants()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(Previous); var seed = await sql.SeedInventoryCatalogAsync();
        await using (var db = sql.CreateTenantContext(seed.StoreId))
        {
            var user = new User { UserName = "web01-upgrade-" + Guid.NewGuid().ToString("N"), PasswordHash = "synthetic-only" };
            var role = new Role { StoreId = seed.StoreId, Code = "WEB01OLD", Name = "Existing custom role" };
            db.UserInStores.Add(new() { StoreId = seed.StoreId, User = user, Role = role });
            var terminal = new POSTerminal { StoreId = seed.StoreId, Code = "WEB01OLD", Name = "Existing terminal" };
            var permission = new Permission { Code = StorefrontPermissions.ViewSettings, Name = "Existing custom label", GroupName = "Custom" };
            db.AddRange(terminal, permission); await db.SaveChangesAsync();
            db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = permission.Id });
            db.AdminMenuItems.Add(new() { StoreId = seed.StoreId, Title = "Existing custom menu", Url = "/custom-old", SortOrder = 719, IsActive = true });
            var shift = new POSShift { StoreId = seed.StoreId, TerminalId = terminal.Id, OpenedByUserId = user.Id, WarehouseId = seed.WarehouseId };
            db.Add(shift); await db.SaveChangesAsync();
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == seed.ProductVariantId);
            var order = new Order { StoreId = seed.StoreId, POSShiftId = shift.Id, Subtotal = 50, GrandTotal = 50, PaidTotal = 10, BalanceDue = 40 };
            db.Add(order); db.InventoryBalances.Add(new() { StoreId = seed.StoreId, WarehouseId = seed.WarehouseId, ProductVariantId = variant.Id, OnHandQty = 17, ReservedQty = 2 });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OrderLines(StoreId,OrderId,ProductId,VariantId,ItemName,UnitName,Quantity,BaseQuantity,Multiplier,UnitPrice,LineDiscount,LineTotal,OriginalUnitPrice,PromotionDiscount,ComboAllocatedDiscount,CreatedAtUtc,IsDeleted) VALUES({seed.StoreId},{order.Id},{variant.ProductId},{variant.Id},N'Existing POS line',N'Unit',2.5,2.5,1,20,0,50,0,0,0,SYSUTCDATETIME(),0)");
            // The pre-WEB01 schema has no later BankVerifiedReceiptId column.
            // Seed the same historical cash payment using only its old columns.
            var paidAt = DateTime.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OrderPayments(StoreId,OrderId,Method,Amount,IsDebtCollection,ReferenceCode,PaidAtUtc,CreatedAtUtc,IsDeleted) VALUES({seed.StoreId},{order.Id},{(int)PaymentMethod.Cash},{10m},0,N'OLD-PAYMENT',{paidAt},{paidAt},0)");
        }
        string before; await using (var db = sql.CreateHostContext()) before = await ExistingData(db);
        await sql.MigrateAsync(); await using (var db = sql.CreateHostContext())
        {
            Assert.Equal(before, await ExistingData(db)); Assert.Empty(await db.StorefrontSettings.ToListAsync()); Assert.Empty(await db.StorefrontProducts.ToListAsync());
            Assert.Equal("Existing custom label", await db.Permissions.Where(x => x.Code == StorefrontPermissions.ViewSettings).Select(x => x.Name).SingleAsync());
            Assert.All(new[] { StorefrontPermissions.ViewSettings, StorefrontPermissions.ManageSettings, StorefrontPermissions.ManageShipping,
                StorefrontPermissions.ViewProducts, StorefrontPermissions.ManageProducts }, code => Assert.True(db.Permissions.Any(x => x.Code == code)));
        }
        await sql.MigrateAsync(); await using var verify = sql.CreateHostContext(); Assert.Equal(before, await ExistingData(verify));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Downgrade_refuses_foundation_data_loss_and_empty_foundation_can_round_trip(bool hasData)
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(); var seed = await sql.SeedInventoryCatalogAsync();
        if (hasData)
        {
            await using (var db = sql.CreateTenantContext(seed.StoreId))
            {
                db.StorefrontSettings.Add(new() { StoreId = seed.StoreId, DisplayName = "Synthetic retained settings" }); await db.SaveChangesAsync();
            }
            var error = await Assert.ThrowsAsync<SqlException>(() => sql.MigrateAsync(Previous)); Assert.Equal(51091, error.Number);
            await using var verify = sql.CreateTenantContext(seed.StoreId);
            Assert.Contains(Migration, await verify.Database.GetAppliedMigrationsAsync());
            Assert.Equal("Synthetic retained settings", (await verify.StorefrontSettings.SingleAsync()).DisplayName);
        }
        else
        {
            string before; await using (var db = sql.CreateHostContext()) before = await ExistingData(db);
            await sql.MigrateAsync(Previous);
            Assert.Equal(0, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name LIKE 'Storefront%';"));
            await using (var db = sql.CreateHostContext()) Assert.Equal(before, await ExistingData(db));
            await sql.MigrateAsync();
            Assert.Equal(51, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name LIKE 'Storefront%';"));
            await using (var current = sql.CreateHostContext())
            {
                var tables = await current.Database.SqlQueryRaw<string>("SELECT name AS [Value] FROM sys.tables WHERE name LIKE 'Storefront%'").ToListAsync();
                Assert.All(NativeTables, native => Assert.Contains(native.Table, tables));
            }
            Assert.Equal(3, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name IN(N'StorefrontQrProcessing',N'StorefrontBankObservations',N'StorefrontUnattributedBankReceipts');"));
            await using var dbAfter = sql.CreateHostContext(); Assert.Equal(before, await ExistingData(dbAfter));
        }
    }

    [Fact]
    public async Task Publication_defaults_are_hidden_and_do_not_change_retail_catalog()
    {
        var c = await fixture.CaseAsync(); var published = await c.PublishAsync(false); await using var db = c.Context();
        Assert.False((await db.StorefrontProducts.SingleAsync(x => x.Id == published.Product)).IsPublished);
        Assert.False((await db.StorefrontVariants.SingleAsync(x => x.Id == published.Variant)).IsPublished);
        Assert.False((await db.StorefrontSellingUnits.SingleAsync(x => x.Id == published.SellingUnit)).IsPublished);
        var product = await db.Products.SingleAsync(x => x.Id == c.ProductId); Assert.True(product.IsActive); Assert.True(product.IsSellable); Assert.Equal(20, product.BasePrice);
        Assert.Equal(20, (await db.ProductVariants.SingleAsync(x => x.Id == c.VariantId)).Price);
    }

    [Fact]
    public async Task Storefront_query_filters_and_write_guard_preserve_tenant_isolation()
    {
        var own = await fixture.CaseAsync(); var other = await fixture.CaseAsync();
        await own.PublishAsync(false); await other.PublishAsync(false);
        await using var db = own.Context(); Assert.Single(await db.StorefrontProducts.ToListAsync());
        Assert.All(await db.StorefrontProducts.ToListAsync(), x => Assert.Equal(own.StoreId, x.StoreId));
        Assert.Empty(await db.StorefrontProducts.Where(x => x.ProductId == other.ProductId).ToListAsync());
        db.StorefrontProducts.Add(new() { StoreId = other.StoreId, ProductId = other.ProductId });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await using var verify = other.Context(); Assert.Single(await verify.StorefrontProducts.ToListAsync());
    }

    [Theory]
    [InlineData("product")]
    [InlineData("variant")]
    [InlineData("unit")]
    [InlineData("warehouse")]
    [InlineData("bank")]
    [InlineData("receivingOwner")]
    public async Task Sql_composite_foreign_keys_reject_foreign_catalog_and_configuration_references(string target)
    {
        var c = await fixture.CaseAsync(); var other = await fixture.CaseAsync(); var own = await c.PublishAsync(false);
        await using var db = c.Context();
        FormattableString command = target switch
        {
            "product" => $"INSERT INTO StorefrontProducts (StoreId,ProductId,IsPublished,SortOrder,IsDeleted) VALUES ({c.StoreId},{other.ProductId},0,0,0)",
            "variant" => $"INSERT INTO StorefrontVariants (StoreId,StorefrontProductId,ProductId,VariantId,IsPublished,IsDeleted) VALUES ({c.StoreId},{own.Product},{c.ProductId},{other.VariantId},0,0)",
            "unit" => $"INSERT INTO StorefrontSellingUnits (StoreId,StorefrontVariantId,VariantId,ProductUnitConversionId,UnitId,IsPublished,IsDeleted) VALUES ({c.StoreId},{own.Variant},{c.VariantId},{other.ConversionId},{c.UnitId},0,0)",
            "warehouse" => $"INSERT INTO StorefrontSettings (StoreId,Revision,DisplayName,ContactPhone,SourceWarehouseId,SourceLegalEntityId,CodEnabled,QrEnabled,AllowNegativeInventory,ShippingByWeight,BankTerminalCode,BankUserCode,IsDeleted) VALUES ({c.StoreId},1,'Synthetic','',{other.WarehouseId},{c.LegalEntityId},1,0,1,0,'','',0)",
            "bank" => $"INSERT INTO StorefrontBankCollectionAuthorities (StoreId,IsActive,BankAccountId,ReceivingLegalEntityId,SourceLegalEntityId,IsDeleted) VALUES ({c.StoreId},1,{other.BankAccountId},{c.LegalEntityId},{c.LegalEntityId},0)",
            _ => $"INSERT INTO StorefrontBankCollectionAuthorities (StoreId,IsActive,BankAccountId,ReceivingLegalEntityId,SourceLegalEntityId,IsDeleted) VALUES ({c.StoreId},1,{c.BankAccountId},{other.LegalEntityId},{c.LegalEntityId},0)"
        };
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(command)); Assert.Equal(547, error.Number);
    }

    [Theory]
    [InlineData("settingsFee")]
    [InlineData("settingsQr")]
    [InlineData("variantKg")]
    [InlineData("zoneMethod")]
    [InlineData("zoneWard")]
    [InlineData("rateMoney")]
    [InlineData("rateKg")]
    public async Task Sql_checks_reject_invalid_foundation_values_even_without_service(string target)
    {
        var c = await fixture.CaseAsync(); var own = await c.PublishAsync(false); await c.SaveDefaultsAsync();
        await using var db = c.Context(); await c.FullService(db).SaveZoneAsync(await c.ZoneAsync());
        FormattableString command = target switch
        {
            "settingsFee" => $"UPDATE StorefrontSettings SET ShippingFeeTreatment=2 WHERE StoreId={c.StoreId}",
            "settingsQr" => $"UPDATE StorefrontSettings SET QrEnabled=1 WHERE StoreId={c.StoreId}",
            "variantKg" => $"UPDATE StorefrontVariants SET BaseUnitWeightKg=0 WHERE Id={own.Variant}",
            "zoneMethod" => $"UPDATE StorefrontShippingZones SET DeliveryMethod=99 WHERE StoreId={c.StoreId}",
            "zoneWard" => $"UPDATE StorefrontShippingZones SET ProvinceCode='', WardCode='001' WHERE StoreId={c.StoreId}",
            "rateMoney" => $"INSERT INTO StorefrontShippingRates (StoreId,ShippingZoneId,Revision,IsCurrent,ByWeight,BaseFee,AdditionalFee,FirstWeightKg,AdditionalStepKg,IsDeleted) SELECT StoreId,ShippingZoneId,99,0,ByWeight,-1,AdditionalFee,FirstWeightKg,AdditionalStepKg,0 FROM StorefrontShippingRates WHERE StoreId={c.StoreId}",
            _ => $"INSERT INTO StorefrontShippingRates (StoreId,ShippingZoneId,Revision,IsCurrent,ByWeight,BaseFee,AdditionalFee,FirstWeightKg,AdditionalStepKg,IsDeleted) SELECT StoreId,ShippingZoneId,99,0,ByWeight,BaseFee,AdditionalFee,0,AdditionalStepKg,0 FROM StorefrontShippingRates WHERE StoreId={c.StoreId}"
        };
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(command)); Assert.Equal(547, error.Number);
    }

    private static async Task<string> ExistingData(AppDbContext db) => JsonSerializer.Serialize(new
    {
        stores = await db.Stores.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.SubDomain, x.IsActive }).ToArrayAsync(),
        products = await db.Products.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.BasePrice, x.IsActive, x.IsSellable }).ToArrayAsync(),
        variants = await db.ProductVariants.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.Price, x.HasInputInvoice }).ToArrayAsync(),
        orders = await db.Orders.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.POSShiftId, x.Status, x.GrandTotal, x.PaidTotal, x.BalanceDue }).ToArrayAsync(),
        lines = await db.OrderLines.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.OrderId, x.Quantity, x.LineTotal, x.UnitPrice }).ToArrayAsync(),
        payments = await db.OrderPayments.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.OrderId, x.Amount, x.Method, x.ReferenceCode }).ToArrayAsync(),
        inventory = await db.InventoryBalances.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.OnHandQty, x.ReservedQty }).ToArrayAsync(),
        menus = await db.AdminMenuItems.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.Title, x.Url, x.SortOrder, x.IsActive }).ToArrayAsync(),
        roles = await db.Roles.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Name }).ToArrayAsync(),
        grants = await db.RolePermissions.OrderBy(x => x.Id).Select(x => new { x.Id, x.RoleId, x.PermissionId }).ToArrayAsync()
    });
    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
