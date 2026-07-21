using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
[Route("admin")]
public class HomeController : Controller
{
    [HttpGet("")]
    [HttpGet("home")]
    [HttpGet("dashboard")]
    public IActionResult Index()
    {
        return View();
    }
}