using System.Net;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class AcbCallbackHealthSqlServerTests
{
    [Fact]
    public async Task Callback_monitor_reads_committed_receipts_across_filters_and_remains_store_and_permission_isolated()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var foreign = app.Stores[1];
        using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store));
        var receiptIds = new Dictionary<int, int>();
        foreach (var target in new[] { store, foreign })
        {
            await using var db = app.Database.CreateTenantContext(target.StoreId);
            var receipt = new AcbCallbackReceipt
            {
                StoreId = target.StoreId, ClientRequestId = Guid.NewGuid().ToString(), Page = 1, TotalPages = 1,
                RequestCode = target.StoreId == store.StoreId ? "TRANSACTION_UPDATE" : "FOREIGN-PRIVATE",
                // The background worker must not consume synthetic test evidence.
                NextAttemptAtUtc = DateTime.UtcNow.AddDays(10)
            };
            db.Add(receipt);
            await db.SaveChangesAsync();
            receiptIds[target.StoreId] = receipt.Id;
        }
        using var status = await admin.Http.GetAsync("/admin/acb/reconciliation/callback-status");
        var html = await status.Content.ReadAsStringAsync();
        Assert.True(status.IsSuccessStatusCode, html);
        Assert.True(status.Headers.CacheControl?.NoStore);
        Assert.Contains($"#{receiptIds[store.StoreId]} ·", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain($"#{receiptIds[foreign.StoreId]} ·", WebUtility.HtmlDecode(html));
        using var page = await admin.Http.GetAsync("/admin/acb/reconciliation?from=2000-01-01&to=2000-01-01&source=TRANSACTION_HISTORY");
        Assert.True(page.IsSuccessStatusCode, await page.Content.ReadAsStringAsync());
        Assert.Contains("acb-callback-health", await page.Content.ReadAsStringAsync());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var receipt = await db.Set<AcbCallbackReceipt>().SingleAsync();
            Assert.Equal(0, receipt.Attempts);
            Assert.Null(receipt.ProcessedAtUtc);
            Assert.Empty(await db.Orders.ToListAsync());
        }
        using var anonymous = app.Anonymous(store);
        using var rejected = await anonymous.Http.GetAsync("/admin/acb/reconciliation/callback-status");
        Assert.False(rejected.IsSuccessStatusCode);
        using var denied = await app.LoginAsync(await app.AddAccountAsync(store));
        using var deniedStatus = await denied.Http.GetAsync("/admin/acb/reconciliation/callback-status");
        Assert.Equal(HttpStatusCode.Forbidden, deniedStatus.StatusCode);
    }
}
