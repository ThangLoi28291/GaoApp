using System.Net;
using System.Net.Http.Headers;
using GaoApp.Application.Common.Security;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Media;

[Collection("SqlServerConcurrency")]
public sealed class MediaLibraryHttpTests
{
    [Fact]
    public async Task Real_media_api_upload_list_detail_policy_preview_cancel_cleanup_enforce_tenant_permissions_and_csrf()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Catalog.Product.View));
        using var outsider = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using var denied = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        using var body = new MultipartFormDataContent();
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j0ZkAAAAASUVORK5CYII=");
        var image = new ByteArrayContent(bytes); image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        body.Add(image, "file", "media-http-test.png");
        using var uploaded = await manager.Http.PostAsync("/admin/media/temp", body);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var token = await uploaded.Content.ReadAsStringAsync();
        int id;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            id = await db.MediaAssets.Where(x => x.TempToken == token).Select(x => x.Id).SingleAsync();
        const string root = "/admin/media-library";
        var data = await manager.JsonAsync(HttpMethod.Get, root + "/data?search=media-http&statusFilter=temp&page=999");
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(id, item.GetProperty("id").GetInt32());
        Assert.Equal("media-http-test.png", item.GetProperty("name").GetString());
        Assert.Equal(bytes.Length, item.GetProperty("sizeBytes").GetInt64());
        Assert.Equal("temp", item.GetProperty("status").GetString());
        Assert.Equal($"{root}/{id}/preview", item.GetProperty("previewUrl").GetString());
        Assert.EndsWith("Z", item.GetProperty("createdAtUtc").GetString());
        Assert.EndsWith("Z", item.GetProperty("expireAtUtc").GetString());
        Assert.False(item.TryGetProperty("storagePath", out _));
        Assert.False(item.TryGetProperty("tempToken", out _));
        Assert.Empty(item.GetProperty("products").EnumerateArray());
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(24, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, data.GetProperty("filteredCount").GetInt32());
        Assert.Equal(1, data.GetProperty("summary").GetProperty("total").GetInt32());
        Assert.True(data.GetProperty("canManage").GetBoolean());
        var csrf = data.GetProperty("requestVerificationToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(csrf));
        var detail = await viewer.JsonAsync(HttpMethod.Get, $"{root}/{id}");
        Assert.Equal(item.GetRawText(), detail.GetRawText());
        Assert.False((await viewer.JsonAsync(HttpMethod.Get, root + "/data")).GetProperty("canManage").GetBoolean());
        Assert.Empty((await outsider.JsonAsync(HttpMethod.Get, root + "/data")).GetProperty("items").EnumerateArray());
        using (var response = await outsider.Http.GetAsync($"{root}/{id}")) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        foreach (var route in new[] { "/data", $"/{id}", "/policy" })
        {
            using var response = await denied.Http.GetAsync(root + route);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        var policy = await viewer.JsonAsync(HttpMethod.Get, root + "/policy");
        Assert.Equal(6, policy.GetProperty("options").GetProperty("tempLifetimeHours").GetInt32());
        Assert.Equal(7, policy.GetProperty("options").GetProperty("unusedRetentionDays").GetInt32());
        var empty = await manager.JsonAsync(HttpMethod.Get, root + "/data?search=no-match&statusFilter=invalid&page=-1");
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal("all", empty.GetProperty("status").GetString());
        Assert.Equal(1, empty.GetProperty("page").GetInt32());
        Assert.Equal(1, empty.GetProperty("summary").GetProperty("total").GetInt32());
        var html = await manager.Http.GetStringAsync(root);
        Assert.Contains("media-http-test.png", html); Assert.Contains("data-cleanup-all", html);
        Assert.DoesNotContain("data-cleanup-all", await viewer.Http.GetStringAsync(root));
        Assert.DoesNotContain("media-http-test.png", await outsider.Http.GetStringAsync(root));
        using (var response = await denied.Http.GetAsync(root)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await viewer.Http.PostAsync(root + "/cleanup", null)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await outsider.Http.GetAsync($"{root}/{id}/preview")) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await outsider.Http.PostAsync($"{root}/{id}/cleanup", null)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await outsider.Http.PostAsync($"{root}/{id}/cancel-temp", null)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await viewer.Http.GetAsync($"{root}/{id}/preview"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        }
        manager.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var response = await manager.Http.PostAsync(root + "/cleanup", null)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        manager.Http.DefaultRequestHeaders.Add("RequestVerificationToken", csrf);
        using (var response = await manager.Http.PostAsync($"{root}/{id}/cancel-temp", null)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await manager.JsonAsync(HttpMethod.Post, $"{root}/{id}/cleanup");
        Assert.Equal("Deleted", result.GetProperty("outcome").GetString());
        using (var response = await manager.Http.GetAsync($"{root}/{id}/preview")) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("media-http-test.png", await manager.Http.GetStringAsync(root));
        Assert.Contains("media-http-test.png", await manager.Http.GetStringAsync(root + "?statusFilter=deleted"));
        Assert.Empty((await manager.JsonAsync(HttpMethod.Get, root + "/data")).GetProperty("items").EnumerateArray());
        var deleted = await manager.JsonAsync(HttpMethod.Get, $"{root}/{id}");
        Assert.Equal("deleted", deleted.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, deleted.GetProperty("previewUrl").ValueKind);
        Assert.Single((await manager.JsonAsync(HttpMethod.Get, root + "/data?statusFilter=deleted")).GetProperty("items").EnumerateArray());
        var sweep = await manager.JsonAsync(HttpMethod.Post, root + "/cleanup?afterId=0");
        Assert.Equal(0, sweep.GetProperty("result").GetProperty("scanned").GetInt32());
        Assert.False(sweep.GetProperty("hasMore").GetBoolean());
        Assert.Equal(0, (await manager.JsonAsync(HttpMethod.Get, root + "/policy")).GetProperty("lastRun").GetProperty("result").GetProperty("scanned").GetInt32());
    }
}
