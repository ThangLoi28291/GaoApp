using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Products;

/// <summary>
/// Service governance cho luồng đổi barcode chuẩn nghiệp vụ:
/// - barcode cũ bị ngưng dùng
/// - tạo barcode mới
/// - ghi lịch sử append-only
/// </summary>
public sealed class BarcodeGovernanceService : IBarcodeGovernanceService
{
    private readonly IProductVariantUnitBarcodeRepository _barcodeRepository;
    private readonly IProductVariantBarcodeHistoryRepository _historyRepository;
    private readonly ICurrentStore _currentStore;
    private readonly ICurrentUser _currentUser;

    public BarcodeGovernanceService(
        IProductVariantUnitBarcodeRepository barcodeRepository,
        IProductVariantBarcodeHistoryRepository historyRepository,
        ICurrentStore currentStore,
        ICurrentUser currentUser)
    {
        _barcodeRepository = barcodeRepository;
        _historyRepository = historyRepository;
        _currentStore = currentStore;
        _currentUser = currentUser;
    }

    public async Task ChangeBarcodeAsync(ChangeBarcodeRequest request, CancellationToken ct = default)
    {
        if (request.ProductUnitConversionId <= 0)
            throw new InvalidOperationException("ProductUnitConversionId không hợp lệ.");

        var storeId = _currentStore.StoreId;
        var newBarcodeText = NormalizeBarcode(request.NewBarcode);

        if (string.IsNullOrWhiteSpace(newBarcodeText))
            throw new InvalidOperationException("Barcode mới không hợp lệ.");

        var conversion = await _barcodeRepository.GetConversionForChangeAsync(request.ProductUnitConversionId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy đơn vị quy đổi.");

        if (conversion.StoreId != storeId)
            throw new InvalidOperationException("Dữ liệu không thuộc store hiện tại.");

        var currentActive = await _barcodeRepository.GetCurrentActiveForConversionAsync(conversion.Id, ct);

        if (currentActive != null &&
            string.Equals(currentActive.Barcode, newBarcodeText, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Barcode mới đang trùng barcode hiện tại.");
        }

        var existsActive = await _barcodeRepository.ExistsActiveBarcodeAsync(
            storeId,
            newBarcodeText,
            currentActive?.Id,
            ct);

        if (existsActive)
            throw new InvalidOperationException("Barcode mới đang được sử dụng bởi đơn vị khác trong cùng store.");

        if (currentActive != null)
        {
            currentActive.IsActive = false;
            currentActive.IsPrimary = false;
        }

        // Với flow governance đổi barcode chuẩn:
        // - barcode mới luôn là mã đang dùng hiện tại
        // - luôn nên là primary để tránh conversion không còn mã chính
        var newBarcode = new ProductVariantUnitBarcode
        {
            StoreId = storeId,
            ProductUnitConversionId = conversion.Id,
            Barcode = newBarcodeText,
            BarcodeType = BarcodeType.External,
            IsPrimary = true,
            IsActive = true,
            Note = request.Reason
        };

        await _barcodeRepository.AddAsync(newBarcode, ct);

        // Save lần 1 để:
        // - cập nhật barcode cũ
        // - insert barcode mới
        // - lấy được newBarcode.Id
        await _barcodeRepository.SaveChangesAsync(ct);

        var history = new ProductVariantBarcodeHistory
        {
            StoreId = storeId,
            ProductVariantId = conversion.ProductVariantId,
            ProductUnitConversionId = conversion.Id,
            OldBarcodeId = currentActive?.Id,
            NewBarcodeId = newBarcode.Id,
            OldBarcode = currentActive?.Barcode,
            NewBarcode = newBarcode.Barcode,
            ActionType = currentActive == null
                ? BarcodeHistoryActionType.Assigned
                : BarcodeHistoryActionType.Replaced,
            Reason = request.Reason,
            ChangedByUserId = _currentUser.UserId,
            ChangedByUserName = _currentUser.UserName,
            ChangedAtUtc = DateTime.UtcNow
        };

        await _historyRepository.AddAsync(history, ct);
        await _historyRepository.SaveChangesAsync(ct);
    }

    private static string NormalizeBarcode(string? barcode)
    {
        return string.IsNullOrWhiteSpace(barcode)
            ? string.Empty
            : barcode.Trim();
    }
}