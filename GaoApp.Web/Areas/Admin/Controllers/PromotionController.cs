using GaoApp.Web.Security;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Promotions;
using GaoApp.Application.Interfaces.Services.Promotions;
using GaoApp.Domain.Enums;
using GaoApp.Web.Areas.Admin.ViewModels.Promotions;
using GaoApp.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Authorize]
[Authorize(Policy = PermissionCodes.Catalog.Promotion.View)]
public sealed class PromotionController : BaseAdminController
{
    private readonly IPromotionAdminService _service;

    public PromotionController(
        IPromotionAdminService service)
    {
        _service = service;
    }


    private static void NormalizePagination(ref int page, ref int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        PromotionType? type = null,
        bool? isActive = null,
        string? customerPriceTier = null,
        string? search = "",
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var paged = await _service.GetPagedAsync(
            CurrentStoreId,
            type,
            isActive,
            customerPriceTier,
            search,
            fromDate,
            toDate,
            page,
            pageSize,
            ct);

        return View(new PromotionIndexVM
        {
            Type = type,
            IsActive = isActive,
            CustomerPriceTier = customerPriceTier,
            SearchString = search,
            FromDate = fromDate,
            ToDate = toDate,
            Page = page,
            PageSize = pageSize,
            Paged = paged
        });
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        PromotionType? type = null,
        bool? isActive = null,
        string? customerPriceTier = null,
        string? search = "",
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var paged = await _service.GetPagedAsync(
            CurrentStoreId,
            type,
            isActive,
            customerPriceTier,
            search,
            fromDate,
            toDate,
            page,
            pageSize,
            ct);

        return PartialView("_PromotionTable", new PromotionIndexVM
        {
            Type = type,
            IsActive = isActive,
            CustomerPriceTier = customerPriceTier,
            SearchString = search,
            FromDate = fromDate,
            ToDate = toDate,
            Page = page,
            PageSize = pageSize,
            Paged = paged
        });
    }

    [HttpGet]
    public async Task<IActionResult> Get(int id, CancellationToken ct = default)
    {
        var result = await _service.GetForEditAsync(CurrentStoreId, id, ct);

        return Json(new
        {
            success = result.IsSuccess,
            message = result.IsSuccess ? "" : result.Error.Message,
            data = result.IsSuccess ? result.Value : null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Promotion.Manage)]
    public async Task<IActionResult> Save([FromBody] SavePromotionRequest request, CancellationToken ct = default)
    {
        if (request == null)
        {
            return Json(new
            {
                success = false,
                message = "Dữ liệu gửi lên không hợp lệ.",
                id = 0
            });
        }

        if (request.Id > 0)
        {
            var updateResult = await _service.UpdateAsync(
                CurrentStoreId,
                request,
                userId: null,
                ct);

            return Json(new
            {
                success = updateResult.IsSuccess,
                message = updateResult.IsSuccess
                    ? "Đã cập nhật chương trình khuyến mãi."
                    : updateResult.Error.Message,
                id = request.Id
            });
        }

        var createResult = await _service.CreateAsync(
            CurrentStoreId,
            request,
            userId: null,
            ct);

        return Json(new
        {
            success = createResult.IsSuccess,
            message = createResult.IsSuccess
                ? "Đã tạo chương trình khuyến mãi."
                : createResult.Error.Message,
            id = createResult.IsSuccess ? createResult.Value : 0
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Promotion.Manage)]
    public async Task<IActionResult> ToggleStatus(int id, CancellationToken ct = default)
    {
        var result = await _service.ToggleStatusAsync(CurrentStoreId, id, userId: null, ct);

        return Json(new
        {
            success = result.IsSuccess,
            message = result.IsSuccess
                ? "Đã cập nhật trạng thái chương trình."
                : result.Error.Message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Promotion.Manage)]
    public async Task<IActionResult> DeleteAjax(int id, CancellationToken ct = default)
    {
        var result = await _service.SoftDeleteAsync(CurrentStoreId, id, userId: null, ct);

        return Json(new
        {
            success = result.IsSuccess,
            message = result.IsSuccess
                ? "Đã xóa chương trình khuyến mãi."
                : result.Error.Message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Promotion.Manage)]
    public async Task<IActionResult> Duplicate(int id, CancellationToken ct = default)
    {
        var result = await _service.DuplicateAsync(CurrentStoreId, id, userId: null, ct);

        return Json(new
        {
            success = result.IsSuccess,
            message = result.IsSuccess
                ? "Đã nhân bản chương trình. Bản sao đang ở trạng thái khóa."
                : result.Error.Message
        });
    }
    [HttpGet]
    public async Task<IActionResult> SearchProduct(
    string term = "",
    int take = 20,
    CancellationToken ct = default)
    {
        var data = await _service.SearchProductsForPromotionAsync(
            CurrentStoreId,
            term,
            take,
            ct);

        return Json(data.Select(x => new
        {
            id = x.VariantId,
            text = x.Text,
            productId = x.ProductId,
            variantId = x.VariantId,
            productName = x.ProductName,
            variantName = x.VariantName,
            sku = x.Sku,
            baseUnitName = x.BaseUnitName
        }));
    }

    [HttpGet]
    public async Task<IActionResult> GetProductUnits(
        int variantId,
        CancellationToken ct = default)
    {
        var data = await _service.GetUnitsForPromotionAsync(
            CurrentStoreId,
            variantId,
            ct);

        return Json(data);
    }
    [HttpGet]
    public async Task<IActionResult> GetProductForPromotion(
    int variantId,
    CancellationToken ct = default)
    {
        var data = await _service.GetProductForPromotionAsync(
            CurrentStoreId,
            variantId,
            ct);

        if (data == null)
        {
            return Json(new
            {
                success = false,
                message = "Không tìm thấy sản phẩm."
            });
        }

        return Json(new
        {
            success = true,
            data = data
        });
    }
}
