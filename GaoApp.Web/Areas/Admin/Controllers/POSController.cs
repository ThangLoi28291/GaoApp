using GaoApp.Application.DTOs.POS;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Web.Common.POS;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/pos")]
public class POSController : BasePOSPageController
{
    private readonly IPOSService _pos;
    private readonly IPOSShiftService _posShiftService;

    public POSController(
        IPOSService pos,
        IPOSRuntimeContextAccessor runtimeContext,
        IPOSShiftService posShiftService)
        : base(runtimeContext, posShiftService)
    {
        _pos = pos;
        _posShiftService = posShiftService;
    }
  
    [HttpPost("draft")]
    public async Task<IActionResult> CreateDraft(int? customerId = null, string? note = null, CancellationToken ct = default)
        => Ok(new { orderId = await _pos.CreateDraftAsync(customerId, note, ct) });

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> GetDraft(int orderId, CancellationToken ct = default)
        => Ok(await _pos.GetDraftAsync(orderId, ct));

    [HttpPost("{orderId:int}/items")]
    public async Task<IActionResult> AddItem(int orderId, int variantId, decimal qty = 1, CancellationToken ct = default)
        => Ok(await _pos.AddItemAsync(orderId, variantId, qty, ct));

    [HttpPost("{orderId:int}/barcode")]
    public async Task<IActionResult> AddByBarcode(int orderId, string barcode, decimal qty = 1, CancellationToken ct = default)
        => Ok(await _pos.AddItemByBarcodeAsync(orderId, barcode, qty, ct));

    [HttpPatch("lines/{lineId:int}")]
    public async Task<IActionResult> UpdateQty(int lineId, decimal qty, CancellationToken ct = default)
        => Ok(await _pos.UpdateLineQtyAsync(lineId, qty, ct));

    [HttpDelete("lines/{lineId:int}")]
    public async Task<IActionResult> RemoveLine(int lineId, CancellationToken ct = default)
        => Ok(await _pos.RemoveLineAsync(lineId, ct));

    [HttpPost("{orderId:int}/payments")]
    public async Task<IActionResult> AddPayment(int orderId, [FromBody] UpsertPaymentRequest dto, CancellationToken ct)
        => Ok(await _pos.AddPaymentAsync(orderId, dto, ct));

