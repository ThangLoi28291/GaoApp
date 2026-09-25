using GaoApp.Application.DTOs.Security.Roles;

namespace GaoApp.Application.Interfaces.Services.Security;

public interface IRoleIndexReadService
{
    Task<RoleIndexPageDto> GetPageAsync(
        int storeId,
        RoleIndexQueryRequest request,
        CancellationToken ct = default);
}
