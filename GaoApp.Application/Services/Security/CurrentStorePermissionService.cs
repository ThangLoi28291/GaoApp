using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Application.Interfaces.Services.Security;

namespace GaoApp.Application.Services.Security;

/// <summary>
/// Service kiểm tra quyền hiệu lực của user trong một store cụ thể.
/// Đọc quyền hiện hành để việc thu hồi có hiệu lực ngay ở request tiếp theo.
///
/// Giai đoạn A.1:
/// - Hỗ trợ permission code chuẩn mới
/// - Hỗ trợ backward compatibility với permission cũ
/// - Không phá logic repository/cache hiện tại
/// </summary>
public class CurrentStorePermissionService : ICurrentStorePermissionService
{
    private readonly IUserInStoreRepository _userInStoreRepository;

    public CurrentStorePermissionService(
        IUserInStoreRepository userInStoreRepository)
    {
        _userInStoreRepository = userInStoreRepository;
    }

    public async Task<bool> HasPermissionAsync(
        int storeId,
        int userId,
        string permissionCode,
        CancellationToken ct = default)
    {
        if (storeId <= 0 || userId <= 0 || string.IsNullOrWhiteSpace(permissionCode))
            return false;

        // Lấy toàn bộ permission hiệu lực hiện tại của user trong store.
        var permissions = await GetPermissionsAsync(storeId, userId, ct);

        if (permissions.Count == 0)
            return false;

        // Lấy tất cả code được coi là "hợp lệ" cho permission đang kiểm tra:
        // - code chuẩn hiện tại
        // - alias cũ
        // - manage code tương ứng (nếu là CRUD)
        var acceptedCodes = PermissionAliasMap.GetAcceptedCodes(permissionCode);

        return permissions.Any(x => acceptedCodes.Contains(x, StringComparer.OrdinalIgnoreCase));
    }

    public async Task<List<string>> GetPermissionsAsync(
        int storeId,
        int userId,
        CancellationToken ct = default)
    {
        if (storeId <= 0 || userId <= 0)
            return new List<string>();

        // A reset/role change must not regain old permissions through a cache
        // warmed by the previous session. Keep authorization current across instances.
        var permissions = await _userInStoreRepository
            .GetEffectivePermissionCodesAsync(storeId, userId, ct);

        // Làm sạch dữ liệu trả về:
        // - bỏ chuỗi rỗng
        // - trim
        // - distinct không phân biệt hoa thường
        return permissions?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<string>();
    }
}
