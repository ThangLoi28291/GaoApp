using GaoApp.Domain.Entities;
using GaoApp.Tests.Configuration;
using GaoApp.Infrastructure.Services.CustomerMarketing;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.CustomerService;

[Collection("SqlServerConcurrency")]
public sealed class CustomerMarketingConsentSqlTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Concurrent_replay_of_same_zalo_opt_out_creates_one_event_and_keeps_consent_denied()
    {
        await using var fixture = new InventoryPostingLocalDb();
        await fixture.MigrateAsync();
        var seeded = await fixture.SeedInventoryCatalogAsync();
        int customerId;
        await using (var seed = fixture.CreateTenantContext(seeded.StoreId))
        {
            var customer = new Customer { StoreId = seeded.StoreId, Name = "Synthetic opt-out customer", Phone = "0398946174" };
            seed.Customers.Add(customer);
            await seed.SaveChangesAsync();
            customerId = customer.Id;
        }

        var evidence = "zalo:synthetic:peer:12345";
        await using var first = fixture.CreateTenantContext(seeded.StoreId);
        await using var second = fixture.CreateTenantContext(seeded.StoreId);
        var results = await Task.WhenAll(
            new CustomerMarketingConsentWriter(first, TimeProvider.System).SetAsync(seeded.StoreId, customerId, false, "ZaloKeyword", evidence, null, default),
            new CustomerMarketingConsentWriter(second, TimeProvider.System).SetAsync(seeded.StoreId, customerId, false, "ZaloKeyword", evidence, null, default));

        Assert.Single(results, changed => changed);
        await using var verify = fixture.CreateTenantContext(seeded.StoreId);
        var consent = await verify.CustomerMarketingConsents.SingleAsync(x => x.CustomerId == customerId && x.Channel == "Zalo");
        Assert.False(consent.IsGranted);
        Assert.Equal(1, await verify.CustomerMarketingConsentEvents.CountAsync(x => x.CustomerId == customerId && x.EvidenceReference == evidence));
    }
}
