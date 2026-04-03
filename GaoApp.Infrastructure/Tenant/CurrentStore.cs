using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;

namespace GaoApp.Infrastructure.Tenant;

public sealed class CurrentStore : ICurrentStore
{
    private readonly ITenantContext _tenant;
    public CurrentStore(ITenantContext tenant) => _tenant = tenant;

    public int StoreId => _tenant.StoreId
        ?? throw new InvalidOperationException("StoreId=null: đang ở host/admin hoặc thiếu TenantResolutionMiddleware.");
}
