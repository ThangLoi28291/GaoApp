namespace GaoApp.Tests.Tenant;

public class FakeTenantContext
{
    public int? StoreId { get; set; }
    public string? Subdomain { get; set; }
    public bool IsHostAdmin { get; set; }
}