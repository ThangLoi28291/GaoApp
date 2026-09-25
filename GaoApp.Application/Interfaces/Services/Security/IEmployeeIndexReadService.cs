using GaoApp.Application.DTOs.Security.UserInStores;

namespace GaoApp.Application.Interfaces.Services.Security;

public interface IEmployeeIndexReadService
{
    Task<EmployeeIndexPageDto> GetPageAsync(
        int storeId,
        int currentUserId,
        EmployeeIndexQueryRequest request,
        CancellationToken ct = default);
}
