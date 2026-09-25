using System.Net;
using GaoApp.Web.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Security;

public sealed class ProxyTrustTests
{
    [Fact]
    public void Enabled_proxy_without_trust_fails_including_null_lists()
    {
        Assert.Throws<InvalidOperationException>(() => ProxyTrustValidation.Validate(new ProxyOptions { EnableForwardedHeaders = true }));
        Assert.Throws<InvalidOperationException>(() => ProxyTrustValidation.Validate(new ProxyOptions { EnableForwardedHeaders = true, KnownNetworks = null!, KnownProxies = null! }));
        ProxyTrustValidation.Validate(new ProxyOptions { EnableForwardedHeaders = false });
    }
    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    [InlineData("10.0.0.0/33")]
    [InlineData("invalid")]
    public void Unsafe_or_invalid_networks_fail(string network)
        => Assert.Throws<InvalidOperationException>(() => ProxyTrustValidation.Validate(new ProxyOptions { EnableForwardedHeaders = true, KnownNetworks = [network] }));

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("192.0.2.77", false)]
    public async Task Only_trusted_peer_can_change_client_address_scheme_and_host(string peer, bool trusted)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Proxy:EnableForwardedHeaders"] = "true", ["Proxy:KnownProxies:0"] = "127.0.0.1" }).Build();
        var services = new ServiceCollection(); services.AddGaoAppForwardedHeaders(config);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>();
        var context = new DefaultHttpContext(); context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Scheme = "http"; context.Request.Host = new HostString("original.example");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "store.example";
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, options);
        await middleware.Invoke(context);
        Assert.Equal(trusted ? "203.0.113.9" : peer, context.Connection.RemoteIpAddress!.ToString());
        Assert.Equal(trusted ? "https" : "http", context.Request.Scheme);
        Assert.Equal(trusted ? "store.example" : "original.example", context.Request.Host.Value);
    }
}
