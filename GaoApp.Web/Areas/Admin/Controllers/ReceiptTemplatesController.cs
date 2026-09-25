using GaoApp.Application.Common.Security;
using GaoApp.Web.Common.POS;
using GaoApp.Web.Services.Printing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/receipt-templates")]
[Authorize(Policy = PermissionCodes.Pos.Order.Reprint)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReceiptTemplatesController(ReceiptTemplateService templates, IPOSRuntimeContextAccessor runtime,
    IAuthorizationService authorization) : BaseAdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.StoreId = CurrentStoreId;
        ViewBag.TerminalId = runtime.TerminalId;
        ViewBag.CanManage = (await authorization.AuthorizeAsync(User, PermissionCodes.System.ReceiptTemplate.Manage)).Succeeded;
        ViewBag.StoreInfo = await templates.GetStoreInfoAsync(ct);
        return View(await templates.ListAsync(ct));
    }
    [HttpGet("data")]
    public async Task<IActionResult> Data(CancellationToken ct) => Ok(await templates.ListAsync(ct));

    [HttpGet("store-info")]
    public async Task<IActionResult> StoreInfo(CancellationToken ct) => Ok(await templates.GetStoreInfoAsync(ct));

    [HttpPut("store-info"), Authorize(Policy = PermissionCodes.System.ReceiptTemplate.Manage)]
    public async Task<IActionResult> SaveStoreInfo([FromBody] SaveReceiptStoreInfoRequest request, CancellationToken ct) =>
        ModelState.IsValid ? Ok(await templates.SaveStoreInfoAsync(request, ct)) : ValidationProblem(ModelState);

    [HttpPost("data"), Authorize(Policy = PermissionCodes.System.ReceiptTemplate.Manage)]
    public async Task<IActionResult> Create([FromBody] SaveReceiptTemplateRequest request, CancellationToken ct) =>
        ModelState.IsValid ? Ok(await templates.SaveAsync(null, request, ct)) : ValidationProblem(ModelState);

    [HttpPut("data/{id:int}"), Authorize(Policy = PermissionCodes.System.ReceiptTemplate.Manage)]
    public async Task<IActionResult> Update(int id, [FromBody] SaveReceiptTemplateRequest request, CancellationToken ct) =>
        ModelState.IsValid ? Ok(await templates.SaveAsync(id, request, ct)) : ValidationProblem(ModelState);

    [HttpDelete("data/{id:int}"), Authorize(Policy = PermissionCodes.System.ReceiptTemplate.Manage)]
    public async Task<IActionResult> Delete(int id, [FromBody] DeleteTemplateRequest request, CancellationToken ct)
    {
        await templates.DeleteAsync(id, request.RowVersion, ct);
        return Ok(new { success = true });
    }
    public sealed record DeleteTemplateRequest(string? RowVersion);
}
