using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Security;

public interface IPermissionRepository
{
    /// <summary>
    /// Lấy toàn bộ permission đang hoạt động / khả dụng trong hệ thống.
    /// </summary>
    Task<List<Permission>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Lấy các permission theo danh sách id.
    /// </summary>
    Task<List<Permission>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra toàn bộ permission ids gửi lên có hợp lệ hay không.
    /// </summary>
    Task<int> CountByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default);
}