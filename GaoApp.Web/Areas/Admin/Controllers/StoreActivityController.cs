using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Security.Claims;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Security;
using GaoApp.Web.Services.StoreMonitor;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize, ApiController, AutoValidateAntiforgeryToken]
[Route("admin/api/store-activity")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StoreActivityController(ITenantContext tenant, AppDbContext db,
    StoreActivityRegistry registry, StoreActivityTicket tickets, IAuthorizationService authorization,
    SessionPrincipalValidator sessions, TimeProvider time, StoreDeliveryMonitor delivery,
    StoreCheckoutMonitor checkouts, StoreMonitorReadCache cache) : ControllerBase
{
    public sealed class PresenceRequest
    {
        [Required, StringLength(StoreActivityTicket.MaxLength)] public string Ticket { get; set; } = "";
        public Guid TabId { get; set; }
        public bool Interaction { get; set; } = true;
        [Required, RegularExpression("^(viewing|editing|leave)$")] public string State { get; set; } = "viewing";
    }
    public sealed class OnlineRequest { public Guid TabId { get; set; } }
    [HttpPost("online"), RequestSizeLimit(4096)]
    public IActionResult Online([FromForm] OnlineRequest request)
    {
        if (tenant.IsHostAdmin || tenant.StoreId is not > 0 || request.TabId == Guid.Empty ||
            !int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var user) || user <= 0) return Forbid();
        return registry.Online(tenant.StoreId.Value, user, request.TabId, StoreActivityTicket.Person(User)) ? NoContent() : StatusCode(429);
    }
    [HttpPost("presence"), RequestSizeLimit(8192)]
    public async Task<IActionResult> Presence([FromForm] PresenceRequest request, CancellationToken ct)
    {
        if (tenant.IsHostAdmin || tenant.StoreId is not > 0 || request.TabId == Guid.Empty) return Forbid();
        var ticket = tickets.Read(request.Ticket, tenant.StoreId.Value, User);
        if (ticket is null) return Forbid();
        var allowed = false;
        foreach (var code in StoreActivityCatalog.Permissions(ticket.Module))
            if ((await authorization.AuthorizeAsync(User, code)).Succeeded) { allowed = true; break; }
        if (!allowed) return Forbid();
        return registry.Presence(ticket.StoreId, ticket.UserId, request.TabId, StoreActivityTicket.Person(User),
            ticket.Module, StoreActivityTicket.Terminal(User), request.State, ticket.WorkKey, ticket.Document, ticket.DocumentTitle, request.Interaction) ? NoContent() : StatusCode(429);
    }
    [HttpGet("snapshot"), Authorize(Policy = PermissionCodes.Admin.StoreMonitorView)]
    public async Task<IActionResult> Snapshot(CancellationToken ct) => await HasCurrentAccess(ct)
        ? Ok(await ReadSnapshot(ct)) : Forbid();

    // SSE deliberately uses HTTP tenant resolution and fresh SQL authorization, independent of the POS terminal hub.
    [HttpGet("stream"), Authorize(Policy = PermissionCodes.Admin.StoreMonitorView)]
    public async Task Stream(CancellationToken ct)
    {
        if (!await HasCurrentAccess(ct)) { Response.StatusCode = 403; return; }
        using var lease = registry.TryOpenStream(tenant.StoreId!.Value);
        if (lease is null) { Response.StatusCode = 429; return; }
        var authentication = await HttpContext.AuthenticateAsync();
        var expires = authentication.Properties?.ExpiresUtc;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.Headers.XContentTypeOptions = "nosniff";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        string? previous = null;
        var checkedAt = time.GetUtcNow();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var snapshot = await ReadSnapshot(ct);
                var fingerprint = JsonSerializer.Serialize(new { snapshot.People, snapshot.Events, snapshot.Delivery, snapshot.Employees, snapshot.Checkouts });
                var changed = previous != fingerprint;
                if (changed || time.GetUtcNow() - checkedAt >= TimeSpan.FromSeconds(5))
                {
                    if ((expires is { } expiry && expiry <= time.GetUtcNow()) || !await HasCurrentAccess(ct))
                    { await WriteEvent("revoked", new { }, ct); return; }
                    checkedAt = time.GetUtcNow();
                    if (changed)
                    {
                        await WriteEvent("snapshot", snapshot, ct);
                        previous = fingerprint;
                    }
                    else await WriteEvent("heartbeat", new { serverTimeUtc = time.GetUtcNow() }, ct);
                }
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
    }
    private async Task<bool> HasCurrentAccess(CancellationToken ct) =>
        !tenant.IsHostAdmin && tenant.StoreId is > 0 &&
        await sessions.ValidateAsync(User, ct, Request.Cookies[PosDeviceSession.CookieName]) &&
        await db.Stores.AsNoTracking().AnyAsync(x => x.Id == tenant.StoreId && x.IsActive && !x.IsDeleted, ct) &&
        (await authorization.AuthorizeAsync(User, PermissionCodes.Admin.StoreMonitorView)).Succeeded;
    private async Task<StoreActivitySnapshot> ReadSnapshot(CancellationToken ct)
    {
        var snapshot = registry.Snapshot(tenant.StoreId!.Value);
        var saved = await cache.ReadAsync(tenant.StoreId.Value, async () => new StoreMonitorSavedData(
            await delivery.ReadAsync(tenant.StoreId.Value, ct), await checkouts.ReadAsync(tenant.StoreId.Value, ct)), ct);
        return snapshot with { Delivery = saved.Delivery, Checkouts = saved.Checkouts, Events = snapshot.Events.Concat(saved.Delivery.Events)
            .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).Take(StoreActivityRegistry.MaxEvents).ToArray() };
    }
    private async Task WriteEvent(string name, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await Response.WriteAsync($"event: {name}\ndata: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
