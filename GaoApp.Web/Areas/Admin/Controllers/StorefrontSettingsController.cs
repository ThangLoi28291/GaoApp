using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/storefront/settings")]
public sealed class StorefrontSettingsController(IStorefrontSettingsService service, IStorefrontSalesControlService salesControl) : BaseAdminController
{
    [HttpGet(""), Authorize(Policy = StorefrontPermissions.ViewSettings)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["SalesControl"] = await salesControl.GetAsync(ct);
        return View(await service.GetAsync(ct));
    }

    [HttpPost("activate"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.ManageSettings)]
    public Task<IActionResult> Activate(StorefrontSalesControlRequest request, CancellationToken ct) => Control(request, true, ct);

    [HttpPost("stop-new-orders"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.ManageSettings)]
    public Task<IActionResult> StopNewOrders(StorefrontSalesControlRequest request, CancellationToken ct) => Control(request, false, ct);

    private async Task<IActionResult> Control(StorefrontSalesControlRequest request, bool enable, CancellationToken ct)
    {
        try
        {
            if (enable) await salesControl.ActivateAsync(request, ct); else await salesControl.StopNewOrdersAsync(request, ct);
            ToastSuccess(enable ? "Đã cho phép nhận đơn trong cấu hình cửa hàng. Cổng public riêng vẫn phải được nghiệm thu và mở bởi người vận hành." : "Đã dừng nhận đơn mới. Tiếp tục xử lý đơn, tiền và yêu cầu cũ.");
            return RedirectToAction(nameof(Index));
        }
        catch (ValidationAppException ex) { Response.StatusCode = 400; ModelState.AddModelError("", ex.Message); }
        catch (ConflictAppException ex) { Response.StatusCode = 409; ModelState.AddModelError("", ex.Message); }
        ViewData["SalesControl"] = await salesControl.GetAsync(ct);
        return View("Index", await service.GetAsync(ct));
    }

    [HttpPost(""), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.ManageSettings)]
    public async Task<IActionResult> Save([Bind(Prefix = "Settings")] StorefrontSettingsRequest request, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try { await service.SaveAsync(request, ct); ToastSuccess("Đã lưu cấu hình website. Website hiện chưa mở bán."); return RedirectToAction(nameof(Index)); }
            catch (ValidationAppException ex) { ModelState.AddModelError("", ex.Message); }
            catch (ConflictAppException ex) { Response.StatusCode = 409; ModelState.AddModelError("", ex.Message); }
        }
        ViewData["SalesControl"] = await salesControl.GetAsync(ct);
        return View("Index", (await service.GetAsync(ct)) with { Settings = request });
    }

    [HttpGet("zones/new"), Authorize(Policy = StorefrontPermissions.ViewSettings)]
    public async Task<IActionResult> NewZone(CancellationToken ct)
    {
        var page = await service.GetAsync(ct);
        if (!page.CanManageShipping) return Forbid();
        ViewBag.ByWeight = page.Settings.ShippingByWeight;
        return View("Zone", new StorefrontShippingZoneRequest { SettingsVersion = page.Settings.Version });
    }

    [HttpGet("zones/{id:int}"), Authorize(Policy = StorefrontPermissions.ViewSettings)]
    public async Task<IActionResult> EditZone(int id, CancellationToken ct)
    {
        var page = await service.GetAsync(ct);
        if (!page.CanManageShipping) return Forbid();
        var zone = page.Zones.SingleOrDefault(x => x.Id == id);
        if (zone is null) return NotFound();
        ViewBag.ByWeight = page.Settings.ShippingByWeight;
        return View("Zone", new StorefrontShippingZoneRequest { Id = zone.Id, Version = zone.Version, SettingsVersion = page.Settings.Version, Code = zone.Code,
            Name = zone.Name, ProvinceCode = zone.ProvinceCode, WardCode = zone.WardCode, DeliveryMethod = zone.DeliveryMethod,
            IsActive = zone.IsActive, MinDeliveryDays = zone.MinDeliveryDays, MaxDeliveryDays = zone.MaxDeliveryDays,
            BaseFee = zone.BaseFee, AdditionalFee = zone.AdditionalFee, FirstWeightKg = zone.FirstWeightKg,
            AdditionalStepKg = zone.AdditionalStepKg, FreeFromGoodsAmount = zone.FreeFromGoodsAmount });
    }

    [HttpPost("zones"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.ManageShipping)]
    public async Task<IActionResult> SaveZone(StorefrontShippingZoneRequest request, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try { await service.SaveZoneAsync(request, ct); ToastSuccess("Đã lưu vùng giao và phiên bản bảng phí."); return RedirectToAction(nameof(Index)); }
            catch (ValidationAppException ex) { ModelState.AddModelError("", ex.Message); }
            catch (ConflictAppException ex) { Response.StatusCode = 409; ModelState.AddModelError("", ex.Message); }
        }
        ViewBag.ByWeight = (await service.GetAsync(ct)).Settings.ShippingByWeight;
        return View("Zone", request);
    }
}
