namespace GaoApp.Application.Common;

public interface ITenantContextWriter
{
    void SetHostAdmin();
    void SetStore(int storeId, string subdomain);
    void Clear();
    void SetAuthenticatedHostAdmin(bool authorized) => throw new InvalidOperationException("Host data access is not supported by this tenant context.");
}
