using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/inventory-adjustment")]
[Authorize]
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