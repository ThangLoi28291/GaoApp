using GaoApp.Application.DTOs.Rewards;
using GaoApp.Application.Interfaces.Services.Rewards;
using GaoApp.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GaoApp.Application.Common.Security;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/reward-vouchers/settings")]
[Authorize(Policy = PermissionCodes.System.Setting.Update)]
public sealed class RewardSettingsController : Controller
{
    private readonly IRewardSettingsAdminService _service;

    public RewardSettingsController(
        IRewardSettingsAdminService service)
    {
        _service = service;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken ct)
    {
        var storeId = GetCurrentStoreId();

        if (storeId <= 0)
            return Forbid();

        var settings =
            await _service.GetAsync(
                storeId,
                ct);

        return View(settings);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        SaveRewardSettingsRequest request,
        CancellationToken ct)
    {
        var storeId = GetCurrentStoreId();

        if (storeId <= 0)
            return Forbid();

        if (!ModelState.IsValid)
            return View("Index", await _service.GetAsync(storeId, ct));

        var result =
            await _service.SaveAsync(
                storeId,
                request,
                ct);

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] =
                "Đã lưu cấu hình tích điểm.";

            return RedirectToAction(
                nameof(Index));
        }

        /*
         * Mapping Result/Error ở đây phải theo convention
         * Result<T> hiện có của GaoApp.
         *
         * Nếu source controller khác dùng:
         * result.Error / result.ValidationErrors / ModelState...
         * hãy giữ đúng pattern đó.
         */
        ModelState.AddModelError(
            string.Empty,
            result.Error.Message);

        var current =
            await _service.GetAsync(
                storeId,
                ct);

        return View(
            "Index",
            new RewardSettingsAdminDto
            {
                Categories = current.Categories.Select(category => new RewardCategoryOptionDto
                {
                    Id = category.Id,
                    Name = category.Name,
                    IsExcluded = request.UpdateCategoryExclusions
                        ? request.ExcludedCategoryIds.Contains(category.Id) : category.IsExcluded
                }).ToList(),
                CategorySelectionVersion = current.CategorySelectionVersion,
                Exists = current.Exists,
                Id = current.Id,

                MoneyPerPoint =
                    request.MoneyPerPoint,

                PointsPerVoucher =
                    request.PointsPerVoucher,

                VoucherValue =
                    request.VoucherValue,

                IsEnabled =
                    request.IsEnabled,

                Note =
                    request.Note,

                RowVersion =
                    current.RowVersion
            });
    }

    private int GetCurrentStoreId()
    {
        /*
         * THAY BODY NÀY bằng đúng store-resolution pattern
         * RewardVouchersController / controller hiện tại đang dùng.
         */

        var raw =
            User.FindFirst("store_id")?.Value;

        return int.TryParse(
            raw,
            out var storeId)
                ? storeId
                : 0;
    }
}
