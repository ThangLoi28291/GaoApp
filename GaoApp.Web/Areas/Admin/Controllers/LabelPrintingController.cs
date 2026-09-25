using GaoApp.Application.Common.Security;
using GaoApp.Infrastructure.Printing;
using GaoApp.Web.Services.Printing;
using GaoApp.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/label-printing")]
[RequireAnyPermission(PermissionCodes.System.ProductLabel.Print, PermissionCodes.System.ProductLabel.Manage)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LabelPrintingController(ProductLabelService labels, IAuthorizationService authorization) : BaseAdminController
{
    [HttpGet(""), Authorize(Policy = PermissionCodes.System.ProductLabel.Print)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.CanManage = (await authorization.AuthorizeAsync(User, PermissionCodes.System.ProductLabel.Manage)).Succeeded;
        ViewBag.IsSettings = false;
        ViewBag.CanPrint = true;
        ViewBag.StoreId = CurrentStoreId;
        return View();
    }
    [HttpGet("templates")] public async Task<IActionResult> Templates(CancellationToken ct) => Ok(await labels.Templates(ct));
    [HttpPost("templates"), Authorize(Policy = PermissionCodes.System.ProductLabel.Manage)]
    public async Task<IActionResult> CreateTemplate([FromBody] SaveLabelTemplate request, CancellationToken ct) =>
        ModelState.IsValid ? Ok(await labels.SaveTemplate(null, request, ct)) : ValidationProblem(ModelState);
    [HttpPut("templates/{id:int}"), Authorize(Policy = PermissionCodes.System.ProductLabel.Manage)]
    public async Task<IActionResult> SaveTemplate(int id, [FromBody] SaveLabelTemplate request, CancellationToken ct) =>
        ModelState.IsValid ? Ok(await labels.SaveTemplate(id, request, ct)) : ValidationProblem(ModelState);
    [HttpDelete("templates/{id:int}"), Authorize(Policy = PermissionCodes.System.ProductLabel.Manage)]
    public async Task<IActionResult> DeleteTemplate(int id, [FromBody] LabelVersion request, CancellationToken ct)
    { await labels.DeleteTemplate(id, request.RowVersion, ct); return Ok(new { success = true }); }
    [HttpPost("preview")]
    public IActionResult Preview([FromBody] LabelPreviewRequest request)
    {
        if (!ModelState.IsValid || request.Design is null) return ValidationProblem(ModelState);
        if (!OperatingSystem.IsWindows()) return Conflict(new { message = "Xem trước tem cần máy chủ Windows." });
        return File(ProductLabelRenderer.Preview(request.Design with { BarcodeFormat = "AUTO" }, request.Product, request.Dpi is 203 or 300 ? request.Dpi : 203), "image/png");
    }
    [HttpGet("layouts/{key}/preview"), Authorize(Policy = PermissionCodes.System.ProductLabel.Manage)]
    public IActionResult LayoutPreview(string key)
    {
        if (!ProductLabelLayouts.All.Any(x => x.Key == key)) return NotFound();
        if (!OperatingSystem.IsWindows()) return Conflict(new { message = "Xem trước tem cần máy chủ Windows." });
        return File(ProductLabelRenderer.Preview(new ProductLabelDesign { Layout = key, Columns = 1 }), "image/png");
    }
    public sealed record LabelPreviewRequest(ProductLabelDesign Design, LabelProduct? Product, int Dpi = 203);
    [HttpGet("printers")] public async Task<IActionResult> Printers(CancellationToken ct) => Ok(await labels.Printers(ct));
    [HttpGet("installed-printers"), Authorize(Policy = PermissionCodes.System.ProductLabel.Manage)]
    public IActionResult InstalledPrinters() => OperatingSystem.IsWindows() ? Ok(WindowsLabelPrinter.Installed()) : Ok(Array.Empty<string>());
    [HttpPost("printers"), Authorize(Policy = PermissionCodes.System.ProductLabel.Manage)]
    public async Task<IActionResult> CreatePrinter([FromBody] SaveLabelPrinter request, CancellationToken ct) =>
        ModelState.IsValid ? Ok(await labels.SavePrinter(null, request, ct)) : ValidationProblem(ModelState);
    [HttpPut("printers/{id:int}"), Authorize(Policy = PermissionCodes.System.ProductLabel.Manage)]
    public async Task<IActionResult> SavePrinter(int id, [FromBody] SaveLabelPrinter request, CancellationToken ct) =>
        ModelState.IsValid ? Ok(await labels.SavePrinter(id, request, ct)) : ValidationProblem(ModelState);
    [HttpGet("tasks"), Authorize(Policy = PermissionCodes.System.ProductLabel.Print)] public async Task<IActionResult> Tasks(CancellationToken ct) => Ok(await labels.Tasks(ct));
    [HttpPost("receipts/{id:int}"), Authorize(Policy = PermissionCodes.System.ProductLabel.Print)] public async Task<IActionResult> AddReceipt(int id, CancellationToken ct) => Ok(await labels.AddReceipt(id, ct));
    [HttpGet("tasks/{id:int}"), Authorize(Policy = PermissionCodes.System.ProductLabel.Print)] public async Task<IActionResult> Detail(int id, CancellationToken ct) => Ok(await labels.Detail(id, ct));
    [HttpPost("tasks/{id:int}/refresh"), Authorize(Policy = PermissionCodes.System.ProductLabel.Print)]
    public async Task<IActionResult> Refresh(int id, [FromBody] LabelVersion request, CancellationToken ct) => Ok(await labels.Refresh(id, request.RowVersion, ct));
    [HttpPut("tasks/{id:int}/plan"), Authorize(Policy = PermissionCodes.System.ProductLabel.Print)]
    public async Task<IActionResult> Plan(int id, [FromBody] LabelPlanRequest request, CancellationToken ct) =>
        ModelState.IsValid ? Ok(await labels.Plan(id, request, ct)) : ValidationProblem(ModelState);
    [HttpPost("tasks/{id:int}/complete"), Authorize(Policy = PermissionCodes.System.ProductLabel.Print)]
    public async Task<IActionResult> Complete(int id, [FromBody] LabelVersion request, CancellationToken ct)
    { await labels.Complete(id, request.RowVersion, ct); return Ok(new { success = true }); }
    [HttpGet("jobs"), Authorize(Policy = PermissionCodes.System.ProductLabel.Print)] public async Task<IActionResult> Jobs([FromQuery] int? taskId, CancellationToken ct) => Ok(await labels.Jobs(taskId, ct));
    [HttpPost("jobs"), Authorize(Policy = PermissionCodes.System.ProductLabel.Print)]
    public async Task<IActionResult> Print([FromBody] LabelJobRequest request, CancellationToken ct) =>
        ModelState.IsValid ? Ok(await labels.Enqueue(request, ct)) : ValidationProblem(ModelState);
    [HttpPost("jobs/{id:int}/confirm"), Authorize(Policy = PermissionCodes.System.ProductLabel.Print)]
    public async Task<IActionResult> Confirm(int id, [FromBody] LabelConfirmRequest request, CancellationToken ct)
    { if (!ModelState.IsValid) return ValidationProblem(ModelState); await labels.Confirm(id, request, false, ct); return Ok(new { success = true }); }
    [HttpPost("jobs/{id:int}/cancel"), Authorize(Policy = PermissionCodes.System.ProductLabel.Print)]
    public async Task<IActionResult> Cancel(int id, [FromBody] LabelVersion request, CancellationToken ct)
    { await labels.Confirm(id, new(request.RowVersion, [], null), true, ct); return Ok(new { success = true }); }
}
