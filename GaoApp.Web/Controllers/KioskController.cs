using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Customers;
using GaoApp.Application.Interfaces.Services.Customers;
using GaoApp.Application.Interfaces.Services.Display;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Kiosk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace GaoApp.Web.Controllers;

[AllowAnonymous, Route("kiosk"), AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class KioskController(AppDbContext db, KioskAccess access, KioskService kiosk,
    IPOSService pos, ICustomerProfileReader profiles, IDisplayPromotionService promotions) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpPost("activate"), EnableRateLimiting("kiosk-activation")]
    public async Task<IActionResult> Activate([FromBody] KioskSecretRequest input, CancellationToken ct)
    {
        if (!ModelState.IsValid || access.StoreId <= 0 || input.Key.Length != 64) return BadRequest(new { message = "Khóa kích hoạt không hợp lệ." });
        var hash = KioskAccess.Hash(input.Key.Trim());
        var station = await db.Set<KioskStation>().SingleOrDefaultAsync(x => x.StoreId == access.StoreId && x.IsActive && !x.IsDeleted && x.ActivationHash == hash, ct);
        if (station == null) return BadRequest(new { message = "Khóa kích hoạt không đúng hoặc đã sử dụng." });
        await using var gate = await KioskLock.AcquireAsync(db, station.Id, ct);
        await db.Entry(station).ReloadAsync(ct);
        if (station.ActivationHash != hash || station.ActivationExpiresAtUtc <= DateTime.UtcNow || !station.IsActive)
            return BadRequest(new { message = "Khóa đã hết hạn hoặc đã sử dụng. Vui lòng tạo khóa mới trong quản trị." });
        var secret = KioskAccess.Secret(); station.DeviceHash = KioskAccess.Hash(secret);
        station.ActivationHash = null; station.ActivationExpiresAtUtc = null;
        await db.SaveChangesAsync(ct);
        Response.Cookies.Append(KioskAccess.Cookie, secret, new CookieOptions { HttpOnly = true, Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict, Path = "/kiosk", MaxAge = TimeSpan.FromDays(180), IsEssential = true });
        return Ok(new { success = true });
    }

    private async Task<IActionResult> Run(Func<KioskStation, Task<object>> action, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Dữ liệu không hợp lệ. Vui lòng kiểm tra lại." });
        var station = await access.FindAsync(ct);
        if (station == null) return Unauthorized(new { message = "Thiết bị chưa được kích hoạt hoặc đã bị thu hồi quyền." });
        await using var gate = await KioskLock.AcquireAsync(db, station.Id, ct);
        await db.Entry(station).ReloadAsync(ct);
        if (!station.IsActive || station.IsDeleted || station.DeviceHash != KioskAccess.Hash(Request.Cookies[KioskAccess.Cookie]!))
            return Unauthorized(new { message = "Quyền thiết bị đã bị thu hồi." });
        using var identity = access.Enter(station);
        try { return Ok(await action(station)); }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
    }
    [HttpGet("api/state")]
    public Task<IActionResult> State(CancellationToken ct) => Run(s => kiosk.StateAsync(s, ct), ct);
    [HttpPost("api/poll")]
    public Task<IActionResult> Poll([FromBody] KioskPollRequest request, CancellationToken ct) => Run(s => {
        s.LastSeenAtUtc = DateTime.UtcNow;
        if (request.RecentActivity && s.OrderId != null) s.CartTouchedAtUtc = DateTime.UtcNow;
        if (request.RecentActivity && s.CustomerId != null && s.CustomerExpiresAtUtc > DateTime.UtcNow)
            s.CustomerExpiresAtUtc = DateTime.UtcNow.AddMinutes(2);
        return kiosk.PollAsync(s, ct, request.CheckPayment);
    }, ct);
    [HttpPost("api/command"), EnableRateLimiting("kiosk-commands")]
    public Task<IActionResult> Command([FromBody] KioskCommand command, CancellationToken ct) => Run(s => kiosk.CommandAsync(s, command, ct), ct);
    [HttpGet("api/promotions")]
    public Task<IActionResult> Promotions(CancellationToken ct) => Run(async _ => await promotions.GetActiveForCustomerDisplayAsync(ct), ct);

    [HttpGet("api/products"), EnableRateLimiting("kiosk-commands")]
    public Task<IActionResult> Products(string? q, CancellationToken ct) => Run(async _ =>
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length > 100) return Array.Empty<object>();
        var items = await pos.SearchProductsForPOSAsync(q.Trim(), 15, ct);
        var ids = items.Select(x => x.VariantId).ToArray();
        var variants = await db.ProductVariants.AsNoTracking().Include(x => x.Product).ThenInclude(x => x.Brand)
            .Include(x => x.Product).ThenInclude(x => x.BaseUnit)
            .Include(x => x.UnitConversions).ThenInclude(x => x.Unit)
            .Include(x => x.UnitConversions).ThenInclude(x => x.Barcodes)
            .Where(x => x.StoreId == access.StoreId && ids.Contains(x.Id) && !x.IsDeleted && x.IsActive && x.Product.IsActive && x.Product.IsSellable).ToListAsync(ct);
        return items.Where(x => variants.Any(v => v.Id == x.VariantId)).Select(item => {
            var variant = variants.Single(v => v.Id == item.VariantId);
            return new { item.VariantId, item.DisplayName, item.Barcode, item.ImageUrl, item.Price,
                canSelfCheckout = variant.HasInputInvoice, description = variant.Product.Description, brand = variant.Product.Brand?.Name, baseUnit = variant.Product.BaseUnit.Name,
                units = variant.UnitConversions.Where(u => !u.IsDeleted && u.IsActive && !u.Unit.IsDeleted).OrderBy(u => u.Factor)
                    .Select(u => new { u.Id, name = u.Unit.Name, u.Factor, price = u.Price > 0 ? u.Price.Value : variant.Price > 0 ? variant.Price.Value : variant.Product.BasePrice,
                        barcodes = u.Barcodes.Where(b => b.IsActive && !b.IsDeleted).Select(b => b.Barcode).ToArray() }).ToArray() };
        }).ToArray();
    }, ct);

    [HttpPost("api/customer"), EnableRateLimiting("kiosk-customer")]
    public Task<IActionResult> Customer([FromBody] KioskPhoneRequest request, CancellationToken ct) => Run(async s =>
    {
        s.CustomerId = null; s.CustomerExpiresAtUtc = null; await db.SaveChangesAsync(ct);
        var phone = new string(request.Phone.Where(char.IsAsciiDigit).ToArray());
        if (phone.StartsWith("84") && phone.Length == 11) phone = "0" + phone[2..];
        if (phone.Length is < 10 or > 11) throw new ValidationAppException("Vui lòng nhập số điện thoại hợp lệ.");
        var intl = "+84" + phone[1..]; var intlPlain = "84" + phone[1..];
        var matches = await db.Customers.AsNoTracking().Where(x => x.StoreId == access.StoreId && !x.IsDeleted && x.IsActive && x.Phone != null &&
            (x.Phone.Replace(" ", "").Replace("-", "").Replace(".", "") == phone ||
             x.Phone.Replace(" ", "").Replace("-", "").Replace(".", "") == intl ||
             x.Phone.Replace(" ", "").Replace("-", "").Replace(".", "") == intlPlain))
            .Select(x => x.Id).Take(2).ToListAsync(ct);
        if (matches.Count != 1) throw new ValidationAppException(matches.Count == 0 ? "Chưa tìm thấy khách hàng với số điện thoại này." : "Số điện thoại có nhiều hồ sơ. Vui lòng nhờ nhân viên kiểm tra.");
        s.CustomerId = matches[0]; s.CustomerExpiresAtUtc = DateTime.UtcNow.AddMinutes(2); await db.SaveChangesAsync(ct);
        var profile = (await profiles.SummaryAsync(matches[0], true, ct))!;
        return new { profile.Customer.Name, profile.Customer.Phone, profile.Customer.PriceTier, profile.Rewards,
            profile.RewardNotice, profile.NetSales, profile.CompletedOrders, profile.LastPurchaseAtUtc };
    }, ct);
    private static int CustomerId(KioskStation s)
    {
        if (s.CustomerId == null || s.CustomerExpiresAtUtc <= DateTime.UtcNow) throw new ForbiddenAppException("Phiên xem thông tin đã kết thúc. Vui lòng nhập lại số điện thoại.");
        s.CustomerExpiresAtUtc = DateTime.UtcNow.AddMinutes(2); return s.CustomerId.Value;
    }
    [HttpPost("api/customer/history")]
    public Task<IActionResult> History([FromBody] CustomerProfileQuery query, CancellationToken ct) => Run(async s =>
    {
        if (query.Tab is not ("orders" or "points" or "vouchers") || query.Search?.Length > 100 || query.From > query.To)
            throw new ValidationAppException("Bộ lọc không hợp lệ.");
        var page = await profiles.PageAsync(CustomerId(s), query, true, ct);
        await db.SaveChangesAsync(ct);
        return new { page!.Page, page.PageSize, page.Total, page.MoneyPerPoint,
            items = page.Items.Select(x => new { x.Id, x.Kind, title = x.Kind == "points" ? PointTitle(x.Status) : x.Title, x.AtUtc, x.Amount, x.BalanceAmount, x.BalancePoints,
                x.DeltaPoints, x.Status, x.PaymentStatus, x.PaymentMethods, x.VoucherId, x.OrderId }) };
    }, ct);
    private static string PointTitle(int type) => type switch { 1 => "Điểm từ hệ thống cũ", 2 => "Tích điểm mua hàng", 3 => "Điều chỉnh do trả hàng", 4 => "Đổi điểm lấy voucher", 5 => "Điều chỉnh điểm", 6 => "Điều chỉnh do hủy đơn", 7 => "Điều chỉnh do hoàn tiền", _ => "Phát sinh điểm" };
    [HttpPost("api/customer/voucher/{id:int}")]
    public Task<IActionResult> CustomerVoucher(int id, CancellationToken ct) => Run(async s => {
        var customerId = CustomerId(s);
        var voucher = await db.CustomerRewardVouchers.AsNoTracking().Where(x => x.Id == id && x.StoreId == access.StoreId && x.CustomerId == customerId && !x.IsDeleted)
            .Select(x => new { x.VoucherCode, x.Value, x.Status, x.IssuedAtUtc, x.UsedAtUtc, x.UsedOrderId,
                usedOrderNumber = x.UsedOrder != null ? x.UsedOrder.OrderNumber : null }).SingleOrDefaultAsync(ct);
        if (voucher == null) throw new NotFoundAppException("Không tìm thấy voucher của khách hàng.");
        await db.SaveChangesAsync(ct); return voucher;
    }, ct);
    [HttpPost("api/customer/order/{id:int}")]
    public Task<IActionResult> CustomerOrder(int id, CancellationToken ct) => Run(async s =>
    {
        var customerId = CustomerId(s);
        var order = await db.Orders.AsNoTracking().Where(x => x.Id == id && x.StoreId == access.StoreId && x.CustomerId == customerId && !x.IsDeleted)
            .Select(x => new { x.OrderNumber, x.GrandTotal, x.PaidTotal, x.Status, x.CompletedAtUtc,
                lines = x.Lines.Where(l => !l.IsDeleted).Select(l => new { l.ItemName, l.Quantity, l.SellingUnitName, l.LineTotal }) }).SingleOrDefaultAsync(ct);
        if (order == null) throw new NotFoundAppException("Không tìm thấy đơn của khách hàng.");
        await db.SaveChangesAsync(ct); return order;
    }, ct);
    [HttpPost("api/customer/end")]
    public Task<IActionResult> EndCustomer(CancellationToken ct) => Run(async s => {
        s.CustomerId = null; s.CustomerExpiresAtUtc = null; await db.SaveChangesAsync(ct); return new { success = true }; }, ct);
}
