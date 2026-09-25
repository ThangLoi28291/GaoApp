using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;

namespace GaoApp.Application.Services.Inventory;

public sealed class InventoryInquiryReadService : IInventoryInquiryReadService
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50, 100, 200];

    private readonly IInventoryInquiryReadRepository _repository;
    private readonly ICurrentStore _currentStore;
    private readonly ICurrentUser _currentUser;

    public InventoryInquiryReadService(
        IInventoryInquiryReadRepository repository,
        ICurrentStore currentStore,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _currentStore = currentStore;
        _currentUser = currentUser;
    }

    public Task<InventoryInquiryPageDto> GetPageAsync(
        InventoryInquiryQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalized = new InventoryInquiryQueryRequest
        {
            WarehouseId = request.WarehouseId is > 0
                ? request.WarehouseId
                : null,
            Keyword = NormalizeKeyword(request.Keyword),
            State = NormalizeState(request.State),
            SortBy = NormalizeSortBy(request.SortBy),
            SortDirection = string.Equals(
                request.SortDirection,
                "desc",
                StringComparison.OrdinalIgnoreCase)
                    ? "desc"
                    : "asc",
            Page = Math.Max(1, request.Page),
            PageSize = AllowedPageSizes.Contains(request.PageSize)
                ? request.PageSize
                : 20
        };

        return _repository.QueryAsync(
            _currentStore.StoreId,
            normalized,
            _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            ct);
    }

    public Task<InventoryInquiryQuickViewDto?> GetQuickViewAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default)
    {
        if (warehouseId <= 0 || productVariantId <= 0)
            return Task.FromResult<InventoryInquiryQuickViewDto?>(null);

        return _repository.GetQuickViewAsync(
            _currentStore.StoreId,
            warehouseId,
            productVariantId,
            _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            ct);
    }

    private static string? NormalizeKeyword(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return null;

        var trimmed = keyword.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }

    private static string? NormalizeState(string? state)
        => state?.Trim().ToLowerInvariant() switch
        {
            InventoryInquiryStates.Positive => InventoryInquiryStates.Positive,
            InventoryInquiryStates.Zero => InventoryInquiryStates.Zero,
            InventoryInquiryStates.Negative => InventoryInquiryStates.Negative,
            _ => null
        };

    private static string NormalizeSortBy(string? sortBy)
        => sortBy?.Trim().ToLowerInvariant() switch
        {
            "warehouse" => "warehouse",
            "onhandqty" => "onhandqty",
            "reservedqty" => "reservedqty",
            "availableqty" => "availableqty",
            _ => "productname"
        };
}
