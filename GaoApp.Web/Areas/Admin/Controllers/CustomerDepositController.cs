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
    ICurrentPOSContext currentPos, IHubContext<PosHub> hub, CustomerDepositDisplayState displayState) : Controller
{
    private string TerminalGroup => $"store:{currentPos.StoreId}:terminal:{currentPos.TerminalId}";
    private Task DisplayAsync(string eventType, object payload, CancellationToken ct)
        => currentPos.IsAvailable
            ? hub.Clients.Group(TerminalGroup).SendAsync("pos:event", new
            {
                eventType,
                terminalId = currentPos.TerminalId.ToString(),
                storeId = currentPos.StoreId,
                payload,
                occurredAtUtc = DateTime.UtcNow
            }, ct)
            : Task.CompletedTask;
    [HttpGet("")]
    [Authorize(Policy = PermissionCodes.CustomerDeposit.View)]
    public async Task<IActionResult> Index(int? customerId, CancellationToken ct)
        => View(await deposits.GetAsync(customerId, ct));
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
        if (currentPos.IsAvailable) displayState.Clear(currentPos.StoreId, currentPos.TerminalId);
        await DisplayAsync("customer_payment_success", new
        {
            finalized = true,
            kind = "deposit",
            title = "Đã nhận tiền đặt cọc",
            message = "Phiếu cọc đã được ghi nhận. Cảm ơn quý khách!",
            paidAmount = request.Amount,
            durationMs = 5000
        }, ct);
        return Ok(new { depositId });
    }
    [HttpPost("receive-qr")]
    [Authorize(Policy = PermissionCodes.CustomerDeposit.Receive)]
    public async Task<IActionResult> ReceiveQr([FromBody] CreateDepositQrRequest request, CancellationToken ct)
    {
        var qr = await deposits.CreateReceiveQrAsync(request, ct);
        if (currentPos.IsAvailable) displayState.Set(currentPos.StoreId, currentPos.TerminalId, qr);
        await DisplayAsync("customer_payment_qr_created", new
        {
            qr.QrDataUrl,
            qr.Amount,
            qr.Content,
            qr.BankName,
            qr.AccountNumber,
            qr.AccountName,
            kind = "deposit",
            title = "Quét mã để đặt cọc",
            intro = "Chuyển tiền đặt cọc đến"
        }, ct);
        return Ok(qr);
    }
    [HttpGet("active-qr")]
    [Authorize(Policy = PermissionCodes.Pos.Order.View)]
    public IActionResult ActiveQr()
    {
        if (!currentPos.IsAvailable) return NoContent();
        var qr = displayState.Get(currentPos.StoreId, currentPos.TerminalId);
        return qr == null ? NoContent() : Ok(qr);
    }
    [HttpPost("display/hide")]
    [Authorize(Policy = PermissionCodes.CustomerDeposit.Receive)]
    public async Task<IActionResult> HideDisplay(CancellationToken ct)
    {
        if (currentPos.IsAvailable) displayState.Clear(currentPos.StoreId, currentPos.TerminalId);
        await DisplayAsync("customer_payment_hide", new { kind = "deposit" }, ct);
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
