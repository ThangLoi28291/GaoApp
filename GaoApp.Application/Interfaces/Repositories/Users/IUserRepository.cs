using GaoApp.Application.DTOs.Security.UserInStores;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Users;

public interface IUserRepository
{
    Task<User?> GetByUserNameAsync(string userName, CancellationToken ct = default);
    Task<User?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<bool> HasOtherStoreMembershipAsync(int userId, int storeId, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<List<User>> SearchForSecurityAssignmentAsync(
    string? keyword,
    int maxResults = 20,
    CancellationToken ct = default);
    Task<List<User>> GetByIdsAsync(List<int> ids, CancellationToken ct = default);
    Task<bool> ExistsByUserNameAsync(
    string userName,
    CancellationToken ct = default);

}
