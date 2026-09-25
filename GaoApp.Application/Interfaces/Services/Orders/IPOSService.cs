using GaoApp.Application.Common;
using GaoApp.Application.DTOs.POS;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Orders;

/// <summary>
/// Service POS.
///
/// Ghi chú kiến trúc barcode:
/// - ProductVariant không còn Barcode
/// - Barcode lookup chính thức đi qua IBarcodeLookupService
/// - Khi add tay theo variant, POS sẽ tự resolve:
///   + đơn vị bán mặc định
///   + barcode đại diện
///   + multiplier tương ứng
/// từ ProductUnitConversion + ProductVariantUnitBarcode
/// </summary>
public interface IPOSService
{
    Task<int> CreateDraftAsync(int? customerId = null, string? note = null, CancellationToken ct = default);

    Task<OrderDraftDto> GetDraftAsync(int orderId, CancellationToken ct = default);

    Task<OrderDraftDto> AddItemAsync(
      int orderId,
      int variantId,
      int? productUnitConversionId = null,
      decimal qty = 1,
      CancellationToken ct = default);

    Task<OrderDraftDto> AddItemByBarcodeAsync(int orderId, string barcode, decimal qty = 1, CancellationToken ct = default);

    Task<OrderDraftDto> UpdateLineQtyAsync(int lineId, decimal qty, CancellationToken ct = default);

    Task<OrderDraftDto> RemoveLineAsync(int lineId, CancellationToken ct = default);

    Task<OrderDraftDto> AddPaymentAsync(int orderId, UpsertPaymentRequest dto, CancellationToken ct = default);

    Task<OrderDraftDto> RemovePaymentAsync(int paymentId, CancellationToken ct = default);

    Task<OrderDraftDto> FinalizeAsync(int orderId, CancellationToken ct = default);
    Task<OrderDraftDto> FinalizeCreditAsync(int orderId, FinalizeCreditRequest request, CancellationToken ct = default) => throw new NotSupportedException();

    Task CancelAsync(int orderId, string? reason = null, CancellationToken ct = default);

    Task<OrderReceiptDto> GetReceiptAsync(int orderId, CancellationToken ct = default);

    Task<PagedResult<OrderListItemDto>> GetOrdersAsync(OrderListQueryDto query, CancellationToken ct = default);

    Task<HoldOrderResultDto> HoldAndCreateNewDraftAsync(int orderId, string? holdNote = null, CancellationToken ct = default);

    Task<int> ResumeHeldAsync(int orderId, CancellationToken ct = default);

    Task<List<HeldOrderDto>> GetHeldOrdersAsync(CancellationToken ct = default);

    Task<CurrentCartDto> GetCurrentCartAsync(CancellationToken ct = default);

    Task SetCurrentCartAsync(int orderId, CancellationToken ct = default);

    Task<List<ActiveDraftOrderDto>> GetDraftOrdersAsync(CancellationToken ct = default);

    Task<POSScreenDto> GetPOSScreenAsync(CancellationToken ct = default);

    Task<OrderDraftDto> EnsureCurrentCartAsync(CancellationToken ct = default);

    Task<OrderDraftDto> ScanToCurrentCartAsync(string barcode, decimal qty = 1, CancellationToken ct = default);

    Task<OrderDraftDto> AddPaymentToCurrentCartAsync(QuickAddPaymentRequest dto, CancellationToken ct = default);

    Task<OrderDraftDto> FinalizeCurrentCartAsync(CancellationToken ct = default);

    Task<HoldOrderResultDto> HoldCurrentCartAsync(string? holdNote = null, CancellationToken ct = default);

    Task CancelCurrentCartAsync(string? reason = null, CancellationToken ct = default);

    Task<int> CreateAndSwitchNewCartAsync(int? customerId = null, string? note = null, CancellationToken ct = default);

    Task<List<POSProductSearchItemDto>> SearchProductsForPOSAsync(string keyword, int take = 20, CancellationToken ct = default);

    Task<List<POSCustomerSearchItemDto>> SearchCustomersForPOSAsync(string keyword, int take = 20, CancellationToken ct = default);

    Task<OrderDraftDto> SetCustomerForCurrentCartAsync(
       int customerId,
       bool repriceExistingLines = false,
       CancellationToken ct = default);

    Task<OrderDraftDto> ClearCustomerForCurrentCartAsync(CancellationToken ct = default);

    Task<OrderDraftDto> CreateCustomerAndSetForCurrentCartAsync(CreatePOSCustomerDto dto, CancellationToken ct = default);

    Task<OrderDraftDto> UpdateCurrentCartNoteAsync(string? note, CancellationToken ct = default);

    Task<OrderDraftDto> UpdateCurrentCartDiscountAsync(decimal discountAmount, CancellationToken ct = default);

    Task<OrderDraftDto> UpdateLineDiscountAsync(int lineId, decimal discountAmount, CancellationToken ct = default);

    Task<OrderReceiptDto> VoidCompletedOrderAsync(int orderId, string reason, CancellationToken ct = default);

    Task<OrderReceiptDto> RefundCompletedOrderAsync(
     int orderId,
     string reason,
     PaymentMethod refundMethod,
     string? refundReferenceCode = null,
     string? refundProvider = null,
     CancellationToken ct = default);

    Task<POSShiftDashboardDto> GetCurrentShiftDashboardAsync(CancellationToken ct = default);
    Task<OrderDraftDto> ApplyRewardVouchersToCurrentCartAsync(
    ApplyRewardVouchersRequest request,
    CancellationToken ct = default);

    Task<OrderDraftDto> ClearRewardVouchersFromCurrentCartAsync(
        CancellationToken ct = default);
}