using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class POSTerminalManagementSqlServerTests
{
    [Fact]
    public async Task Admin_without_paired_device_creates_terminal_and_key_through_real_forms()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store);
        using var admin = app.Anonymous(store);
        var login = await admin.Http.GetStringAsync("/admin/account/login?manageTerminals=true");
        Assert.Contains("ManageTerminals", login);
        using var signedIn = await admin.Http.PostAsync("/admin/account/login", Form(login,
            ("UserName", account.Name), ("Password", account.Password), ("ManageTerminals", "true")));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/admin/pos-terminals", signedIn.Headers.Location?.OriginalString);

        var create = await admin.Http.GetStringAsync("/admin/pos-terminals/create");
        using var noToken = await admin.Http.PostAsync("/admin/pos-terminals/create",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Code"] = "FORGED", ["Name"] = "No CSRF token" }));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var created = await admin.Http.PostAsync("/admin/pos-terminals/create", Form(create,
            ("Code", "UI-POS02"), ("Name", "Quầy tạo bằng UI")));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var terminal = await db.POSTerminals.SingleAsync(x => x.Code == "UI-POS02");
        var list = await admin.Http.GetStringAsync("/admin/pos-terminals");
        Assert.Contains("UI-POS02", list);
        var keyPath = $"/admin/pos-terminals/{terminal.Id}/keys/create";
        var keyForm = await admin.Http.GetStringAsync(keyPath);
        using var keyResponse = await admin.Http.PostAsync(keyPath, Form(keyForm,
            ("TerminalId", terminal.Id.ToString()), ("DeviceName", "PC mới")));
        keyResponse.EnsureSuccessStatusCode();
        Assert.True(keyResponse.Headers.CacheControl?.NoStore);
        var keyHtml = await keyResponse.Content.ReadAsStringAsync();
        var device = await db.POSTerminalDevices.SingleAsync(x => x.TerminalId == terminal.Id);
        Assert.Contains(device.DeviceKey, keyHtml);
        list = await admin.Http.GetStringAsync("/admin/pos-terminals");
        Assert.DoesNotContain(device.DeviceKey, list);

        var employeeAccount = await app.AddAccountAsync(store, "*");
        using var employee = app.Anonymous(store);
        var employeeLogin = await employee.Http.GetStringAsync("/admin/account/login");
        using var paired = await employee.Http.PostAsync("/admin/account/login", Form(employeeLogin,
            ("UserName", employeeAccount.Name), ("Password", employeeAccount.Password), ("PairingKey", device.DeviceKey)));
        Assert.Equal(HttpStatusCode.Redirect, paired.StatusCode);
        Assert.Contains(paired.Headers.GetValues("Set-Cookie"), x => x.StartsWith("POS_DEVICE_KEY=" + device.DeviceKey));
        using var denied = await employee.Http.GetAsync("/admin/pos-terminals");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var foreign = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, app.Stores[1]));
        using var foreignKey = await foreign.Http.GetAsync(keyPath);
        Assert.Equal(HttpStatusCode.NotFound, foreignKey.StatusCode);
        using var revoked = await admin.Http.PostAsync($"/admin/pos-terminals/keys/{device.Id}/revoke", Form(list));
        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode);

        using var fresh = app.Anonymous(store);
        var freshLogin = await fresh.Http.GetStringAsync("/admin/account/login");
        using var rejected = await fresh.Http.PostAsync("/admin/account/login", Form(freshLogin,
            ("UserName", employeeAccount.Name), ("Password", employeeAccount.Password), ("PairingKey", device.DeviceKey)));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Contains("validation-summary-errors", await rejected.Content.ReadAsStringAsync());
        Assert.False(rejected.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(x => x.StartsWith("POS_DEVICE_KEY=")));
    }

    private static FormUrlEncodedContent Form(string html, params (string Name, string Value)[] values)
    {
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var fields = values.ToDictionary(x => x.Name, x => x.Value);
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        return new FormUrlEncodedContent(fields);
    }
}
