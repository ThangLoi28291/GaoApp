using GaoApp.Domain.Common;

namespace GaoApp.Domain.Storefront;

public enum StorefrontDeliveryMethod { Local, Nationwide }
public enum StorefrontPaymentState { Unpaid, Pending, Confirmed, Review }
public enum StorefrontDeliveryState { NotPlanned, Ready, Picking, HandedOver, Delivered, ReturnPending, Returned }
public enum StorefrontRefundState { None, Requested, Approved, Executed, Rejected }
public enum StorefrontMediaKind { Image, Model3D }

// WEB01 persistence foundation. Public checkout/fulfilment adapters are later steps.
public sealed class StorefrontSettings : BaseStoreEntity, IAuditTrackedEntity
{
    public bool IsEnabled { get; set; }
    public int Revision { get; set; } = 1;
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string? LogoPath { get; set; }
    public string ContactPhone { get; set; } = "";
    public int? SourceWarehouseId { get; set; }
    public int? SourceLegalEntityId { get; set; }
    public bool CodEnabled { get; set; } = true;
    public bool QrEnabled { get; set; }
    public bool AllowNegativeInventory { get; set; } = true;
    public bool ShippingByWeight { get; set; }
    public int? QrHoldMinutes { get; set; }
    public StorefrontShippingFeeTreatment ShippingFeeTreatment { get; set; } = StorefrontShippingFeeTreatment.StoreServiceFee;
    public int? BankAccountId { get; set; }
    public int? ReceivingLegalEntityId { get; set; }
    public string BankTerminalCode { get; set; } = "";
    public string BankUserCode { get; set; } = "";
}

public sealed class StorefrontBankCollectionAuthority : BaseStoreEntity, IAuditTrackedEntity
{
    public bool IsActive { get; set; } = true;
    public int BankAccountId { get; set; }
    public int ReceivingLegalEntityId { get; set; }
    public int SourceLegalEntityId { get; set; }
}

public sealed class StorefrontProduct : BaseStoreEntity, IAuditTrackedEntity
{
    public int ProductId { get; set; }
    public bool IsPublished { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? DisplayGroup { get; set; }
    public int SortOrder { get; set; }
}

public sealed class StorefrontVariant : BaseStoreEntity, IAuditTrackedEntity
{
    public int StorefrontProductId { get; set; }
    public int ProductId { get; set; }
    public int VariantId { get; set; }
    public bool IsPublished { get; set; }
    public bool BaseUnitIsPublished { get; set; }
    public decimal? BaseUnitWeightKg { get; set; }
}

public sealed class StorefrontSellingUnit : BaseStoreEntity, IAuditTrackedEntity
{
    public int StorefrontVariantId { get; set; }
    public int VariantId { get; set; }
    public int ProductUnitConversionId { get; set; }
    public int UnitId { get; set; }
    public bool IsPublished { get; set; }
    public decimal? WeightKg { get; set; }
}

public sealed class StorefrontMedia : BaseStoreEntity, IAuditTrackedEntity
{
    public int StorefrontProductId { get; set; }
    public int MediaAssetId { get; set; }
    public StorefrontMediaKind Kind { get; set; }
    public string AltText { get; set; } = "";
    public int SortOrder { get; set; }
}

public sealed class StorefrontShippingZone : BaseStoreEntity, IAuditTrackedEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string CountryCode { get; set; } = "VN";
    public string ProvinceCode { get; set; } = "";
    public string WardCode { get; set; } = "";
    public StorefrontDeliveryMethod DeliveryMethod { get; set; }
    public bool IsActive { get; set; } = true;
    public int Revision { get; set; } = 1;
    public int MinDeliveryDays { get; set; }
    public int MaxDeliveryDays { get; set; }
}

public sealed class StorefrontShippingRate : BaseStoreEntity, IAuditTrackedEntity
{
    public int ShippingZoneId { get; set; }
    public int Revision { get; set; } = 1;
    public bool IsCurrent { get; set; } = true;
    public bool ByWeight { get; set; }
    public decimal BaseFee { get; set; }
    public decimal AdditionalFee { get; set; }
    public decimal FirstWeightKg { get; set; } = 1;
    public decimal AdditionalStepKg { get; set; } = 1;
    public decimal? FreeFromGoodsAmount { get; set; }
}
