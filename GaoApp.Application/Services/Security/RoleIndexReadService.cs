using GaoApp.Application.DTOs.Security.Roles;
using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Application.Interfaces.Services.Security;

namespace GaoApp.Application.Services.Security;

public sealed class RoleIndexReadService : IRoleIndexReadService
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];

    private readonly IRoleIndexReadRepository _repository;

    public RoleIndexReadService(IRoleIndexReadRepository repository)
    {
        _repository = repository;
    }

    public Task<RoleIndexPageDto> GetPageAsync(
        int storeId,
        RoleIndexQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (storeId <= 0)
            throw new InvalidOperationException("Không xác định được cửa hàng hiện tại.");

        var normalized = new RoleIndexQueryRequest
        {
            Keyword = NormalizeText(request.Keyword, 150),
            Type = NormalizeType(request.Type),
            Lifecycle = NormalizeLifecycle(request.Lifecycle),
            Page = Math.Max(1, request.Page),
            PageSize = AllowedPageSizes.Contains(request.PageSize)
                ? request.PageSize
                : 10
        };

        return _repository.QueryAsync(storeId, normalized, ct);
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

    private static string NormalizeType(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            RoleIndexTypes.System => RoleIndexTypes.System,
            RoleIndexTypes.Custom => RoleIndexTypes.Custom,
            _ => RoleIndexTypes.All
        };

    private static string NormalizeLifecycle(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            RoleIndexLifecycles.Active => RoleIndexLifecycles.Active,
            RoleIndexLifecycles.Inactive => RoleIndexLifecycles.Inactive,
            _ => RoleIndexLifecycles.All
        };
}
