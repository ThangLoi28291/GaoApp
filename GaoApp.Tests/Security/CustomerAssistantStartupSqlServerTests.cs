using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using GaoApp.Web.Services.CustomerService;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class CustomerAssistantStartupSqlServerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Web_starts_and_restarts_with_customer_service_on_and_off_without_exposing_filter_actions(bool assistantEnabled)
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        await app.ConfigureAssistantAsync(new Dictionary<string, string> { ["Enabled"] = assistantEnabled.ToString() });
        var account = await app.AddAccountAsync(app.Stores[0], CustomerServiceRights.View, CustomerServiceRights.Manage);
        using var admin = await app.LoginAsync(account);
        using var guest = app.Anonymous(app.Stores[0]);

        async Task AssertRunningAsync(bool enabled)
        {
            using var health = await guest.Http.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            var support = await guest.Http.GetStringAsync("/support");
            Assert.Contains($"data-enabled=\"{enabled.ToString().ToLowerInvariant()}\"", support);
            Assert.Equal(enabled, support.Contains("id=\"csStartForm\"", StringComparison.Ordinal));
            Assert.Contains("__RequestVerificationToken", await guest.Http.GetStringAsync("/admin/account/login"));
        }

        async Task SetEnabledAsync(bool enabled)
        {
            var inbox = await admin.Http.GetStringAsync("/admin/customer-service");
            var version = Field(inbox, "version");
            Assert.NotEmpty(version);
            using var saved = await admin.Http.PostAsync("/admin/customer-service/settings", new FormUrlEncodedContent(
                new Dictionary<string, string> { ["enabled"] = enabled.ToString(), ["version"] = version }));
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        }

        async Task AssertSourceStillAuthenticatesAsync()
        {
            var posts = new (string Route, object Body)[]
            {
                ("context/authorize", new { requiredPermissions = new[] { "assistant.use" } }),
                ("catalog/search", new { query = "TEST", limit = 1 }),
                ("catalog/aliases/resolve", new { query = "TEST" }),
                ("quotes/lookup", new { items = Array.Empty<object>(), tiers = new[] { "retail" } }),
                ("knowledge/lookup", new { topics = new[] { "store" } })
            };
            // These calls have neither a login cookie nor CSRF. Each real source route
            // must reject missing/forged JWTs, rather than become public after the fix.
            foreach (var invalidToken in new[] { false, true })
            {
                foreach (var (route, body) in posts)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customer-assistant/" + route)
                        { Content = JsonContent.Create(body) };
                    if (invalidToken) request.Headers.Authorization = new("Bearer", "invalid-b10-test-token");
                    request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString());
                    using var response = await guest.Http.SendAsync(request);
                    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                }
                using var lookup = new HttpRequestMessage(HttpMethod.Get,
                    "/api/v1/customer-assistant/catalog/variants/" + app.Stores[0].VariantId);
                if (invalidToken) lookup.Headers.Authorization = new("Bearer", "invalid-b10-test-token");
                lookup.Headers.Add("X-Request-Id", Guid.NewGuid().ToString());
                using var variant = await guest.Http.SendAsync(lookup);
                Assert.Equal(HttpStatusCode.Unauthorized, variant.StatusCode);
            }
            foreach (var path in new[] { "/api/v1/customer-assistant/OnActionExecutionAsync", "/CustomerAssistantSource/OnActionExecutionAsync" })
            {
                using var helper = await guest.Http.PostAsJsonAsync(path, new { });
                Assert.Equal(HttpStatusCode.NotFound, helper.StatusCode);
            }
        }

        await AssertRunningAsync(enabled: false);
        await AssertSourceStillAuthenticatesAsync();
        await SetEnabledAsync(true);
        await app.RestartAsync(enableDeliveryOutbox: false, enableAuditOutbox: false);
        await AssertRunningAsync(enabled: true);
        await AssertSourceStillAuthenticatesAsync();

        using (var withoutCsrf = await guest.Http.PostAsJsonAsync("/support/conversations",
                   new { requestId = Guid.NewGuid(), name = "B10 synthetic guest", consent = true }))
            Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);

        await SetEnabledAsync(false);
        await app.RestartAsync(enableDeliveryOutbox: false, enableAuditOutbox: false);
        await AssertRunningAsync(enabled: false);
        await AssertSourceStillAuthenticatesAsync();
    }

    private static string Field(string html, string name) => WebUtility.HtmlDecode(
        Regex.Match(html, "name=\"" + Regex.Escape(name) + "\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
}
