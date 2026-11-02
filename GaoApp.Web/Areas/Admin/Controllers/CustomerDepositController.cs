using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Web.Hubs;
using GaoApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc;
namespace GaoApp.Web.Areas.Admin.Controllers;
[Area("Admin")]
[Route("admin/customer-deposit")]
[AutoValidateAntiforgeryToken]
public sealed class CustomerDepositController(ICustomerDepositService deposits, IPOSService pos,
    ICurrentPOSContext currentPos, IHubContext<PosHub> hub, CustomerDepositDisplayState displayState,
    CustomerPaymentDisplayService paymentDisplay) : Controller
{
    private string TerminalGroup => $"store:{currentPos.StoreId}:terminal:{currentPos.TerminalId}";
    private Task DisplayAsync(CancellationToken ct)
        => currentPos.IsAvailable
            ? hub.Clients.Group(TerminalGroup).SendAsync("pos:event", new
            {
                eventType = "customer_payment_display_changed",
                terminalId = currentPos.TerminalId.ToString(),
                storeId = currentPos.StoreId,
                occurredAtUtc = DateTime.UtcNow
            }, ct)
            : Task.CompletedTask;
    [HttpGet("")]
    [Authorize(Policy = PermissionCodes.CustomerDeposit.View)]
    public async Task<IActionResult> Index(int? customerId, CancellationToken ct, string? search = null, int page = 1, string? status = null, string? tab = null, int? received = null)
    {
        ViewBag.DepositTab = tab == "list" ? "list" : null;
        var model = await deposits.GetAsync(customerId, ct, search, page, status);
        ViewBag.ReceivedDepositId = model.List.Rows.Any(d => d.Id == received) ? received : null;
        return View(model);
    }
    [HttpGet("{depositId:int}/print")]
    [Authorize(Policy = PermissionCodes.CustomerDeposit.View)]
    public async Task<IActionResult> Print(int depositId, CancellationToken ct)
        => View("Print", await deposits.GetReceiptAsync(depositId, ct));
    [HttpGet("customers/search")]
    [Authorize(Policy = PermissionCodes.CustomerDeposit.View)]
    public async Task<IActionResult> SearchCustomers([FromQuery] string? keyword, [FromQuery] int take = 12, CancellationToken ct = default)
    {
        var term = (keyword ?? string.Empty).Trim();
        if (term.Length < 2) return Ok(Array.Empty<object>());
        return Ok(await pos.SearchCustomersForPOSAsync(term, Math.Clamp(take, 1, 20), ct));
    }
    [HttpPost("receive")]
    [Authorize(Policy = PermissionCodes.CustomerDeposit.Receive)]
    public async Task<IActionResult> Receive([FromBody] ReceiveDepositRequest request, CancellationToken ct)
    {
        var depositId = await deposits.ReceiveAsync(request, ct);
        if (currentPos.IsAvailable)
            await paymentDisplay.ShowDepositReceiptAsync(currentPos.StoreId, currentPos.TerminalId, depositId, ct);
        await DisplayAsync(ct);
        return Ok(new { depositId });
    }
    [HttpPost("receive-qr")]
    [Authorize(Policy = PermissionCodes.CustomerDeposit.Receive)]
    public async Task<IActionResult> ReceiveQr([FromBody] CreateDepositQrRequest request, CancellationToken ct)
    {
        var qr = await deposits.CreateReceiveQrAsync(request, ct);
        if (currentPos.IsAvailable)
            await paymentDisplay.ShowDepositQrAsync(currentPos.StoreId, currentPos.TerminalId, qr, ct);
        await DisplayAsync(ct);
        return Ok(qr);
    }
    [HttpGet("active-qr")]
    [Authorize(Policy = PermissionCodes.Pos.Order.View)]
    public async Task<IActionResult> ActiveQr(CancellationToken ct)
    {
        if (!currentPos.IsAvailable) return NoContent();
        var snapshot = await paymentDisplay.GetAsync(currentPos.StoreId, currentPos.TerminalId, ct);
        return snapshot.Mode == "qr" && snapshot.OrderId == null ? Ok(snapshot.Payload) : NoContent();
    }
    [HttpPost("display/hide")]
    [Authorize(Policy = PermissionCodes.CustomerDeposit.Receive)]
    public async Task<IActionResult> HideDisplay(CancellationToken ct)
    {
        if (currentPos.IsAvailable) displayState.Clear(currentPos.StoreId, currentPos.TerminalId);
        await DisplayAsync(ct);
        return NoContent();
    }
    [HttpPost("refund")]
    [Authorize(Policy = PermissionCodes.CustomerDeposit.Refund)]
    public async Task<IActionResult> Refund([FromBody] RefundDepositRequest request, CancellationToken ct)
        => Ok(new { entryId = await deposits.RefundAsync(request, ct) });
    [HttpPost("orders/{orderId:int}/select")]
    [Authorize(Policy = PermissionCodes.CustomerDeposit.Use)]
    [Authorize(Policy = PermissionCodes.Pos.Order.View)]
    public async Task<IActionResult> Select(int orderId, [FromBody] SelectDepositRequest request, CancellationToken ct)
    {
        if (Request.Headers["X-POS-Offline"] == "1") return BadRequest(new { message = "Sử dụng cọc cần kết nối mạng." });
        await deposits.SelectAsync(orderId, request, ct);
        return Ok(await pos.GetDraftAsync(orderId, ct));
    }
}
