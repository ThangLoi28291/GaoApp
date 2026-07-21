using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Media;
using GaoApp.Application.Interfaces.Services.Media;
using GaoApp.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/media")]
[Authorize]
public class MediaController : Controller
{
    private readonly ITempUploadService _temp;
    private readonly ITenantContext _tenant;

    public MediaController(ITempUploadService temp, ITenantContext tenant)
    {
        _temp = temp;
        _tenant = tenant;
    }

    [HttpPost("temp")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TempUpload(CancellationToken ct)
    {
        // 1) check store
        var storeId = _tenant.StoreId ?? 0;
        if (storeId <= 0)
            return BadRequest("Missing StoreId (TenantContext chưa set). Hãy chạy đúng subdomain store, ví dụ: store1.localhost.");

        // 2) check file
        var file = Request.Form.Files.FirstOrDefault();
        if (file == null)
            return BadRequest("No file received. Check FilePond field name and request form-data.");

        var uploadError = await UploadSecurityValidator
            .ValidateProductImageAsync(file, ct);

        if (uploadError != null)
            return BadRequest(uploadError);

        await using var stream = file.OpenReadStream();

        var token = await _temp.UploadAsync(new TempUploadRequest
        {
            Content = stream,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length
        }, storeId, userId: null, ct);

        return Content(token);
    }


    [HttpDelete("temp")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TempRevert(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var token = (await reader.ReadToEndAsync()).Trim();

        var storeId = _tenant.StoreId ?? 0;
        if (storeId <= 0) return BadRequest("Missing StoreId");

        var ok = await _temp.RevertAsync(token, storeId, userId: null, ct);
        return ok ? Ok() : NotFound();
    }
    [HttpGet("test")]
    public IActionResult Test()
    {
        return View();
    }

}

