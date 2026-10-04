using System.Net;
using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.WebUtilities;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class POSRequestsPageSqlServerTests
{
    [Fact]
    public async Task Unified_page_preserves_permissions_and_legacy_scoped_links_without_redirecting_json_apis()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var cashier = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Shift.View));
        var html = await cashier.Http.GetStringAsync("/admin/pos-shift/requests");
        Assert.Contains("<h1>Yêu cầu nghiệp vụ</h1>", html);
        Assert.Contains("id=\"operationsCashTab\"", html);
        Assert.Contains("id=\"operationsPaymentTab\"", html);
        Assert.Contains("id=\"cashAdjustments\" class=\"ca-shell\" data-admin=\"false\"", html);
        Assert.Contains("id=\"paymentAdjustments\" class=\"ca-shell\" data-admin=\"false\"", html);

        foreach (var (legacy, tab, key, id) in new[] {
            ("cash-adjustments", "cash", "transactionId", "8"),
            ("payment-adjustments", "payment", "entryId", "31") })
        {
            using var response = await cashier.Http.GetAsync($"/admin/pos-shift/{legacy}?{key}={id}&shiftId=5&requestId=7&status=Approved&tab=wrong&keyword=chi%20ph%C3%AD");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var location = response.Headers.Location!;
            var target = new Uri(cashier.Http.BaseAddress!, location);
            Assert.Equal("/admin/pos-shift/requests", target.AbsolutePath);
            var query = QueryHelpers.ParseQuery(target.Query);
            Assert.Equal(tab, query["tab"].ToString());
            Assert.Equal(id, query[key].ToString());
            Assert.Equal("5", query["shiftId"].ToString());
            Assert.Equal("7", query["requestId"].ToString());
            Assert.Equal("Approved", query["status"].ToString());
            Assert.Equal("chi phí", query["keyword"].ToString());
            Assert.Contains($"data-active-tab=\"{tab}\"", await cashier.Http.GetStringAsync(location));
        }

        foreach (var path in new[] { "cash-adjustments/data", "payment-adjustments/data", "payment-adjustments/deposits" })
        {
            var data = await cashier.JsonAsync(HttpMethod.Get, "/admin/pos-shift/" + path);
            Assert.Equal(0, data.GetProperty("totalItems").GetInt32());
        }
        using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store));
        Assert.Contains("data-admin=\"true\"", await admin.Http.GetStringAsync("/admin/pos-shift/requests?tab=payment"));
        using var restricted = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        using var denied = await restricted.Http.GetAsync("/admin/pos-shift/requests");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
