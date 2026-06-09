using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/inventory-ledger")]
[Authorize]
public class InventoryLedgerController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Thẻ kho / Lịch sử giao dịch kho";
        return View();
    }
}