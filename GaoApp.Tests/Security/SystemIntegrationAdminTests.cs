using System.Reflection;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using GaoApp.Web.Services.SystemIntegrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Security;

public sealed class SystemIntegrationAdminTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaoIntegrationAdminTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Store_administrator_cannot_read_or_change_global_switches()
    {
        var tenant = new TenantContext();
        await using var db = await Database(tenant, false);
        var controller = new SystemIntegrationsController(Switches(), db, new Actor());

        Assert.IsType<ForbidResult>(await controller.Index(default));
        Assert.IsType<ForbidResult>(await controller.Save(new() { Version = "initial" }, default));
        Assert.IsType<ForbidResult>(await controller.SaveConnections(new() { Version = "initial", AiPrivateKeyPem = "SYNTHETIC_ONLY" }, default));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Active_system_administrator_can_view_and_save_global_switches()
    {
        var tenant = new TenantContext();
        await using var db = await Database(tenant, true);
        var switches = Switches();
        var controller = new SystemIntegrationsController(switches, db, new Actor())
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new EmptyTempData())
        };

        var view = Assert.IsType<ViewResult>(await controller.Index(default));
        var state = Assert.IsType<SystemIntegrationState>(view.Model);
        var saved = Assert.IsType<RedirectToActionResult>(await controller.Save(new() { Version = state.Saved.Version }, default));

        Assert.Equal("Index", saved.ActionName);
        Assert.Equal(1, switches.Read().UpdatedBy);
        Assert.False(switches.State().ZaloEnabled);
        Assert.False(switches.State().AiEnabled);
    }

    [Fact]
    public void Global_controller_requires_settings_permission_antiforgery_and_no_response_cache()
    {
        var type = typeof(SystemIntegrationsController);
        Assert.Equal(PermissionCodes.System.Setting.Update, type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.NotNull(type.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>());
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Inactive_or_deleted_host_admin_cannot_read_or_change_connections(bool active, bool deleted)
    {
        var tenant = new TenantContext();
        await using var db = await Database(tenant, true);
        var account = await db.Users.SingleAsync();
        account.IsActive = active; account.IsDeleted = deleted;
        using (tenant.BeginSystemAccess(SystemDataAccessPurpose.Provisioning)) await db.SaveChangesAsync();
        using var switches = Switches();
        var controller = new SystemIntegrationsController(switches, db, new Actor());

        Assert.IsType<ForbidResult>(await controller.Index(default));
        Assert.IsType<ForbidResult>(await controller.SaveConnections(new() { Version = "initial" }, default));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Host_admin_can_save_connection_for_an_active_store_without_enabling_the_channel()
    {
        var tenant = new TenantContext();
        await using var db = await Database(tenant, true);
        db.Stores.Add(new Store { Id = 1, Name = "Synthetic store", SubDomain = "test", SubDomainNormalized = "TEST", RowVersion = new byte[8] });
        using (tenant.BeginSystemAccess(SystemDataAccessPurpose.Provisioning)) await db.SaveChangesAsync();
        using var switches = Switches();
        var controller = new SystemIntegrationsController(switches, db, new Actor())
        { TempData = new TempDataDictionary(new DefaultHttpContext(), new EmptyTempData()) };
        var form = new SystemIntegrationConnectionsForm { Version = "initial", Zalo = [new()
        { Id = "zalo-test", StoreId = 1, BaseUrl = "https://synthetic.example.test", AccountId = Guid.NewGuid().ToString(), AccessToken = "SYNTHETIC-SECRET-TEST-ONLY" }, new()] };

        Assert.IsType<RedirectToActionResult>(await controller.SaveConnections(form, default));
        Assert.Equal(1, switches.Read().UpdatedBy);
        Assert.Equal("zalo-test", Assert.Single(switches.State().Connections!.Zalo).Id);
        Assert.False(switches.State().ZaloEnabled);
        Assert.DoesNotContain("SYNTHETIC-SECRET-TEST-ONLY", System.Text.Json.JsonSerializer.Serialize(switches.State()));
    }

    [Fact]
    public async Task Invalid_connection_save_does_not_redisplay_secret_model_values_or_persist_them()
    {
        var tenant = new TenantContext();
        await using var db = await Database(tenant, true);
        using var switches = Switches();
        var controller = new SystemIntegrationsController(switches, db, new Actor())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.ModelState.SetModelValue("AiPrivateKeyPem", "SYNTHETIC_SECRET", "SYNTHETIC_SECRET");
        controller.ModelState.AddModelError("AiPrivateKeyPem", "SYNTHETIC_SECRET");

        var result = Assert.IsType<ViewResult>(await controller.SaveConnections(new() { Version = "initial", AiPrivateKeyPem = "SYNTHETIC_SECRET" }, default));

        Assert.Null(controller.ModelState["AiPrivateKeyPem"]);
        Assert.DoesNotContain("SYNTHETIC_SECRET", System.Text.Json.JsonSerializer.Serialize(result.Model));
        Assert.Equal("initial", switches.Read().Version);
    }

    private SystemIntegrationSwitchStore Switches()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Assistant:StoreSettingsDirectory"] = directory }).Build();
        return new(config, new EnvironmentStub(), TimeProvider.System, new OptionsCache<AssistantOptions>(),
            new OptionsCache<CustomerChannelOptions>(), new OptionsCache<CustomerMarketingCampaignOptions>(), new EphemeralDataProtectionProvider());
    }

    private static async Task<AppDbContext> Database(TenantContext tenant, bool hostAdmin)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("integration-admin-" + Guid.NewGuid()).Options, tenant, new Actor());
        using (tenant.BeginSystemAccess(SystemDataAccessPurpose.Provisioning))
        {
            db.Users.Add(new User { Id = 1, UserName = "synthetic-admin", FullName = "Synthetic user", PasswordHash = "synthetic-only",
                IsHostAdmin = hostAdmin, IsActive = true, RowVersion = new byte[8] });
            await db.SaveChangesAsync();
        }
        return db;
    }

    public void Dispose()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GaoIntegrationAdminTests")) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(directory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(target), "N", out _))
            throw new InvalidOperationException("Unsafe test cleanup path.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    private sealed class Actor : ICurrentUser
    {
        public int? UserId => 1;
        public string? UserName => "synthetic-admin";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed class EmptyTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class EnvironmentStub : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GaoApp.Web";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string WebRootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
