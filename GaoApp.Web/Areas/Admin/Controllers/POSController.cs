using GaoApp.Domain.Enums;
using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Application.DTOs.POS;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Web.Common.POS;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using GaoApp.Application.DTOs.POSPaymentQrs;
using GaoApp.Application.Interfaces.Services.POSPaymentQrs;
using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Interfaces.Repositories.Orders;

using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/pos")]
[Authorize(Policy = PermissionCodes.Pos.Order.View)]
[AutoValidateAntiforgeryToken]
[ServiceFilter(typeof(GaoApp.Web.Services.Offline.PosOperationFilter))]
public class POSController : BasePOSPageController
{
    private readonly IPOSService _pos;
    private readonly IPOSShiftService _posShiftService;
    private readonly IPosRealtimeNotifier _posRealtimeNotifier;
    private readonly IPOSPaymentQrService _paymentQrService;

    public POSController(
        IPOSService pos,
        IPOSRuntimeContextAccessor runtimeContext,
        IPOSShiftService posShiftService,
        IPosRealtimeNotifier posRealtimeNotifier,
        IPOSPaymentQrService paymentQrService)
        : base(runtimeContext, posShiftService)
    {
        _pos = pos;
        _posShiftService = posShiftService;
        _posRealtimeNotifier = posRealtimeNotifier;
        _paymentQrService = paymentQrService;
    }
    public sealed class SetInvoiceIssuanceRouteRequest
    {
        public InvoiceIssuanceRoute Route { get; set; }
    }
    private int CurrentStoreIdValue()
    {
        return int.TryParse(User.FindFirstValue("store_id"), out var id) ? id : 0;
    }

    private string CurrentTerminalId()
    {
        return User.FindFirstValue("terminal_id") ?? "";
    }
    private static class PosRealtimeEventTypes
    {
        // Terminal-local
        public const string DraftCreated = "draft_created";
        public const string CartChanged = "cart_changed";
        public const string PaymentChanged = "payment_changed";
        public const string CurrentCartSwitched = "current_cart_switched";
        public const string CartEnsured = "cart_ensured";
        public const string CartCreated = "cart_created";
        public const string CustomerChanged = "customer_changed";

        // Store-wide
        public const string HeldChanged = "held_changed";
        public const string HeldResumed = "held_resumed";
        public const string OrderFinalized = "order_finalized";
        public const string CartCancelled = "cart_cancelled";
        public const string OrderVoided = "order_voided";
        public const string OrderRefunded = "order_refunded";
    }

    /// <summary>
    /// Event chỉ dành cho terminal hiện tại.
    /// Dùng cho các thay đổi riêng 1 máy:
    /// - cart
    /// - payment
    /// - customer
    /// - summary
    /// </summary>
    private async Task NotifyTerminalAsync(
        string eventType,
        int? orderId = null,
        int? relatedOrderId = null,
        bool cartChanged = false,
        bool summaryChanged = false,
        bool paymentsChanged = false,
        bool customerChanged = false,
        string? message = null,
        CancellationToken ct = default)
    {
        await _posRealtimeNotifier.NotifyTerminalAsync(
            storeId: CurrentStoreIdValue(),
            terminalId: CurrentTerminalId(),
            eventType: eventType,
            orderId: orderId,
            relatedOrderId: relatedOrderId,
            cartChanged: cartChanged,
            heldChanged: false,
            summaryChanged: summaryChanged,
            paymentsChanged: paymentsChanged,
            customerChanged: customerChanged,
            message: message,
            ct: ct);
    }

