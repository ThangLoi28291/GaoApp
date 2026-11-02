using GaoApp.Application.Common;
using GaoApp.Infrastructure.Tenant;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace GaoApp.Web.Common.POS;

public class POSRuntimeContextAccessor : IPOSRuntimeContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenant;

    public POSRuntimeContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        ITenantContext tenant)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenant = tenant;
    }

    private HttpContext? HttpContext => _httpContextAccessor.HttpContext;
    private ClaimsPrincipal? User => HttpContext?.User;

    public int? StoreId => _tenant.StoreId;

    public string? StoreName
        => GetStringFromItems("CurrentStoreName")
           ?? User?.FindFirst("store_name")?.Value;

    public int? TerminalId
        => User?.Identity?.IsAuthenticated == true
            ? GetIntClaim("terminal_id")
            : GetIntFromItems("CurrentTerminalId");

    public string? TerminalName
        => TerminalValue("CurrentTerminalName", "terminal_name");

    public string? TerminalCode
        => TerminalValue("CurrentTerminalCode", "terminal_code");

    private string? TerminalValue(string item, string claim)
    {
        if (User?.Identity?.IsAuthenticated != true) return GetStringFromItems(item);
        return GetIntClaim("terminal_id") is > 0 && GetIntClaim("terminal_id") == GetIntFromItems("CurrentTerminalId")
            ? GetStringFromItems(item) ?? User.FindFirst(claim)?.Value
            : User.FindFirst(claim)?.Value;
    }

    public int? UserId
        => GetIntClaim(ClaimTypes.NameIdentifier);

    public string? UserName
        => User?.FindFirst(ClaimTypes.Name)?.Value
           ?? GetStringFromItems("CurrentUserName");

    private int? GetIntClaim(string claimType)
    {
        var raw = User?.FindFirst(claimType)?.Value;
        return int.TryParse(raw, out var value) ? value : null;
    }

    private string? GetStringFromItems(string key)
    {
        if (HttpContext?.Items.TryGetValue(key, out var obj) == true)
        {
            return obj?.ToString();
        }

        return null;
    }

    private int? GetIntFromItems(string key)
    {
        var raw = GetStringFromItems(key);
        return int.TryParse(raw, out var value) ? value : null;
    }
}
