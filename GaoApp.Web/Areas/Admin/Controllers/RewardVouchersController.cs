using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Rewards.Vouchers;
using GaoApp.Application.Interfaces.Services.Rewards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/reward-vouchers")]
[Authorize(Policy = PermissionCodes.Catalog.Customer.View)]
public sealed class RewardVouchersController : BaseAdminController
{
    private readonly IRewardVoucherIndexReadService _indexReadService;

    public RewardVouchersController(IRewardVoucherIndexReadService indexReadService)
    {
        _indexReadService = indexReadService;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet("data")]
    public async Task<IActionResult> GetRewardVoucherIndexData(
        [FromQuery] RewardVoucherIndexQueryRequest request,
        CancellationToken ct)
    {
        var result = await _indexReadService.GetPageAsync(CurrentStoreId, request, ct);
        return Json(result);
    }

    [HttpGet("{id:int}/quick-view")]
    public async Task<IActionResult> GetRewardVoucherQuickView(
        int id,
        CancellationToken ct)
    {
        var result = await _indexReadService.GetQuickViewAsync(CurrentStoreId, id, ct);
        return result is null ? NotFound() : Json(result);
    }
}
