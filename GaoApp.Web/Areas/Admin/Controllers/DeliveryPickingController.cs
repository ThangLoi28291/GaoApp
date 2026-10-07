using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Interfaces.Services.Delivery;
using GaoApp.Domain.Delivery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), ApiController, Route("admin/api/deliveries/{id:int}/picking")]
[Authorize(Policy = PermissionCodes.Delivery.View)]
public sealed class DeliveryPickingController(IDeliveryPickingService picking) : ControllerBase
{
    [HttpGet("")]
    public Task<IActionResult> Get(int id, CancellationToken ct) => Execute(() => picking.GetAsync(id, ct));
    [HttpGet("replacement-options"), Authorize(Policy = PermissionCodes.Delivery.ApproveChanges)]
    public Task<IActionResult> ReplacementOptions(int id, [FromQuery] string? query, [FromQuery] int take = 25, CancellationToken ct = default)
        => Execute(() => picking.ReplacementOptionsAsync(id, query, take, ct));
    [HttpPost("claim"), Authorize(Policy = PermissionCodes.Delivery.Pick), ValidateAntiForgeryToken]
    public Task<IActionResult> Claim(int id, [FromBody] DeliveryPickingEnvelope request, CancellationToken ct)
        => Execute(() => picking.ClaimAsync(id, request, ct));
    [HttpPost("report"), Authorize(Policy = PermissionCodes.Delivery.Pick), ValidateAntiForgeryToken]
    public Task<IActionResult> Report(int id, [FromBody] DeliveryPickingReportRequest request, CancellationToken ct)
        => Execute(() => picking.ReportAsync(id, request, ct));
    [HttpPost("submit"), Authorize(Policy = PermissionCodes.Delivery.Pick), ValidateAntiForgeryToken]
    public Task<IActionResult> Submit(int id, [FromBody] DeliveryPickingEnvelope request, CancellationToken ct)
        => Execute(() => picking.SubmitAsync(id, request, ct));
    [HttpPost("plan"), Authorize(Policy = PermissionCodes.Delivery.ApproveChanges), ValidateAntiForgeryToken]
    public Task<IActionResult> Plan(int id, [FromBody] DeliveryPickingPlanRequest request, CancellationToken ct)
        => Execute(() => picking.PlanAsync(id, request, ct));
    [HttpPost("approve"), Authorize(Policy = PermissionCodes.Delivery.ApproveChanges), ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(int id, [FromBody] DeliveryPickingApproveRequest request, CancellationToken ct)
        => Execute(() => picking.ApproveAsync(id, request, ct));
    [HttpPost("reopen"), Authorize(Policy = PermissionCodes.Delivery.ApproveChanges), ValidateAntiForgeryToken]
    public Task<IActionResult> Reopen(int id, [FromBody] DeliveryPickingReopenRequest request, CancellationToken ct)
        => Execute(() => picking.ReopenAsync(id, request, ct));
    [HttpPost("reassign"), Authorize(Policy = PermissionCodes.Delivery.ApproveChanges), ValidateAntiForgeryToken]
    public Task<IActionResult> Reassign(int id, [FromBody] DeliveryPickingReassignRequest request, CancellationToken ct)
        => Execute(() => picking.ReassignAsync(id, request, ct));
    private async Task<IActionResult> Execute<T>(Func<Task<T>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await operation()); }
        catch (DeliveryFoundationException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message }); }
        catch (DeliveryRuleException ex) { return BadRequest(new { code = ex.Code, message = ex.Message }); }
    }
}