    /// <summary>
    /// Event dùng chung toàn store.
    /// Chỉ dùng cho:
    /// - held list
    /// - finalize / void / refund
    /// - thông báo chung
    ///
    /// QUAN TRỌNG:
    /// Store-wide event KHÔNG được kéo theo cartChanged/summaryChanged/paymentsChanged/customerChanged,
    /// nếu không terminal khác sẽ reload giỏ hàng hiện tại.
    /// </summary>
    private async Task NotifyStoreAsync(
        string eventType,
        int? orderId = null,
        int? relatedOrderId = null,
        bool heldChanged = false,
        string? message = null,
        CancellationToken ct = default)
    {
        await _posRealtimeNotifier.NotifyStoreAsync(
            storeId: CurrentStoreIdValue(),
            eventType: eventType,
            terminalId: CurrentTerminalId(),
            orderId: orderId,
            relatedOrderId: relatedOrderId,
            cartChanged: false,
            heldChanged: heldChanged,
            summaryChanged: false,
            paymentsChanged: false,
            customerChanged: false,
            message: message,
            ct: ct);
    }
    [HttpPost("cart/current/payment-qr")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Payment.Create)]
    public async Task<IActionResult> CreatePaymentQrForCurrentCart(
    [FromBody] CreatePOSPaymentQrRequest request,
    [FromServices] GaoApp.Web.Services.Acb.AcbPaymentService acb,
    CancellationToken ct = default)
    {
        var currentCart = await _pos.GetCurrentCartAsync(ct);

        if (!currentCart.CurrentOrderId.HasValue)
        {
            return BadRequest("Chưa có giỏ hiện tại.");
        }

        var draft = await _pos.GetDraftAsync(
            currentCart.CurrentOrderId.Value,
            ct);

        if (draft == null)
        {
            return BadRequest("Không tìm thấy draft đơn hàng.");
        }

        var qr = await acb.CreateQrAsync(draft, request ?? new CreatePOSPaymentQrRequest(), _paymentQrService, ct);

        await NotifyTerminalAsync(
            eventType: "payment_qr_created",
            orderId: qr.OrderId,
            summaryChanged: true,
            paymentsChanged: false,
            message: "Đã tạo QR chuyển khoản.",
            ct: ct);

        return Ok(qr);
    }
    [HttpPost("payment-qr/{qrId:int}/manual-confirm")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Payment.Create)]
    [Authorize(Policy = PermissionCodes.Pos.Order.Finalize)]
    public async Task<IActionResult> ManualConfirmPaymentQr(int qrId, [FromServices] GaoApp.Web.Services.Acb.AcbPaymentService acb, CancellationToken ct = default)
    {
        return Ok(await acb.ConfirmManualQrAsync(qrId, ct));
    }

    [HttpPost("payment-qr/{qrId:int}/cancel")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Payment.Create)]
    public async Task<IActionResult> CancelPaymentQr(int qrId, [FromServices] GaoApp.Web.Services.Acb.AcbPaymentService acb, CancellationToken ct = default)
    {
        await acb.CancelSavedQrAsync(qrId, ct);
        return Ok(new { success = true, message = "Đã hủy QR chuyển khoản." });
    }

    public sealed class CancelPaymentQrByContentRequest
    {
        public string? Content { get; set; }
    }
    [HttpPost("payment-qr/cancel-by-content")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Payment.Create)]
    public async Task<IActionResult> CancelPaymentQrByContent(
    [FromBody] CancelPaymentQrByContentRequest request,
    CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request?.Content))
        {
            return Ok(new
            {
                success = true,
                message = "Không có mã QR cần hủy."
            });
        }

        await _paymentQrService.CancelByContentAsync(request.Content, ct);

        return Ok(new
        {
            success = true,
            message = "Đã cập nhật trạng thái QR liên quan."
        });
    }

    [HttpPost("draft")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> CreateDraft(int? customerId = null, string? note = null, CancellationToken ct = default)
    {
        var orderId = await _pos.CreateDraftAsync(customerId, note, ct);

        await NotifyTerminalAsync(
            eventType: PosRealtimeEventTypes.DraftCreated,
            orderId: orderId,
            cartChanged: true,
            summaryChanged: true,
            customerChanged: customerId.HasValue,
            message: "Đã tạo draft mới.",
            ct: ct);

        return Ok(new { orderId });
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> GetDraft(int orderId, CancellationToken ct = default)
        => Ok(await _pos.GetDraftAsync(orderId, ct));

    [HttpPost("{orderId:int}/items")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> AddItem(
    int orderId,
    [FromQuery] int variantId,
    [FromQuery] int? productUnitConversionId = null,
    [FromQuery] decimal qty = 1,
    CancellationToken ct = default)
    {
       
            var draft = await _pos.AddItemAsync(orderId, variantId, productUnitConversionId, qty, ct);

            await NotifyTerminalAsync(
                eventType: PosRealtimeEventTypes.CartChanged,
                orderId: orderId,
                cartChanged: true,
                summaryChanged: true,
                message: "Đã thêm sản phẩm vào giỏ.",
                ct: ct);

            return Ok(draft);
       
    }

    [HttpPost("{orderId:int}/barcode")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> AddByBarcode(int orderId, string barcode, decimal qty = 1, CancellationToken ct = default)
    {
        var draft = await _pos.AddItemByBarcodeAsync(orderId, barcode, qty, ct);

        await NotifyTerminalAsync(
            eventType: PosRealtimeEventTypes.CartChanged,
            orderId: orderId,
            cartChanged: true,
            summaryChanged: true,
            message: "Đã quét barcode vào giỏ.",
            ct: ct);

        return Ok(draft);
    }

    [HttpPatch("lines/{lineId:int}")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> UpdateQty(int lineId, decimal qty, CancellationToken ct = default)
    {
        var draft = await _pos.UpdateLineQtyAsync(lineId, qty, ct);

        await NotifyTerminalAsync(
            eventType: PosRealtimeEventTypes.CartChanged,
            orderId: draft?.OrderId,
            cartChanged: true,
            summaryChanged: true,
            message: "Đã cập nhật số lượng dòng hàng.",
            ct: ct);

        return Ok(draft);
    }

    [HttpDelete("lines/{lineId:int}")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> RemoveLine(int lineId, CancellationToken ct = default)
    {
        var draft = await _pos.RemoveLineAsync(lineId, ct);

        await NotifyTerminalAsync(
            eventType: PosRealtimeEventTypes.CartChanged,
            orderId: draft?.OrderId,
            cartChanged: true,
            summaryChanged: true,
            message: "Đã xóa dòng hàng khỏi giỏ.",
            ct: ct);

        return Ok(draft);
    }

    [HttpPost("{orderId:int}/payments")]
    [Authorize(Policy = PermissionCodes.Pos.Payment.Create)]
    public async Task<IActionResult> AddPayment(int orderId, [FromBody] UpsertPaymentRequest dto, CancellationToken ct)
    {
        var draft = await _pos.AddPaymentAsync(orderId, dto, ct);

        await NotifyTerminalAsync(
            eventType: PosRealtimeEventTypes.PaymentChanged,
            orderId: orderId,
            summaryChanged: true,
            paymentsChanged: true,
            message: "Đã thêm thanh toán.",
            ct: ct);

        return Ok(draft);
    }

    [HttpDelete("payments/{paymentId:int}")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Payment.Create)]
    public async Task<IActionResult> RemovePayment(int paymentId, CancellationToken ct = default)
    {
       
            var draft = await _pos.RemovePaymentAsync(paymentId, ct);

            await NotifyTerminalAsync(
                eventType: PosRealtimeEventTypes.PaymentChanged,
                orderId: draft?.OrderId,
                summaryChanged: true,
                paymentsChanged: true,
                message: "Đã xóa thanh toán.",
                ct: ct);

            return Ok(draft);

    }

    [HttpPost("{orderId:int}/finalize-credit")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Finalize)]
    [Authorize(Policy = PermissionCodes.CustomerDebt.Sell)]
    public async Task<IActionResult> FinalizeCredit(int orderId, [FromBody] FinalizeCreditRequest request, CancellationToken ct)
    {
        if (Request.Headers["X-POS-Offline"] == "1")
            throw new BusinessRuleException("Ghi công nợ cần thực hiện trực tuyến để kiểm tra khách và số dư mới nhất.");
        var result = await _pos.FinalizeCreditAsync(orderId, request, ct);
        await NotifyStoreAsync(PosRealtimeEventTypes.OrderFinalized, orderId: orderId, heldChanged: true,
            message: "Đã chốt đơn và ghi công nợ khách hàng.", ct: ct);
        return Ok(result);
    }

    [HttpPost("{orderId:int}/finalize")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Finalize)]
    public async Task<IActionResult> Finalize(int orderId, CancellationToken ct)
    {
        if (Request.Headers["X-POS-Offline"] == "1" && (await _pos.GetDraftAsync(orderId, ct)).DepositAmount > 0)
            throw new BusinessRuleException("Đơn sử dụng tiền cọc cần chốt trực tuyến.");
        var result = await _pos.FinalizeAsync(orderId, ct);

        await NotifyStoreAsync(
            eventType: PosRealtimeEventTypes.OrderFinalized,
            orderId: orderId,
            heldChanged: true,
            message: "Đơn đã được chốt.",
            ct: ct);

        return Ok(result);
    }
    [HttpPost("{orderId:int}/invoice-route")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Finalize)]
    public async Task<IActionResult> SetInitialInvoiceIssuanceRoute(
    int orderId,
    [FromBody] SetInvoiceIssuanceRouteRequest request,
    [FromServices] IInvoiceIssuanceRouteService routeService,
    [FromServices] IOrderRepository orders,
    CancellationToken ct)
    {
        if (request == null ||
            request.Route is not
                (InvoiceIssuanceRoute.Automatic or
                 InvoiceIssuanceRoute.Manual))
        {
            return BadRequest(new
            {
                success = false,
                message =
                    "Phương thức phát hành hóa đơn không hợp lệ."
            });
        }

        if (Request.Headers.ContainsKey("X-POS-Operation-Id"))
        {
            // A finalized order is no longer necessarily the current cart. Bind replay to
            // its originating shift, whose store/terminal/owner are checked by the POS service.
            var shift = await _posShiftService.GetCurrentOpenAsync(ct);
            var order = await orders.GetByIdAsync(orderId, ct);
            if (order == null || order.StoreId != CurrentStoreIdValue())
                return NotFound(new { success = false, message = "Không tìm thấy đơn hàng tại cửa hàng hiện tại." });
            if (shift == null ||
                !int.TryParse(Request.Headers["X-POS-Shift-Id"], out var originalShiftId) ||
                originalShiftId != shift.Id || order.POSShiftId != shift.Id)
                return Conflict(new { success = false, message = "Đơn hàng không thuộc ca gốc của lựa chọn hóa đơn. Cần đối soát giao dịch tại quầy." });
        }

        var result =
            await routeService.SetInitialRouteAsync(
                orderId,
                request.Route,
                ct);

        if (!result.IsSuccess)
        {
            var payload = new
            {
                success = false,
                code = result.Error?.Code,
                message =
                    result.Error?.Message ??
                    "Không thể lưu phương thức phát hành hóa đơn."
            };

            return result.Error?.Code switch
            {
                "NotFound" => NotFound(payload),
                "Conflict" => Conflict(payload),
                _ => BadRequest(payload)
            };
        }

        return Ok(new
        {
            success = true,
            orderId = result.Value.OrderId,
            route = result.Value.Route.ToString(),
            message =
                result.Value.Route ==
                InvoiceIssuanceRoute.Manual
                    ? "Đã chuyển hóa đơn sang chờ phát hành thủ công."
                    : "Đã đưa hóa đơn vào hàng đợi phát hành tự động."
        });
    }

    [HttpPost("{orderId:int}/cancel")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> Cancel(int orderId, string? reason = null, CancellationToken ct = default)
    {
        await _pos.CancelAsync(orderId, reason, ct);

        await NotifyStoreAsync(
            eventType: PosRealtimeEventTypes.CartCancelled,
            orderId: orderId,
            heldChanged: true,
            message: "Đơn đã bị hủy.",
            ct: ct);

        return Ok(new { success = true });
    }

    [HttpGet("orders/{orderId:int}")]
    public async Task<IActionResult> GetReceipt(int orderId, CancellationToken ct = default)
        => Ok(await _pos.GetReceiptAsync(orderId, ct));

    [HttpGet("orders/{orderId:int}/receipt")]
    public async Task<IActionResult> GetReceiptData(int orderId, CancellationToken ct = default)
        => Ok(await _pos.GetReceiptAsync(orderId, ct));

    [HttpGet("orders/{orderId:int}/print")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Reprint)]
    public async Task<IActionResult> PrintReceipt(
        int orderId,
        [FromServices] GaoApp.Web.Services.Printing.ReceiptTemplateService templates,
        [FromServices] IPOSRuntimeContextAccessor runtime,
        [FromQuery] string? size = null,
        [FromQuery] bool autoPrint = true,
        CancellationToken ct = default)
    {
        var model = await _pos.GetReceiptAsync(orderId, ct);

        return View("~/Areas/Admin/Views/ReceiptTemplates/Print.cshtml",
            new GaoApp.Web.Services.Printing.ReceiptPrintModel(model, await templates.ListAsync(ct), CurrentStoreId, runtime.TerminalId, autoPrint, size));
    }

    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] GaoApp.Domain.Enums.OrderStatus? status,
        [FromQuery] string? keyword,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = new OrderListQueryDto
        {
            FromDate = fromDate,
            ToDate = toDate,
            Status = status,
            Keyword = keyword,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await _pos.GetOrdersAsync(query, ct));
    }

    [HttpPost("orders/{orderId:int}/hold")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Hold)]
    public async Task<IActionResult> HoldOrder(int orderId, [FromBody] HoldOrderRequest request, CancellationToken ct)
    {
        var result = await _pos.HoldAndCreateNewDraftAsync(orderId, request?.HoldNote, ct);

        await NotifyStoreAsync(
            eventType: PosRealtimeEventTypes.HeldChanged,
            orderId: orderId,
            relatedOrderId: null,
            heldChanged: true,
            message: "Đã giữ đơn và tạo draft mới.",
            ct: ct);

        return Ok(result);
    }

    [HttpGet("orders/held")]
    public async Task<IActionResult> GetHeldOrders(CancellationToken ct)
    {
        var result = await _pos.GetHeldOrdersAsync(ct);
        return Ok(result);
    }

    [HttpPost("orders/{orderId:int}/resume")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Hold)]
    public async Task<IActionResult> ResumeHeldOrder(int orderId, CancellationToken ct)
    {
        var resumedOrderId = await _pos.ResumeHeldAsync(orderId, ct);

        await NotifyStoreAsync(
            eventType: PosRealtimeEventTypes.HeldResumed,
            orderId: resumedOrderId,
            relatedOrderId: orderId,
            heldChanged: true,
            message: "Đã mở lại đơn giữ.",
            ct: ct);

        return Ok(new
        {
            message = "Đã mở lại đơn giữ thành công.",
            orderId = resumedOrderId
        });
    }

    [HttpGet("cart/current")]
    public async Task<IActionResult> GetCurrentCart(CancellationToken ct)
    {
        var result = await _pos.GetCurrentCartAsync(ct);
        return Ok(result);
    }

    [HttpPost("cart/current")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> SetCurrentCart([FromBody] SwitchCurrentCartRequest request, CancellationToken ct)
    {
        await _pos.SetCurrentCartAsync(request.OrderId, ct);

        await NotifyTerminalAsync(
            eventType: PosRealtimeEventTypes.CurrentCartSwitched,
            orderId: request.OrderId,
            cartChanged: true,
            summaryChanged: true,
            message: "Đã chuyển giỏ hiện tại.",
            ct: ct);

        return Ok(new { message = "Đã chuyển giỏ hiện tại." });
    }

    [HttpGet("orders/drafts")]
    public async Task<IActionResult> GetDraftOrders(CancellationToken ct)
    {
        var result = await _pos.GetDraftOrdersAsync(ct);
        return Ok(result);
    }

    [HttpGet("screen")]
    public async Task<IActionResult> GetPOSScreen(CancellationToken ct)
    {
        var result = await _pos.GetPOSScreenAsync(ct);
        return Ok(result);
    }

    [HttpPost("cart/ensure")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> EnsureCurrentCart(CancellationToken ct)
    {
        var result = await _pos.EnsureCurrentCartAsync(ct);

        await NotifyTerminalAsync(
            eventType: PosRealtimeEventTypes.CartEnsured,
            orderId: result?.OrderId,
            cartChanged: true,
            summaryChanged: true,
            message: "Đã đảm bảo có giỏ hiện tại.",
            ct: ct);

        return Ok(result);
    }

    [HttpPost("cart/current/scan")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> ScanToCurrentCart([FromBody] ScanBarcodeToCurrentCartRequest request, CancellationToken ct)
    {
        var result = await _pos.ScanToCurrentCartAsync(request.Barcode, request.Quantity, ct);

        await NotifyTerminalAsync(
            eventType: PosRealtimeEventTypes.CartChanged,
            orderId: result?.OrderId,
            cartChanged: true,
            summaryChanged: true,
            message: "Đã quét hàng vào giỏ hiện tại.",
            ct: ct);

        return Ok(result);
    }

    [HttpPost("cart/current/payments")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Payment.Create)]
    public async Task<IActionResult> AddPaymentToCurrentCart(
        [FromBody] QuickAddPaymentRequest request,
        CancellationToken ct = default)
    {
        
            var draft = await _pos.AddPaymentToCurrentCartAsync(request, ct);

            await NotifyTerminalAsync(
                eventType: PosRealtimeEventTypes.PaymentChanged,
                orderId: draft?.OrderId,
                summaryChanged: true,
                paymentsChanged: true,
                message: "Đã thêm thanh toán vào giỏ hiện tại.",
                ct: ct);

            return Ok(draft);

    }

    [HttpPost("cart/current/payment-and-finalize")]
    [Authorize(Policy = PermissionCodes.Pos.Payment.Create)]
    [Authorize(Policy = PermissionCodes.Pos.Order.Finalize)]
    public async Task<IActionResult> AddPaymentAndMaybeFinalizeCurrentCart([FromBody] QuickAddPaymentRequest request, CancellationToken ct)
    {

        var draft = await _pos.AddPaymentToCurrentCartAsync(request, ct)
            ?? throw new InvalidOperationException(
                "POS service returned no cart after adding a payment.");

        await NotifyTerminalAsync(
                eventType: PosRealtimeEventTypes.PaymentChanged,
               orderId: draft.OrderId,
                summaryChanged: true,
                paymentsChanged: true,
                message: "Đã ghi nhận thanh toán.",
                ct: ct);

            if (draft.BalanceDue > 0)
            {
                return Ok(new
                {
                    success = true,
                    finalized = false,
                    message = "Đã ghi nhận thanh toán.",
                    draft
                });
            }

        if (draft.Status is not (OrderStatus.Draft or OrderStatus.Completed))
            throw new GaoApp.Application.Common.Exceptions.ConflictAppException("Đơn đã đổi trạng thái. Vui lòng kiểm tra lịch sử thanh toán.");
        var finalizedDraft = draft.Status == OrderStatus.Completed ? draft : await _pos.FinalizeAsync(draft.OrderId, ct)
            ?? throw new InvalidOperationException(
                "POS service returned no result after automatic finalization.");

        await NotifyStoreAsync(
                eventType: PosRealtimeEventTypes.OrderFinalized,
            orderId: finalizedDraft.OrderId,
                heldChanged: true,
                message: "Đã thanh toán và tự động chốt đơn.",
                ct: ct);

            return Ok(new
            {
                success = true,
                finalized = true,
                message = "Đã thanh toán và tự động chốt đơn.",
                orderId = finalizedDraft.OrderId,
                draft = finalizedDraft
            });

    }

    [HttpPost("cart/current/hold")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Hold)]
    public async Task<IActionResult> HoldCurrentCart([FromBody] HoldCurrentCartRequest request, CancellationToken ct)
    {
       
            var result = await _pos.HoldCurrentCartAsync(request?.HoldNote, ct);

            await NotifyStoreAsync(
                eventType: PosRealtimeEventTypes.HeldChanged,
                orderId: null,
                heldChanged: true,
                message: "Đã giữ giỏ hiện tại.",
                ct: ct);

            return Ok(result);

    }

    [HttpPost("cart/current/cancel")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> CancelCurrentCart([FromBody] CancelCurrentCartRequest request, CancellationToken ct)
    {
        await _pos.CancelCurrentCartAsync(request?.Reason, ct);

        await NotifyStoreAsync(
            eventType: PosRealtimeEventTypes.CartCancelled,
            heldChanged: true,
            message: "Đã hủy giỏ hiện tại.",
            ct: ct);

        return Ok(new { message = "Đã hủy giỏ hiện tại." });
    }

    [HttpPost("cart/current/new")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> CreateAndSwitchNewCart(
        [FromBody] CreateNewCartRequest request,
        CancellationToken ct)
    {
       
            var id = await _pos.CreateAndSwitchNewCartAsync(
                request.CustomerId,
                request.Note,
                ct);

            await NotifyTerminalAsync(
                eventType: PosRealtimeEventTypes.CartCreated,
                orderId: id,
                cartChanged: true,
                summaryChanged: true,
                customerChanged: request.CustomerId.HasValue,
                message: "Đã tạo giỏ mới.",
                ct: ct);

            return Ok(new
            {
                success = true,
                orderId = id,
                message = "Đã tạo giỏ mới."
            });

    }

    [HttpPost("cart/current/finalize")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Finalize)]
    public async Task<IActionResult> FinalizeCurrentCart(CancellationToken ct)
    {
        // B8.1:
        // Không bắt exception ở đây.
        // Nếu có lỗi:
        // - PosAppException → middleware trả business error
        // - DbUpdateException → middleware log + trả system error

        var result = await _pos.FinalizeCurrentCartAsync(ct)
              ?? throw new InvalidOperationException(
                 "POS service returned no result after finalization.");

        await NotifyStoreAsync(
            eventType: PosRealtimeEventTypes.OrderFinalized,
           orderId: result.OrderId,
            heldChanged: true,
            message: "Đã chốt đơn thành công.",
            ct: ct);

        return Ok(new
        {
            success = true,
            message = "Đã chốt đơn thành công.",
            orderId = result.OrderId,
            data = result
        });
    }

    [HttpGet]
    public Task<IActionResult> Index(CancellationToken ct)
    {
        return RenderPosPageAsync(
            isPrime: true,
            ct);
    }

    [HttpGet("v3")]
    public Task<IActionResult> Prime(CancellationToken ct)
    {
        return RenderPosPageAsync(
            isPrime: true,
            ct);
    }

    [HttpGet("legacy")]
    public Task<IActionResult> Legacy(CancellationToken ct)
    {
        return RenderPosPageAsync(
            isPrime: false,
            ct);
    }

    private async Task<IActionResult> RenderPosPageAsync(
        bool isPrime,
        CancellationToken ct)
    {
        ViewData["IsPOSPrime"] = isPrime;

        try
        {
            await BindPOSHeaderContextAsync(ct);

            return View("Index");
        }
        catch (PosAppException ex) when (
            ex.ErrorType == PosErrorTypes.Ownership ||
            ex.ErrorCode == PosErrorCodes.ShiftNotOpen ||
            ex.ErrorCode == PosErrorCodes.ContextTerminalNotResolved ||
            ex.ErrorCode == PosErrorCodes.ShiftOwnedByAnotherUser ||
            ex.ErrorCode == "POS_SHIFT_OWNED_BY_ANOTHER_USER")
        {
            await BindPOSHeaderContextAsync(ct);

            ViewBag.PosBootstrapError =
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    message = ex.SafeMessage,
                    errorCode = ex.ErrorCode,
                    actionHint = ex.ActionHint,
                    errorType = ex.ErrorType,
                    metadata = ex.Metadata
                });

            return View("Index");
        }
    }

