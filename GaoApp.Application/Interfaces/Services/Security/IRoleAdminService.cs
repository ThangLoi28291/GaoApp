using GaoApp.Application.DTOs.Security.Roles;
using GaoApp.Application.DTOs.Security.UserInStores;

namespace GaoApp.Application.Interfaces.Services.Security;

/// <summary>
/// Service use-case cho UI quản lý Role trong admin.
/// Chỉ thao tác trên role của store hiện tại.
/// </summary>
public interface IRoleAdminService
{
    /// <summary>
    /// Lấy dữ liệu màn danh sách role theo store hiện tại.
    /// </summary>
    Task<RoleIndexVm> GetPagedAsync(int storeId, RoleIndexQueryDto query, CancellationToken ct = default);

    /// <summary>
    /// Lấy dữ liệu edit role.
    /// </summary>
    Task<RoleEditDto?> GetByIdAsync(int storeId, int roleId, CancellationToken ct = default);

    /// <summary>
    /// Tạo mới role cho store hiện tại.
    /// Trả về Id role mới tạo.
    /// </summary>
    Task<int> CreateAsync(int storeId, int? actorUserId, RoleCreateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Cập nhật role thuộc store hiện tại.
    /// </summary>
    Task<bool> UpdateAsync(int storeId, int? actorUserId, RoleUpdateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Xóa mềm / vô hiệu hóa role nếu hợp lệ.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> DeleteAsync(int storeId, int roleId, int? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Danh sách role dùng cho dropdown ở store hiện tại.
    /// </summary>
    Task<List<RoleLookupItemDto>> GetLookupAsync(int storeId, bool onlyActive = true, CancellationToken ct = default);
}