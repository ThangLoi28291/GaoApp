using GaoApp.Web.Security;
using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/inventory-adjustment")]
[Authorize]
[Authorize(Policy = PermissionCodes.Inventory.Adjustment.View)]
public class InventoryAdjustmentController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        return RedirectToAction(
            "Index",
            "InventoryAdjustmentDocuments",
            new { area = "Admin" });
    }
}