using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.CustomerMarketing;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.CustomerService;

public sealed class CustomerMarketingCampaignDispatcherScopeTests : IDisposable
{
    private readonly HttpClient http = new(new RejectNetwork());

    [Fact]
    public async Task Empty_queue_is_readable_without_tenant_and_discovery_access_is_released()
    {
        var tenant = new TenantContext();
        await using var db = Context(tenant);

        Assert.False(await Dispatcher(db, tenant, new()).ProcessNextAsync(default));

        Assert.Null(tenant.StoreId);
        Assert.False(tenant.CanReadAllStores);
        Assert.False(tenant.CanWriteAllStores);
        await Assert.ThrowsAsync<TenantDataScopeException>(() => db.CustomerMarketingCampaigns.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Disabled_sending_leaves_scheduled_campaigns_waiting_without_selecting_a_store()
    {
        var tenant = new TenantContext();
        await using var db = Context(tenant);
        var now = DateTime.UtcNow;
        var later = Campaign(1, "Scheduled", now.AddMinutes(-1));
        var oldest = Campaign(2, "Scheduled", now.AddMinutes(-5));
        var future = Campaign(3, "Scheduled", now.AddHours(1));
        var completed = Campaign(4, "Completed", now.AddDays(-1));
        await Seed(db, tenant, later, oldest, future, completed);

        Assert.False(await Dispatcher(db, tenant, new() { Enabled = false }).ProcessNextAsync(default));

        Assert.Null(tenant.StoreId);
        Assert.False(tenant.CanReadAllStores);
        Assert.False(tenant.CanWriteAllStores);
        Assert.Empty(db.ChangeTracker.Entries<CustomerMarketingCampaign>());
        using (tenant.BeginSystemAccess(SystemDataAccessPurpose.CustomerMarketingCampaignDiscovery))
        {
            Assert.Equal(3, await db.CustomerMarketingCampaigns.IgnoreQueryFilters().CountAsync(x => x.Status == "Scheduled"));
        }
    }

    [Fact]
    public async Task Dispatching_campaign_is_written_only_after_discovery_grant_is_closed()
    {
        var tenant = new TenantContext();
        await using var db = Context(tenant);
        var other = Campaign(1, "Scheduled", DateTime.UtcNow.AddMinutes(-5));
        var selected = Campaign(2, "Dispatching", DateTime.UtcNow.AddMinutes(-1));
        await Seed(db, tenant, other, selected);

        Assert.True(await Dispatcher(db, tenant, new() { Enabled = false, PilotPhone = "invalid-test-setting" }).ProcessNextAsync(default));

        Assert.Equal(2, tenant.StoreId);
        Assert.False(tenant.CanReadAllStores);
        Assert.False(tenant.CanWriteAllStores);
        Assert.Equal("NeedsReview", (await db.CustomerMarketingCampaigns.SingleAsync()).Status);
        using (tenant.BeginSystemAccess(SystemDataAccessPurpose.CustomerMarketingCampaignDiscovery))
        {
            Assert.Equal("Scheduled", await db.CustomerMarketingCampaigns.IgnoreQueryFilters()
                .Where(x => x.Id == other.Id).Select(x => x.Status).SingleAsync());
        }
        Assert.False(tenant.CanReadAllStores);
    }

    [Fact]
    public async Task Cancelled_discovery_does_not_leave_system_access_enabled()
    {
        var tenant = new TenantContext();
        await using var db = Context(tenant);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Dispatcher(db, tenant, new()).ProcessNextAsync(cancellation.Token));

        Assert.Null(tenant.StoreId);
        Assert.False(tenant.CanReadAllStores);
        Assert.False(tenant.CanWriteAllStores);
    }

    private CustomerMarketingCampaignDispatcher Dispatcher(AppDbContext db, TenantContext tenant, CustomerMarketingCampaignOptions options)
    {
        var registry = new CustomerChannelRegistry(new Monitor<CustomerChannelOptions>(new() { Enabled = false }), new TestEnvironment());
        return new(db, tenant, registry, new CustomerMarketingCampaignTransport(http, registry),
            new CustomerMarketingCampaignDispatchThrottle(db), new CustomerMarketingConsentWriter(db, TimeProvider.System),
            new Monitor<CustomerMarketingCampaignOptions>(options), TimeProvider.System);
    }

    private static AppDbContext Context(TenantContext tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("campaign-scope-" + Guid.NewGuid()).Options,
        tenant, new TestUser());

    private static CustomerMarketingCampaign Campaign(int store, string status, DateTime scheduled) => new()
    {
        StoreId = store, Status = status, ScheduledAtUtc = scheduled, CampaignKey = Guid.NewGuid(), PreviewRequestId = Guid.NewGuid(),
        Name = "Synthetic campaign", MessageText = "Synthetic message; never sent", ConnectionId = "synthetic-channel",
        ConnectionBinding = new string('a', 64), RequestFingerprint = new string('b', 64), SnapshotSha256 = new string('c', 64),
        SenderProfileId = Guid.NewGuid(), CreatedByUserId = 1, RecipientCount = 1, RowVersion = new byte[8]
    };

    private static async Task Seed(AppDbContext db, TenantContext tenant, params CustomerMarketingCampaign[] campaigns)
    {
        using (tenant.BeginSystemAccess(SystemDataAccessPurpose.Provisioning))
        {
            db.AddRange(campaigns);
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
    }

    public void Dispose() => http.Dispose();

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new InvalidOperationException("Network must not be used by campaign scope tests.");
    }

    private sealed class Monitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class TestUser : ICurrentUser
    {
        public int? UserId => null;
        public string? UserName => null;
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => false;
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GaoApp.Web";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
