using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Storefront;

namespace GaoApp.Application.Storefront;

public sealed class StorefrontCheckoutSelection
{
    public int ProductId { get; set; }
    public int VariantId { get; set; }
    public int UnitId { get; set; }
    public decimal Quantity { get; set; }
}
public sealed class StorefrontQuoteRequest
{
    public StorefrontPaymentMethod PaymentMethod { get; set; } = StorefrontPaymentMethod.Cod;
    public List<StorefrontCheckoutSelection> Items { get; set; } = [];
    [Required(ErrorMessage = "Nhập họ và tên người nhận."), StringLength(200, ErrorMessage = "Họ tên tối đa 200 ký tự.")] public string RecipientName { get; set; } = "";
    [Required(ErrorMessage = "Nhập số điện thoại nhận hàng."), StringLength(30, ErrorMessage = "Số điện thoại quá dài.")] public string RecipientPhone { get; set; } = "";
    [Required(ErrorMessage = "Chọn tỉnh hoặc thành phố."), StringLength(20, ErrorMessage = "Tỉnh/thành không hợp lệ.")] public string ProvinceCode { get; set; } = "";
    [Required(ErrorMessage = "Chọn phường hoặc xã."), StringLength(20, ErrorMessage = "Phường/xã không hợp lệ.")] public string WardCode { get; set; } = "";
    [Required(ErrorMessage = "Nhập số nhà, đường hoặc thôn/ấp."), StringLength(800, ErrorMessage = "Địa chỉ đường tối đa 800 ký tự.")] public string StreetAddress { get; set; } = "";
    [StringLength(1000, ErrorMessage = "Ghi chú tối đa 1.000 ký tự.")] public string? Note { get; set; }
}
public sealed class StorefrontPlaceOrderRequest
{
    public Guid ClientRequestId { get; set; }
    public Guid QuoteToken { get; set; }
    [Required, StringLength(64, MinimumLength = 64)] public string Fingerprint { get; set; } = "";
}
public sealed record StorefrontAddressWard(string Code, string Name);
public sealed record StorefrontAddressProvince(string Code, string Name, IReadOnlyList<StorefrontAddressWard> Wards);
public sealed record StorefrontAddressDirectory(string Version, string Source, string Fingerprint, IReadOnlyList<StorefrontAddressProvince> Provinces);
public interface IStorefrontAddressCatalog
{
    Task<StorefrontAddressDirectory> GetAsync(CancellationToken ct = default);
}
public sealed record StorefrontRecipient(string Name, string Phone, string ProvinceCode, string ProvinceName,
    string WardCode, string WardName, string StreetAddress, string? Note);
