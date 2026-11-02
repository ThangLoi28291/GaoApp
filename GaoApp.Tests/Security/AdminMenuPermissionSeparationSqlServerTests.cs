using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Services.AdminMenus;
using GaoApp.Application.Services.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.AdminMenus;
using GaoApp.Infrastructure.Repositories.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class AdminMenuPermissionSeparationSqlServerTests
{
    private const string Product = PermissionCodes.Catalog.Product.View;
    private const string Bank = PermissionCodes.System.BankAccount.Manage;

    [Fact]
    public async Task Menu_create_edit_disable_and_delete_preserve_the_catalog_all_grants_and_authenticated_sessions()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var admin = await Admin(app);
        var employee = await app.AddAccountAsync(store, Product);
        using var client = await app.LoginAsync(admin);
        using var staff = await app.LoginAsync(employee);
        var baseline = await SecurityGraph(app);
        var first = await Create(app, client, admin, "Menu A");
        var second = await Create(app, client, admin, "Menu B");
        Assert.Equal(baseline, await SecurityGraph(app));
        Assert.Contains(first, await Render(app, employee));
        Assert.Contains(second, await Render(app, employee));

        var form = Form(admin, "Menu A renamed", first);
        form["SortOrder"] = "17";
        using (var response = await Post(client, "Edit", form)) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(baseline, await SecurityGraph(app));
        form["PermissionCode"] = Bank;
        using (var response = await Post(client, "Edit", form)) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(first, await Render(app, employee));
        Assert.Contains(second, await Render(app, employee));
        Assert.Equal(baseline, await SecurityGraph(app));
        form["PermissionCode"] = "";
        form["IsActive"] = "false";
        using (var response = await Post(client, "Edit", form)) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var response = await Post(client, "Delete", new() { ["id"] = first.ToString() }))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(baseline, await SecurityGraph(app));
        using (var response = await staff.Http.GetAsync("/Admin/Product")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var deleted = await db.AdminMenuItems.IgnoreQueryFilters().SingleAsync(x => x.StoreId == store.StoreId && x.Id == first);
        Assert.True(deleted.IsDeleted);
        Assert.Equal(17, deleted.SortOrder);
        Assert.Equal(Product, (await db.AdminMenuItems.SingleAsync(x => x.Id == second)).PermissionCode);
        var audit = await db.AuditLogs.Where(x => x.EntityName == nameof(AdminMenuItem) && x.EntityId == first.ToString()).ToListAsync();
        Assert.True(audit.Count >= 4);
        Assert.All(audit, entry => Assert.Empty(JsonDocument.Parse(entry.NewValuesJson!).RootElement.GetProperty("GrantChanges").EnumerateArray()));
        Assert.All(audit, entry => Assert.DoesNotContain(admin.Password, entry.NewValuesJson!));
    }

    [Fact]
    public async Task Legacy_forms_and_direct_credential_grant_fields_are_rejected_before_menu_or_security_writes()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var admin = await Admin(app);
        var employee = await app.AddAccountAsync(admin.Store, Product);
        using var client = await app.LoginAsync(admin);
        var menuId = await Create(app, client, admin, "Legacy protected menu");
        var baseline = await SecurityGraph(app);
        Dictionary<string, string>[] attempts = [
            new() { ["CreateNewPermission"] = "false" },
            new() { ["CreateNewPermission"] = "true", ["NewPermissionCode"] = "forged.menu.permission" },
            new() { ["AssignRoleIds"] = employee.RoleId.ToString() },
            new() { ["AssignRoleIds[0]"] = employee.RoleId.ToString() },
            new() { ["NewPermissionCode"] = Product },
            new() { ["NewPermissionName"] = "forged permission" },
            new() { ["NewPermissionGroupName"] = "forged group" },
            new() { ["NewPermissionCode"] = "" }
        ];
        foreach (var action in new[] { "Create", "Edit" })
        foreach (var attempt in attempts)
        {
            var form = Form(admin, "Must not be saved", menuId);
            // Rejection precedes confirmation; this test does not consume password-attempt quota.
            form.Remove("AdministratorPassword");
            foreach (var field in attempt) form[field.Key] = field.Value;
            using var response = await Post(client, action, form);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.Contains("Biểu mẫu menu đã thay đổi", html);
            Assert.DoesNotContain("name=\"AssignRoleIds", html);
            Assert.Equal(baseline, await SecurityGraph(app));
        }
        await using var db = app.Database.CreateTenantContext(admin.Store.StoreId);
        Assert.False(await db.AdminMenuItems.AnyAsync(x => x.Title == "Must not be saved"));
        Assert.Equal("Legacy protected menu", (await db.AdminMenuItems.SingleAsync(x => x.Id == menuId)).Title);
        client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using var noCsrf = await Post(client, "Create", Form(admin, "No CSRF"));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }

    [Fact]
    public async Task Menu_edits_do_not_restore_stale_role_grants_and_the_separate_permission_editor_still_controls_access()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var admin = await Admin(app);
        var employee = await app.AddAccountAsync(admin.Store, Product);
        using var client = await app.LoginAsync(admin);
        var menuId = await Create(app, client, admin, "Stale menu");
        var staleForm = Form(admin, "Metadata saved after a grant change", menuId);
        var html = await client.Http.GetStringAsync("/Admin/AdminMenus/Edit/" + menuId);
        Assert.DoesNotContain("name=\"AssignRoleIds", html);
        Assert.DoesNotContain("name=\"CreateNewPermission", html);
        Assert.Contains("/Admin/RolePermissions", html, StringComparison.OrdinalIgnoreCase);
        int permissionId;
        await using (var db = app.Database.CreateTenantContext(admin.Store.StoreId))
            permissionId = await db.Permissions.Where(x => x.Code == Bank).Select(x => x.Id).SingleAsync();
        using (var response = await client.Http.PostAsync("/Admin/RolePermissions/Save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["RoleId"] = employee.RoleId.ToString(), ["SelectedPermissionIds"] = permissionId.ToString(),
            ["AdministratorPassword"] = admin.Password, ["Reason"] = "Change grants separately from menus"
        }))) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var changedGrants = await SecurityGraph(app);
        using (var response = await Post(client, "Edit", staleForm)) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(changedGrants, await SecurityGraph(app));
        Assert.DoesNotContain(menuId, await Render(app, employee));
        using var staff = await app.LoginAsync(employee);
        using var denied = await staff.Http.GetAsync("/Admin/Product");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Menu_requests_reject_foreign_parents_cycles_unknown_new_conditions_and_non_administrators()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var admin = await Admin(app);
        var hr = await app.AddAccountAsync(admin.Store, PermissionCodes.Security.Role.Permissions);
        using var client = await app.LoginAsync(admin);
        using var hrClient = await app.LoginAsync(hr);
        int foreignId, parentId, childId, legacyId;
        await using (var db = app.Database.CreateTenantContext(app.Stores[1].StoreId))
        {
            var menu = new AdminMenuItem { StoreId = app.Stores[1].StoreId, Title = "Foreign unchanged", Controller = "Product" };
            db.Add(menu); await db.SaveChangesAsync(); foreignId = menu.Id;
        }
        await using (var db = app.Database.CreateTenantContext(admin.Store.StoreId))
        {
            var parent = new AdminMenuItem { StoreId = admin.Store.StoreId, Title = "Parent" };
            var child = new AdminMenuItem { StoreId = admin.Store.StoreId, Title = "Child", Parent = parent, Controller = "Product" };
            var legacy = new AdminMenuItem { StoreId = admin.Store.StoreId, Title = "Legacy condition", Controller = "Product", PermissionCode = "legacy.not.in.catalog" };
            db.AddRange(parent, child, legacy); await db.SaveChangesAsync();
            parentId = parent.Id; childId = child.Id; legacyId = legacy.Id;
        }
        var graph = await SecurityGraph(app);
        foreach (var action in new[] { "Create", "Edit", "Delete" })
        {
            using var response = await Post(hrClient, action, Form(hr, "Unauthorized", parentId));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        var parentForm = Form(admin, "Foreign parent");
        parentForm["PermissionCode"] = ""; parentForm.Remove("AdministratorPassword"); parentForm["ParentId"] = foreignId.ToString();
        using (var response = await Post(client, "Create", parentForm)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var cycle = Form(admin, "Cycle", parentId);
        cycle["PermissionCode"] = ""; cycle.Remove("AdministratorPassword"); cycle["ParentId"] = childId.ToString();
        using (var response = await Post(client, "Edit", cycle)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unknown = Form(admin, "Unknown permission"); unknown["PermissionCode"] = "unregistered.condition";
        using (var response = await Post(client, "Create", unknown)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var foreign = Form(admin, "Foreign changed", foreignId);
        using (var response = await Post(client, "Edit", foreign)) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var legacyHtml = await client.Http.GetStringAsync("/Admin/AdminMenus/Edit/" + legacyId);
        Assert.Contains("value=\"legacy.not.in.catalog\" selected=\"selected\"", legacyHtml);
        var legacyForm = Form(admin, "Legacy renamed", legacyId); legacyForm["PermissionCode"] = "legacy.not.in.catalog";
        using (var response = await Post(client, "Edit", legacyForm)) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(graph, await SecurityGraph(app));
        await using var check = app.Database.CreateHostContext();
        Assert.Equal("Foreign unchanged", (await check.AdminMenuItems.SingleAsync(x => x.Id == foreignId)).Title);
        Assert.Equal("legacy.not.in.catalog", (await check.AdminMenuItems.SingleAsync(x => x.Id == legacyId)).PermissionCode);
        Assert.False(await check.Permissions.AnyAsync(x => x.Code == "legacy.not.in.catalog" || x.Code == "unregistered.condition"));
    }

    [Fact]
    public async Task Chrome_can_use_both_new_menu_forms_without_grant_fields_and_legacy_posts_show_a_reload_error()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var admin = await Admin(app);
        var graph = await SecurityGraph(app);
        var root = FullApplicationFixture.SourceRoot();
        var output = Path.Combine(root, ".artifacts", "security-issue9", "browser"); Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "admin-menu-permissions.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{admin.Store.Host}:{app.Address.Port}",
            user = admin.Name, password = admin.Password, terminalId = admin.Store.TerminalId, permission = Product, output }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout + "\n" + await stderr;
        await File.WriteAllTextAsync(Path.Combine(output, "chrome.log"), log);
        Assert.True(process.ExitCode == 0, log);
        Assert.Equal(graph, await SecurityGraph(app));
        await using var db = app.Database.CreateTenantContext(admin.Store.StoreId);
        Assert.Single(await db.AdminMenuItems.Where(x => x.Title == "Browser menu renamed").ToListAsync());
        Assert.False(await db.AdminMenuItems.AnyAsync(x => x.Title == "Browser forged menu"));
    }

    private static async Task<FullApplicationFixture.Account> Admin(FullApplicationFixture app)
        => await SecurityAdministrationPlanATests.MakeAdmin(app, await app.AddAccountAsync(app.Stores[0], "*"));

    private static Dictionary<string, string> Form(FullApplicationFixture.Account actor, string title, int id = 0) => new()
    {
        ["Id"] = id.ToString(), ["Title"] = title, ["Area"] = "Admin", ["Controller"] = "Product", ["Action"] = "Index",
        ["PermissionCode"] = Product, ["SortOrder"] = "3", ["IsActive"] = "true",
        ["AdministratorPassword"] = actor.Password, ["Reason"] = "Test menu display configuration"
    };
    private static Task<HttpResponseMessage> Post(FullApplicationFixture.Client client, string action, Dictionary<string, string> form)
        => client.Http.PostAsync("/Admin/AdminMenus/" + action, new FormUrlEncodedContent(form));
    private static async Task<int> Create(FullApplicationFixture app, FullApplicationFixture.Client client, FullApplicationFixture.Account actor, string title)
    {
        using var response = await Post(client, "Create", Form(actor, title));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = app.Database.CreateTenantContext(actor.Store.StoreId);
        return await db.AdminMenuItems.Where(x => x.Title == title).Select(x => x.Id).SingleAsync();
    }
    private static async Task<string> SecurityGraph(FullApplicationFixture app)
    {
        await using var db = app.Database.CreateHostContext();
        var catalog = await db.Permissions.OrderBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Name, x.GroupName }).ToArrayAsync();
        var grants = await db.RolePermissions.OrderBy(x => x.Id).Select(x => new { x.Id, x.RoleId, x.PermissionId }).ToArrayAsync();
        var members = await db.UserInStores.OrderBy(x => x.Id).Select(x => new { x.Id, x.StoreId, x.UserId, x.RoleId, x.UpdatedAtUtc, x.RowVersion }).ToArrayAsync();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { catalog, grants, members }))));
    }
    private static async Task<int[]> Render(FullApplicationFixture app, FullApplicationFixture.Account actor)
    {
        await using var db = app.Database.CreateTenantContext(actor.Store.StoreId);
        var permissions = new CurrentStorePermissionService(new UserInStoreRepository(db));
        var menu = new AdminMenuService(new AdminMenuRepository(db), new AdminMenuPermissionRepository(db), permissions);
        return Flatten(await menu.GetForRenderAsync(actor.Store.StoreId, actor.UserId)).ToArray();
        static IEnumerable<int> Flatten(IEnumerable<AdminMenuItemDto> nodes)
            => nodes.SelectMany(node => new[] { node.Id }.Concat(Flatten(node.Children)));
    }
}
