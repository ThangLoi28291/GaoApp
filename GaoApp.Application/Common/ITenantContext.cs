namespace GaoApp.Application.Common;

public interface ITenantContext
{
    int? StoreId { get; }
    bool IsHostAdmin { get; }
    string? Subdomain { get; }
    // Routing to the admin host is not a data-access grant.
    bool CanReadAllStores => false;
    bool CanWriteAllStores => false;
}
