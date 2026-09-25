using GaoApp.Application.DTOs.StoreBankAccounts;
using GaoApp.Application.Interfaces.Services.StoreBankAccounts;
using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GaoApp.Web.Areas.Admin.ViewModels.StoreBankAccounts;
namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.System.BankAccount.View)]
public class StoreBankAccountsController : BaseAdminController
{
    private readonly IStoreBankAccountService _service;
    private readonly IStoreBankAccountIndexReadService _indexReadService;

    public StoreBankAccountsController(
        IStoreBankAccountService service,
        IStoreBankAccountIndexReadService indexReadService)
    {
        _service = service;
        _indexReadService = indexReadService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = await BuildIndexVmAsync(null, 1, 20, ct);
        return View(model);
    }

    [HttpGet("/admin/store-bank-accounts/data")]
    public async Task<IActionResult> GetStoreBankAccountIndexData(
        [FromQuery] StoreBankAccountIndexQueryRequest request,
        CancellationToken ct)
    {
        var result = await _indexReadService.GetPageAsync(
            CurrentStoreId,
            request,
            ct);

        return Json(result);
    }
    [HttpGet]
    public async Task<IActionResult> Search(
     string? search,
     int page = 1,
     int pageSize = 20,
     CancellationToken ct = default)
    {
        var model = await BuildIndexVmAsync(search, page, pageSize, ct);
        return PartialView("_StoreBankAccountTable", model);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.System.BankAccount.Manage)]
    public async Task<IActionResult> Edit(int? id, CancellationToken ct)
    {
        if (!id.HasValue)
        {
            return View(new StoreBankAccountUpsertDto
            {
                IsActive = true,
                ProviderCode = "LOCAL",
                NoteTemplate = "POS-{OrderId}-{QrRequestId}"
            });
        }

        var model = await _service.GetByIdAsync(id.Value, ct);
        if (model == null) return NotFound();

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.System.BankAccount.Manage)]
    public async Task<IActionResult> Edit(StoreBankAccountUpsertDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        if (dto.Id.HasValue)
            await _service.UpdateAsync(dto, ct);
        else
            await _service.CreateAsync(dto, ct);

        TempData["Success"] = "Đã lưu tài khoản ngân hàng.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.System.BankAccount.Manage)]
    public async Task<IActionResult> ToggleStatus(int id, CancellationToken ct)
    {
        await _service.ToggleStatusAsync(id, ct);
        return Json(new { success = true, message = "Đã cập nhật trạng thái ngân hàng." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.System.BankAccount.Manage)]
    public async Task<IActionResult> SetDefault(int id, CancellationToken ct)
    {
        await _service.SetDefaultAsync(id, ct);
        return Json(new { success = true, message = "Đã đặt ngân hàng mặc định." });
    }
    private async Task<StoreBankAccountIndexVM> BuildIndexVmAsync(
    string? search,
    int page,
    int pageSize,
    CancellationToken ct)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 20 : pageSize;

        var all = await _service.GetListAsync(ct);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim().ToLower();

            all = all
                .Where(x =>
                    (x.BankCode ?? "").ToLower().Contains(keyword) ||
                    (x.BankName ?? "").ToLower().Contains(keyword) ||
                    (x.AccountNumber ?? "").ToLower().Contains(keyword) ||
                    (x.AccountName ?? "").ToLower().Contains(keyword) ||
                    (x.ProviderCode ?? "").ToLower().Contains(keyword))
                .ToList();
        }

        var total = all.Count;

        var items = all
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new StoreBankAccountIndexVM
        {
            SearchString = search,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            Items = items
        };
    }
}
