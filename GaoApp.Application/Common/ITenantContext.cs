namespace GaoApp.Application.Common;

public interface ITenantContext
{
    int? StoreId { get; }
    bool IsHostAdmin { get; }
    string? Subdomain { get; }
}
