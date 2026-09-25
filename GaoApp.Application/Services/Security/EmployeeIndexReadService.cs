using GaoApp.Application.DTOs.Security.UserInStores;
using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Application.Interfaces.Services.Security;

namespace GaoApp.Application.Services.Security;

public sealed class EmployeeIndexReadService : IEmployeeIndexReadService
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];

    private readonly IEmployeeIndexReadRepository _repository;

    public EmployeeIndexReadService(IEmployeeIndexReadRepository repository)
    {
        _repository = repository;
    }

    public Task<EmployeeIndexPageDto> GetPageAsync(
        int storeId,
        int currentUserId,
        EmployeeIndexQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (storeId <= 0)
            throw new InvalidOperationException("Không xác định được cửa hàng hiện tại.");

        if (currentUserId <= 0)
            throw new InvalidOperationException("Không xác định được người dùng hiện tại.");

        var normalized = new EmployeeIndexQueryRequest
        {
            Keyword = NormalizeText(request.Keyword, 200),
            RoleId = request.RoleId > 0 ? request.RoleId : null,
            Lifecycle = NormalizeLifecycle(request.Lifecycle),
            Page = Math.Max(1, request.Page),
            PageSize = AllowedPageSizes.Contains(request.PageSize)
                ? request.PageSize
                : 10
        };

        return _repository.QueryAsync(storeId, currentUserId, normalized, ct);
    }

    private static string? NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string NormalizeLifecycle(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            EmployeeIndexLifecycles.Active => EmployeeIndexLifecycles.Active,
            EmployeeIndexLifecycles.Inactive => EmployeeIndexLifecycles.Inactive,
            _ => EmployeeIndexLifecycles.All
        };
}
