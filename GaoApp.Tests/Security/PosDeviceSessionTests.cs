using System.Security.Claims;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using GaoApp.Web.Common.POS;
using GaoApp.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

public sealed class PosDeviceSessionTests
{
    [Theory]
    [InlineData("unchanged", true)]
    [InlineData("revoked", false)]
    [InlineData("deleted", false)]
    [InlineData("terminal-disabled", false)]
    [InlineData("terminal-deleted", false)]
    [InlineData("key-rotated", false)]
    [InlineData("new-generation", false)]
    [InlineData("cookie-missing", false)]
    [InlineData("cookie-swapped", false)]
    [InlineData("legacy-ticket", false)]
    [InlineData("missing-device-stamp", false)]
    [InlineData("management-bypass", false)]
    [InlineData("other-terminal", false)]
    [InlineData("other-store", false)]
    public async Task HTTP_session_requires_current_device_key_generation_and_ownership(string change, bool allowed)
    {
        var tenant = new TenantContext(); tenant.SetStore(7, "store7");
        await using var db = new InMemoryAppDbContext(new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant, new SessionPrincipalValidatorTests.CurrentUser());
        var member = new UserInStore { StoreId = 7,
            User = new User { UserName = "device-test", PasswordHash = "synthetic" },
            Role = new Role { StoreId = 7, Code = "CASHIER", Name = "Cashier" } };
        var terminal = new POSTerminal { StoreId = 7, Code = "T1", Name = "Terminal" };
        var device = new POSTerminalDevice { StoreId = 7, Terminal = terminal, DeviceKey = "synthetic-private-device-key", RowVersion = [1] };
        db.AddRange(member, device); await db.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, member.UserId.ToString()), new Claim("store_id", "7"),
            new Claim("role_id", member.RoleId.ToString()), new Claim(ClaimTypes.Role, member.Role.Code),
            new Claim(AuthSessionStamp.ClaimType, AuthSessionStamp.Create(member.User, member)),
            new Claim("terminal_id", terminal.Id.ToString()), new Claim(PosDeviceSession.IdClaim, device.Id.ToString()),
            new Claim(PosDeviceSession.StampClaim, PosDeviceSession.CreateStamp(device))
        }, "test"));
        string? cookie = device.DeviceKey;
        switch (change)
        {
            case "revoked": device.IsActive = false; break;
            case "deleted": device.IsDeleted = true; break;
            case "terminal-disabled": terminal.IsActive = false; break;
            case "terminal-deleted": terminal.IsDeleted = true; break;
            case "key-rotated": device.DeviceKey = "new-device-key"; cookie = device.DeviceKey; break;
            case "new-generation": device.RowVersion = [99]; break;
            case "cookie-missing": cookie = null; break;
            case "cookie-swapped": cookie = "other-valid-key"; break;
            case "legacy-ticket": ((ClaimsIdentity)principal.Identity!).RemoveClaim(principal.FindFirst(PosDeviceSession.IdClaim)!); break;
            case "missing-device-stamp": ((ClaimsIdentity)principal.Identity!).RemoveClaim(principal.FindFirst(PosDeviceSession.StampClaim)!); break;
            case "management-bypass":
                var identity = (ClaimsIdentity)principal.Identity!;
                identity.RemoveClaim(identity.FindFirst("terminal_id")!);
                identity.AddClaim(new("terminal_id", "0")); identity.AddClaim(new(PosDeviceSession.ManagementClaim, "1")); break;
            case "other-terminal": device.Terminal = new POSTerminal { StoreId = 7, Code = "T2", Name = "Other" }; break;
            case "other-store": tenant.SetStore(8, "store8"); break;
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(allowed, await new SessionPrincipalValidator(db, tenant).ValidateAsync(principal, deviceKey: cookie));
    }

    [Fact]
    public void Device_claims_do_not_contain_the_raw_pairing_key_and_internal_fields_are_not_serialized()
    {
        var device = new POSTerminalDevice { Id = 3, StoreId = 7, TerminalId = 1, DeviceKey = "private-pairing-key", RowVersion = [1] };
        var stamp = PosDeviceSession.CreateStamp(device);
        Assert.DoesNotContain(device.DeviceKey, stamp);
        var json = System.Text.Json.JsonSerializer.Serialize(new GaoApp.Application.DTOs.Auth.LoginResponse
            { DeviceId = 3, DeviceSessionStamp = stamp });
        Assert.DoesNotContain(stamp, json);
        Assert.DoesNotContain("DeviceId", json);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Authenticated_runtime_uses_its_own_terminal_and_never_a_different_pairing_cookie(int terminalId)
    {
        var context = new DefaultHttpContext();
        context.User = new(new ClaimsIdentity(new[] { new Claim("terminal_id", terminalId.ToString()),
            new Claim("terminal_code", "BOUND"), new Claim("terminal_name", "Bound terminal") }, "test"));
        context.Items["CurrentTerminalId"] = "99";
        context.Items["CurrentTerminalCode"] = "OTHER";
        var tenant = new TenantContext(); tenant.SetStore(7, "store7");
        var runtime = new POSRuntimeContextAccessor(new HttpContextAccessor { HttpContext = context }, tenant);
        Assert.Equal(terminalId, runtime.TerminalId);
        Assert.Equal("BOUND", runtime.TerminalCode);
    }

    [Fact]
    public void Internal_kiosk_runtime_keeps_the_resolved_terminal_name()
    {
        var context = new DefaultHttpContext();
        context.User = new(new ClaimsIdentity(new[] { new Claim("terminal_id", "10"), new Claim("terminal_code", "KIOSK") }, "KioskInternal"));
        context.Items["CurrentTerminalId"] = 10;
        context.Items["CurrentTerminalName"] = "Existing kiosk";
        var tenant = new TenantContext(); tenant.SetStore(7, "store7");
        var runtime = new POSRuntimeContextAccessor(new HttpContextAccessor { HttpContext = context }, tenant);
        Assert.Equal(10, runtime.TerminalId);
        Assert.Equal("Existing kiosk", runtime.TerminalName);
        Assert.Equal("KIOSK", runtime.TerminalCode);
    }
}
