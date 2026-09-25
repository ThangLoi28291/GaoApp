using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/label-printing-settings")]
[Authorize(Policy = PermissionCodes.System.ProductLabel.Manage)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LabelPrintingSettingsController(IAuthorizationService authorization) : BaseAdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewBag.IsSettings = true;
        ViewBag.CanManage = true;
        ViewBag.CanPrint = (await authorization.AuthorizeAsync(User, PermissionCodes.System.ProductLabel.Print)).Succeeded;
        ViewBag.StoreId = CurrentStoreId;
        return View("~/Areas/Admin/Views/LabelPrinting/Index.cshtml");
    }
}
