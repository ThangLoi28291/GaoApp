using GaoApp.Application.Common;
using GaoApp.Application.Common.Errors;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Auth;
using GaoApp.Application.Services.Auth;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.Auth;
using GaoApp.Infrastructure.Repositories.POSTerminals;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Areas.Admin.ViewModels.POSTerminals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GaoApp.Tests.Security;

public sealed class POSTerminalManagementTests
{
    [Theory]
    [InlineData("ADMIN", true)]
    [InlineData("MANAGER", false)]
    [InlineData("CASHIER", false)]
    public async Task Only_admin_can_sign_in_for_management_without_a_terminal(string role, bool allowed)
    {
        await using var f = await Fixture.Create(role);
        var result = await f.Auth.LoginAsync(new LoginRequest { UserName = "test", Password = "password", ManageTerminals = true });
        Assert.Equal(allowed, result.IsSuccess);
        if (allowed)
        {
            Assert.Equal(0, result.Value.TerminalId);
            Assert.Null(result.Value.DeviceKey);
            Assert.False(result.Value.DevicePaired);
        }
        else Assert.Equal(AuthErrors.TerminalManagementDenied.Code, result.Error.Code);
        Assert.Empty(await f.Db.POSTerminalDevices.ToListAsync());
    }

    [Fact]
    public async Task Management_mode_still_checks_password_and_store_membership()
    {
        await using var f = await Fixture.Create();
        var request = new LoginRequest { UserName = "test", Password = "wrong", ManageTerminals = true };
        Assert.Equal(AuthErrors.InvalidCredentials.Code, (await f.Auth.LoginAsync(request)).Error.Code);
        request.Password = "password";
        var member = await f.Db.UserInStores.SingleAsync();
        member.IsActive = false;
        await f.Db.SaveChangesAsync();
        Assert.True((await f.Auth.LoginAsync(request)).IsFailure);
    }

    [Fact]
    public async Task Normal_login_still_requires_a_terminal()
    {
        await using var f = await Fixture.Create();
        var result = await f.Auth.LoginAsync(new LoginRequest { UserName = "test", Password = "password" });
        Assert.Equal(AuthErrors.TerminalSelectionRequired.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_terminal_normalizes_code_and_rejects_duplicates()
    {
        await using var f = await Fixture.Create();
        Assert.IsType<RedirectToActionResult>(await f.Controller.Create(new POSTerminalVm { Code = "pos02", Name = " Quầy 2 " }, default));
        var terminal = await f.Db.POSTerminals.SingleAsync();
        Assert.Equal("POS02", terminal.Code);
        Assert.Equal("Quầy 2", terminal.Name);
        Assert.Equal(7, terminal.StoreId);
        Assert.IsType<ViewResult>(await f.Controller.Create(new POSTerminalVm { Code = "POS02", Name = "Trùng mã" }, default));
        Assert.False(f.Controller.ModelState.IsValid);
        Assert.Single(await f.Db.POSTerminals.ToListAsync());
    }

    [Fact]
    public async Task Created_key_logs_in_to_selected_terminal_and_revoked_key_is_rejected()
    {
        await using var f = await Fixture.Create();
        var terminal = await f.AddTerminal();
        var created = Assert.IsType<ViewResult>(await f.Controller.CreateKey(terminal.Id,
            new CreatePOSKeyVm { TerminalId = terminal.Id, DeviceName = "PC quầy 1" }, default));
        var device = Assert.IsType<POSTerminalDevice>(created.Model);
        Assert.Equal(64, device.DeviceKey.Length);
        Assert.Equal("KeyCreated", created.ViewName);
        var request = new LoginRequest { UserName = "test", Password = "password", PairingKey = device.DeviceKey };
        var result = await f.Auth.LoginAsync(request);
        Assert.True(result.IsSuccess);
        Assert.Equal(terminal.Id, result.Value.TerminalId);
        Assert.Equal(device.DeviceKey, result.Value.DeviceKey);
        Assert.Single(await f.Db.POSTerminalDevices.ToListAsync());
        await f.Controller.RevokeKey(device.Id, default);
        request.SelectedTerminalId = terminal.Id;
        Assert.Equal(AuthErrors.InvalidPairingKey.Code, (await f.Auth.LoginAsync(request)).Error.Code);
    }

    [Fact]
    public async Task Key_cannot_bypass_password_and_invalid_key_does_not_fall_back_to_pairing()
    {
        await using var f = await Fixture.Create();
        var terminal = await f.AddTerminal();
        var request = new LoginRequest { UserName = "test", Password = "wrong", PairingKey = "unknown", SelectedTerminalId = terminal.Id };
        Assert.Equal(AuthErrors.InvalidCredentials.Code, (await f.Auth.LoginAsync(request)).Error.Code);
        request.Password = "password";
        Assert.Equal(AuthErrors.InvalidPairingKey.Code, (await f.Auth.LoginAsync(request)).Error.Code);
        Assert.Empty(await f.Db.POSTerminalDevices.ToListAsync());
    }

    [Fact]
    public async Task Management_queries_and_key_operations_are_scoped_to_store()
    {
        await using var f = await Fixture.Create();
        var mine = await f.AddTerminal();
        f.Tenant.SetStore(8, "other");
        var other = new POSTerminal { StoreId = 8, Code = "OTHER", Name = "Other store" };
        var otherDevice = new POSTerminalDevice { StoreId = 8, Terminal = other, DeviceKey = "other-store-key" };
        f.Db.Add(otherDevice);
        await f.Db.SaveChangesAsync();
        f.Tenant.SetStore(7, "test");
        var index = Assert.IsType<POSTerminalIndexVm>(Assert.IsType<ViewResult>(await f.Controller.Index(default)).Model);
        Assert.Equal(mine.Id, Assert.Single(index.Terminals).Id);
        Assert.IsType<NotFoundResult>(await f.Controller.CreateKey(other.Id, default));
        Assert.IsType<NotFoundResult>(await f.Controller.CreateKey(other.Id,
            new CreatePOSKeyVm { TerminalId = other.Id, DeviceName = "Intruder" }, default));
        Assert.IsType<NotFoundResult>(await f.Controller.RevokeKey(otherDevice.Id, default));
        var result = await f.Auth.LoginAsync(new LoginRequest { UserName = "test", Password = "password", PairingKey = otherDevice.DeviceKey });
        Assert.Equal(AuthErrors.InvalidPairingKey.Code, result.Error.Code);
        Assert.True(otherDevice.IsActive);
    }

    [Fact]
    public async Task Inactive_terminal_cannot_receive_new_keys()
    {
        await using var f = await Fixture.Create();
        var terminal = await f.AddTerminal();
        terminal.IsActive = false;
        await f.Db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await f.Controller.CreateKey(terminal.Id,
            new CreatePOSKeyVm { TerminalId = terminal.Id, DeviceName = "PC" }, default));
        Assert.Empty(await f.Db.POSTerminalDevices.ToListAsync());
    }

    [Fact]
    public void Management_endpoints_require_admin_and_mutations_require_antiforgery()
    {
        var type = typeof(POSTerminalsController);
        Assert.Contains(type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(), x => x.Roles == "ADMIN");
        foreach (var method in type.GetMethods().Where(x => x.IsDefined(typeof(HttpPostAttribute), true)))
            Assert.True(method.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true));
        Assert.True(type.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>().Single().NoStore);
    }