[HttpGet("dashboard")]
public async Task<IActionResult> Dashboard(CancellationToken ct)
{
    await BindPOSHeaderContextAsync(ct);
    return View("~/Areas/Admin/Views/POS/Dashboard.cshtml");
}

    [HttpGet("products/search")]
    public async Task<IActionResult> SearchProducts([FromQuery] string keyword, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        var result = await _pos.SearchProductsForPOSAsync(keyword, take, ct);
        return Ok(result);
    }

    [HttpGet("customers/search")]
    public async Task<IActionResult> SearchCustomers([FromQuery] string keyword, [FromQuery] int take = 20, CancellationToken ct = default)
    {
     
            var items = await _pos.SearchCustomersForPOSAsync(keyword, take, ct);
            return Ok(items);

    }
    public sealed class SetCurrentCartCustomerRequest
    {
        public bool RepriceExistingLines { get; set; } = false;
    }

    [HttpPost("cart/current/customer/{customerId:int}")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> SetCustomerForCurrentCart(
        int customerId,
        [FromBody] SetCurrentCartCustomerRequest? request,
        CancellationToken ct = default)
    {
        var draft = await _pos.SetCustomerForCurrentCartAsync(
            customerId,
            request?.RepriceExistingLines ?? false,
            ct);

        await NotifyTerminalAsync(
            eventType: PosRealtimeEventTypes.CustomerChanged,
            orderId: draft?.OrderId,
            summaryChanged: true,
            customerChanged: true,
            message: "Đã gán khách hàng vào giỏ hiện tại.",
            ct: ct);

        return Ok(draft);
    }

    [HttpDelete("cart/current/customer")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> ClearCustomerForCurrentCart(CancellationToken ct = default)
    {
       
            var draft = await _pos.ClearCustomerForCurrentCartAsync(ct);

            await NotifyTerminalAsync(
                eventType: PosRealtimeEventTypes.CustomerChanged,
                orderId: draft?.OrderId,
                summaryChanged: true,
                customerChanged: true,
                message: "Đã bỏ khách hàng khỏi giỏ hiện tại.",
                ct: ct);

            return Ok(draft);

    }

    [HttpPost("customers/quick-create")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> QuickCreateCustomer([FromBody] CreatePOSCustomerDto request, CancellationToken ct = default)
    {
       
            var draft = await _pos.CreateCustomerAndSetForCurrentCartAsync(request, ct);

            await NotifyTerminalAsync(
                eventType: PosRealtimeEventTypes.CustomerChanged,
                orderId: draft?.OrderId,
                summaryChanged: true,
                customerChanged: true,
                message: "Đã tạo nhanh và gán khách hàng vào giỏ.",
                ct: ct);

            return Ok(draft);

    }

    [HttpPost("cart/current/note")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Order.Create)]
    public async Task<IActionResult> UpdateCurrentCartNote(
        [FromBody] UpdateCurrentCartNoteRequest request,
        CancellationToken ct = default)
    {
       
            var draft = await _pos.UpdateCurrentCartNoteAsync(request.Note, ct);

            await NotifyTerminalAsync(
                eventType: PosRealtimeEventTypes.CartChanged,
                orderId: draft?.OrderId,
                cartChanged: true,
                message: "Đã cập nhật ghi chú đơn.",
                ct: ct);

            return Ok(draft);

    }

    [HttpPost("cart/current/discount")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Order.Discount)]
    public async Task<IActionResult> UpdateCurrentCartDiscount(
        [FromBody] UpdateOrderDiscountRequest request,
        CancellationToken ct = default)
    {
       
            var draft = await _pos.UpdateCurrentCartDiscountAsync(request.DiscountAmount, ct);

            await NotifyTerminalAsync(
                eventType: PosRealtimeEventTypes.CartChanged,
                orderId: draft?.OrderId,
                cartChanged: true,
                summaryChanged: true,
                message: "Đã cập nhật giảm giá đơn.",
                ct: ct);

            return Ok(draft);

    }

    [HttpPost("cart/current/reward-vouchers")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Order.Discount)]
    public async Task<IActionResult> ApplyRewardVouchersToCurrentCart(
    [FromBody] ApplyRewardVouchersRequest request,
    CancellationToken ct = default)
    {
        var draft = await _pos.ApplyRewardVouchersToCurrentCartAsync(request, ct);

        await NotifyTerminalAsync(
            eventType: PosRealtimeEventTypes.CartChanged,
            orderId: draft?.OrderId,
            cartChanged: true,
            summaryChanged: true,
            message: "Đã áp dụng voucher vào giỏ.",
            ct: ct);

        return Ok(draft);
    }
    

    [HttpDelete("cart/current/reward-vouchers")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Order.Discount)]
    public async Task<IActionResult> ClearRewardVouchersFromCurrentCart(
        CancellationToken ct = default)
    {
        var draft = await _pos.ClearRewardVouchersFromCurrentCartAsync(ct);

        await NotifyTerminalAsync(
            eventType: PosRealtimeEventTypes.CartChanged,
            orderId: draft?.OrderId,
            cartChanged: true,
            summaryChanged: true,
            message: "Đã bỏ voucher khỏi giỏ.",
            ct: ct);

        return Ok(draft);
    }

    [HttpPost("lines/{lineId:int}/discount")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Order.Discount)]
    public async Task<IActionResult> UpdateLineDiscount(
        int lineId,
        [FromBody] UpdateLineDiscountRequest request,
        CancellationToken ct = default)
    {
       
            var draft = await _pos.UpdateLineDiscountAsync(lineId, request.DiscountAmount, ct);

            await NotifyTerminalAsync(
                eventType: PosRealtimeEventTypes.CartChanged,
                orderId: draft?.OrderId,
                cartChanged: true,
                summaryChanged: true,
                message: "Đã cập nhật giảm giá dòng hàng.",
                ct: ct);

            return Ok(draft);
 
    }

    [HttpPost("orders/{orderId:int}/void")]
    [Authorize(Policy = PermissionCodes.Pos.Order.Void)]
    public async Task<IActionResult> VoidOrder(
        int orderId,
        [FromBody] VoidOrderRequest request,
        CancellationToken ct = default)
    {
       
            var result = await _pos.VoidCompletedOrderAsync(orderId, request.Reason, ct);

            await NotifyStoreAsync(
                eventType: PosRealtimeEventTypes.OrderVoided,
                orderId: result.OrderId,
                heldChanged: true,
                message: "Đã void đơn thành công.",
                ct: ct);

            return Ok(new
            {
                success = true,
                message = "Đã void đơn thành công.",
                orderId = result.OrderId,
                data = result
            });

    }

    [HttpPost("orders/{orderId:int}/refund")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Order.Refund)]
    [Authorize(Policy = PermissionCodes.Pos.Payment.Refund)]
    public async Task<IActionResult> RefundOrder(
       int orderId,
       [FromBody] RefundOrderRequest request,
       CancellationToken ct = default)
    {
        request ??= new RefundOrderRequest();

        var result = await _pos.RefundCompletedOrderAsync(
            orderId: orderId,
            reason: request.Reason,
            refundMethod: request.RefundMethod,
            refundReferenceCode: request.RefundReferenceCode,
            refundProvider: request.RefundProvider,
            ct: ct);

        await NotifyStoreAsync(
            eventType: PosRealtimeEventTypes.OrderRefunded,
            orderId: result.OrderId,
            heldChanged: true,
            message: "Đã trả hàng / hoàn tiền thành công.",
            ct: ct);

        return Ok(new
        {
            success = true,
            message = "Đã trả hàng / hoàn tiền thành công.",
            orderId = result.OrderId,
            data = result
        });
    }

    /// <summary>
    /// API dashboard ca POS.
    /// - Không đọc refund từ logic cũ của POSService nữa.
    /// - Đọc trực tiếp từ POSShift hiện tại + summary của shift.
    /// - RefundTotal = CashRefundTotal + NonCashRefundTotal
    /// - RefundCount = số giao dịch refund trong ca
    /// </summary>
    [HttpGet("shift/dashboard")]
    public async Task<IActionResult> GetShiftDashboard(CancellationToken ct)
    {
        var currentShift = await _posShiftService.GetCurrentOpenAsync(ct);

        if (currentShift == null)
        {
            return Ok(new
            {
                shiftId = 0,
                shiftCode = "",
                openedAt = (DateTime?)null,
                totalSales = 0m,
                cashSales = 0m,
                bankSales = 0m,
                refundTotal = 0m,
                refundCount = 0,
                voidCount = 0,
                ordersCount = 0
            });
        }

        var summary = await _posShiftService.GetSummaryAsync(currentShift.Id, ct);

        return Ok(new
        {
            shiftId = currentShift.Id,
            shiftCode = currentShift.ShiftCode,
            openedAt = currentShift.OpenedAtUtc,
            totalSales = summary.CompletedSalesTotal,
            cashSales = currentShift.CashSalesTotal,
            bankSales = currentShift.NonCashSalesTotal,
            refundTotal = currentShift.RefundTotal,
            refundCount = currentShift.RefundCount,
            voidCount = currentShift.VoidCount,
            ordersCount = summary.CompletedOrders
        });
    }

    #region CustomerDisplay
    [HttpGet("customer-display/info")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CustomerDisplayInfo(
        [FromServices] GaoApp.Web.Services.CustomerDisplayService display, CancellationToken ct)
        => Ok(await display.GetInfoAsync(ct));

    [HttpGet("customer-display")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CustomerDisplay(
        [FromServices] GaoApp.Web.Services.Printing.ReceiptTemplateService receiptTemplates,
        [FromServices] GaoApp.Web.Services.CustomerDisplayService display,
        CancellationToken ct)
    {
        await BindPOSHeaderContextAsync(ct);
        ViewBag.CustomerDisplayStore = await receiptTemplates.GetStoreInfoAsync(ct);
        ViewBag.CustomerDisplayInfo = await display.GetInfoAsync(ct);

        return View("~/Areas/Admin/Views/POS/CustomerDisplay.cshtml");
    }
    #endregion
}
