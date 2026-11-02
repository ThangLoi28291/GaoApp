using System.Text.Json;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Configuration;

[Collection("SqlServerConcurrency")]
public sealed class CurrentModelMigrationSqlTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_model_migrates_fresh_and_upgrades_pre_P3_without_changing_existing_data(bool upgrade)
    {
        await using var fixture = new InventoryPostingLocalDb();
        await using (var model = fixture.CreateHostContext()) Assert.False(model.Database.HasPendingModelChanges());
        string? before = null;
        if (upgrade)
        {
            await fixture.MigrateAsync("20261025090000_AddAdminMenuPinSettings");
            var catalog = await fixture.SeedInventoryCatalogAsync();
            await using var db = fixture.CreateTenantContext(catalog.StoreId);
            before = await ExistingData(db);
        }
        await fixture.MigrateAsync();
        await using (var db = fixture.CreateHostContext())
        {
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Contains("20261027010000_AddInvoiceNotificationsP3", await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains("20261030120000_AddCustomerMarketingConsentP6", await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains("20261030130000_AddCustomerMarketingCampaignsP6", await db.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await db.Set<InvoiceNotificationAudit>().ToListAsync());
            Assert.Empty(await db.Set<InvoiceNotificationOutbox>().ToListAsync());
            Assert.Empty(await db.Set<InvoiceRecipientBinding>().ToListAsync());
            Assert.Empty(await db.CustomerMarketingConsents.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.CustomerMarketingConsentEvents.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.CustomerMarketingCampaigns.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.CustomerMarketingCampaignRecipients.IgnoreQueryFilters().ToListAsync());
            await db.InvoiceHeads.Select(x => new { x.OfficialPdfSha256, x.OfficialPdfByteLength, x.OfficialZipXmlSha256, x.OfficialZipXmlByteLength }).ToListAsync();
            if (before is not null) Assert.Equal(before, await ExistingData(db));
            var migrated = await ExistingData(db);
            await fixture.MigrateAsync();
            Assert.Equal(migrated, await ExistingData(db));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Marketing_campaign_migration_refuses_downgrade_when_campaign_history_exists()
    {
        await using var fixture = new InventoryPostingLocalDb();
        await fixture.MigrateAsync();
        var seeded = await fixture.SeedInventoryCatalogAsync();
        await using (var db = fixture.CreateTenantContext(seeded.StoreId))
        {
            db.CustomerMarketingCampaigns.Add(new CustomerMarketingCampaign
            {
                CampaignKey = Guid.NewGuid(), PreviewRequestId = Guid.NewGuid(), RequestFingerprint = new string('c', 64), StoreId = seeded.StoreId, Name = "Synthetic campaign",
                MessageText = "Synthetic body", ConnectionId = "synthetic-zalo", ConnectionBinding = new string('a', 64),
                SenderProfileId = Guid.NewGuid(), SnapshotSha256 = new string('b', 64), RecipientCount = 1,
                ScheduledAtUtc = DateTime.UtcNow.AddHours(1), Status = "Draft", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 1
            });
            await db.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            fixture.MigrateAsync("20261030120000_AddCustomerMarketingConsentP6"));
        Assert.Equal(51000, error.Number);
        await using var verify = fixture.CreateTenantContext(seeded.StoreId);
        Assert.Contains("20261030130000_AddCustomerMarketingCampaignsP6", await verify.Database.GetAppliedMigrationsAsync());
        Assert.Equal("Draft", await verify.CustomerMarketingCampaigns.Select(x => x.Status).SingleAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Marketing_consent_migration_refuses_downgrade_when_opt_out_evidence_exists()
    {
        await using var fixture = new InventoryPostingLocalDb();
        await fixture.MigrateAsync();
        var seeded = await fixture.SeedInventoryCatalogAsync();
        int customerId;
        await using (var db = fixture.CreateTenantContext(seeded.StoreId))
        {
            var customer = new Customer { StoreId = seeded.StoreId, Name = "Synthetic marketing customer", Phone = "0398946174" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            db.CustomerMarketingConsents.Add(new CustomerMarketingConsent
            {
                StoreId = seeded.StoreId, CustomerId = customerId, Channel = "Zalo", IsGranted = false,
                Source = "ZaloKeyword", EvidenceReference = "synthetic-opt-out", ChangedAtUtc = DateTime.UtcNow, ChangedByUserId = 1
            });
            db.CustomerMarketingConsentEvents.Add(new CustomerMarketingConsentEvent
            {
                StoreId = seeded.StoreId, CustomerId = customerId, Channel = "Zalo", IsGranted = false,
                Source = "ZaloKeyword", EvidenceReference = "synthetic-opt-out", RecordedAtUtc = DateTime.UtcNow, RecordedByUserId = 1
            });
            await db.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            fixture.MigrateAsync("20261030110000_AddCustomerInvoiceNotificationsPermission"));
        Assert.Equal(51000, error.Number);
        await using var verify = fixture.CreateTenantContext(seeded.StoreId);
        Assert.Contains("20261030120000_AddCustomerMarketingConsentP6", await verify.Database.GetAppliedMigrationsAsync());
        var consent = await verify.CustomerMarketingConsents.SingleAsync(x => x.CustomerId == customerId);
        Assert.False(consent.IsGranted);
        Assert.Equal("synthetic-opt-out", await verify.CustomerMarketingConsentEvents.Where(x => x.CustomerId == customerId).Select(x => x.EvidenceReference).SingleAsync());
    }

    private static async Task<string> ExistingData(GaoApp.Infrastructure.Data.AppDbContext db)
        => JsonSerializer.Serialize(new {
            stores = await db.Stores.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.SubDomain, x.IsActive }).ToListAsync(),
            products = await db.Products.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.BasePrice }).ToListAsync(),
            variants = await db.ProductVariants.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.ProductVariantName, x.Price }).ToListAsync(),
            menus = await db.AdminMenuItems.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.Title, x.Url, x.SortOrder, x.IsActive }).ToListAsync(),
            grants = await db.RolePermissions.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.RoleId, x.PermissionId }).ToListAsync()
        });
}