    [HttpDelete("payments/{paymentId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePayment(int paymentId, CancellationToken ct = default)
    {
        try
        {
            var draft = await _pos.RemovePaymentAsync(paymentId, ct);
            return Ok(draft);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("{orderId:int}/finalize")]
    public async Task<IActionResult> Finalize(int orderId, CancellationToken ct)
        => Ok(await _pos.FinalizeAsync(orderId, ct));

    [HttpPost("{orderId:int}/cancel")]
    public async Task<IActionResult> Cancel(int orderId, string? reason = null, CancellationToken ct = default)
    {
        await _pos.CancelAsync(orderId, reason, ct);
        return Ok(new { success = true });
    }

    [HttpGet("orders/{orderId:int}")]
    public async Task<IActionResult> GetReceipt(int orderId, CancellationToken ct = default)
        => Ok(await _pos.GetReceiptAsync(orderId, ct));

    [HttpGet("orders/{orderId:int}/receipt")]
    public async Task<IActionResult> GetReceiptData(int orderId, CancellationToken ct = default)
        => Ok(await _pos.GetReceiptAsync(orderId, ct));

    [HttpGet("orders/{orderId:int}/print")]
    public async Task<IActionResult> PrintReceipt(
        int orderId,
        [FromQuery] string size = "80",
        [FromQuery] bool autoPrint = true,
        CancellationToken ct = default)
    {
        var model = await _pos.GetReceiptAsync(orderId, ct);

        ViewBag.PrintSize = size;
        ViewBag.AutoPrint = autoPrint;

        return View("~/Areas/Admin/Views/POS/PrintReceipt.cshtml", model);
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
    public async Task<IActionResult> HoldOrder(int orderId, [FromBody] HoldOrderRequest request, CancellationToken ct)
    {
        var result = await _pos.HoldAndCreateNewDraftAsync(orderId, request?.HoldNote, ct);
        return Ok(result);
    }

    [HttpGet("orders/held")]
    public async Task<IActionResult> GetHeldOrders(CancellationToken ct)
    {
        var result = await _pos.GetHeldOrdersAsync(ct);
        return Ok(result);
    }

    [HttpPost("orders/{orderId:int}/resume")]
    public async Task<IActionResult> ResumeHeldOrder(int orderId, CancellationToken ct)
    {
        var resumedOrderId = await _pos.ResumeHeldAsync(orderId, ct);
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
    public async Task<IActionResult> SetCurrentCart([FromBody] SwitchCurrentCartRequest request, CancellationToken ct)
    {
        await _pos.SetCurrentCartAsync(request.OrderId, ct);
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
    public async Task<IActionResult> EnsureCurrentCart(CancellationToken ct)
    {
        var result = await _pos.EnsureCurrentCartAsync(ct);
        return Ok(result);
    }

    [HttpPost("cart/current/scan")]
    public async Task<IActionResult> ScanToCurrentCart([FromBody] ScanBarcodeToCurrentCartRequest request, CancellationToken ct)
    {
        var result = await _pos.ScanToCurrentCartAsync(request.Barcode, request.Quantity, ct);
        return Ok(result);
    }

    [HttpPost("cart/current/payments")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPaymentToCurrentCart(
        [FromBody] QuickAddPaymentRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var draft = await _pos.AddPaymentToCurrentCartAsync(request, ct);
            return Ok(draft);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("cart/current/payment-and-finalize")]
    public async Task<IActionResult> AddPaymentAndMaybeFinalizeCurrentCart([FromBody] QuickAddPaymentRequest request, CancellationToken ct)
    {
        try
        {
            var draft = await _pos.AddPaymentToCurrentCartAsync(request, ct);

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

            var finalizedDraft = await _pos.FinalizeCurrentCartAsync(ct);

            return Ok(new
            {
                success = true,
                finalized = true,
                message = "Đã thanh toán và tự động chốt đơn.",
                orderId = finalizedDraft.OrderId,
                draft = finalizedDraft
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("cart/current/hold")]
    public async Task<IActionResult> HoldCurrentCart([FromBody] HoldCurrentCartRequest request, CancellationToken ct)
    {
        var result = await _pos.HoldCurrentCartAsync(request?.HoldNote, ct);
        return Ok(result);
    }

    [HttpPost("cart/current/cancel")]
    public async Task<IActionResult> CancelCurrentCart([FromBody] CancelCurrentCartRequest request, CancellationToken ct)
    {
        await _pos.CancelCurrentCartAsync(request?.Reason, ct);
        return Ok(new { message = "Đã hủy giỏ hiện tại." });
    }

    [HttpPost("cart/current/new")]
    public async Task<IActionResult> CreateAndSwitchNewCart(
        [FromBody] CreateNewCartRequest request,
        CancellationToken ct)
    {
        try
        {
            var id = await _pos.CreateAndSwitchNewCartAsync(
                request.CustomerId,
                request.Note,
                ct);

            return Ok(new
            {
                success = true,
                orderId = id,
                message = "Đã tạo giỏ mới."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("cart/current/finalize")]
    public async Task<IActionResult> FinalizeCurrentCart(CancellationToken ct)
    {
        try
        {
            var result = await _pos.FinalizeCurrentCartAsync(ct);

            return Ok(new
            {
                success = true,
                message = "Đã chốt đơn thành công.",
                orderId = result.OrderId,
                data = result
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            await BindPOSHeaderContextAsync(ct);
            await _pos.GetCurrentCartAsync(ct);
            return View();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Chưa mở ca POS"))
        {
            TempData["Error"] = "Bạn cần mở ca POS trước khi bán hàng.";
            return RedirectToAction("Index", "POSShiftPage", new { area = "Admin" });
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
        try
        {
            var result = await _pos.SearchProductsForPOSAsync(keyword, take, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("customers/search")]
    public async Task<IActionResult> SearchCustomers([FromQuery] string keyword, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        try
        {
            var items = await _pos.SearchCustomersForPOSAsync(keyword, take, ct);
            return Ok(items);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("cart/current/customer/{customerId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCustomerForCurrentCart(int customerId, CancellationToken ct = default)
    {
        try
        {
            var draft = await _pos.SetCustomerForCurrentCartAsync(customerId, ct);
            return Ok(draft);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpDelete("cart/current/customer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearCustomerForCurrentCart(CancellationToken ct = default)
    {
        try
        {
            var draft = await _pos.ClearCustomerForCurrentCartAsync(ct);
            return Ok(draft);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("customers/quick-create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickCreateCustomer([FromBody] CreatePOSCustomerDto request, CancellationToken ct = default)
    {
        try
        {
            var draft = await _pos.CreateCustomerAndSetForCurrentCartAsync(request, ct);
            return Ok(draft);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("cart/current/note")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCurrentCartNote(
        [FromBody] UpdateCurrentCartNoteRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var draft = await _pos.UpdateCurrentCartNoteAsync(request.Note, ct);
            return Ok(draft);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("cart/current/discount")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCurrentCartDiscount(
        [FromBody] UpdateOrderDiscountRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var draft = await _pos.UpdateCurrentCartDiscountAsync(request.DiscountAmount, ct);
            return Ok(draft);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("lines/{lineId:int}/discount")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateLineDiscount(
        int lineId,
        [FromBody] UpdateLineDiscountRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var draft = await _pos.UpdateLineDiscountAsync(lineId, request.DiscountAmount, ct);
            return Ok(draft);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("orders/{orderId:int}/void")]
   
    public async Task<IActionResult> VoidOrder(
        int orderId,
        [FromBody] VoidOrderRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _pos.VoidCompletedOrderAsync(orderId, request.Reason, ct);

            return Ok(new
            {
                success = true,
                message = "Đã void đơn thành công.",
                orderId = result.OrderId,
                data = result
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("orders/{orderId:int}/refund")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefundOrder(
        int orderId,
        [FromBody] RefundOrderRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _pos.RefundCompletedOrderAsync(orderId, request.Reason, ct);

            return Ok(new
            {
                success = true,
                message = "Đã trả hàng / hoàn tiền thành công.",
                orderId = result.OrderId,
                data = result
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// API dashboard ca POS.
    ///
    /// SỬA THEO NGHIỆP VỤ MỚI:
    /// - Không đọc refund từ logic cũ của POSService nữa.
    /// - Đọc trực tiếp từ POSShift hiện tại + summary của shift.
    /// - RefundTotal = CashRefundTotal + NonCashRefundTotal
    /// - RefundCount = số giao dịch refund trong ca
    /// </summary>
    [HttpGet("shift/dashboard")]
    public async Task<IActionResult> GetShiftDashboard(CancellationToken ct)
    {
        try
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

                // Doanh thu ca
                totalSales = summary.CompletedSalesTotal,
                cashSales = currentShift.CashSalesTotal,
                bankSales = currentShift.NonCashSalesTotal,

                // Refund ca
                refundTotal = currentShift.RefundTotal,
                refundCount = currentShift.RefundCount,

                // Tạm thời giữ void = 0 nếu chưa có nguồn riêng
                voidCount = currentShift.VoidCount,

                // Số đơn hoàn tất của ca
                ordersCount = summary.CompletedOrders
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }
}