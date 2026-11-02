namespace GaoApp.Infrastructure.Tenant;

// Trusted, isolated fixture setup. Production routing deliberately does not make this grant.
internal static class TenantTestAccess
{
    internal static void SetHostAdminForTest(this TenantContext tenant)
    {
        tenant.SetHostAdmin();
        tenant.SetAuthenticatedHostAdmin(true);
    }
}
