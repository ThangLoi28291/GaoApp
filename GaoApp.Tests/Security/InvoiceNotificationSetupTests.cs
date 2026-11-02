using System.Data.Common;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.InvoiceNotifications;
using GaoApp.Infrastructure.Services.Invoices;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Controllers;
using GaoApp.Web.Services.CustomerService;
using GaoApp.Web.Services.InvoiceNotifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Security;

public sealed class InvoiceNotificationSetupTests
{
    [Fact]
    public async Task Production_setup_resolves_all_P3_paths_and_shared_aliases_with_scope_validation_without_IO()
    {
        await using var fixture = new Fixture();
        await using var provider = fixture.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope(); var services = scope.ServiceProvider;
        var core = services.GetRequiredService<EfInvoiceNotificationStore>();
        Assert.Same(core, services.GetRequiredService<IInvoiceNotificationStore>());
        Assert.Same(core, services.GetRequiredService<IInvoiceNotificationOutbox>());
        Assert.Same(core, services.GetRequiredService<IInvoiceOfficialDocumentLinkAuthorizer>());
        Assert.IsType<InvoiceNotificationDispatcher>(services.GetRequiredService<InvoiceNotificationDispatcher>());
        Assert.IsType<CustomerInvoiceRecipientSource>(services.GetRequiredService<ICustomerInvoiceRecipientSource>());
        Assert.IsType<CustomerInvoiceNotificationAdminService>(services.GetRequiredService<ICustomerInvoiceNotificationAdmin>());
        Assert.IsType<EfInvoiceNotificationCapture>(services.GetRequiredService<IInvoiceNotificationCapture>());
        Assert.IsType<InvoiceNotificationBundleBuilder>(services.GetRequiredService<IInvoiceNotificationBundleBuilder>());
        Assert.IsAssignableFrom<IInvoiceWorkItemBundleBuilder>(services.GetRequiredService<IInvoiceNotificationBundleBuilder>());
        Assert.IsType<InvoiceDocumentController>(ActivatorUtilities.CreateInstance<InvoiceDocumentController>(services));
        Assert.Same(services.GetRequiredService<IInvoiceFileStorage>(), services.GetRequiredService<IBoundedInvoiceFileStorage>());
        Assert.Same(provider.GetRequiredService<PrivateInvoicePreparedAssetStore>(), services.GetRequiredService<IInvoicePreparedAssetReader>());
        Assert.Same(provider.GetRequiredService<InvoiceOfficialDocumentLinks>(), services.GetRequiredService<IInvoiceOfficialDocumentLinkFactory>());
        Assert.False(services.GetRequiredService<IOptionsMonitor<InvoiceNotificationOptions>>().CurrentValue.Enabled);
        Assert.Equal("", services.GetRequiredService<IOptionsMonitor<InvoiceOfficialDocumentLinkOptions>>().CurrentValue.PublicBaseUrl);
        Assert.IsType<InvoiceNotificationWorker>(Assert.Single(provider.GetServices<IHostedService>(), service => service is InvoiceNotificationWorker));
        using (var other = provider.CreateScope()) Assert.NotSame(core, other.ServiceProvider.GetRequiredService<IInvoiceNotificationStore>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<InvoiceNotificationDispatcher>());
        Assert.Equal(0, fixture.Guard.ConnectionOpens); Assert.Equal(0, fixture.Guard.HttpRequests);
        Assert.False(Directory.Exists(fixture.ArtifactsDirectory));
    }

