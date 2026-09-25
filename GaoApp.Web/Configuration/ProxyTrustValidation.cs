using System.Net;

namespace GaoApp.Web.Configuration;

public static class ProxyTrustValidation
{
    public static void Validate(ProxyOptions options)
    {
        if (!options.EnableForwardedHeaders) return;
        var proxies = options.KnownProxies ?? [];
        var networks = options.KnownNetworks ?? [];
        if (proxies.Count + networks.Count == 0)
            throw new InvalidOperationException("Proxy: enabled forwarded headers require an explicit trusted proxy or network.");
        foreach (var proxy in proxies)
            if (!IPAddress.TryParse(proxy, out var ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
                throw new InvalidOperationException("Proxy:KnownProxies contains an invalid or unspecified address.");
        foreach (var network in networks)
        {
            var parts = network?.Split('/');
            if (parts is null || parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || !int.TryParse(parts[1], out var prefix) ||
                prefix <= 0 || prefix > (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128))
                throw new InvalidOperationException("Proxy:KnownNetworks contains an invalid CIDR or trusts the entire Internet.");
        }
    }
}
