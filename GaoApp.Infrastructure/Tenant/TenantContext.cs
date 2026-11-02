using GaoApp.Application.Common;

namespace GaoApp.Infrastructure.Tenant;

public sealed class TenantContext : ITenantContext, ITenantContextWriter
{
    public int? StoreId { get; private set; }
    public bool IsHostAdmin { get; private set; }
    public string? Subdomain { get; private set; }
    private bool authenticatedHostAdmin;
    private SystemScope? systemScope;
    public bool CanReadAllStores => authenticatedHostAdmin || systemScope is not null;
    public bool CanWriteAllStores => authenticatedHostAdmin || systemScope?.Purpose == SystemDataAccessPurpose.Provisioning;

    public void SetHostAdmin()
    {
        RequireNoSystemScope();
        authenticatedHostAdmin = false;
        IsHostAdmin = true;
        Subdomain = "admin";
        StoreId = null;
    }

    public void SetStore(int storeId, string subdomain)
    {
        if (storeId <= 0) throw new ArgumentOutOfRangeException(nameof(storeId));
        if (string.IsNullOrWhiteSpace(subdomain)) throw new ArgumentException("A store subdomain is required.", nameof(subdomain));
        RequireNoSystemScope();
        authenticatedHostAdmin = false;
        IsHostAdmin = false;
        Subdomain = subdomain;
        StoreId = storeId;
    }

    public void Clear()
    {
        RequireNoSystemScope();
        authenticatedHostAdmin = false;
        IsHostAdmin = false;
        Subdomain = null;
        StoreId = null;
    }

    public void SetAuthenticatedHostAdmin(bool authorized)
    {
        if (authorized && (!IsHostAdmin || StoreId is not null))
            throw new InvalidOperationException("Host data access requires the host administration context.");
        authenticatedHostAdmin = authorized;
    }

    /// <summary>Trusted server entry points only. Read-only unless explicitly provisioning.</summary>
    public IDisposable BeginSystemAccess(SystemDataAccessPurpose purpose)
    {
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        var scope = new SystemScope(this, purpose, systemScope);
        systemScope = scope;
        return scope;
    }

    private void RequireNoSystemScope()
    {
        if (systemScope is not null) throw new InvalidOperationException("Dispose the system data scope before changing the tenant.");
    }

    private sealed class SystemScope(TenantContext owner, SystemDataAccessPurpose purpose, SystemScope? previous) : IDisposable
    {
        public SystemDataAccessPurpose Purpose { get; } = purpose;
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            if (!ReferenceEquals(owner.systemScope, this)) throw new InvalidOperationException("System data scopes must be disposed in reverse order.");
            owner.systemScope = previous;
            disposed = true;
        }
    }
}

public enum SystemDataAccessPurpose
{
    Provisioning,
    BankCallbackDiscovery,
    DeliveryOutboxDiscovery,
    RealtimeSessionValidation,
    AuditOutboxDiscovery,
    CustomerMarketingCampaignDiscovery
}