    [Fact]
    public async Task Default_off_hosted_worker_ticks_and_stops_with_no_channels_no_DbContext_no_network_and_no_artifacts()
    {
        await using var fixture = new Fixture();
        var channels = new ReadSignalOptions();
        fixture.Services.AddSingleton<IOptionsMonitor<CustomerChannelOptions>>(channels);
        await using var provider = fixture.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var worker = Assert.IsType<InvoiceNotificationWorker>(Assert.Single(provider.GetServices<IHostedService>(), service => service is InvoiceNotificationWorker));
        Assert.False(provider.GetRequiredService<IOptionsMonitor<InvoiceNotificationOptions>>().CurrentValue.Enabled);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(timeout.Token);
        await fixture.Clock.TimerCreated.Task.WaitAsync(timeout.Token);
        fixture.Clock.Pulse();
        await channels.Read.Task.WaitAsync(timeout.Token);
        await worker.StopAsync(timeout.Token);
        Assert.True(channels.Reads > 0); Assert.Equal(0, fixture.Guard.ContextOptionsBuilt);
        Assert.Equal(0, fixture.Guard.ConnectionOpens); Assert.Equal(0, fixture.Guard.HttpRequests); Assert.False(Directory.Exists(fixture.ArtifactsDirectory));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "GaoP3SetupTests", Guid.NewGuid().ToString("N"));
        public string ArtifactsDirectory => Path.Combine(directory, "private-artifacts");
        public readonly ServiceCollection Services = new();
        public readonly IoGuard Guard = new();
        public readonly ManualClock Clock = new();
        public Fixture()
        {
            Directory.CreateDirectory(directory);
            var environment = new EnvironmentStub(Path.Combine(directory, "web"));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["InvoiceNotifications:Artifacts:RootDirectory"] = ArtifactsDirectory,
                ["Storage:UploadRoot"] = Path.Combine(directory, "public-uploads")
            }).Build();
            Services.AddLogging(); Services.AddSingleton<IConfiguration>(config);
            Services.AddSingleton<IWebHostEnvironment>(environment); Services.AddSingleton<IHostEnvironment>(environment);
            Services.AddSingleton<TimeProvider>(Clock); Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            Services.AddScoped<TenantContext>(); Services.AddScoped<ITenantContext>(p => p.GetRequiredService<TenantContext>());
            Services.AddScoped<ITenantContextWriter>(p => p.GetRequiredService<TenantContext>()); Services.AddScoped<ICurrentUser, AnonymousUser>();
            Services.AddDbContext<AppDbContext>(options => {
                Interlocked.Increment(ref Guard.ContextOptionsBuilt);
                options.UseSqlServer("Server=127.0.0.1,1;Database=SYNTHETIC_P3_SETUP_DO_NOT_OPEN;Integrated Security=true;Encrypt=true;Connect Timeout=1");
                options.AddInterceptors(Guard);
            });
            // Real host prerequisites; no fake implementation replaces a P3 production registration.
            Services.AddScoped<IInvoiceFileStorage, InvoiceFileStorage>();
            Services.AddOptions<CustomerChannelOptions>().Bind(config.GetSection(CustomerChannelOptions.Section));
            Services.AddSingleton<CustomerChannelRegistry>(); Services.AddScoped<CustomerServiceAccess>();
            Services.AddHttpClient<CustomerChannelTransport>();
            Services.AddInvoiceNotifications(config);
            // Guard against future constructor/worker regressions before any socket or SQL connection opens.
            Services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = new NoNetwork(Guard)));
        }
        public ValueTask DisposeAsync() { Directory.Delete(directory, true); return ValueTask.CompletedTask; }
    }
    private sealed class EnvironmentStub(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GaoApp.Web"; public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root; public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class AnonymousUser : ICurrentUser
    { public int? UserId => null; public string? UserName => null; public int? TerminalId => null; public string? TerminalCode => null; public bool IsAuthenticated => false; }
    private sealed class IoGuard : DbConnectionInterceptor
    {
        public int ConnectionOpens, HttpRequests, ContextOptionsBuilt;
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        { Interlocked.Increment(ref ConnectionOpens); throw new InvalidOperationException("SQL is forbidden in the setup test."); }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        { Interlocked.Increment(ref ConnectionOpens); throw new InvalidOperationException("SQL is forbidden in the setup test."); }
    }
    private sealed class NoNetwork(IoGuard guard) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Interlocked.Increment(ref guard.HttpRequests); throw new InvalidOperationException("HTTP is forbidden in the setup test."); } }
    private sealed class ReadSignalOptions : IOptionsMonitor<CustomerChannelOptions>
    {
        private readonly CustomerChannelOptions value = new(); public int Reads;
        public readonly TaskCompletionSource<bool> Read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CustomerChannelOptions CurrentValue { get { Interlocked.Increment(ref Reads); Read.TrySetResult(true); return value; } }
        public CustomerChannelOptions Get(string? name) => CurrentValue; public IDisposable? OnChange(Action<CustomerChannelOptions,string?> listener) => null;
    }
    private sealed class ManualClock : TimeProvider
    {
        private ManualTimer? timer;
        public readonly TaskCompletionSource<bool> TimerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var created = new ManualTimer(callback, state); timer = created; TimerCreated.TrySetResult(true); return created; }
        public void Pulse() => timer!.Pulse();
        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;
            public void Pulse() { if (!disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;
            public void Dispose() => disposed = true; public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
