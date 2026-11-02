using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Assistant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class AssistantSearchHttpTests
{
    [Fact]
    public async Task Real_SQL_cookie_csrf_owner_store_review_and_revocation_protect_search_learning()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var rights = new[] { AssistantPermissions.Use, AssistantPermissions.Chat, PermissionCodes.Catalog.Product.View, PermissionCodes.System.Setting.Update };
        var account = await app.AddAccountAsync(app.Stores[0], rights);
        var otherAccount = await app.AddAccountAsync(app.Stores[1], rights.Where(r => r != PermissionCodes.System.Setting.Update).ToArray());
        using var own = await app.LoginAsync(account); using var other = await app.LoginAsync(otherAccount);
        using var upstream = new HistorySource(account.UserId, account.Store.StoreId); using var rsa = RSA.Create(2048);
        var keyFile = Path.Combine(Path.GetTempPath(), "gao-search-" + Guid.NewGuid().ToString("N") + ".pem");
        await File.WriteAllTextAsync(keyFile, rsa.ExportRSAPrivateKeyPem());
        try {
            await app.ConfigureAssistantAsync(new Dictionary<string,string> { ["Enabled"] = "true", ["BaseUrl"] = upstream.Address,
                ["Issuer"] = "https://search.test", ["KeyId"] = "search-test", ["PrivateKeyFile"] = keyFile });
            string sku;
            await using (var db = app.Database.CreateTenantContext(account.Store.StoreId))
                sku = await db.ProductVariants.Where(v => v.Id == account.Store.VariantId).Select(v => v.Sku).SingleAsync();
            var products = await own.Http.GetFromJsonAsync<JsonElement>("/admin/api/assistant/search/products?query=" + Uri.EscapeDataString(sku) + "&storeId=" + otherAccount.Store.StoreId);
            Assert.Equal(account.Store.StoreId.ToString(), products.GetProperty("scope").GetProperty("storeId").GetString());
            var item = Assert.Single(products.GetProperty("items").EnumerateArray());
            Assert.Equal(account.Store.VariantId.ToString(), item.GetProperty("variantId").GetString());
            Assert.False(item.TryGetProperty("price", out _)); Assert.False(item.TryGetProperty("costPrice", out _));
            const string query = "mặt hàng nhân viên gọi tên riêng";
            var payload = new { requestId = Guid.NewGuid(), conversationId = upstream.Conversation, messageId = "12", query,
                productId = int.Parse(item.GetProperty("productId").GetString()!), variantId = account.Store.VariantId };
            var csrf = own.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single(); own.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
            using (var denied = await own.Http.PostAsJsonAsync("/admin/api/assistant/search/corrections", payload)) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            own.Http.DefaultRequestHeaders.Add("RequestVerificationToken", csrf);
            using (var foreign = await other.Http.PostAsJsonAsync("/admin/api/assistant/search/corrections", payload)) Assert.True(foreign.StatusCode == HttpStatusCode.ServiceUnavailable, await foreign.Content.ReadAsStringAsync());

            JsonElement proposed;
            using (var response = await own.Http.PostAsJsonAsync("/admin/api/assistant/search/corrections", payload)) {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode); proposed = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("pending", proposed.GetProperty("correction").GetProperty("status").GetString());
            }
            using (var response = await own.Http.PostAsJsonAsync("/admin/api/assistant/search/corrections", payload)) {
                var retry = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(proposed.GetProperty("correction").GetProperty("id").GetGuid(), retry.GetProperty("correction").GetProperty("id").GetGuid());
            }
            var before = await own.Http.GetFromJsonAsync<JsonElement>("/admin/api/assistant/search/products?query=" + Uri.EscapeDataString(query));
            Assert.Empty(before.GetProperty("items").EnumerateArray());
            var page = await own.Http.GetStringAsync("/admin/assistant/search");
            Assert.Contains(query, WebUtility.HtmlDecode(page));
            var output = Path.Combine(FullApplicationFixture.SourceRoot(), "Logs", "assistant-search-step-a"); Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "search-page.html"), page);
            Assert.True((await other.Http.GetAsync("/admin/assistant/search")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
            string Field(string html, string name) => Regex.Match(html, "name=\"" + name + "\" value=\"([^\"]+)\"").Groups[1].Value;
            var id = proposed.GetProperty("correction").GetProperty("id").GetGuid();
            async Task<HttpResponseMessage> Review(string version, string decision) => await own.Http.PostAsync("/admin/assistant/search/review", new FormUrlEncodedContent(new Dictionary<string,string> {
                ["id"] = id.ToString(), ["version"] = version, ["decision"] = decision, ["requestId"] = Guid.NewGuid().ToString() }));
            using (var response = await Review(Field(page, "version"), "approve")) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var approved = await own.Http.GetFromJsonAsync<JsonElement>("/admin/api/assistant/search/products?query=" + Uri.EscapeDataString(query));
            Assert.Single(approved.GetProperty("items").EnumerateArray()); Assert.True(approved.GetProperty("search").GetProperty("requiresConfirmation").GetBoolean());
            var exported = await own.Http.GetStringAsync("/admin/assistant/search/catalog.json"); Assert.DoesNotContain("PRIVATE-TRANSCRIPT", exported); Assert.DoesNotContain("costPrice", exported);
            var judged = await own.Http.GetFromJsonAsync<JsonElement>("/admin/assistant/search/approved-cases.json");
            Assert.Equal("tune", Assert.Single(judged.GetProperty("cases").EnumerateArray()).GetProperty("split").GetString());
            Assert.DoesNotContain("PRIVATE-TRANSCRIPT", judged.GetRawText());
            var refreshed = await own.Http.GetStringAsync("/admin/assistant/search");
            using (var response = await Review(Field(refreshed, "version"), "reject")) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var revoked = await own.Http.GetFromJsonAsync<JsonElement>("/admin/api/assistant/search/products?query=" + Uri.EscapeDataString(query)); Assert.Empty(revoked.GetProperty("items").EnumerateArray());
            upstream.Expires = DateTimeOffset.UtcNow.AddMinutes(-1);
            using (var expired = await own.Http.PostAsJsonAsync("/admin/api/assistant/search/corrections", payload)) Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
            upstream.Expires = DateTimeOffset.UtcNow.AddMinutes(5);
            upstream.BeforeReply = async () => {
                await using var db = app.Database.CreateTenantContext(account.Store.StoreId);
                var permission = await db.Permissions.SingleAsync(p => p.Code == PermissionCodes.Catalog.Product.View);
                db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(r => r.RoleId == account.RoleId && r.PermissionId == permission.Id)); await db.SaveChangesAsync();
            };
            using (var denied = await own.Http.PostAsJsonAsync("/admin/api/assistant/search/corrections", new { requestId = Guid.NewGuid(), payload.conversationId, payload.messageId, payload.query, payload.productId, payload.variantId })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        } finally { File.Delete(keyFile); }
    }
    private sealed class HistorySource : IDisposable
    {
        private readonly HttpListener listener = new(); private readonly CancellationTokenSource stop = new(); private readonly Task loop;
        private readonly int owner, store; public string Address { get; } public Guid Conversation { get; } = Guid.NewGuid();
        public DateTimeOffset Expires = DateTimeOffset.UtcNow.AddMinutes(5); public Func<Task>? BeforeReply;
        public HistorySource(int owner, int store) {
            this.owner = owner; this.store = store;
            var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            Address = $"http://127.0.0.1:{port}/"; listener.Prefixes.Add(Address); listener.Start(); loop = Serve();
        }
        private async Task Serve() {
            while (!stop.IsCancellationRequested) {
                HttpListenerContext http; try { http = await listener.GetContextAsync().WaitAsync(stop.Token); } catch { return; }
                try {
                    var token = http.Request.Headers["Authorization"]![7..].Split('.')[1];
                    var payload = JsonDocument.Parse(Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + new string('=', (4-token.Length%4)%4))).RootElement;
                    if (payload.GetProperty("sub").GetString() != owner.ToString() || !http.Request.Url!.AbsolutePath.Contains(Conversation.ToString())) { http.Response.StatusCode = 404; http.Response.Close(); continue; }
                    if (BeforeReply is not null) await BeforeReply();
                    var scope = new { storeId = store.ToString(), warehouseId = (string?)null, legalEntityId = (string?)null };
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(new { contractVersion = "1.0", conversationId = Conversation,
                        items = new[] { new { messageId = "12", role = "assistant", content = "PRIVATE-TRANSCRIPT", scope, expiresAt = Expires } }, nextCursor = (string?)null });
                    http.Response.ContentType = "application/json"; await http.Response.OutputStream.WriteAsync(bytes); http.Response.Close();
                } catch { http.Response.StatusCode = 500; http.Response.Close(); }
            }
        }
        public void Dispose() { stop.Cancel(); listener.Close(); try { loop.GetAwaiter().GetResult(); } catch { } stop.Dispose(); }
    }
}