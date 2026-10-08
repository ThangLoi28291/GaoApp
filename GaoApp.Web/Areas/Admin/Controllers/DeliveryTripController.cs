using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Interfaces.Services.Delivery;
using GaoApp.Domain.Delivery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), ApiController, Route("admin/api/delivery-trips")]
[Authorize(Policy = PermissionCodes.Delivery.View)]
public sealed class DeliveryTripController(IDeliveryTripService trips) : ControllerBase
{
    [HttpGet("")]
    public Task<IActionResult> List([FromQuery] int take = 30, [FromQuery] int? beforeId = null,
        [FromQuery] string? state = null, [FromQuery] int? courierUserId = null, CancellationToken ct = default)
        => Execute(() => trips.ListAsync(take, beforeId, state, courierUserId, ct));
    [HttpGet("options"), Authorize(Policy = PermissionCodes.Delivery.Handover)]
    public Task<IActionResult> Options(CancellationToken ct) => Execute(() => trips.GetOptionsAsync(ct));
    [HttpGet("{id:int}")]
    public Task<IActionResult> Get(int id, CancellationToken ct) => Execute(() => trips.GetAsync(id, ct));
    [HttpGet("{id:int}/handover")]
    public Task<IActionResult> HandoverWorkspace(int id, CancellationToken ct) => Execute(() => trips.GetHandoverAsync(id, ct));
    [HttpPost("{id:int}/handover/{orderId:int}/preview"), ValidateAntiForgeryToken]
    public Task<IActionResult> HandoverPreview(int id, int orderId, [FromBody] DeliveryHandoverPreviewRequest request, CancellationToken ct)
        => Execute(() => trips.PreviewHandoverAsync(id, orderId, request, ct));
    [HttpPost("{id:int}/handover/{orderId:int}"), Authorize(Policy = PermissionCodes.Delivery.Finalize), ValidateAntiForgeryToken]
    public Task<IActionResult> ConfirmHandover(int id, int orderId, [FromBody] DeliveryHandoverRequest request, CancellationToken ct)
        => Execute(() => trips.ConfirmHandoverAsync(id, orderId, request, ct));
    [HttpPost(""), Authorize(Policy = PermissionCodes.Delivery.Handover), ValidateAntiForgeryToken]
    public Task<IActionResult> Create([FromBody] DeliveryTripCreateDraftRequest request, CancellationToken ct)
        => Execute(() => trips.CreateDraftAsync(request, ct));
    [HttpPost("{id:int}/draft"), Authorize(Policy = PermissionCodes.Delivery.Handover), ValidateAntiForgeryToken]
    public Task<IActionResult> Edit(int id, [FromBody] DeliveryTripEditDraftRequest request, CancellationToken ct)
        => Execute(() => trips.EditDraftAsync(id, request, ct));
    [HttpPost("{id:int}/abandon"), Authorize(Policy = PermissionCodes.Delivery.Handover), ValidateAntiForgeryToken]
    public Task<IActionResult> Abandon(int id, [FromBody] DeliveryTripAbandonRequest request, CancellationToken ct)
        => Execute(() => trips.AbandonDraftAsync(id, request, ct));
    [HttpPost("{id:int}/handover"), Authorize(Policy = PermissionCodes.Delivery.Handover), ValidateAntiForgeryToken]
    public Task<IActionResult> Handover(int id, [FromBody] DeliveryTripHandoverRequest request, CancellationToken ct)
        => Execute(() => trips.HandoverAsync(id, request, ct));
    [HttpPost("{id:int}/depart"), Authorize(Policy = PermissionCodes.Delivery.RecordDeparture), ValidateAntiForgeryToken]
    public Task<IActionResult> Depart(int id, [FromBody] DeliveryTripDepartureRequest request, CancellationToken ct)
        => Execute(() => trips.DepartAsync(id, request, ct));
    [HttpPost("{id:int}/courier/reassign"), Authorize(Policy = PermissionCodes.Delivery.Handover)]
    [Authorize(Policy = PermissionCodes.Delivery.ReassignCourier), ValidateAntiForgeryToken]
    public Task<IActionResult> Reassign(int id, [FromBody] DeliveryTripReassignRequest request, CancellationToken ct)
        => Execute(() => trips.ReassignCourierAsync(id, request, ct));
    [HttpPost("{id:int}/orders/{orderId:int}/cancel-request"), Authorize(Policy = PermissionCodes.Delivery.Cancel), ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(int id, int orderId, [FromBody] DeliveryTripCancellationRequest request, CancellationToken ct)
        => Execute(() => trips.RequestCancellationAsync(id, orderId, request, ct));
    // Own/proxy permission is resolved against the real courier inside the locked service command.
    [HttpPost("{id:int}/orders/{orderId:int}/outcome"), ValidateAntiForgeryToken]
    public Task<IActionResult> Outcome(int id, int orderId, [FromBody] DeliveryOutcomeRequest request, CancellationToken ct)
        => Execute(() => trips.RecordOutcomeAsync(id, orderId, request, ct));
    [HttpPost("{id:int}/return-to-store"), ValidateAntiForgeryToken]
    public Task<IActionResult> ReturnToStore(int id, [FromBody] DeliveryTripReturnRequest request, CancellationToken ct)
        => Execute(() => trips.RecordReturnToStoreAsync(id, request, ct));
    private async Task<IActionResult> Execute<T>(Func<Task<T>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await operation()); }
        catch (DeliveryFoundationException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message }); }
        catch (DeliveryRuleException ex) { return BadRequest(new { code = ex.Code, message = ex.Message }); }
    }
}
