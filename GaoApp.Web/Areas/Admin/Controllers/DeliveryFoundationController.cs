using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Interfaces.Services.Delivery;
using GaoApp.Domain.Delivery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), ApiController, Route("admin/api/deliveries")]
[Authorize(Policy = PermissionCodes.Delivery.View)]
public sealed class DeliveryFoundationController(IDeliveryFoundationService deliveries) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> List([FromQuery] int take = 50, CancellationToken ct = default)
        => Execute(() => deliveries.ListAsync(take, ct));
    [HttpGet("{id:int}")]
    public Task<IActionResult> Detail(int id, CancellationToken ct) => Execute(() => deliveries.GetAsync(id, ct));
    [HttpGet("lookup"), EnableRateLimiting("delivery-lookup")]
    public Task<IActionResult> Lookup([FromQuery] string key, CancellationToken ct) => Execute(() => deliveries.LookupAsync(key, ct));
    [HttpGet("{id:int}/history")]
    public Task<IActionResult> History(int id, CancellationToken ct) => Execute(() => deliveries.HistoryAsync(id, ct));
    [HttpPost("{id:int}/recipient"), Authorize(Policy = PermissionCodes.Delivery.Create), ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateRecipient(int id, [FromBody] DeliveryRecipientRequest request, CancellationToken ct)
        => Execute(() => deliveries.UpdateRecipientAsync(id, request, ct));
    private async Task<IActionResult> Execute<T>(Func<Task<T>> operation)
    {
        try { return Ok(await operation()); }
        catch (DeliveryFoundationException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message }); }
        catch (DeliveryRuleException ex) { return BadRequest(new { code = ex.Code, message = ex.Message }); }
    }
}
