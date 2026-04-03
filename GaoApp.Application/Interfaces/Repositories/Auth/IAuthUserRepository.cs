using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Auth;

public interface IAuthUserRepository
{
    Task<User?> GetByUserNameAsync(string userName, CancellationToken ct = default);
    Task<UserInStore?> GetUserInStoreAsync(int userId, int storeId, CancellationToken ct = default);
}