using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/reward-vouchers")]
[Authorize(Policy = PermissionCodes.Catalog.Customer.View)]
public sealed class RewardVouchersController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        return View();
    }
}