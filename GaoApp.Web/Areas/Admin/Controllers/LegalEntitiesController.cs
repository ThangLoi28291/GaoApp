using GaoApp.Application.Common.Results;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.LegalEntities;
using GaoApp.Application.Interfaces.Services.LegalEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/api/legal-entities")]
[ApiController]
[AutoValidateAntiforgeryToken]
[Authorize(Policy = PermissionCodes.System.LegalEntity.View)]
public sealed class LegalEntitiesController : ControllerBase
{
    private readonly ILegalEntityService _service;
    private readonly ILegalEntityCanaryService _canaryService;

    public LegalEntitiesController(
        ILegalEntityService service,
        ILegalEntityCanaryService canaryService)
    {
        _service = service;
        _canaryService = canaryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetManagement(CancellationToken ct)
    {
        var result = await _service.GetManagementAsync(ct);
        return ToActionResult(result);
    }

    [HttpGet("preflight")]
    [Authorize(Policy = PermissionCodes.System.LegalEntity.Activate)]
    public async Task<IActionResult> GetPreflight(CancellationToken ct)
    {
        var result = await _service.GetActivationPreflightAsync(ct);
        return ToActionResult(result);
    }

    [HttpPatch("feature-state")]
    [Authorize(Policy = PermissionCodes.System.LegalEntity.Activate)]
    public async Task<IActionResult> SetFeatureState(
        [FromBody] SetMultiLegalEntityEnabledRequest request,
        CancellationToken ct)
    {
        var result = await _canaryService.SetStateAsync(request, ct);
        if (!result.IsSuccess)
            return ToErrorResult(result.Error);

        return Ok(result.Value);
    }

    [HttpGet("canary")]
    [Authorize(Policy = PermissionCodes.System.LegalEntity.Activate)]
    public async Task<IActionResult> GetCanaryStatus(CancellationToken ct)
    {
        var result = await _canaryService.GetStatusAsync(ct);
        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.System.LegalEntity.Create)]
    public async Task<IActionResult> Create(
        [FromBody] CreateLegalEntityRequest request,
        CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ct);
        if (!result.IsSuccess)
            return ToErrorResult(result.Error);

        return Ok(new
        {
            id = result.Value,
            message = "Đã tạo HKD. Hãy tạo kho thuộc HKD rồi chọn làm kho bán mặc định."
        });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = PermissionCodes.System.LegalEntity.Update)]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateLegalEntityRequest request,
        CancellationToken ct)
    {
        if (id != request.Id)
        {
            return BadRequest(new
            {
                code = "LegalEntity.IdMismatch",
                message = "LegalEntityId trên URL không khớp dữ liệu gửi lên."
            });
        }

        var result = await _service.UpdateAsync(request, ct);
        if (!result.IsSuccess)
            return ToErrorResult(result.Error);

        return Ok(new { message = "Đã cập nhật HKD." });
    }

    [HttpPatch("{id:int}/active")]
    [Authorize(Policy = PermissionCodes.System.LegalEntity.Update)]
    public async Task<IActionResult> SetActive(
        int id,
        [FromBody] SetLegalEntityActiveRequest request,
        CancellationToken ct)
    {
        var result = await _service.SetActiveAsync(id, request.IsActive, ct);
        if (!result.IsSuccess)
            return ToErrorResult(result.Error);

        return Ok(new
        {
            message = request.IsActive ? "Đã mở lại HKD." : "Đã khóa HKD."
        });
    }

    private IActionResult ToActionResult<T>(Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : ToErrorResult(result.Error);

    private IActionResult ToErrorResult(Error error)
    {
        var body = new { code = error.Code, message = error.Message };
        return error.Code switch
        {
            "NotFound" => NotFound(body),
            "Conflict" => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
