using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed class StorefrontSettingsService(AppDbContext db, ICurrentUser user,
    ICurrentStorePermissionService permissions) : IStorefrontSettingsService
{
    private int Store => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ForbiddenAppException("Chưa xác định cửa hàng.");
    private async Task Guard(string permission, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserId is not > 0 ||
            !await permissions.HasPermissionAsync(Store, user.UserId.Value, permission, ct) ||
            !await db.Stores.AnyAsync(x => x.Id == Store && x.IsActive, ct))
            throw new ForbiddenAppException("Không có quyền cấu hình website tại cửa hàng này.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new ValidationAppException(message); }
    private static void Validate(object request)
    {
        var errors = new List<ValidationResult>();
        Require(Validator.TryValidateObject(request, new ValidationContext(request), errors, true),
            string.Join(" ", errors.Select(x => x.ErrorMessage)));
    }
    private static void Version(string? version, byte[]? current)
    {
        byte[] supplied;
        try { supplied = string.IsNullOrEmpty(version) ? [] : version.Length <= 20 ? Convert.FromBase64String(version) : throw new FormatException(); }
        catch (FormatException) { throw new ConflictAppException("Phiên bản cấu hình không hợp lệ. Hãy tải lại trang."); }
        if (!(current ?? []).AsSpan().SequenceEqual(supplied))
            throw new ConflictAppException("Cấu hình đã thay đổi. Hãy tải lại trang trước khi lưu.");
    }
    private async Task Lock(CancellationToken ct)
    {
        if (!db.CanWriteStoreData || Store <= 0) throw new ForbiddenAppException("Không có phạm vi ghi dữ liệu cửa hàng.");
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Configuration lock requires a transaction.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = tx.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @result;";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = "GaoApp:Storefront:Configuration:" + Store; command.Parameters.Add(parameter);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) < 0) throw new ConflictAppException("Cấu hình đang được cập nhật. Hãy thử lại.");
    }

    public async Task<StorefrontSettingsPage> GetAsync(CancellationToken ct = default)
    {
        await Guard(StorefrontPermissions.ViewSettings, ct);
        var store = await db.Stores.AsNoTracking().SingleAsync(x => x.Id == Store, ct);
        var entity = await db.StorefrontSettings.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store, ct);
        var value = entity ?? new StorefrontSettings { DisplayName = store.Name };
        var request = new StorefrontSettingsRequest
        {
            Version = entity is null ? "" : Convert.ToBase64String(value.RowVersion),
            IsEnabled = value.IsEnabled, DisplayName = value.DisplayName, Description = value.Description,
            LogoPath = value.LogoPath, ContactPhone = value.ContactPhone, SourceWarehouseId = value.SourceWarehouseId,
            CodEnabled = value.CodEnabled, QrEnabled = value.QrEnabled, AllowNegativeInventory = value.AllowNegativeInventory,
            ShippingByWeight = value.ShippingByWeight, QrHoldMinutes = value.QrHoldMinutes,
            ShippingFeeTreatment = value.ShippingFeeTreatment, BankAccountId = value.BankAccountId,
            ReceivingLegalEntityId = value.ReceivingLegalEntityId, BankTerminalCode = value.BankTerminalCode, BankUserCode = value.BankUserCode,
            CollectionSourceLegalEntityIds = await db.Set<StorefrontBankCollectionAuthority>().Where(x => x.StoreId == Store && x.IsActive &&
                x.BankAccountId == value.BankAccountId && x.ReceivingLegalEntityId == value.ReceivingLegalEntityId)
                .Select(x => x.SourceLegalEntityId).ToListAsync(ct)
        };
        var zones = await (from zone in db.StorefrontShippingZones.AsNoTracking()
            join rate in db.StorefrontShippingRates.AsNoTracking() on new { zone.StoreId, Id = zone.Id } equals new { rate.StoreId, Id = rate.ShippingZoneId }
            where zone.StoreId == Store && rate.IsCurrent
            orderby zone.DeliveryMethod, zone.Code
            select new StorefrontShippingZoneDto(zone.Id, Convert.ToBase64String(zone.RowVersion), zone.Code, zone.Name,
                zone.ProvinceCode, zone.WardCode, zone.DeliveryMethod, zone.IsActive, zone.MinDeliveryDays, zone.MaxDeliveryDays,
                rate.Revision, rate.ByWeight, rate.BaseFee, rate.AdditionalFee, rate.FirstWeightKg, rate.AdditionalStepKg, rate.FreeFromGoodsAmount)).ToListAsync(ct);
        return new(request, entity?.Revision ?? 0, store.IsMultiLegalEntityEnabled,
            await permissions.HasPermissionAsync(Store, user.UserId!.Value, StorefrontPermissions.ManageSettings, ct),
            await permissions.HasPermissionAsync(Store, user.UserId!.Value, StorefrontPermissions.ManageShipping, ct),
            await db.Warehouses.AsNoTracking().Where(x => x.StoreId == Store && x.IsActive && x.LegalEntity.IsActive)
                .OrderBy(x => x.Name).Select(x => new StorefrontOption(x.Id, x.Name)).ToListAsync(ct),
            await db.StoreBankAccounts.AsNoTracking().Where(x => x.StoreId == Store && x.IsActive && x.BankCode == "ACB")
                .OrderBy(x => x.BankName).Select(x => new StorefrontOption(x.Id, x.BankName + " · " + x.AccountNumber)).ToListAsync(ct),
            await db.LegalEntities.AsNoTracking().Where(x => x.StoreId == Store && x.IsActive)
                .OrderBy(x => x.SalePriority).Select(x => new StorefrontOption(x.Id, x.Name)).ToListAsync(ct), zones);
    }

    public async Task SaveAsync(StorefrontSettingsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Guard(StorefrontPermissions.ManageSettings, ct);
        Validate(request);
        Require(!request.IsEnabled, "Website chưa thể mở bán ở bước dữ liệu nền. Chỉ mở sau khi hoàn tất các cổng phát hành.");
        Require(request.ShippingFeeTreatment == StorefrontShippingFeeTreatment.StoreServiceFee,
            "Phí giao đã chốt là dịch vụ cửa hàng, chi phí giao ghi riêng.");
        Require(request.CodEnabled || request.QrEnabled, "Cần chọn ít nhất một phương thức thanh toán.");
        Require(!string.IsNullOrWhiteSpace(request.DisplayName), "Cần nhập tên hiển thị cửa hàng.");
        Require(Regex.IsMatch(request.ContactPhone ?? "", @"^[+0-9 ().-]{0,30}$"), "Số điện thoại liên hệ không hợp lệ.");
        var logo = request.LogoPath?.Trim();
        Require(string.IsNullOrEmpty(logo) || (logo.StartsWith("/uploads/", StringComparison.Ordinal) &&
            !logo.Contains("..", StringComparison.Ordinal) && !logo.Contains('%') && !logo.Contains('\\') &&
            new[] { ".png", ".jpg", ".jpeg", ".webp", ".avif" }.Contains(Path.GetExtension(logo).ToLowerInvariant())),
            "Logo cần là đường dẫn ảnh trong uploads của hệ thống.");
        Require(request.SourceWarehouseId is null or > 0 && request.BankAccountId is null or > 0 && request.ReceivingLegalEntityId is null or > 0,
            "Mã kho/tài khoản/chủ thể không hợp lệ.");
        var warehouse = request.SourceWarehouseId.HasValue
            ? await db.Warehouses.Include(x => x.LegalEntity).SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == request.SourceWarehouseId && x.IsActive, ct) : null;
        Require(!request.SourceWarehouseId.HasValue || warehouse?.LegalEntity is { IsActive: true } && warehouse.LegalEntity.StoreId == Store,
            "Kho nguồn phải đang hoạt động và thuộc đúng cửa hàng/chủ thể.");
        Require(request.BankAccountId.HasValue == request.ReceivingLegalEntityId.HasValue, "Tài khoản và chủ thể nhận tiền phải được chọn cùng nhau.");
        if (request.BankAccountId.HasValue)
        {
            Require(await db.StoreBankAccounts.AnyAsync(x => x.StoreId == Store && x.Id == request.BankAccountId && x.IsActive && x.BankCode == "ACB", ct),
                "Cần tài khoản ACB đang hoạt động của cửa hàng.");
            Require(await db.LegalEntities.AnyAsync(x => x.StoreId == Store && x.Id == request.ReceivingLegalEntityId && x.IsActive, ct), "Chủ thể nhận tiền không hợp lệ.");
        }
        var sourceIds = request.CollectionSourceLegalEntityIds;
        Require(sourceIds is not null && sourceIds.Count <= 1000 && sourceIds.All(x => x > 0) && sourceIds.Distinct().Count() == sourceIds.Count,
            "Danh sách chủ thể được thu hộ không hợp lệ.");
        Require(sourceIds!.Count == 0 || request.BankAccountId.HasValue, "Cần chọn tài khoản/chủ thể nhận tiền trước khi khai quyền thu hộ.");
        Require(await db.LegalEntities.CountAsync(x => x.StoreId == Store && x.IsActive && sourceIds.Contains(x.Id), ct) == sourceIds.Count,
            "Chủ thể được thu tiền phải thuộc cửa hàng và đang hoạt động.");
        if (request.QrEnabled)
        {
            Require(request.BankAccountId.HasValue && request.QrHoldMinutes.HasValue &&
                Regex.IsMatch(request.BankTerminalCode ?? "", @"^[A-Za-z0-9._-]{1,100}$") &&
                Regex.IsMatch(request.BankUserCode ?? "", @"^[A-Za-z0-9._-]{1,100}$"),
                "QR cần tài khoản, hạn giữ và định danh terminal/user đã đăng ký với ngân hàng.");
            var acb = await db.Set<StoreAcbSettings>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store, ct);
            Require(acb is { Enabled: true } && acb.BankAccountId == request.BankAccountId &&
                acb.MerchantId.Length is > 0 and <= 30 && acb.BeneficiaryName.Length is > 0 and <= 30 &&
                !string.IsNullOrWhiteSpace(acb.ClientId) && !string.IsNullOrWhiteSpace(acb.ClientSecretProtected),
                "Cấu hình ACB chưa sẵn sàng hoặc merchant/tên nhận tiền vượt giới hạn 30 ký tự.");
            var multi = await db.Stores.Where(x => x.Id == Store).Select(x => x.IsMultiLegalEntityEnabled).SingleAsync(ct);
            var owners = multi ? await db.LegalEntities.Where(x => x.StoreId == Store && x.IsActive && x.SalePriority > 0 &&
                    x.DefaultWarehouse!=null&&x.DefaultWarehouse.IsActive&&x.DefaultWarehouse.StoreId==Store&&x.DefaultWarehouse.LegalEntityId==x.Id)
                .Select(x => x.Id).ToListAsync(ct) : warehouse is null ? [] : new List<int> { warehouse.LegalEntityId };
            // Configuration may expose several possible owners. Checkout seals the actual
            // allocation and requires every one of its owners; an unused candidate is not a debt.
            Require(owners.Count > 0 && owners.Any(sourceIds.Contains), "Cần khai quyền thu tiền cho ít nhất một chủ thể nguồn hàng. Mỗi đơn QR chỉ nhận khi đủ quyền cho nguồn thực tế.");
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await Lock(ct);
            // Publication writes use this same store lock: enabling kg cannot race a weightless publication.
            if (request.ShippingByWeight)
            {
                var published = from product in db.StorefrontProducts
                    join variant in db.StorefrontVariants on new { product.StoreId, Id = product.Id, product.ProductId }
                        equals new { variant.StoreId, Id = variant.StorefrontProductId, variant.ProductId }
                    where product.StoreId == Store && product.IsPublished && variant.IsPublished
                    select variant;
                Require(!await published.AnyAsync(v => v.BaseUnitIsPublished && v.BaseUnitWeightKg == null ||
                    db.StorefrontSellingUnits.Any(u => u.StoreId == Store && u.StorefrontVariantId == v.Id && u.VariantId == v.VariantId &&
                        u.IsPublished && u.WeightKg == null), ct),
                    "Cần kg hợp lệ cho mọi đơn vị đang đăng bán trước khi bật tính theo kg.");
            }
            var entity = await db.StorefrontSettings.SingleOrDefaultAsync(x => x.StoreId == Store, ct);
            Version(request.Version, entity?.RowVersion);
            var modeChanged = entity is not null && entity.ShippingByWeight != request.ShippingByWeight;
            if (entity is null) { entity = new() { StoreId = Store }; db.StorefrontSettings.Add(entity); }
            else entity.Revision++;
            entity.IsEnabled = false; entity.DisplayName = request.DisplayName.Trim(); entity.Description = request.Description?.Trim();
            entity.LogoPath = string.IsNullOrEmpty(logo) ? null : logo; entity.ContactPhone = (request.ContactPhone ?? "").Trim();
            entity.SourceWarehouseId = warehouse?.Id; entity.SourceLegalEntityId = warehouse?.LegalEntityId;
            entity.CodEnabled = request.CodEnabled; entity.QrEnabled = request.QrEnabled; entity.AllowNegativeInventory = request.AllowNegativeInventory;
            entity.ShippingByWeight = request.ShippingByWeight; entity.QrHoldMinutes = request.QrHoldMinutes;
            entity.ShippingFeeTreatment = StorefrontShippingFeeTreatment.StoreServiceFee;
            entity.BankAccountId = request.BankAccountId; entity.ReceivingLegalEntityId = request.ReceivingLegalEntityId;
            entity.BankTerminalCode = (request.BankTerminalCode ?? "").Trim(); entity.BankUserCode = (request.BankUserCode ?? "").Trim();
            var authorities = await db.Set<StorefrontBankCollectionAuthority>().Where(x => x.StoreId == Store).ToListAsync(ct);
            foreach (var authority in authorities) authority.IsActive = false;
            foreach (var sourceId in sourceIds)
            {
                var authority = authorities.SingleOrDefault(x => x.BankAccountId == request.BankAccountId &&
                    x.ReceivingLegalEntityId == request.ReceivingLegalEntityId && x.SourceLegalEntityId == sourceId);
                if (authority is null) db.Add(new StorefrontBankCollectionAuthority { StoreId = Store, BankAccountId = request.BankAccountId!.Value,
                    ReceivingLegalEntityId = request.ReceivingLegalEntityId!.Value, SourceLegalEntityId = sourceId });
                else authority.IsActive = true;
            }
            var oldRates = modeChanged ? await db.StorefrontShippingRates.Where(x => x.StoreId == Store && x.IsCurrent).ToListAsync(ct) : [];
            if (modeChanged)
            {
                var zones = await db.StorefrontShippingZones.Where(x => x.StoreId == Store).ToListAsync(ct);
                foreach (var zone in zones) zone.Revision++;
            }
            foreach (var rate in oldRates) rate.IsCurrent = false;
            await db.SaveChangesAsync(ct);
            foreach (var rate in oldRates) db.StorefrontShippingRates.Add(new() { StoreId = Store, ShippingZoneId = rate.ShippingZoneId,
                Revision = rate.Revision + 1, ByWeight = request.ShippingByWeight, BaseFee = rate.BaseFee, AdditionalFee = rate.AdditionalFee,
                FirstWeightKg = rate.FirstWeightKg, AdditionalStepKg = rate.AdditionalStepKg, FreeFromGoodsAmount = rate.FreeFromGoodsAmount });
            if (oldRates.Count > 0) await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw new ConflictAppException("Cấu hình đã được người khác cập nhật. Hãy tải lại trang."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new ConflictAppException("Cấu hình vừa được tạo hoặc thay đổi. Hãy tải lại trang."); }
    }

    public async Task SaveZoneAsync(StorefrontShippingZoneRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Guard(StorefrontPermissions.ManageShipping, ct); Validate(request);
        var code = request.Code.Trim().ToUpperInvariant(); var province = (request.ProvinceCode ?? "").Trim().ToUpperInvariant();
        var ward = (request.WardCode ?? "").Trim().ToUpperInvariant();
        Require(Regex.IsMatch(code, @"^[A-Z0-9_-]{1,40}$") && (province == "" || Regex.IsMatch(province, @"^[A-Z0-9_-]{1,20}$")) &&
            (ward == "" || Regex.IsMatch(ward, @"^[A-Z0-9_-]{1,20}$")), "Mã vùng/tỉnh/xã không hợp lệ.");
        Require(Enum.IsDefined(request.DeliveryMethod) && (ward == "" || province != "") &&
            (request.DeliveryMethod == StorefrontDeliveryMethod.Nationwide || province != ""), "Giao gần cần mã tỉnh; mã xã cần mã tỉnh tương ứng.");
        Require(request.MinDeliveryDays <= request.MaxDeliveryDays, "Khoảng ngày giao không hợp lệ.");
        Require(request.Id is null or > 0, "Mã vùng giao không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(ct);
        var settings = await db.StorefrontSettings.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store, ct);
        Require(settings is not null, "Hãy lưu cấu hình website trước khi thêm vùng giao.");
        Version(request.SettingsVersion, settings!.RowVersion);
        try
        {
            StorefrontShippingPolicy.Quote(Store, 1, [new(1, 1)], new(Store, 1, 1, settings!.ShippingByWeight,
                request.BaseFee, request.AdditionalFee, request.FirstWeightKg, request.AdditionalStepKg, request.FreeFromGoodsAmount));
            Require(request.FirstWeightKg > 0 && request.AdditionalStepKg > 0 &&
                request.FirstWeightKg < 1_000_000_000_000m && request.AdditionalStepKg < 1_000_000_000_000m &&
                decimal.Round(request.FirstWeightKg, 6) == request.FirstWeightKg && decimal.Round(request.AdditionalStepKg, 6) == request.AdditionalStepKg,
                "Mức kg phải dương, tối đa sáu số lẻ và trong giới hạn lưu trữ.");
        }
        catch (StorefrontRuleException ex) { throw new ValidationAppException(ex.Message); }
        try
        {
            var zone = request.Id.HasValue ? await db.StorefrontShippingZones.SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == request.Id, ct) : null;
            if (request.Id.HasValue && zone is null) throw new NotFoundAppException("Không tìm thấy vùng giao.");
            Version(request.Version, zone?.RowVersion);
            if (zone is null) { zone = new() { StoreId = Store }; db.StorefrontShippingZones.Add(zone); }
            else zone.Revision++;
            zone.Code = code; zone.Name = request.Name.Trim(); zone.CountryCode = "VN"; zone.ProvinceCode = province; zone.WardCode = ward;
            zone.DeliveryMethod = request.DeliveryMethod; zone.IsActive = request.IsActive; zone.MinDeliveryDays = request.MinDeliveryDays; zone.MaxDeliveryDays = request.MaxDeliveryDays;
            var oldRate = zone.Id > 0 ? await db.StorefrontShippingRates.SingleOrDefaultAsync(x => x.StoreId == Store && x.ShippingZoneId == zone.Id && x.IsCurrent, ct) : null;
            if (oldRate is not null) oldRate.IsCurrent = false;
            await db.SaveChangesAsync(ct);
            db.StorefrontShippingRates.Add(new() { StoreId = Store, ShippingZoneId = zone.Id, Revision = (oldRate?.Revision ?? 0) + 1,
                ByWeight = settings!.ShippingByWeight, BaseFee = request.BaseFee, AdditionalFee = request.AdditionalFee,
                FirstWeightKg = request.FirstWeightKg, AdditionalStepKg = request.AdditionalStepKg, FreeFromGoodsAmount = request.FreeFromGoodsAmount });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw new ConflictAppException("Vùng giao đã thay đổi. Hãy tải lại trang."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new ConflictAppException("Mã hoặc phạm vi vùng giao đã tồn tại. Hãy sửa vùng hiện có."); }
    }
}
