using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Interfaces.Services.Delivery;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Areas.Admin.ViewModels.Delivery;
using GaoApp.Web.Services.Delivery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Policy = PermissionCodes.Delivery.View)]
[Route("admin/deliveries")]
public sealed class DeliveryPageController(IDeliveryFoundationService deliveries, AppDbContext db,
    IDeliveryPickingService? picking = null) : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        Response.Headers.CacheControl = "no-store";
        return View();
    }
    [HttpGet("{id:int}/bill")]
    public async Task<IActionResult> Bill(int id, CancellationToken ct)
    {
        try
        {
            var detail = await deliveries.GetAsync(id, ct);
            var currentPicking = picking is null ? null : await picking.GetAsync(id, ct);
            detail = currentPicking?.Delivery ?? detail;
            var store = await db.Stores.AsNoTracking().SingleAsync(x => x.Id == db.CurrentStoreId, ct);
            // Verified delivery provenance can retain names after the originating employee/terminal is retired.
            var terminal = await db.POSTerminals.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.StoreId == store.Id && x.Id == detail.CreatedTerminalId, ct);
            var employee = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == detail.CreatedByUserId, ct);
            var warehouse = await db.Warehouses.AsNoTracking().SingleAsync(x => x.StoreId == store.Id && x.Id == detail.SourceWarehouseId, ct);
            var lookup = Url.Action(nameof(Index), "DeliveryPage", new { area = "Admin", key = detail.LookupToken, revision = detail.Revision }, Request.Scheme)!;
            Response.Headers.CacheControl = "no-store";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            return View(new DeliveryBillViewModel(detail, store.Name, terminal.Name, employee.FullName ?? employee.UserName,
                warehouse.Name, lookup, "data:image/png;base64," + Convert.ToBase64String(DeliveryQrRenderer.Png(lookup)), currentPicking));
        }
        catch (DeliveryFoundationException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message }); }
    }
}
