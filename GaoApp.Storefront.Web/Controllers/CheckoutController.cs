using System.Text.RegularExpressions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Storefront.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Storefront.Web.Controllers;

public sealed class CheckoutController(IStorefrontCheckoutService service, IStorefrontAddressCatalog addresses,
    StorefrontCheckoutReadiness readiness, AppDbContext db,IStorefrontQrService? qrService=null,IStorefrontBankRegistration? bankRegistration=null) : Controller
{
    [HttpGet("/shop/checkout")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var settings=await db.StorefrontSettings.AsNoTracking().SingleOrDefaultAsync(x=>x.StoreId==db.CurrentStoreId,ct);
        ViewData["CodEnabled"]=settings?.CodEnabled==true;ViewData["QrEnabled"]=settings?.QrEnabled==true&&bankRegistration?.Get(db.CurrentStoreId??0)?.Verified==true;
        if (settings?.IsEnabled!=true||!settings.CodEnabled&&!(bool)ViewData["QrEnabled"]!)
        { ViewData["Unavailable"] = "Cửa hàng đang tạm ngừng nhận đơn mới. Bạn vẫn có thể kiểm tra lại yêu cầu trước đó và xem đơn bằng liên kết riêng."; return View(); }
        if (!readiness.Ready) { ViewData["Unavailable"] = "Chức năng đặt hàng chưa sẵn sàng. Cửa hàng cần kiểm tra cấu hình bảo vệ trước khi nhận đơn."; return View(); }
        try { return View(await addresses.GetAsync(ct)); }
        catch (ValidationAppException ex) { ViewData["Unavailable"] = ex.Message; return View(); }
    }
    [HttpGet("/shop/checkout/wards"), EnableRateLimiting("checkout-read")]
    public async Task<IActionResult> Wards(string provinceCode, CancellationToken ct)
    {
        if (!readiness.Ready) return StatusCode(503, new { message = "Chức năng đặt hàng chưa sẵn sàng." });
        try
        {
            var directory = await addresses.GetAsync(ct);
            var province = directory.Provinces.SingleOrDefault(x => x.Code == provinceCode);
            return province is null ? NotFound() : Json(new { wards = province.Wards });
        }
        catch (ValidationAppException ex) { return StatusCode(503, new { message = ex.Message }); }
    }
    [HttpPost("/shop/checkout/quote"), ValidateAntiForgeryToken, EnableRateLimiting("checkout-quote")]
    [Consumes("application/json"), RequestSizeLimit(49152)]
    public async Task<IActionResult> Quote([FromBody] StorefrontQuoteRequest? request, CancellationToken ct)
    {
        if (!readiness.Ready) return StatusCode(503, new { message = "Chức năng đặt hàng chưa sẵn sàng." });
        if (request is not null)
        {
            if (!Regex.IsMatch(Regex.Replace(request.RecipientPhone ?? "", @"[ .()\-]", ""), @"^\+?[0-9]{9,15}$")) ModelState.AddModelError(nameof(request.RecipientPhone), "Nhập số điện thoại từ 9 đến 15 chữ số.");
            try
            {
                var directory = await addresses.GetAsync(ct); var province = directory.Provinces.SingleOrDefault(x => x.Code == request.ProvinceCode?.Trim().ToUpperInvariant());
                if (province is null) ModelState.AddModelError(nameof(request.ProvinceCode), "Hãy chọn tỉnh/thành trong danh sách.");
                else if (!province.Wards.Any(x => x.Code == request.WardCode?.Trim().ToUpperInvariant())) ModelState.AddModelError(nameof(request.WardCode), "Hãy chọn phường/xã thuộc tỉnh/thành đã chọn.");
            }
            catch (ValidationAppException ex) { return StatusCode(503, new { message = ex.Message }); }
        }
        if (request is null || !ModelState.IsValid) return BadRequest(new { message = "Hãy kiểm tra các thông tin được đánh dấu.", errors = ModelState.Where(x => x.Value?.Errors.Count > 0).ToDictionary(x => x.Key, x => x.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Giá trị không hợp lệ." : e.ErrorMessage).ToArray()) });
        try { return Json(await service.QuoteAsync(request, ct)); }
        catch (ValidationAppException ex) { return UnprocessableEntity(new { message = ex.Message }); }
        catch (ConflictAppException ex) { return Conflict(new { message = ex.Message }); }
    }
    [HttpPost("/shop/checkout/place"), ValidateAntiForgeryToken, EnableRateLimiting("checkout-place")]
    [Consumes("application/json"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Place([FromBody] StorefrontPlaceOrderRequest? request, CancellationToken ct)
    {
        if (!readiness.Ready) return StatusCode(503, new { message = "Chưa mở lại được khóa bảo vệ yêu cầu. Không tạo đơn mới; hãy thử lại cùng yêu cầu khi cửa hàng khắc phục." });
        if (request is null || !ModelState.IsValid) return BadRequest(new { message = "Yêu cầu đặt hàng không hợp lệ." });
        try { return Json(await service.PlaceAsync(request, ct)); }
        catch (ValidationAppException ex) { return UnprocessableEntity(new { message = ex.Message, safeToRequote = true }); }
        catch (ConflictAppException ex) { return Conflict(new { message = ex.Message }); }
        catch (NotFoundAppException) { return NotFound(new { message = "Không tìm thấy báo giá. Hãy kiểm tra lại yêu cầu trước khi đặt.", safeToRequote = true }); }
    }
    public sealed class AccessRequest { public string Token { get; set; } = ""; }
    [HttpPost("/shop/orders/qr"),ValidateAntiForgeryToken,EnableRateLimiting("guest-order")]
    [Consumes("application/json"),RequestSizeLimit(1024)]
    public async Task<IActionResult> Qr([FromBody]AccessRequest? request,CancellationToken ct)
    {var qr=request is null||qrService is null?null:await qrService.GetAsync(request.Token,ct);return qr is null?NotFound():Json(qr);}
    [HttpPost("/shop/orders/qr/image"),ValidateAntiForgeryToken,EnableRateLimiting("guest-order")]
    [Consumes("application/json"),RequestSizeLimit(1024)]
    public async Task<IActionResult> QrImage([FromBody]AccessRequest? request,CancellationToken ct)
    {var bytes=request is null||qrService is null?null:await qrService.ImageAsync(request.Token,ct);return bytes is null?NotFound():File(bytes,"image/png");}
    [HttpPost("/shop/orders/access"), ValidateAntiForgeryToken, EnableRateLimiting("guest-order")]
    [Consumes("application/json"), RequestSizeLimit(1024)]
    public async Task<IActionResult> Access([FromBody] AccessRequest? request, CancellationToken ct)
    {
        var order = request is null ? null : await service.GetGuestOrderAsync(request.Token, ct);
        return order is null ? NotFound(new { message = "Liên kết riêng không hợp lệ hoặc không thuộc cửa hàng này." }) : Json(order);
    }
}
