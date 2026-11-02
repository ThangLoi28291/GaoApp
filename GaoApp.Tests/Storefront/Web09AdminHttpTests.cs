using System.Net;
using System.Text.RegularExpressions;
using GaoApp.Application.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb09"), Trait("Category", "StorefrontWeb09")]
public sealed class Web09AdminHttpTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Actual_settings_HTTP_activation_stop_require_current_auth_CSFR_version_and_never_create_menus_or_money()
    {
        var c = await Web09Case.CreateAsync(fixture); var foreign = await Web09Case.CreateAsync(fixture);
        await using var host = await Web09AdminHost.StartAsync(c); using var anonymous = host.Client(); using var other = host.Client(foreign.Seed.UserId);
        using var authorized = host.Client(c.Seed.UserId); var command = await c.CommandAsync(); var before = await c.Checkout.CountsAsync();
        foreach (var action in new[] { "activate", "stop-new-orders" })
        {
            using var denied = await Form(anonymous, action, command); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using var foreignDenied = await Form(other, action, command); Assert.Equal(HttpStatusCode.Forbidden, foreignDenied.StatusCode);
            using var missingCsrf = await Form(authorized, action, command); Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        }
        Assert.Equal(before, await c.Checkout.CountsAsync());
        var csrf = await Csrf(authorized); var originalVersion = command.Version;
        using (var activate = await Form(authorized, "activate", command, csrf)) Assert.Equal(HttpStatusCode.Redirect, activate.StatusCode);
        await using (var db = c.Context()) Assert.True((await db.StorefrontSettings.SingleAsync()).IsEnabled);
        using (var stale = await Form(authorized, "activate", command, csrf)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        command = await c.CommandAsync("WEB09 actual emergency stop HTTP"); Assert.NotEqual(originalVersion, command.Version);
        using (var stop = await Form(authorized, "stop-new-orders", command, csrf)) Assert.Equal(HttpStatusCode.Redirect, stop.StatusCode);
        await using (var db = c.Context()) Assert.False((await db.StorefrontSettings.SingleAsync()).IsEnabled);
        Assert.Equal(before, await c.Checkout.CountsAsync());
        command = await c.CommandAsync(); await c.RevokeAsync(StorefrontPermissions.ManageSettings);
        using var revoked = await Form(authorized, "activate", command, csrf); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        await using var verify = c.Context(); Assert.False((await verify.StorefrontSettings.SingleAsync()).IsEnabled); Assert.Empty(await verify.AdminMenuItems.ToListAsync());
    }

    [Fact]
    public async Task Operator_opt_out_blocks_actual_enable_POST_but_real_stop_POST_still_succeeds_with_invalid_unrelated_settings()
    {
        var c = await Web09Case.CreateAsync(fixture); await using var deniedHost = await Web09AdminHost.StartAsync(c, activationPolicy: false);
        using var client = deniedHost.Client(c.Seed.UserId); var csrf = await Csrf(client);
        using (var denied = await Form(client, "activate", await c.CommandAsync(), csrf)) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        await c.ActivateAsync();
        await using (var db = c.Context())
        { var settings = await db.StorefrontSettings.SingleAsync(); settings.ContactPhone = "<invalid>"; await db.SaveChangesAsync(); }
        var command = await c.CommandAsync(); await using var beforeDb = c.Context(); var before = await beforeDb.StorefrontSettings.AsNoTracking().SingleAsync();
        using (var stop = await Form(client, "stop-new-orders", command, csrf)) Assert.Equal(HttpStatusCode.Redirect, stop.StatusCode);
        await using var verify = c.Context(); var after = await verify.StorefrontSettings.AsNoTracking().SingleAsync();
        Assert.False(after.IsEnabled); Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(Web09Support.SettingsData(before, false), Web09Support.SettingsData(after, false));
    }

    internal static async Task<string> Csrf(HttpClient client)
    {
        var html = await client.GetStringAsync("/admin/storefront/settings");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "The actual settings view must issue the antiforgery token."); return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static async Task<HttpResponseMessage> Form(HttpClient client, string action, StorefrontSalesControlRequest command, string? csrf = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/storefront/settings/" + action)
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Version"] = command.Version, ["Reason"] = command.Reason }) };
        if (csrf is not null) request.Headers.Add("RequestVerificationToken", csrf); return await client.SendAsync(request);
    }
}
