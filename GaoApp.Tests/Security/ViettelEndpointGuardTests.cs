using System.Net;
using GaoApp.Infrastructure.Security;
using GaoApp.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GaoApp.Tests.Security;

public sealed class ViettelEndpointGuardTests
{
    [Fact]
    public void All_seven_registered_viettel_clients_have_destination_guard_and_cannot_follow_redirects()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddInfrastructure(config);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        foreach (var name in new[] { "IViettelInvoiceAuthClient", "IViettelInvoicePreviewClient", "IViettelInvoiceIssueClient",
                     "IViettelOfficialFileClient", "IViettelInvoiceLookupClient", "IViettelInvoiceEmailClient", "IViettelInvoiceListClient" })
        {
            var handler = factory.CreateHandler(name);
            var guards = 0;
            while (handler is DelegatingHandler delegating)
            {
                if (handler is ViettelEndpointGuardHandler) guards++;
                handler = delegating.InnerHandler!;
            }
            Assert.Equal(1, guards);
            Assert.False(Assert.IsType<HttpClientHandler>(handler).AllowAutoRedirect);
        }
    }

    [Theory]
    [InlineData("http://api-vinvoice.viettel.vn/api")]
    [InlineData("https://127.0.0.1/api")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("https://localhost/api")]
    [InlineData("https://api-vinvoice.viettel.vn.attacker.test/api")]
    [InlineData("https://api-vinvoice.viettel.vn@attacker.test/api")]
    [InlineData("https://user:password@api-vinvoice.viettel.vn/api")]
    [InlineData("https://api-vinvoice.viettel.vn:444/api")]
    [InlineData("https://api-vinvoice.viettel.vn/api#fragment")]
    public async Task Unsafe_or_unapproved_destination_is_rejected_before_any_network_request(string url)
    {
        var transport = new Recorder();
        using var client = new HttpClient(new ViettelEndpointGuardHandler(new ConfigurationBuilder().Build()) { InnerHandler = transport });
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url));
        Assert.Equal(0, transport.Calls);
    }

    [Theory]
    [InlineData("https://api-vinvoice.viettel.vn/services/einvoiceapplication/api/auth/login")]
    [InlineData("https://api-sinvoice.viettel.vn/InvoiceAPI/InvoiceWS/search")]
    [InlineData("https://demo-sinvoice.viettel.vn:8443/InvoiceAPI/InvoiceWS/search")]
    public async Task Approved_provider_origin_reaches_transport_once(string url)
    {
        var transport = new Recorder();
        using var client = new HttpClient(new ViettelEndpointGuardHandler(new ConfigurationBuilder().Build()) { InnerHandler = transport });
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task Additional_origin_requires_explicit_host_configuration_and_exact_origin()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Security:Viettel:AdditionalAllowedOrigins:0"] = "https://dedicated-provider.example.test:8443" }).Build();
        var transport = new Recorder();
        using var client = new HttpClient(new ViettelEndpointGuardHandler(config) { InnerHandler = transport });
        using var result = await client.GetAsync("https://dedicated-provider.example.test:8443/api");
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://dedicated-provider.example.test/api"));
        Assert.Equal(1, transport.Calls);
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
