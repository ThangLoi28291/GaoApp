using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Customers;
using GaoApp.Application.Interfaces.Services.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace GaoApp.Web.Areas.Admin.Controllers;
[Area("Admin")]
[Route("admin/customers/{id:int}/profile")]
[Authorize(Policy=PermissionCodes.Catalog.Customer.View)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CustomerProfileController(ICustomerProfileReader reader,IAuthorizationService authorization) : Controller
{
    private async Task<bool> OrdersAllowed() => (await authorization.AuthorizeAsync(User,PermissionCodes.Pos.Order.View)).Succeeded;
    [HttpGet("")]
    public async Task<IActionResult> Index(int id,CancellationToken ct) {
        var summary=await reader.SummaryAsync(id,await OrdersAllowed(),ct);
        return summary is null?NotFound():View("~/Areas/Admin/Views/Customer/Profile.cshtml",summary);
    }
    [HttpGet("data")]
    public async Task<IActionResult> Data(int id,[FromQuery]CustomerProfileQuery query,CancellationToken ct) {
        if(!ModelState.IsValid || query.Tab is not ("overview" or "orders" or "points" or "vouchers") || query.Search?.Length>100 ||
            query.From?.Year<1900 || query.To?.Year>9998 || (query.From.HasValue&&query.To.HasValue&&query.From>query.To))
            return BadRequest(new {message="Bộ lọc không hợp lệ. Kiểm tra từ ngày, đến ngày và từ khóa."});
        var canOrders=await OrdersAllowed();if(query.Tab=="orders"&&!canOrders)return Forbid();
        var page=await reader.PageAsync(id,query,canOrders,ct);return page is null?NotFound():Json(page);
    }
}
