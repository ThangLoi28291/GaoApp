using GaoApp.Application.DTOs.Security.UserInStores;

namespace GaoApp.Application.Interfaces.Services.Security;

/// <summary>
/// Service use-case cho UI quản lý UserInStore.
/// </summary>
public interface IUserInStoreAdminService
{
    /// <summary>
    /// Lấy danh sách user thuộc store hiện tại.
    /// </summary>
    Task<UserInStoreIndexVm> GetPagedAsync(int storeId, UserInStoreIndexQueryDto query, CancellationToken ct = default);

    /// <summary>
    /// Lấy dữ liệu cho màn edit mapping UserInStore.
    /// </summary>
    Task<UserInStoreEditDto?> GetByIdAsync(int storeId, int id, CancellationToken ct = default);

    /// <summary>
    /// Tạo mới mapping user-store-role.
    /// </summary>
    Task<(bool Success, string? ErrorMessage, int? Id)> CreateAsync(
        int storeId,
        int? actorUserId,
        CreateUserInStoreRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Cập nhật mapping user-store-role.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> UpdateAsync(
        int storeId,
        int? actorUserId,
        UpdateUserInStoreRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Xóa mềm / xóa mapping user-store-role.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> DeleteAsync(
        int storeId,
        int id,
        int? actorUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Lookup user để gán vào store.
    /// </summary>
    Task<List<UserLookupItemDto>> SearchUsersAsync(
        string? keyword,
        int maxResults = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Lookup role của store hiện tại.
    /// </summary>
    Task<List<RoleLookupItemDto>> GetRoleLookupAsync(
        int storeId,
        bool onlyActive = true,
        CancellationToken ct = default);
}