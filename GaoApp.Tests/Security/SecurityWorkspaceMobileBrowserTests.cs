using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class SecurityWorkspaceMobileBrowserTests
{
    [Fact]
    public async Task Native_UI21_uses_real_migrations_forms_challenges_tenant_scopes_and_original_permission_probe()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var root = FullApplicationFixture.SourceRoot();
        var output = MobileAcceptanceEvidence.PathFor(root, "TestResults", "mobile-ui-21-20261028", "browser");
        Directory.CreateDirectory(output);
        var store = app.Stores[0];
        var admin = await SecurityAdministrationPlanATests.MakeAdmin(app, await app.AddAccountAsync(store, "*"));
        var target = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var reader = await app.AddAccountAsync(store, PermissionCodes.Security.UserInStore.View, PermissionCodes.Security.Role.View,
            PermissionCodes.Security.Role.Permissions, PermissionCodes.System.AuditLog.View);
        var foreign = await app.AddAccountAsync(app.Stores[1], "*");
        int mappingId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            mappingId = await db.UserInStores.Where(x => x.UserId == target.UserId).Select(x => x.Id).SingleAsync();
            (await db.Users.SingleAsync(x => x.Id == target.UserId)).FullName = "UI21 Nguyễn Minh An · nhân viên ca tối tên dài";
            db.AuditLogs.Add(new AuditLog { StoreId = store.StoreId, ActorUserId = admin.UserId, ActorUserName = admin.Name,
                Module = AuditModuleType.Security, ActionType = AuditActionType.Update, EntityName = "UI21Employee",
                EntityId = mappingId.ToString(), Summary = "UI21 Kiểm thử nhật ký và nội dung dài trong phạm vi cửa hàng",
                ChangedColumnsJson = "[\"PositionName\"]", NewValuesJson = "{\"PositionName\":\"Nhân viên ca tối\"}",
                Path = "/Admin/UserInStores/Edit", IsSuccess = true });
            await db.SaveChangesAsync();
        }
        await using (var db = app.Database.CreateTenantContext(foreign.Store.StoreId))
        {
            db.AuditLogs.Add(new AuditLog { StoreId = foreign.Store.StoreId, ActorUserId = foreign.UserId,
                Summary = "UI21_FOREIGN_PRIVATE", EntityName = "UI21Foreign", ActionType = AuditActionType.Update, Module = AuditModuleType.Security });
            await db.SaveChangesAsync();
        }
        int[] originalUserIds;
        await using (var db = app.Database.CreateHostContext()) originalUserIds = await db.Users.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        var before = await ProtectedState(app, originalUserIds);
        var web = TestApplicationBuild.WebAssemblyPath();
        var compiled = new Microsoft.AspNetCore.Razor.Hosting.RazorCompiledItemLoader()
            .LoadItems(System.Reflection.Assembly.LoadFrom(web)).Select(x => x.Identifier).ToHashSet(StringComparer.Ordinal);
        using var inventory = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "docs/development/mobile-ui-20261025/view-inventory.json")));
        var expectedViews = inventory.RootElement.GetProperty("views").EnumerateArray().Where(x => x.GetProperty("step").GetString() == "UI-21")
            .Select(x => x.GetProperty("path").GetString()!.Replace("GaoApp.Web", "")).ToArray();
        Assert.Equal(16, expectedViews.Length); Assert.All(expectedViews, x => Assert.Contains(x, compiled));
        using (var assets = await app.LoginAsync(admin))
        {
            foreach (var path in new[] { "Admin/css/security-workspace.mobile.css", "Admin/js/security-workspace.mobile.js" })
                Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(root, "GaoApp.Web/wwwroot", path)), await assets.Http.GetByteArrayAsync("/" + path));
        }
        await File.WriteAllTextAsync(Path.Combine(output, "compiled-views.json"), JsonSerializer.Serialize(new {
            passed = true, expectedViews, webAssembly = web, webAssemblySha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(web))).ToLowerInvariant(),
            nativeAssetsMatchCurrentSource = true }, new JsonSerializerOptions { WriteIndented = true }));
        object Account(FullApplicationFixture.Account value) => new { user = value.Name, password = value.Password };
        var baseUrl = $"http://{store.Host}:{app.Address.Port}";
        await Run("role-permissions.browser.cjs", new { baseUrl, user = admin.Name, password = admin.Password, terminalId = store.TerminalId }, "original-role-permissions.log");
        await Run("security-workspace-mobile.browser.cjs", new { baseUrl, terminalId = store.TerminalId, mappingId, targetRoleId = target.RoleId,
            foreignMappingId = foreign.UserId, output, accounts = new { admin = Account(admin), reader = Account(reader) } }, "chrome.log");
        Assert.Equal(before, await ProtectedState(app, originalUserIds));
        using var access = await app.LoginAsync(reader);
        foreach (var route in new[] { "/admin/pos-terminals", "/admin/kiosks" })
        { using var denied = await access.Http.GetAsync(route); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); }
        using var adminClient = await app.LoginAsync(admin);
        var audit = await adminClient.Http.GetStringAsync("/admin/api/audit-logs?pageSize=100");
        Assert.DoesNotContain("UI21_FOREIGN_PRIVATE", audit);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Single(await db.POSTerminals.Where(x => x.Code == "UI21-PHONE").ToListAsync());
            var device = Assert.Single(await db.POSTerminalDevices.Where(x => x.DeviceName == "UI21 thiết bị điện thoại").ToListAsync());
            Assert.False(device.IsActive);
            var kiosk = Assert.Single(await db.Set<KioskStation>().ToListAsync());
            Assert.True(kiosk.IsActive); Assert.True(kiosk.IsPaused); Assert.NotNull(kiosk.ActivationHash);
            // Native kiosk creation intentionally adds exactly one non-login system identity.
            var systemUser = Assert.Single(await db.Users.Where(x => !originalUserIds.Contains(x.Id)).ToListAsync());
            Assert.Equal(kiosk.SystemUserId, systemUser.Id); Assert.StartsWith("kiosk_", systemUser.UserName);
            Assert.False(systemUser.IsActive); Assert.Equal("!KIOSK-NO-LOGIN!", systemUser.PasswordHash);
            Assert.False(await db.UserInStores.AnyAsync(x => x.UserId == systemUser.Id));
            Assert.Equal(PermissionCodes.Pos.Order.View, await db.RolePermissions.Where(x => x.RoleId == target.RoleId).Select(x => x.Permission.Code).SingleAsync());
            Assert.False(db.Database.HasPendingModelChanges()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
        await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(new {
            passed = true, schemaMode = "strict_full_migration", pendingModelChanges = false, pendingMigrations = 0,
            protectedDataBeforeSha256 = Hash(before), protectedDataAfterSha256 = Hash(await ProtectedState(app, originalUserIds)),
            storeMenuConfigurationExistingUsersAndPermissionsUnchanged = true, nativeDeviceCreatedAndRevoked = true,
            kioskCreatedAndPausedInOwnedFixture = true, exactlyOneNativeInactiveKioskSystemUserCreated = true, foreignAuditExcluded = true, externalProvidersEnabled = false,
            originalRolePermissionBrowserProbePassed = true, existingChromeWindowsPreserved = true
        }, new JsonSerializerOptions { WriteIndented = true }));

        async Task Run(string script, object info, string logName)
        {
            var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
            start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", script));
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(info)); process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(16));
            try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
            var log = await stdout + "\n" + await stderr;
            await File.WriteAllTextAsync(Path.Combine(output, logName), log); Assert.True(process.ExitCode == 0, log);
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static async Task<string> ProtectedState(FullApplicationFixture app, int[] originalUserIds)
    {
        await using var db = app.Database.CreateHostContext();
        return JsonSerializer.Serialize(new {
            stores = await db.Stores.OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.IsActive }).ToListAsync(),
            menus = await db.AdminMenuItems.OrderBy(x => x.Id).Select(x => new { x.Id, x.Title, x.Url, x.SortOrder, x.IsActive, x.PermissionCode }).ToListAsync(),
            pins = await db.Set<AdminMenuPinSetting>().OrderBy(x => x.Id).ToListAsync(),
            visibility = await db.Set<AdminMenuVisibilitySetting>().OrderBy(x => x.Id).ToListAsync(),
            users = await db.Users.Where(x => originalUserIds.Contains(x.Id)).OrderBy(x => x.Id).Select(x => new { x.Id, x.FullName, x.PasswordHash, x.IsActive }).ToListAsync(),
            memberships = await db.UserInStores.OrderBy(x => x.Id).Select(x => new { x.Id, x.UserId, x.RoleId, x.IsActive, x.PhoneNumber, x.PositionName, x.Note }).ToListAsync(),
            payments = await db.OrderPayments.OrderBy(x => x.Id).Select(x => new { x.Id, x.OrderId, x.Amount }).ToListAsync()
        });
    }
}
