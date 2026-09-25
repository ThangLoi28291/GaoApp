using GaoApp.Application.DTOs.Security.Roles;

namespace GaoApp.Application.Interfaces.Repositories.Security;

public interface IRoleIndexReadRepository
{
    Task<RoleIndexPageDto> QueryAsync(
        int storeId,
        RoleIndexQueryRequest request,
        CancellationToken ct = default);
}
