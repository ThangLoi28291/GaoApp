using GaoApp.Application.Common;

namespace GaoApp.Infrastructure.Tenant;

public sealed class TenantContext : ITenantContext, ITenantContextWriter
{
    public int? StoreId { get; private set; }
    public bool IsHostAdmin { get; private set; }
    public string? Subdomain { get; private set; }

    public void SetHostAdmin()
    {
        IsHostAdmin = true;
        Subdomain = "admin";
        StoreId = null;
    }

    public void SetStore(int storeId, string subdomain)
    {
        IsHostAdmin = false;
        Subdomain = subdomain;
        StoreId = storeId;
    }

    public void Clear()
    {
        IsHostAdmin = false;
        Subdomain = null;
        StoreId = null;
    }
}
