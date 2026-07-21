namespace GaoApp.Application.Common;

public interface ITenantContextWriter
{
    void SetHostAdmin();
    void SetStore(int storeId, string subdomain);
    void Clear();
}
