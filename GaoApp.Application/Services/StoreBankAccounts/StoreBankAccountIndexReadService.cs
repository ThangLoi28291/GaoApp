using GaoApp.Application.DTOs.StoreBankAccounts;
using GaoApp.Application.Interfaces.Repositories.StoreBankAccounts;
using GaoApp.Application.Interfaces.Services.StoreBankAccounts;

namespace GaoApp.Application.Services.StoreBankAccounts;

public sealed class StoreBankAccountIndexReadService
    : IStoreBankAccountIndexReadService
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];

    private readonly IStoreBankAccountIndexReadRepository _repository;

    public StoreBankAccountIndexReadService(
        IStoreBankAccountIndexReadRepository repository)
    {
        _repository = repository;
    }

    public Task<StoreBankAccountIndexPageDto> GetPageAsync(
        int storeId,
        StoreBankAccountIndexQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (storeId <= 0)
            throw new InvalidOperationException("Không xác định được cửa hàng hiện tại.");

        var normalized = new StoreBankAccountIndexQueryRequest
        {
            Keyword = NormalizeText(request.Keyword, 150),
            QrMode = NormalizeQrMode(request.QrMode),
            ConfirmMode = NormalizeConfirmMode(request.ConfirmMode),
            Lifecycle = NormalizeLifecycle(request.Lifecycle),
            DefaultRole = NormalizeDefaultRole(request.DefaultRole),
            Page = Math.Max(1, request.Page),
            PageSize = AllowedPageSizes.Contains(request.PageSize)
                ? request.PageSize
                : 20
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

    private static string NormalizeQrMode(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            StoreBankAccountIndexQrModes.LocalEmvQr => StoreBankAccountIndexQrModes.LocalEmvQr,
            StoreBankAccountIndexQrModes.VietQrQuickLink => StoreBankAccountIndexQrModes.VietQrQuickLink,
            StoreBankAccountIndexQrModes.ProviderApi => StoreBankAccountIndexQrModes.ProviderApi,
            _ => StoreBankAccountIndexQrModes.All
        };

    private static string NormalizeConfirmMode(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            StoreBankAccountIndexConfirmModes.Manual => StoreBankAccountIndexConfirmModes.Manual,
            StoreBankAccountIndexConfirmModes.Callback => StoreBankAccountIndexConfirmModes.Callback,
            StoreBankAccountIndexConfirmModes.Polling => StoreBankAccountIndexConfirmModes.Polling,
            _ => StoreBankAccountIndexConfirmModes.All
        };

    private static string NormalizeLifecycle(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            StoreBankAccountIndexLifecycles.Active => StoreBankAccountIndexLifecycles.Active,
            StoreBankAccountIndexLifecycles.Inactive => StoreBankAccountIndexLifecycles.Inactive,
            _ => StoreBankAccountIndexLifecycles.All
        };

    private static string NormalizeDefaultRole(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            StoreBankAccountIndexDefaults.Default => StoreBankAccountIndexDefaults.Default,
            StoreBankAccountIndexDefaults.NotDefault => StoreBankAccountIndexDefaults.NotDefault,
            _ => StoreBankAccountIndexDefaults.All
        };
}
