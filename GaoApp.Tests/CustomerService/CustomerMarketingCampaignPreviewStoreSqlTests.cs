using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.CustomerService;

[Collection("SqlServerConcurrency")]
public sealed class CustomerMarketingCampaignPreviewStoreSqlTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Profile_dispatch_throttle_allows_only_one_concurrent_slot_across_stores()
    {
        await using var fixture = new InventoryPostingLocalDb();
        await fixture.MigrateAsync();
        var seeded = await fixture.SeedInventoryCatalogAsync();
        var profile = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var first = fixture.CreateTenantContext(seeded.StoreId);
        await using var second = fixture.CreateTenantContext(seeded.StoreId);
        var values = await Task.WhenAll(new CustomerMarketingCampaignDispatchThrottle(first).ReserveAsync(profile, now, TimeSpan.FromSeconds(15), default),
            new CustomerMarketingCampaignDispatchThrottle(second).ReserveAsync(profile, now, TimeSpan.FromSeconds(15), default));
        Assert.Single(values, x => x == now);
        Assert.Single(values, x => Math.Abs((x - now.AddSeconds(15)).TotalMilliseconds) < 5);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Concurrent_preview_replay_creates_one_campaign_and_changed_body_conflicts()
    {
        await using var fixture = new InventoryPostingLocalDb();
        await fixture.MigrateAsync();
        var seeded = await fixture.SeedInventoryCatalogAsync();
        int customerId;
        var bindingId = Guid.NewGuid();
        var profile = Guid.NewGuid();
        var conversation = Guid.NewGuid();
        await using (var seed = fixture.CreateTenantContext(seeded.StoreId))
        {
            var customer = new Customer { StoreId = seeded.StoreId, Name = "Synthetic campaign recipient", Phone = "0398946174" };
            seed.Customers.Add(customer);
            await seed.SaveChangesAsync();
            customerId = customer.Id;
            seed.InvoiceRecipientBindings.Add(new InvoiceRecipientBinding
            {
                Id = bindingId, StoreId = seeded.StoreId, CustomerId = customerId, VerificationRequestId = Guid.NewGuid(),
                ConnectionId = "synthetic-zalo", ConnectionBinding = new string('a', 64), SenderProfileId = profile,
                ConversationId = conversation, IncomingSequence = 1, NativePeer = "sdk-v1:synthetic", Phone = "0398946174",
                Version = 1, VerifiedByUserId = 1, VerifiedAtUtc = DateTime.UtcNow, VerificationMethod = "Synthetic", EvidenceReference = "test"
            });
            await seed.SaveChangesAsync();
        }

        var requestId = Guid.NewGuid();
        var fingerprint = new string('a', 64);
        CustomerMarketingCampaign CreateCampaign() => new()
        {
            CampaignKey = Guid.NewGuid(), PreviewRequestId = requestId, RequestFingerprint = fingerprint, StoreId = seeded.StoreId,
            Name = "Synthetic preview", MessageText = "Test only", ConnectionId = "synthetic-zalo", ConnectionBinding = new string('a', 64),
            SenderProfileId = profile, SnapshotSha256 = new string('b', 64), RecipientCount = 1,
            ScheduledAtUtc = DateTime.UtcNow.AddMinutes(10), Status = "Draft", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 1
        };
        CustomerMarketingCampaignRecipientDraft[] Recipients() => [new(customerId, "Synthetic campaign recipient", bindingId, 1,
            "synthetic-zalo", new string('a', 64), profile, conversation, "sdk-v1:synthetic", "0398946174")];

        await using var first = fixture.CreateTenantContext(seeded.StoreId);
        await using var second = fixture.CreateTenantContext(seeded.StoreId);
        var results = await Task.WhenAll(new CustomerMarketingCampaignPreviewStore(first).SaveAsync(CreateCampaign(), Recipients(), default),
            new CustomerMarketingCampaignPreviewStore(second).SaveAsync(CreateCampaign(), Recipients(), default));
        Assert.Equal(results[0].Id, results[1].Id);

        await using var verify = fixture.CreateTenantContext(seeded.StoreId);
        Assert.Equal(1, await verify.CustomerMarketingCampaigns.CountAsync(x => x.PreviewRequestId == requestId));
        Assert.Equal(1, await verify.CustomerMarketingCampaignRecipients.CountAsync(x => x.CampaignId == results[0].Id));
        var changed = CreateCampaign(); changed.RequestFingerprint = new string('c', 64);
        await Assert.ThrowsAsync<AssistantException>(() => new CustomerMarketingCampaignPreviewStore(verify).SaveAsync(changed, Recipients(), default));
    }
}
