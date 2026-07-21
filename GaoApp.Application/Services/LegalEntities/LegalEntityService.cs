using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.LegalEntities;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Services.LegalEntities;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.LegalEntities;

/// <summary>
/// Quản lý LegalEntity, kiểm tra preflight và bật/tắt Multi LegalEntity cho store hiện tại.
/// </summary>
public sealed class LegalEntityService : ILegalEntityService
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IWarehouseRepository _warehouses;
    private readonly IInvoiceProviderSettingRepository _invoiceSettings;
    private readonly ICurrentStore _currentStore;

    public LegalEntityService(
        ILegalEntityRepository legalEntities,
        IWarehouseRepository warehouses,
        IInvoiceProviderSettingRepository invoiceSettings,
        ICurrentStore currentStore)
    {
        _legalEntities = legalEntities;
        _warehouses = warehouses;
        _invoiceSettings = invoiceSettings;
        _currentStore = currentStore;
    }

    public async Task<Result<List<LegalEntityDto>>> GetAllAsync(
        CancellationToken ct = default)
    {
        var entities = await _legalEntities.GetAllAsync(ct);
        return Result<List<LegalEntityDto>>.Success(entities.Select(Map).ToList());
    }

    public async Task<Result<List<LegalEntityOptionDto>>> GetActiveOptionsAsync(
        CancellationToken ct = default)
    {
        var entities = await _legalEntities.GetAllAsync(ct);
        return Result<List<LegalEntityOptionDto>>.Success(entities
            .Where(x => x.IsActive)
            .OrderBy(x => x.SalePriority)
            .Select(x => new LegalEntityOptionDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                SalePriority = x.SalePriority,
                IsDefaultForPurchase = x.IsDefaultForPurchase
            })
            .ToList());
    }

    public async Task<Result<LegalEntityManagementDto>> GetManagementAsync(
        CancellationToken ct = default)
    {
        var store = await _legalEntities.GetStoreAsync(_currentStore.StoreId, ct);
        if (store == null)
        {
            return Result<LegalEntityManagementDto>.Failure(Error.NotFound(
                "Không tìm thấy cửa hàng hiện tại."));
        }

        var entities = await _legalEntities.GetAllAsync(ct);
        var warehouses = await _warehouses.GetAllAsync(ct);
        var invoiceSettings = await _invoiceSettings.GetAllAsync(ct);

        return Result<LegalEntityManagementDto>.Success(new LegalEntityManagementDto
        {
            IsMultiLegalEntityEnabled = store.IsMultiLegalEntityEnabled,
            MultiLegalEntityActivatedAtUtc = store.MultiLegalEntityActivatedAtUtc,
            LegalEntities = entities.Select(Map).ToList(),
            Warehouses = warehouses
                .OrderBy(x => x.LegalEntityId)
                .ThenByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .Select(x => new LegalEntityWarehouseOptionDto
                {
                    Id = x.Id,
                    LegalEntityId = x.LegalEntityId,
                    Code = x.Code,
                    Name = x.Name,
                    IsActive = x.IsActive,
                    AllowNegativeInventory = x.AllowNegativeInventory
                })
                .ToList(),
            InvoiceSettings = invoiceSettings
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.SupplierTaxCode)
                .Select(x => new LegalEntityInvoiceSettingOptionDto
                {
                    Id = x.Id,
                    ProviderCode = x.ProviderCode,
                    SupplierTaxCode = x.SupplierTaxCode,
                    TemplateCode = x.TemplateCode,
                    InvoiceSeries = x.InvoiceSeries,
                    IsActive = x.IsActive
                })
                .ToList()
        });
    }

    public async Task<Result<LegalEntityActivationPreflightDto>> GetActivationPreflightAsync(
        CancellationToken ct = default)
    {
        var store = await _legalEntities.GetStoreAsync(_currentStore.StoreId, ct);
        if (store == null)
        {
            return Result<LegalEntityActivationPreflightDto>.Failure(Error.NotFound(
                "Không tìm thấy cửa hàng hiện tại."));
        }

        var all = await _legalEntities.GetAllAsync(ct);
        var active = all.Where(x => x.IsActive).OrderBy(x => x.SalePriority).ToList();
        var checks = new List<LegalEntityPreflightCheckDto>();

        AddCheck(
            checks,
            "LegalEntity.ActiveCount",
            "Số HKD hoạt động",
            active.Count >= 2,
            active.Count >= 2
                ? $"Có {active.Count} HKD đang hoạt động."
                : "Cần ít nhất 2 HKD hoạt động để bật chế độ Multi LegalEntity.");

        var prioritiesValid = active.All(x => x.SalePriority > 0)
            && active.Select(x => x.SalePriority).Distinct().Count() == active.Count;
        AddCheck(
            checks,
            "LegalEntity.SalePriority",
            "Thứ tự ưu tiên bán",
            prioritiesValid,
            prioritiesValid
                ? "Thứ tự ưu tiên bán hợp lệ và không trùng nhau."
                : "Mỗi HKD hoạt động phải có ưu tiên bán dương và không trùng nhau.");

        var purchaseDefaults = active.Count(x => x.IsDefaultForPurchase);
        AddCheck(
            checks,
            "LegalEntity.DefaultPurchase",
            "HKD nhập hàng mặc định",
            purchaseDefaults == 1,
            purchaseDefaults == 1
                ? "Đã có đúng một HKD nhập hàng mặc định."
                : "Phải có đúng một HKD hoạt động được chọn làm mặc định nhập hàng.");

        var duplicatedInvoiceSettings = active
            .Where(x => x.InvoiceProviderSettingId.HasValue)
            .GroupBy(x => x.InvoiceProviderSettingId!.Value)
            .Any(x => x.Count() > 1);
        AddCheck(
            checks,
            "LegalEntity.InvoiceSettingUnique",
            "Cấu hình hóa đơn riêng",
            !duplicatedInvoiceSettings,
            duplicatedInvoiceSettings
                ? "Một cấu hình hóa đơn đang được gán cho nhiều HKD hoạt động."
                : "Không có cấu hình hóa đơn bị dùng chung giữa các HKD hoạt động.");

        foreach (var entity in active)
        {
            var hasLegalIdentity = !string.IsNullOrWhiteSpace(entity.LegalName)
                && !string.IsNullOrWhiteSpace(entity.TaxCode)
                && !string.IsNullOrWhiteSpace(entity.Address);
            AddCheck(
                checks,
                $"LegalEntity.{entity.Id}.Identity",
                $"Thông tin pháp lý - {entity.Name}",
                hasLegalIdentity,
                hasLegalIdentity
                    ? "Đã có tên pháp lý, mã số thuế và địa chỉ."
                    : "Thiếu tên pháp lý, mã số thuế hoặc địa chỉ.",
                entity.Id);

            var warehouse = entity.DefaultWarehouse;
            var warehouseValid = warehouse != null
                && warehouse.LegalEntityId == entity.Id
                && warehouse.IsActive;
            AddCheck(
                checks,
                $"LegalEntity.{entity.Id}.DefaultWarehouse",
                $"Kho bán mặc định - {entity.Name}",
                warehouseValid,
                warehouseValid
                    ? $"Đang dùng kho {warehouse!.Code} - {warehouse.Name}."
                    : "Chưa chọn kho bán active thuộc đúng HKD.",
                entity.Id);

            var negativeInventoryBlocked = warehouseValid
                && warehouse!.AllowNegativeInventory == false;
            AddCheck(
                checks,
                $"LegalEntity.{entity.Id}.NegativeInventory",
                $"Chặn âm kho - {entity.Name}",
                negativeInventoryBlocked,
                negativeInventoryBlocked
                    ? "Kho bán mặc định đang chặn âm kho."
                    : "Kho bán mặc định phải tắt 'Cho phép âm kho'.",
                entity.Id);

            var invoiceSetting = entity.InvoiceProviderSetting;
            var invoiceSettingValid = invoiceSetting != null
                && invoiceSetting.IsActive
                && string.Equals(
                    invoiceSetting.ProviderCode,
                    "VIETTEL",
                    StringComparison.OrdinalIgnoreCase);
            AddCheck(
                checks,
                $"LegalEntity.{entity.Id}.InvoiceSetting",
                $"Cấu hình hóa đơn - {entity.Name}",
                invoiceSettingValid,
                invoiceSettingValid
                    ? $"Đã chọn Viettel MST {invoiceSetting!.SupplierTaxCode}."
                    : "Chưa chọn cấu hình Viettel đang hoạt động.",
                entity.Id);

            var invoiceTaxCodeMatches = invoiceSettingValid
                && TaxCodesEqual(entity.TaxCode, invoiceSetting!.SupplierTaxCode);
            AddCheck(
                checks,
                $"LegalEntity.{entity.Id}.InvoiceTaxCode",
                $"Đối chiếu MST hóa đơn - {entity.Name}",
                invoiceTaxCodeMatches,
                invoiceTaxCodeMatches
                    ? "MST của HKD khớp cấu hình phát hành hóa đơn."
                    : "MST của HKD phải khớp SupplierTaxCode trong cấu hình Viettel.",
                entity.Id);
        }

        var configurationReady = checks
            .Where(x => string.Equals(x.Level, "Error", StringComparison.OrdinalIgnoreCase))
            .All(x => x.IsPassed);

        return Result<LegalEntityActivationPreflightDto>.Success(
            new LegalEntityActivationPreflightDto
            {
                IsFeatureEnabled = store.IsMultiLegalEntityEnabled,
                ActivatedAtUtc = store.MultiLegalEntityActivatedAtUtc,
                ActiveLegalEntityCount = active.Count,
                IsConfigurationReady = configurationReady,
                CanActivate = configurationReady,
                ActivationGateMessage = configurationReady
                    ? store.IsMultiLegalEntityEnabled
                        ? "Multi LegalEntity đang bật. Có thể tắt để quay lại luồng xuất kho legacy."
                        : "Cấu hình HKD đã sẵn sàng. Có thể bật Multi LegalEntity cho cửa hàng hiện tại."
                    : "Cần xử lý toàn bộ điều kiện lỗi trước khi bật Multi LegalEntity.",
                Checks = checks
            });
    }

    public async Task<Result<bool>> SetMultiLegalEntityEnabledAsync(
        bool isEnabled,
        CancellationToken ct = default)
    {
        if (isEnabled)
        {
            var preflight = await GetActivationPreflightAsync(ct);
            if (!preflight.IsSuccess)
                return Result<bool>.Failure(preflight.Error);

            if (!preflight.Value.IsConfigurationReady)
            {
                var blockers = preflight.Value.Checks
                    .Where(x => !x.IsPassed &&
                        string.Equals(x.Level, "Error", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Title)
                    .Take(3)
                    .ToList();
                var blockerText = blockers.Count == 0
                    ? string.Empty
                    : $" Cần xử lý: {string.Join("; ", blockers)}.";

                return Result<bool>.Failure(Error.Validation(
                    "LegalEntity.ActivationPreflightFailed",
                    $"Chưa thể bật Multi LegalEntity vì preflight chưa đạt.{blockerText}"));
            }
        }

        var store = await _legalEntities.GetStoreForUpdateAsync(_currentStore.StoreId, ct);
        if (store == null)
        {
            return Result<bool>.Failure(Error.NotFound(
                "Không tìm thấy cửa hàng hiện tại."));
        }

        if (store.IsMultiLegalEntityEnabled == isEnabled &&
            (!isEnabled || store.MultiLegalEntityActivatedAtUtc.HasValue))
        {
            return Result<bool>.Success(true);
        }

        store.IsMultiLegalEntityEnabled = isEnabled;
        store.MultiLegalEntityActivatedAtUtc = isEnabled
            ? DateTime.UtcNow
            : null;

        await _legalEntities.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<int>> CreateAsync(
        CreateLegalEntityRequest request,
        CancellationToken ct = default)
    {
        var normalized = Normalize(request);
        var validationError = await ValidateCommonAsync(
            normalized.Code,
            normalized.Name,
            normalized.LegalName,
            normalized.TaxCode,
            normalized.SalePriority,
            normalized.IsDefaultForPurchase,
            normalized.InvoiceProviderSettingId,
            defaultWarehouseId: null,
            excludeId: null,
            isActive: true,
            ct);

        if (validationError != null)
            return Result<int>.Failure(validationError);

        if (normalized.IsDefaultForPurchase)
        {
            await _legalEntities.ClearDefaultForPurchaseAsync(null, ct);
            await _legalEntities.SaveChangesAsync(ct);
        }

        var entity = new LegalEntity
        {
            StoreId = _currentStore.StoreId,
            Code = normalized.Code,
            Name = normalized.Name,
            LegalName = normalized.LegalName,
            TaxCode = normalized.TaxCode,
            Address = normalized.Address,
            Phone = normalized.Phone,
            Email = normalized.Email,
            InvoiceProviderSettingId = normalized.InvoiceProviderSettingId,
            SalePriority = normalized.SalePriority,
            IsDefaultForPurchase = normalized.IsDefaultForPurchase,
            IsActive = true,
            Note = normalized.Note
        };

        await _legalEntities.AddAsync(entity, ct);
        await _legalEntities.SaveChangesAsync(ct);

        return Result<int>.Success(entity.Id);
    }

    public async Task<Result<bool>> UpdateAsync(
        UpdateLegalEntityRequest request,
        CancellationToken ct = default)
    {
        if (request.Id <= 0)
        {
            return Result<bool>.Failure(Error.Validation(
                "LegalEntity.IdInvalid",
                "LegalEntityId không hợp lệ."));
        }

        var entity = await _legalEntities.GetByIdAsync(request.Id, ct);
        if (entity == null)
            return Result<bool>.Failure(Error.NotFound("Không tìm thấy HKD cần cập nhật."));

        var normalized = Normalize(request);

        if (entity.IsDefaultForPurchase && !normalized.IsDefaultForPurchase)
        {
            return Result<bool>.Failure(Error.Validation(
                "LegalEntity.DefaultPurchaseRequired",
                "Hãy chọn HKD khác làm mặc định nhập hàng thay vì bỏ trực tiếp HKD hiện tại."));
        }

        var validationError = await ValidateCommonAsync(
            normalized.Code,
            normalized.Name,
            normalized.LegalName,
            normalized.TaxCode,
            normalized.SalePriority,
            normalized.IsDefaultForPurchase,
            normalized.InvoiceProviderSettingId,
            normalized.DefaultWarehouseId,
            entity.Id,
            entity.IsActive,
            ct);

        if (validationError != null)
            return Result<bool>.Failure(validationError);

        if (normalized.IsDefaultForPurchase && !entity.IsDefaultForPurchase)
        {
            await _legalEntities.ClearDefaultForPurchaseAsync(entity.Id, ct);
            await _legalEntities.SaveChangesAsync(ct);
        }

        entity.Code = normalized.Code;
        entity.Name = normalized.Name;
        entity.LegalName = normalized.LegalName;
        entity.TaxCode = normalized.TaxCode;
        entity.Address = normalized.Address;
        entity.Phone = normalized.Phone;
        entity.Email = normalized.Email;
        entity.DefaultWarehouseId = normalized.DefaultWarehouseId;
        entity.InvoiceProviderSettingId = normalized.InvoiceProviderSettingId;
        entity.SalePriority = normalized.SalePriority;
        entity.IsDefaultForPurchase = normalized.IsDefaultForPurchase;
        entity.Note = normalized.Note;

        await _legalEntities.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> SetActiveAsync(
        int legalEntityId,
        bool isActive,
        CancellationToken ct = default)
    {
        var entity = await _legalEntities.GetByIdAsync(legalEntityId, ct);
        if (entity == null)
            return Result<bool>.Failure(Error.NotFound("Không tìm thấy HKD trong cửa hàng hiện tại."));

        if (entity.IsActive == isActive)
            return Result<bool>.Success(true);

        if (!isActive)
        {
            if (entity.IsDefaultForPurchase)
            {
                return Result<bool>.Failure(Error.Validation(
                    "LegalEntity.DefaultPurchaseCannotDeactivate",
                    "Không thể khóa HKD đang là mặc định nhập hàng. Hãy chuyển mặc định trước."));
            }

            if (await _legalEntities.HasActiveWarehousesAsync(entity.Id, ct))
            {
                return Result<bool>.Failure(Error.Validation(
                    "LegalEntity.ActiveWarehouseExists",
                    "Không thể khóa HKD khi vẫn còn kho đang hoạt động."));
            }
        }
        else if (await _legalEntities.ExistsSalePriorityAsync(
                     entity.SalePriority,
                     entity.Id,
                     ct))
        {
            return Result<bool>.Failure(Error.Conflict(
                $"Ưu tiên bán {entity.SalePriority} đang được một HKD hoạt động khác sử dụng."));
        }

        entity.IsActive = isActive;
        await _legalEntities.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> SetDefaultWarehouseAsync(
        int legalEntityId,
        int warehouseId,
        CancellationToken ct = default)
    {
        var legalEntity = await _legalEntities.GetByIdAsync(legalEntityId, ct);
        if (legalEntity == null)
            return Result<bool>.Failure(Error.NotFound("Không tìm thấy HKD trong cửa hàng hiện tại."));

        var error = await ValidateDefaultWarehouseAsync(
            legalEntity.Id,
            warehouseId,
            legalEntity.Id,
            ct);
        if (error != null)
            return Result<bool>.Failure(error);

        legalEntity.DefaultWarehouseId = warehouseId;
        await _legalEntities.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private async Task<Error?> ValidateCommonAsync(
        string code,
        string name,
        string legalName,
        string? taxCode,
        int salePriority,
        bool isDefaultForPurchase,
        int? invoiceProviderSettingId,
        int? defaultWarehouseId,
        int? excludeId,
        bool isActive,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Error.Validation("LegalEntity.CodeRequired", "Mã HKD không được để trống.");
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("LegalEntity.NameRequired", "Tên hiển thị HKD không được để trống.");
        if (string.IsNullOrWhiteSpace(legalName))
            return Error.Validation("LegalEntity.LegalNameRequired", "Tên pháp lý không được để trống.");
        if (salePriority <= 0)
            return Error.Validation("LegalEntity.SalePriorityInvalid", "Ưu tiên bán phải lớn hơn 0.");
        if (isDefaultForPurchase && !isActive)
            return Error.Validation("LegalEntity.DefaultPurchaseInactive", "HKD ngưng hoạt động không thể là mặc định nhập hàng.");
        if (await _legalEntities.ExistsCodeAsync(code, excludeId, ct))
            return Error.Conflict($"Mã HKD '{code}' đã tồn tại trong cửa hàng.");
        if (isActive && await _legalEntities.ExistsSalePriorityAsync(salePriority, excludeId, ct))
            return Error.Conflict($"Ưu tiên bán {salePriority} đã được một HKD hoạt động sử dụng.");
        if (taxCode != null && await _legalEntities.ExistsTaxCodeAsync(taxCode, excludeId, ct))
            return Error.Conflict($"Mã số thuế '{taxCode}' đã được một HKD khác sử dụng.");

        if (invoiceProviderSettingId.HasValue)
        {
            var setting = await _invoiceSettings.GetByIdAsync(invoiceProviderSettingId.Value, ct);
            if (setting == null)
                return Error.NotFound("Không tìm thấy cấu hình hóa đơn trong cửa hàng hiện tại.");
            if (await _legalEntities.ExistsInvoiceSettingAssignmentAsync(
                    invoiceProviderSettingId.Value,
                    excludeId,
                    ct))
                return Error.Conflict("Cấu hình hóa đơn này đã được gán cho một HKD khác.");
        }

        if (defaultWarehouseId.HasValue)
        {
            if (!excludeId.HasValue)
                return Error.Validation("LegalEntity.DefaultWarehouseCreateOrder", "Hãy tạo HKD trước rồi tạo kho thuộc HKD và chọn kho mặc định khi sửa.");

            var warehouseError = await ValidateDefaultWarehouseAsync(
                excludeId.Value,
                defaultWarehouseId.Value,
                excludeId,
                ct);
            if (warehouseError != null)
                return warehouseError;
        }

        return null;
    }

    private async Task<Error?> ValidateDefaultWarehouseAsync(
        int legalEntityId,
        int warehouseId,
        int? excludeId,
        CancellationToken ct)
    {
        var warehouse = await _warehouses.GetByIdAsync(warehouseId, ct);
        if (warehouse == null)
            return Error.NotFound("Không tìm thấy kho trong cửa hàng hiện tại.");
        if (warehouse.LegalEntityId != legalEntityId)
            return Error.Validation("LegalEntity.WarehouseOwnershipMismatch", "Kho mặc định phải thuộc chính HKD được cấu hình.");
        if (!warehouse.IsActive)
            return Error.Validation("LegalEntity.DefaultWarehouseInactive", "Không thể chọn kho đã ngưng hoạt động làm kho mặc định.");
        if (await _legalEntities.ExistsDefaultWarehouseAssignmentAsync(warehouseId, excludeId, ct))
            return Error.Conflict("Kho này đã là kho mặc định của một HKD khác.");
        return null;
    }

    private static CreateLegalEntityRequest Normalize(CreateLegalEntityRequest request)
        => new()
        {
            Code = NormalizeCode(request.Code),
            Name = NormalizeRequired(request.Name),
            LegalName = NormalizeRequired(request.LegalName),
            TaxCode = NormalizeOptional(request.TaxCode),
            Address = NormalizeOptional(request.Address),
            Phone = NormalizeOptional(request.Phone),
            Email = NormalizeOptional(request.Email),
            InvoiceProviderSettingId = request.InvoiceProviderSettingId,
            SalePriority = request.SalePriority,
            IsDefaultForPurchase = request.IsDefaultForPurchase,
            Note = NormalizeOptional(request.Note)
        };

    private static UpdateLegalEntityRequest Normalize(UpdateLegalEntityRequest request)
        => new()
        {
            Id = request.Id,
            Code = NormalizeCode(request.Code),
            Name = NormalizeRequired(request.Name),
            LegalName = NormalizeRequired(request.LegalName),
            TaxCode = NormalizeOptional(request.TaxCode),
            Address = NormalizeOptional(request.Address),
            Phone = NormalizeOptional(request.Phone),
            Email = NormalizeOptional(request.Email),
            DefaultWarehouseId = request.DefaultWarehouseId,
            InvoiceProviderSettingId = request.InvoiceProviderSettingId,
            SalePriority = request.SalePriority,
            IsDefaultForPurchase = request.IsDefaultForPurchase,
            Note = NormalizeOptional(request.Note)
        };

    private static string NormalizeCode(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string NormalizeRequired(string? value)
        => (value ?? string.Empty).Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TaxCodesEqual(string? left, string? right)
        => string.Equals(
            NormalizeOptional(left)?.ToUpperInvariant(),
            NormalizeOptional(right)?.ToUpperInvariant(),
            StringComparison.Ordinal);

    private static void AddCheck(
        ICollection<LegalEntityPreflightCheckDto> checks,
        string code,
        string title,
        bool isPassed,
        string message,
        int? legalEntityId = null)
        => checks.Add(new LegalEntityPreflightCheckDto
        {
            Code = code,
            Title = title,
            IsPassed = isPassed,
            Level = "Error",
            Message = message,
            LegalEntityId = legalEntityId
        });

    private static LegalEntityDto Map(LegalEntity entity)
        => new()
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            LegalName = entity.LegalName,
            TaxCode = entity.TaxCode,
            Address = entity.Address,
            Phone = entity.Phone,
            Email = entity.Email,
            DefaultWarehouseId = entity.DefaultWarehouseId,
            DefaultWarehouseName = entity.DefaultWarehouse?.Name,
            DefaultWarehouseCode = entity.DefaultWarehouse?.Code,
            InvoiceProviderSettingId = entity.InvoiceProviderSettingId,
            InvoiceSupplierTaxCode = entity.InvoiceProviderSetting?.SupplierTaxCode,
            InvoiceProviderCode = entity.InvoiceProviderSetting?.ProviderCode,
            IsInvoiceSettingActive = entity.InvoiceProviderSetting?.IsActive,
            SalePriority = entity.SalePriority,
            IsDefaultForPurchase = entity.IsDefaultForPurchase,
            IsActive = entity.IsActive,
            WarehouseCount = entity.Warehouses.Count,
            ActiveWarehouseCount = entity.Warehouses.Count(x => x.IsActive),
            Note = entity.Note
        };
}
