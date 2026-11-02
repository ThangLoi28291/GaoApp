using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Controllers;

[ApiController, AllowAnonymous, IgnoreAntiforgeryToken]
[Route("api/bank-notifications")]
public sealed class BankNotificationDeviceController(AppDbContext db, BankNotificationBridgeService bridge) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(40000)]
    public async Task<IActionResult> Receive([FromBody] BankNotificationUpload upload, CancellationToken ct)
    {
        var authenticated = await GaoApp.Web.Security.BankNotificationDeviceAuthentication.AuthenticateContextAsync(db, HttpContext, ct);
        if (authenticated == null) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        await bridge.RecordContactAsync(authenticated.Device, ct);
        try { return Ok(new { receiptId = await bridge.AcceptAuthenticatedAsync(authenticated, upload, ct), accepted = true }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
    [HttpPost("heartbeat")]
    [RequestSizeLimit(2000)]
    public async Task<IActionResult> Heartbeat([FromBody] BankNotificationHeartbeat heartbeat, CancellationToken ct)
    {
        var device = await AuthenticateDeviceAsync(ct);
        if (device == null) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        try { await bridge.HeartbeatAsync(device, heartbeat, ct); return Ok(new { accepted = true, serverNowUtc = DateTime.UtcNow }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
    private async Task<BankNotificationDevice?> AuthenticateDeviceAsync(CancellationToken ct)
    {
        return await GaoApp.Web.Security.BankNotificationDeviceAuthentication.AuthenticateAsync(db, HttpContext, ct);
    }
}
