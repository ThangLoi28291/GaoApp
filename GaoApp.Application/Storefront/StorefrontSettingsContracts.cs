using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Storefront;

namespace GaoApp.Application.Storefront;

public static class StorefrontPermissions
{
    public const string ViewSettings = "storefront.settings.view";
    public const string ManageSettings = "storefront.settings.manage";
    public const string ManageShipping = "storefront.shipping.manage";
    public const string ViewProducts = "storefront.products.view";
    public const string ManageProducts = "storefront.products.manage";
    public const string ViewOrders = "storefront.orders.view";
    public const string Prepare = "storefront.orders.prepare";
    public const string Dispatch = "storefront.orders.dispatch";
    public const string RecordDelivery = "storefront.orders.delivery";
    public const string ReceiveReturns = "storefront.orders.returns";
    public const string Reconcile = "storefront.orders.reconcile";
    public const string PostSale = "storefront.orders.post-sale";
    public const string ApproveCancellation = "storefront.orders.cancel-approve";
    public const string ApproveRefund = "storefront.refunds.approve";
    public const string ConfirmRefund = "storefront.refunds.confirm";
}

public sealed class StorefrontSettingsRequest
{
    public string Version { get; set; } = "";
    public bool IsEnabled { get; set; }
    [Required, StringLength(200)] public string DisplayName { get; set; } = "";
    [StringLength(2000)] public string? Description { get; set; }
    [StringLength(260)] public string? LogoPath { get; set; }
    [StringLength(30)] public string ContactPhone { get; set; } = "";
    public int? SourceWarehouseId { get; set; }
    public bool CodEnabled { get; set; } = true;
    public bool QrEnabled { get; set; }
    public bool AllowNegativeInventory { get; set; } = true;
    public bool ShippingByWeight { get; set; }
    [Range(1, 10080)] public int? QrHoldMinutes { get; set; }
    public StorefrontShippingFeeTreatment ShippingFeeTreatment { get; set; } = StorefrontShippingFeeTreatment.StoreServiceFee;
    public int? BankAccountId { get; set; }
    public int? ReceivingLegalEntityId { get; set; }
    [StringLength(100)] public string BankTerminalCode { get; set; } = "";
    [StringLength(100)] public string BankUserCode { get; set; } = "";
    public List<int> CollectionSourceLegalEntityIds { get; set; } = [];
}

public sealed class StorefrontShippingZoneRequest
{
    public string SettingsVersion { get; set; } = "";
    public int? Id { get; set; }
    public string Version { get; set; } = "";
    [Required, StringLength(40)] public string Code { get; set; } = "";
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [StringLength(20)] public string ProvinceCode { get; set; } = "";
    [StringLength(20)] public string WardCode { get; set; } = "";
    public StorefrontDeliveryMethod DeliveryMethod { get; set; }
    public bool IsActive { get; set; } = true;
    [Range(0, 365)] public int MinDeliveryDays { get; set; }
    [Range(0, 365)] public int MaxDeliveryDays { get; set; }
    public decimal BaseFee { get; set; }
    public decimal AdditionalFee { get; set; }
    public decimal FirstWeightKg { get; set; } = 1;
    public decimal AdditionalStepKg { get; set; } = 1;
    public decimal? FreeFromGoodsAmount { get; set; }
}

public sealed record StorefrontOption(int Id, string Name);
public sealed record StorefrontShippingZoneDto(int Id, string Version, string Code, string Name,
    string ProvinceCode, string WardCode, StorefrontDeliveryMethod DeliveryMethod,
    bool IsActive, int MinDeliveryDays, int MaxDeliveryDays, int RateRevision, bool ByWeight,
    decimal BaseFee, decimal AdditionalFee, decimal FirstWeightKg, decimal AdditionalStepKg, decimal? FreeFromGoodsAmount);
public sealed record StorefrontSettingsPage(StorefrontSettingsRequest Settings, int Revision,
    bool UsesMultipleLegalEntities, bool CanManageSettings, bool CanManageShipping,
    IReadOnlyList<StorefrontOption> Warehouses, IReadOnlyList<StorefrontOption> BankAccounts,
    IReadOnlyList<StorefrontOption> LegalEntities, IReadOnlyList<StorefrontShippingZoneDto> Zones);

public interface IStorefrontSettingsService
{
    Task<StorefrontSettingsPage> GetAsync(CancellationToken ct = default);
    Task SaveAsync(StorefrontSettingsRequest request, CancellationToken ct = default);
    Task SaveZoneAsync(StorefrontShippingZoneRequest request, CancellationToken ct = default);
}
