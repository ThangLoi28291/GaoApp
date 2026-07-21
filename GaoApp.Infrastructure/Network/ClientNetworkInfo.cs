using GaoApp.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Net;

namespace GaoApp.Infrastructure.Network;

public sealed class ClientNetworkInfo : IClientNetworkInfo
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClientNetworkInfo(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetClientIp()
    {
        var http = _httpContextAccessor.HttpContext;
        if (http == null) return null;

        // Ưu tiên header proxy nếu có reverse proxy
        var forwardedFor = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var firstIp = forwardedFor.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(firstIp))
                return NormalizeIp(firstIp);
        }

        var realIp = http.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(realIp))
            return NormalizeIp(realIp);

        var remoteIp = http.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(remoteIp))
            return null;

        return NormalizeIp(remoteIp);
    }

    private static string NormalizeIp(string ip)
    {
        if (ip == "::1") return "127.0.0.1";

        if (IPAddress.TryParse(ip, out var parsed))
        {
            if (parsed.IsIPv4MappedToIPv6)
                return parsed.MapToIPv4().ToString();

            return parsed.ToString();
        }

        return ip;
    }
}