    [Fact]
    public void Device_keys_are_redacted_from_audit_payloads()
    {
        var json = AuditSensitiveData.SanitizeJson("{\"DeviceKey\":\"key-secret\",\"PairingKey\":\"pair-secret\",\"DeviceName\":\"PC\"}");
        Assert.DoesNotContain("key-secret", json);
        Assert.DoesNotContain("pair-secret", json);
        Assert.Contains("PC", json);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public TenantContext Tenant { get; } = new();
        public InMemoryAppDbContext Db { get; }
        public POSTerminalsController Controller { get; }
        public AuthService Auth { get; }
        private readonly ServiceProvider services;

        private Fixture()
        {
            Tenant.SetStore(7, "test");
            Db = new InMemoryAppDbContext(new DbContextOptionsBuilder<InMemoryAppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Tenant, new SessionPrincipalValidatorTests.CurrentUser());
            services = new ServiceCollection().AddSingleton<ITenantContext>(Tenant).BuildServiceProvider();
            var context = new DefaultHttpContext { RequestServices = services };
            Controller = new POSTerminalsController(Db)
            {
                ControllerContext = new ControllerContext { HttpContext = context },
                Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(new ActionContext(context,
                    new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor())),
                TempData = new TempDataDictionary(context, new TempDataProvider())
            };
            Auth = new AuthService(new AuthUserRepository(Db), new POSTerminalRepository(Db),
                new CurrentStore(), new ClientNetwork(), new PasswordHasher());
        }

        public static async Task<Fixture> Create(string role = "ADMIN")
        {
            var f = new Fixture();
            f.Db.UserInStores.Add(new UserInStore
            {
                StoreId = 7,
                User = new User { UserName = "test", PasswordHash = "password", IsActive = true },
                Role = new Role { StoreId = 7, Code = role, Name = role }
            });
            await f.Db.SaveChangesAsync();
            return f;
        }

        public async Task<POSTerminal> AddTerminal()
        {
            var terminal = new POSTerminal { StoreId = 7, Code = "POS01", Name = "Quầy 1" };
            Db.Add(terminal);
            await Db.SaveChangesAsync();
            return terminal;
        }

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await services.DisposeAsync(); }
    }

    private sealed class CurrentStore : ICurrentStore { public int StoreId => 7; }
    private sealed class ClientNetwork : IClientNetworkInfo { public string? GetClientIp() => "127.0.0.1"; }
    private sealed class PasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => password;
        public bool Verify(string password, string hash) => password == hash;
    }
    private sealed class TempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
