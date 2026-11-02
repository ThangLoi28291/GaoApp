using System.Net;
using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Storefront.Web.Controllers;
using GaoApp.Storefront.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Storefront;

// Real public controller/reader/catalog/SQL HTTP component host. Store scope is
// fixed by this server-owned fixture, never selected by a client parameter.
// Full public startup/tenant gates are exercised by the separate browser host.
internal sealed class Web08MediaHost : IAsyncDisposable
{
    private readonly WebApplication app;
    private int selectedStore;
    internal StorefrontImageService Images { get; } = new();
    internal HttpClient Http { get; }
    private Web08MediaHost(WebApplication app, int store, string origin)
    { this.app = app; selectedStore = store; Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new(origin) }; }
    internal void SelectOwnedStore(int store) => Volatile.Write(ref selectedStore, store);
    internal static async Task<Web08MediaHost> StartAsync(Web08Case c)
    {
        var directory = Web08Support.NewDirectory("Actual media HTTP controller component; in-memory DP; no Main");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ApplicationName = typeof(ShopController).Assembly.FullName,
            ContentRootPath = Path.Combine(Web08Support.Root(), "GaoApp.Storefront.Web") });
        builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        builder.Configuration["StorefrontHost:UploadsRoot"] = c.Uploads;
        builder.Configuration["StorefrontHost:Host"] = "127.0.0.1";
        builder.Configuration["StorefrontHost:StoreSubdomain"] = await c.Seed.SubdomainAsync();
        Web08MediaHost? host = null;
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddScoped<AppDbContext>(_ => c.Seed.Database.CreateTenantContext(host is null ? c.Seed.StoreId : Volatile.Read(ref host.selectedStore)));
        builder.Services.AddScoped<IStorefrontCatalogService, StorefrontCatalogService>();
        builder.Services.AddScoped<StorefrontCartReader>();
        builder.Services.AddScoped(s => new StorefrontMediaReader(s.GetRequiredService<IStorefrontCatalogService>(), builder.Configuration, host!.Images));
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ShopController).Assembly);
        var app = builder.Build(); app.MapControllers();
        try
        {
            await app.StartAsync(); var origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            host = new(app, c.Seed.StoreId, origin);
            File.WriteAllText(Path.Combine(directory, "owned-host.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, Owner = Web08Support.Owner(),
                Purpose = "WEB08 actual media controller/SQL component, in-memory DP", c.Seed.StoreId, RuntimeRoot = directory,
                Database = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(c.Seed.Database.ConnectionString).InitialCatalog,
                SourceCaseRoot = c.Directory, HasFilesystemDataProtectionKeys = false, NoMainOrMenu = true }));
            return host;
        }
        catch
        {
            try { await app.StopAsync(); } finally { await app.DisposeAsync(); host?.Images.Dispose(); }
            throw;
        }
    }
    public async ValueTask DisposeAsync()
    {
        Http.Dispose(); try { await app.StopAsync(); } finally { try { await app.DisposeAsync(); } finally { Images.Dispose(); } }
    }
}