public sealed record StorefrontCheckoutLine(int Index, int ProductId, int VariantId, int UnitId, int? ProductUnitConversionId,
    string Name, string VariantName, string UnitName, decimal Quantity, decimal BaseMultiplier, decimal BaseQuantity,
    decimal UnitPrice, decimal GrossAmount, decimal Discount, decimal RoundingAdjustment, decimal NetAmount,
    decimal? WeightKg, bool IsGift, string? PromotionName, string PromotionSnapshotJson, int? BaseUnitId = null, string? BaseUnitName = null)
{
    // GrossAmount already includes RoundingAdjustment; Net = GrossAmount - Discount.
    public string UnitPriceText => StorefrontCheckoutText.Money(UnitPrice, true);
    public string GrossAmountText => StorefrontCheckoutText.Money(GrossAmount);
    public string DiscountText => StorefrontCheckoutText.Money(Discount);
    public string RoundingAdjustmentText => StorefrontCheckoutText.Money(RoundingAdjustment);
    public string NetAmountText => StorefrontCheckoutText.Money(NetAmount);
}
public sealed record StorefrontQuoteDto(Guid Token, string Fingerprint, DateTime ExpiresAtUtc, StorefrontRecipient Recipient,
    IReadOnlyList<StorefrontCheckoutLine> Lines, decimal GoodsAmount, decimal ShippingCharge, decimal TotalDue, decimal? TotalWeightKg,
    StorefrontDeliveryMethod DeliveryMethod, string ShippingZoneName, int MinDeliveryDays, int MaxDeliveryDays, bool HasInventoryShortage,
    StorefrontPaymentMethod PaymentMethod = StorefrontPaymentMethod.Cod)
{
    public string GoodsAmountText => StorefrontCheckoutText.Money(GoodsAmount);
    public string ShippingChargeText => StorefrontCheckoutText.Money(ShippingCharge);
    public string TotalDueText => StorefrontCheckoutText.Money(TotalDue);
}
public enum StorefrontPlacementStatus { Placed, QuoteChanged }
public sealed record StorefrontPlacedOrder(string Code, string GuestLink, decimal TotalDue, DateTime CreatedAtUtc,StorefrontPaymentMethod PaymentMethod=StorefrontPaymentMethod.Cod)
{
    public string TotalDueText => StorefrontCheckoutText.Money(TotalDue);
}
public sealed record StorefrontPlacementResult(StorefrontPlacementStatus Status, StorefrontPlacedOrder? Order, StorefrontQuoteDto? ReplacementQuote, string Message);
public sealed record StorefrontGuestOrderLine(string Name, string UnitName, decimal Quantity, decimal UnitPrice,
    decimal Discount, decimal RoundingAdjustment, decimal NetAmount, bool IsGift, string? PromotionName, string? VariantName = null)
{
    public string UnitPriceText => StorefrontCheckoutText.Money(UnitPrice, true);
    public string DiscountText => StorefrontCheckoutText.Money(Discount);
    public string RoundingAdjustmentText => StorefrontCheckoutText.Money(RoundingAdjustment);
    public string NetAmountText => StorefrontCheckoutText.Money(NetAmount);
}
public sealed record StorefrontGuestOrder(string Code, DateTime CreatedAtUtc, StorefrontOrderPhase Phase,
    StorefrontPaymentState PaymentState, StorefrontDeliveryState DeliveryState, StorefrontRefundState RefundState,
    StorefrontPaymentMethod PaymentMethod, StorefrontRecipient Recipient, IReadOnlyList<StorefrontGuestOrderLine> Lines,
    decimal GoodsAmount, decimal ShippingCharge, decimal TotalDue, string Version,
    IReadOnlyList<StorefrontGuestShipment>? Shipments = null, bool HasCustodyIncident = false)
{
    public string GoodsAmountText => StorefrontCheckoutText.Money(GoodsAmount);
    public string ShippingChargeText => StorefrontCheckoutText.Money(ShippingCharge);
    public string TotalDueText => StorefrontCheckoutText.Money(TotalDue);
}
public sealed record StorefrontGuestShipment(string Label, string Status, string? CarrierName, string? TrackingCode);
public interface IStorefrontCheckoutService
{
    Task<StorefrontQuoteDto> QuoteAsync(StorefrontQuoteRequest request, CancellationToken ct = default);
    Task<StorefrontPlacementResult> PlaceAsync(StorefrontPlaceOrderRequest request, CancellationToken ct = default);
    Task<StorefrontGuestOrder?> GetGuestOrderAsync(string token, CancellationToken ct = default);
}
public sealed record StorefrontOrderInboxItem(int Id, string Code, DateTime CreatedAtUtc, string RecipientName,
    StorefrontOrderPhase Phase, decimal TotalDue, bool HasInventoryShortage,
    StorefrontPaymentMethod PaymentMethod=StorefrontPaymentMethod.Cod,StorefrontPaymentState PaymentState=StorefrontPaymentState.Unpaid,
    StorefrontDeliveryMethod DeliveryMethod=StorefrontDeliveryMethod.Local);
public sealed record StorefrontOrderInboxPage(int Page, int PageSize, int TotalCount, IReadOnlyList<StorefrontOrderInboxItem> Items,
    int AwaitingPreparationCount = 0, int NewestId = 0);
public sealed record StorefrontInboxNotification(int NewOrders, int AwaitingPreparationCount, int NewestId);
public interface IStorefrontOrderInboxService
{
    Task<StorefrontOrderInboxPage> ListAsync(int page = 1, int pageSize = 25, CancellationToken ct = default);
    Task<StorefrontGuestOrder> GetAsync(int id, CancellationToken ct = default);
    Task<StorefrontInboxNotification> NotificationsAsync(int afterId, CancellationToken ct = default);
}
internal static class StorefrontCheckoutText
{
    internal static string Money(decimal value, bool fractional = false) => value.ToString(fractional ? "#,0.##" : "#,0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " đ";
}
