using GaoApp.Application.DTOs.Security.UserInStores;

namespace GaoApp.Application.Interfaces.Repositories.Security;

public interface IEmployeeIndexReadRepository
{
    Task<EmployeeIndexPageDto> QueryAsync(
        int storeId,
        int currentUserId,
        EmployeeIndexQueryRequest request,
        CancellationToken ct = default);
}
