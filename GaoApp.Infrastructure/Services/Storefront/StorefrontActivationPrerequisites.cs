using System.Text.RegularExpressions;
using GaoApp.Application.Services.Products;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GaoApp.Infrastructure.Services.Storefront;

// Reads the real tenant scope, including while intake is stopped. Never changes
// IsEnabled temporarily and never builds a synthetic cart/order to validate it.
public sealed class StorefrontActivationPrerequisites(AppDbContext db, IConfiguration configuration, TimeProvider? clock = null)
{
    public async Task<IReadOnlyList<string>> EvaluateAsync(CancellationToken ct = default)
    {
        var codes = (await EvaluateIntakeAsync(ct)).ToList();
        if (!bool.TryParse(configuration["StorefrontRelease:AllowSettingsActivation"], out var allowed) || !allowed)
            codes.Insert(0, "ACTIVATION_POLICY_CLOSED");
        return codes;
    }

    public async Task<IReadOnlyList<string>> EvaluateIntakeAsync(CancellationToken ct = default)
    {
        var codes = new List<string>(); var store = db.CurrentStoreId ?? 0;
        var shop = await db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == store && x.IsActive, ct);
        if (shop is null) return ["STORE_SCOPE_INVALID"];
        if (StorefrontHostIdentity.Normalize(configuration["StorefrontHost:Host"]) is null ||
            !string.Equals(configuration["StorefrontHost:StoreSubdomain"]?.Trim(), shop.SubDomainNormalized, StringComparison.OrdinalIgnoreCase))
            codes.Add("HOST_STORE_BINDING_INVALID");
        var settings = await db.StorefrontSettings.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == store, ct);
        if (settings is null) { codes.Add("SETTINGS_MISSING"); return codes; }
        if (string.IsNullOrWhiteSpace(settings.DisplayName) || string.IsNullOrWhiteSpace(settings.ContactPhone) ||
            !Regex.IsMatch(settings.ContactPhone, @"^[+0-9 ().-]{1,30}$")) codes.Add("BRANDING_CONTACT_INVALID");
        if (!settings.CodEnabled && !settings.QrEnabled) codes.Add("PAYMENT_METHOD_MISSING");
        if (settings.ShippingFeeTreatment != StorefrontShippingFeeTreatment.StoreServiceFee) codes.Add("SHIPPING_TREATMENT_INVALID");
        var owners = shop.IsMultiLegalEntityEnabled
            ? await db.LegalEntities.AsNoTracking().Where(x => x.StoreId == store && x.IsActive && x.SalePriority > 0 &&
                x.DefaultWarehouse != null && x.DefaultWarehouse.IsActive && x.DefaultWarehouse.StoreId == store && x.DefaultWarehouse.LegalEntityId == x.Id)
                .Select(x => x.Id).ToListAsync(ct)
            : await db.Warehouses.AsNoTracking().Where(x => x.StoreId == store && x.Id == settings.SourceWarehouseId && x.IsActive &&
                x.LegalEntity.StoreId == store && x.LegalEntity.IsActive && x.LegalEntityId == settings.SourceLegalEntityId).Select(x => x.LegalEntityId).ToListAsync(ct);
        if (owners.Count == 0 || shop.IsMultiLegalEntityEnabled && shop.MultiLegalEntityActivatedAtUtc is null) codes.Add("SOURCE_OWNER_INVALID");

        var rows = await (from p in db.StorefrontProducts.AsNoTracking()
            join product in db.Products.AsNoTracking() on new { p.StoreId, Id = p.ProductId } equals new { product.StoreId, product.Id }
            join vp in db.StorefrontVariants.AsNoTracking() on new { p.StoreId, Id = p.Id, p.ProductId } equals new { vp.StoreId, Id = vp.StorefrontProductId, vp.ProductId }
            join variant in db.ProductVariants.AsNoTracking() on new { vp.StoreId, Id = vp.VariantId, vp.ProductId } equals new { variant.StoreId, variant.Id, variant.ProductId }
            join unit in db.Units.AsNoTracking() on new { product.StoreId, Id = product.BaseUnitId } equals new { unit.StoreId, unit.Id }
            where p.StoreId == store && p.IsPublished && vp.IsPublished
            select new { Product = product, Variant = variant, Publication = vp, Unit = unit }).ToListAsync(ct);
        if (rows.Count == 0) codes.Add("PUBLICATION_MISSING");
        var publishedProducts = await db.StorefrontProducts.AsNoTracking().Where(x => x.StoreId == store && x.IsPublished).Select(x => x.ProductId).ToListAsync(ct);
        var ids = rows.Select(x => x.Variant.Id).ToList();
        var conversions = await db.ProductUnitConversions.AsNoTracking().Include(x => x.Unit)
            .Where(x => x.StoreId == store && ids.Contains(x.ProductVariantId)).ToListAsync(ct);
        var units = await db.StorefrontSellingUnits.AsNoTracking().Where(x => x.StoreId == store && ids.Contains(x.VariantId) && x.IsPublished).ToListAsync(ct);
        var invalid = publishedProducts.Except(rows.Select(x => x.Product.Id)).Any(); var positive = false;
        foreach (var row in rows)
        {
            row.Variant.Product = row.Product;
            invalid |= !row.Product.IsActive || !row.Product.IsSellable || !row.Variant.IsActive || !row.Unit.IsActive;
            var choices = 0;
            void Price(ProductUnitConversion? conversion, decimal? weight)
            {
                choices++; var price = ProductSalePricePolicy.Pos(row.Variant, conversion, false);
                invalid |= price < 0 || price >= StorefrontValues.MoneyLimit || decimal.Round(price, 4) != price || settings.ShippingByWeight && weight is not > 0;
                positive |= price > 0;
            }
            if (row.Publication.BaseUnitIsPublished) Price(StorefrontCatalogQueries.BaseConversion(row.Product, conversions.Where(x => x.ProductVariantId == row.Variant.Id)), row.Publication.BaseUnitWeightKg);
            foreach (var sale in units.Where(x => x.StorefrontVariantId == row.Publication.Id && x.VariantId == row.Variant.Id))
            {
                var conversion = conversions.SingleOrDefault(x => x.Id == sale.ProductUnitConversionId && x.ProductVariantId == row.Variant.Id && x.UnitId == sale.UnitId &&
                    x.IsActive && x.Factor > 0 && x.Unit.StoreId == store && x.Unit.IsActive && x.UnitId != row.Product.BaseUnitId);
                if (conversion is null) invalid = true; else Price(conversion, sale.WeightKg);
            }
            invalid |= choices == 0;
        }
        if (invalid || rows.Count > 0 && !positive) codes.Add("PUBLICATION_UNIT_PRICE_WEIGHT_INVALID");
        var zones = await db.StorefrontShippingZones.AsNoTracking().Where(x => x.StoreId == store && x.IsActive).ToListAsync(ct);
        var rates = await db.StorefrontShippingRates.AsNoTracking().Where(x => x.StoreId == store && x.IsCurrent).ToListAsync(ct);
        var shippingInvalid = zones.Count == 0;
        foreach (var zone in zones)
        {
            var matches = rates.Where(x => x.ShippingZoneId == zone.Id).ToList();
            if (matches.Count != 1 || zone.CountryCode != "VN" || !Enum.IsDefined(zone.DeliveryMethod) || zone.MinDeliveryDays < 0 || zone.MaxDeliveryDays < zone.MinDeliveryDays ||
                zone.DeliveryMethod == StorefrontDeliveryMethod.Local && string.IsNullOrWhiteSpace(zone.ProvinceCode) || zone.WardCode != "" && zone.ProvinceCode == "") { shippingInvalid = true; continue; }
            var rate = matches[0];
            try
            {
                if (rate.ByWeight != settings.ShippingByWeight) shippingInvalid = true;
                _ = StorefrontShippingPolicy.Quote(store, 1, [new(1, 1)], new(store, rate.Id, rate.Revision, rate.ByWeight, rate.BaseFee, rate.AdditionalFee, rate.FirstWeightKg, rate.AdditionalStepKg, rate.FreeFromGoodsAmount));
            }
            catch (StorefrontRuleException) { shippingInvalid = true; }
        }
        if (shippingInvalid) codes.Add("SHIPPING_ZONE_RATE_INVALID");
        if (settings.QrEnabled) await Qr(settings, owners, store, codes, ct);
        _ = clock; // Optional consistent factory seam; no fabricated clock/date evidence.
        return codes.Distinct(StringComparer.Ordinal).ToArray();
    }

    private async Task Qr(StorefrontSettings settings, List<int> owners, int store, List<string> codes, CancellationToken ct)
    {
        var acb = await db.Set<StoreAcbSettings>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == store && x.Enabled, ct);
        var bank = await db.StoreBankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == store && x.Id == settings.BankAccountId && x.IsActive, ct);
        var registration = new StorefrontBankRegistration(configuration).Get(store);
        var valid = acb is not null && bank is not null && bank.BankCode.Trim().Equals("ACB", StringComparison.OrdinalIgnoreCase) && acb.BankAccountId == bank.Id &&
            acb.MerchantId.Length is > 0 and <= 30 && acb.BeneficiaryName.Length is > 0 and <= 30 && !string.IsNullOrWhiteSpace(acb.ClientId) &&
            !string.IsNullOrWhiteSpace(acb.ClientSecretProtected) && !string.IsNullOrWhiteSpace(acb.CallbackApiKeyProtected) && settings.QrHoldMinutes is > 0 and <= 10080;
        if (valid)
        {
            try { _ = AcbProtocolCore.ValidateEnvironment(acb!.TokenEndpoint, acb.ApiBaseUrl, acb.QrEndpoint); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { valid = false; }
        }
        valid &= registration?.Verified == true && bank is not null && registration.BankAccountId == bank.Id && registration.ReceivingLegalEntityId == settings.ReceivingLegalEntityId &&
            registration.AccountIdentity == GlobalBankReceiptLedger.AccountIdentity(bank.BankCode, bank.AccountNumber) && registration.MerchantId == acb?.MerchantId &&
            registration.TerminalCode == settings.BankTerminalCode && registration.UserCode == settings.BankUserCode;
        valid &= await db.LegalEntities.AsNoTracking().AnyAsync(x => x.StoreId == store && x.Id == settings.ReceivingLegalEntityId && x.IsActive, ct);
        valid &= await db.Set<StorefrontBankCollectionAuthority>().AsNoTracking().AnyAsync(x => x.StoreId == store && x.IsActive && x.BankAccountId == settings.BankAccountId &&
            x.ReceivingLegalEntityId == settings.ReceivingLegalEntityId && owners.Contains(x.SourceLegalEntityId), ct);
        if (bank is not null)
        {
            var identity = GlobalBankReceiptLedger.AccountIdentity(bank.BankCode, bank.AccountNumber);
            var phoneExposure = await db.Set<BankNotificationDevice>().IgnoreQueryFilters().AsNoTracking().AnyAsync(device => device.BankAccountFingerprint == identity &&
                (device.Enabled || db.Set<BankNotificationReceipt>().IgnoreQueryFilters().Any(receipt => receipt.DeviceId == device.Id)), ct);
            valid &= !phoneExposure || registration?.NotificationIdentityMappingVerified == true;
        }
        if (!valid) codes.Add("QR_BINDING_REGISTRATION_INVALID");
    }
}
