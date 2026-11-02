using GaoApp.Infrastructure.Tenant;

namespace GaoApp.Tests.Security;

public sealed class TenantDataScopeTests
{
    [Fact]
    public void Missing_context_and_admin_routing_do_not_grant_cross_store_access()
    {
        var tenant = new TenantContext();
        Assert.Null(tenant.StoreId); Assert.False(tenant.CanReadAllStores); Assert.False(tenant.CanWriteAllStores);
        tenant.SetHostAdmin();
        Assert.True(tenant.IsHostAdmin); Assert.False(tenant.CanReadAllStores); Assert.False(tenant.CanWriteAllStores);
        tenant.SetAuthenticatedHostAdmin(true);
        Assert.True(tenant.CanReadAllStores); Assert.True(tenant.CanWriteAllStores);
        tenant.Clear(); Assert.False(tenant.CanReadAllStores); Assert.False(tenant.CanWriteAllStores);
    }

    [Fact]
    public void Host_grant_is_cleared_on_store_selection_and_cannot_be_enabled_in_a_store_context()
    {
        var tenant = new TenantContext(); tenant.SetHostAdmin(); tenant.SetAuthenticatedHostAdmin(true);
        tenant.SetStore(1, "one");
        Assert.False(tenant.CanReadAllStores); Assert.False(tenant.CanWriteAllStores);
        Assert.Throws<InvalidOperationException>(() => tenant.SetAuthenticatedHostAdmin(true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_store_ids_are_rejected_without_changing_the_current_context(int store)
    {
        var tenant = new TenantContext(); tenant.SetStore(1, "one");
        Assert.Throws<ArgumentOutOfRangeException>(() => tenant.SetStore(store, "invalid"));
        Assert.Equal(1, tenant.StoreId);
    }

    [Theory]
    [InlineData(SystemDataAccessPurpose.BankCallbackDiscovery)]
    [InlineData(SystemDataAccessPurpose.DeliveryOutboxDiscovery)]
    [InlineData(SystemDataAccessPurpose.RealtimeSessionValidation)]
    [InlineData(SystemDataAccessPurpose.CustomerMarketingCampaignDiscovery)]
    public void Discovery_scopes_are_read_only_and_restore_the_previous_access(SystemDataAccessPurpose purpose)
    {
        var tenant = new TenantContext();
        using (tenant.BeginSystemAccess(purpose))
        {
            Assert.True(tenant.CanReadAllStores); Assert.False(tenant.CanWriteAllStores);
            Assert.Throws<InvalidOperationException>(() => tenant.SetStore(2, "two"));
            Assert.Throws<InvalidOperationException>(tenant.Clear);
        }
        Assert.False(tenant.CanReadAllStores); Assert.False(tenant.CanWriteAllStores);
    }

    [Fact]
    public void Nested_scopes_restore_access_and_cannot_be_disposed_out_of_order()
    {
        var tenant = new TenantContext();
        var outer = tenant.BeginSystemAccess(SystemDataAccessPurpose.Provisioning);
        var inner = tenant.BeginSystemAccess(SystemDataAccessPurpose.BankCallbackDiscovery);
        Assert.True(tenant.CanReadAllStores); Assert.False(tenant.CanWriteAllStores);
        Assert.Throws<InvalidOperationException>(outer.Dispose);
        inner.Dispose(); Assert.True(tenant.CanWriteAllStores);
        outer.Dispose(); outer.Dispose(); Assert.False(tenant.CanReadAllStores);
    }

    [Fact]
    public async Task Missing_scope_is_a_safe_context_error_instead_of_an_offline_retryable_server_failure()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GaoApp.Web.Middlewares.GlobalExceptionMiddleware(_ => throw new GaoApp.Infrastructure.Data.TenantDataScopeException(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GaoApp.Web.Middlewares.GlobalExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.Equal(409, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var response = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("TENANT_CONTEXT_REQUIRED", response.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, response.RootElement.GetProperty("detail").ValueKind);
    }
}
