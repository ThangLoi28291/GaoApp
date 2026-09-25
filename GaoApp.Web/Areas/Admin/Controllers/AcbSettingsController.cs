using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Configuration;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Authorize(Policy = PermissionCodes.System.Integration.Manage)]
[Route("admin/acb/settings")]
public sealed class AcbSettingsController(AppDbContext db, AcbProtocol protocol,
    AcbCallbackRoutingService? callbackRouting = null) : BaseAdminController
{
    private async Task PopulateAsync(CancellationToken ct)
    {
        var bankAccounts = await db.StoreBankAccounts.Where(x => x.StoreId == CurrentStoreId && x.IsActive && x.BankCode.ToUpper() == "ACB")
            .Select(x => new { x.Id, Label = x.BankName + " — " + x.AccountNumber }).ToListAsync(ct);
        ViewBag.BankAccounts = new SelectList(bankAccounts, "Id", "Label");
        ViewBag.HasAcbBankAccounts = bankAccounts.Count > 0;
        ViewBag.TestTerminals = new SelectList(await db.POSTerminals.Where(x => x.StoreId == CurrentStoreId && x.IsActive)
            .Select(x => new { x.Id, x.Name }).ToListAsync(ct), "Id", "Name");
        var receipts = db.Set<AcbCallbackReceipt>().Where(x => x.StoreId == CurrentStoreId);
        ViewBag.CallbackLastReceivedAt = await receipts.MaxAsync(x => (DateTime?)x.CreatedAtUtc, ct);
        ViewBag.CallbackPendingCount = await receipts.CountAsync(x => x.ProcessedAtUtc == null, ct);
        ViewBag.CallbackReviewCount = await receipts.CountAsync(x => x.NeedsReview, ct);
        ViewBag.CallbackReceipts = await receipts.AsNoTracking().OrderByDescending(x => x.Id).Take(20)
            .Select(x => new AcbCallbackReceiptSummary(x.Id, x.Page, x.Attempts, x.CreatedAtUtc,
                x.ProcessedAtUtc, x.NextAttemptAtUtc, x.NeedsReview, x.LastErrorCode, x.RequestCode, x.TotalPages)).ToListAsync(ct);
        var host = Request.Host.Host;
        ViewBag.CallbackIsLocal = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            (System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address));
        ViewBag.CanTestConnection = await db.Set<StoreAcbSettings>().AnyAsync(x => x.StoreId == CurrentStoreId &&
            x.ClientId != "" && x.ClientSecretProtected != "", ct);
        var savedEndpoints = await db.Set<StoreAcbSettings>().Where(x => x.StoreId == CurrentStoreId)
            .Select(x => new { x.TokenEndpoint, x.ApiBaseUrl, x.QrEndpoint }).SingleOrDefaultAsync(ct);
        ViewBag.AcbEnvironment = "Chưa cấu hình";
        if (savedEndpoints != null)
        {
            try { ViewBag.AcbEnvironment = AcbProtocol.ValidateEnvironment(savedEndpoints.TokenEndpoint, savedEndpoints.ApiBaseUrl, savedEndpoints.QrEndpoint); }
            catch (InvalidOperationException) { ViewBag.AcbEnvironment = "Endpoint chưa đồng bộ"; }
        }
        ViewBag.CallbackUrl = $"{Request.Scheme}://{Request.Host}/Admin/api-callback";
        ViewBag.CallbackAliasUrls = callbackRouting != null ? await callbackRouting.UrlsForStoreAsync(CurrentStoreId, ct) : Array.Empty<string>();
        ViewBag.CallbackStoreId = CurrentStoreId;
        ViewBag.CanManageCallbackRouting = callbackRouting != null && await callbackRouting.CanManageAsync(User, ct);
    }
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var entity = await db.Set<StoreAcbSettings>().SingleOrDefaultAsync(x => x.StoreId == CurrentStoreId, ct);
        var form = new AcbSettingsForm();
        if (entity != null)
        {
            form.Enabled = entity.Enabled; form.BankAccountId = entity.BankAccountId;
            foreach (var name in AcbSettingsForm.TextFields)
                typeof(AcbSettingsForm).GetProperty(name)!.SetValue(form, typeof(StoreAcbSettings).GetProperty(name)!.GetValue(entity));
            form.HasClientSecret = entity.ClientSecretProtected.Length > 0;
            form.HasCallbackApiKey = entity.CallbackApiKeyProtected.Length > 0;
        }
        await PopulateAsync(ct);
        return View(form);
    }
    [HttpPost("test-connection"), ValidateAntiForgeryToken]
    public async Task<IActionResult> TestConnection(CancellationToken ct)
    {
        var settings = await db.Set<StoreAcbSettings>().SingleOrDefaultAsync(x => x.StoreId == CurrentStoreId, ct);
        if (settings == null) ToastError("Hãy lưu cấu hình ACB của cửa hàng trước khi kiểm tra.");
        else
        {
            try
            {
                var result = await protocol.CheckConnectionAsync(settings, ct);
                ToastSuccess($"Kết nối xác thực ACB {result.Environment} thành công. Chưa kiểm tra quyền tạo QR hoặc khả năng nhận callback.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { ToastError("ACB chưa phản hồi trong thời gian cho phép. Hãy thử lại."); }
            catch (HttpRequestException) { ToastError("Chưa kết nối được ACB. Kiểm tra mạng, endpoint và điều kiện IP kết nối do ACB cấp."); }
            catch (System.Security.Cryptography.CryptographicException) { ToastError("Không giải mã được cấu hình ACB trên máy này. Hãy lưu lại Client secret tại cửa hàng hiện tại."); }
            catch (System.Text.Json.JsonException) { ToastError("ACB trả dữ liệu xác thực không đúng định dạng mong đợi."); }
            catch (InvalidOperationException ex) { ToastError(ex.Message); }
        }
        return RedirectToSettings();
    }

    [HttpPost("test-sandbox"), ValidateAntiForgeryToken]
    public Task<IActionResult> TestSandbox(int terminalId, [FromServices] AcbSandboxCheck check, CancellationToken ct)
        => TestQrAsync(terminalId, check, false, ct);

    [HttpPost("test-production"), ValidateAntiForgeryToken]
    public Task<IActionResult> TestProduction(int terminalId, [FromServices] AcbSandboxCheck check, CancellationToken ct)
        => TestQrAsync(terminalId, check, true, ct);

    private async Task<IActionResult> TestQrAsync(int terminalId, AcbSandboxCheck check, bool production, CancellationToken ct)
    {
        var settings = await db.Set<StoreAcbSettings>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == CurrentStoreId, ct);
        if (settings == null || !await db.POSTerminals.AnyAsync(x => x.StoreId == CurrentStoreId && x.Id == terminalId && x.IsActive, ct))
        {
            ToastError("Hãy lưu cấu hình ACB và chọn máy thuộc cửa hàng hiện tại.");
            return RedirectToSettings();
        }
        try { return View("SandboxResult", production ? await check.RunProductionAsync(settings, terminalId, ct) : await check.RunAsync(settings, terminalId, ct)); }
        catch (InvalidOperationException ex) { ToastError(ex.Message); return RedirectToSettings(); }
        catch (System.Security.Cryptography.CryptographicException) { ToastError("Không giải mã được cấu hình ACB. Hãy lưu lại Client secret trên máy này."); return RedirectToSettings(); }
        catch (IOException) { ToastError("Không ghi được nhật ký kiểm thử. Kiểm tra quyền ghi App_Data/Logs trước khi thử tiếp."); return RedirectToSettings(); }
        catch (UnauthorizedAccessException) { ToastError("Ứng dụng chưa có quyền ghi nhật ký kiểm thử vào App_Data/Logs."); return RedirectToSettings(); }
    }

    private IActionResult RedirectToSettings() => RedirectToAction(nameof(Index), new { tenant = Request.Query["tenant"].ToString() });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(AcbSettingsForm form, CancellationToken ct)
    {
        var entity = await db.Set<StoreAcbSettings>().SingleOrDefaultAsync(x => x.StoreId == CurrentStoreId, ct);
        try
        {
            if (!ModelState.IsValid) return await InvalidSettingsAsync(form, entity, ct);
            if (!form.BankAccountId.HasValue || !await db.StoreBankAccounts.AnyAsync(x => x.StoreId == CurrentStoreId &&
                x.Id == form.BankAccountId.Value && x.IsActive && x.BankCode.ToUpper() == "ACB", ct))
            {
                ModelState.AddModelError(nameof(form.BankAccountId), "Hãy chọn tài khoản ACB đang hoạt động thuộc cửa hàng hiện tại.");
                return await InvalidSettingsAsync(form, entity, ct);
            }
            _ = AcbProtocol.ValidateEnvironment(form.TokenEndpoint, form.ApiBaseUrl, form.QrEndpoint);
            if (entity != null && await db.Set<AcbQrSession>().AnyAsync(x => x.StoreId == CurrentStoreId &&
                x.Status != AcbSessionStatus.Completed && x.Status != AcbSessionStatus.Cancelled, ct))
                throw new InvalidOperationException("Cần xử lý các QR đang chờ hoặc cần kiểm tra trước khi đổi cấu hình ACB.");
            if (form.Enabled && (string.IsNullOrWhiteSpace(form.ClientId) || string.IsNullOrWhiteSpace(form.MerchantId) ||
                string.IsNullOrWhiteSpace(form.XProviderId) || string.IsNullOrWhiteSpace(form.XOwnerNumber) ||
                string.IsNullOrWhiteSpace(form.XService) || string.IsNullOrWhiteSpace(form.XOwnerType) ||
                string.IsNullOrWhiteSpace(form.VirtualAccountPrefix) || string.IsNullOrWhiteSpace(form.BeneficiaryName) ||
                (string.IsNullOrWhiteSpace(form.ClientSecret) && string.IsNullOrEmpty(entity?.ClientSecretProtected)) ||
                (string.IsNullOrWhiteSpace(form.CallbackApiKey) && string.IsNullOrEmpty(entity?.CallbackApiKeyProtected))))
                throw new InvalidOperationException("Cần điền đủ thông tin kết nối và khóa ACB trước khi bật xác nhận tự động.");
            if (entity == null) { entity = new StoreAcbSettings { StoreId = CurrentStoreId }; db.Add(entity); }
            foreach (var name in AcbSettingsForm.TextFields)
                typeof(StoreAcbSettings).GetProperty(name)!.SetValue(entity, ((string?)typeof(AcbSettingsForm).GetProperty(name)!.GetValue(form) ?? "").Trim());
            entity.Enabled = form.Enabled; entity.BankAccountId = form.BankAccountId.Value;
            if (!string.IsNullOrWhiteSpace(form.ClientSecret)) entity.ClientSecretProtected = protocol.Protect(CurrentStoreId, form.ClientSecret);
            if (!string.IsNullOrWhiteSpace(form.CallbackApiKey)) entity.CallbackApiKeyProtected = protocol.Protect(CurrentStoreId, form.CallbackApiKey);
            await db.SaveChangesAsync(ct);
            ToastSuccess("Đã lưu cấu hình ACB của cửa hàng.");
            return RedirectToSettings();
        }
        catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        return await InvalidSettingsAsync(form, entity, ct);
    }

    private async Task<IActionResult> InvalidSettingsAsync(AcbSettingsForm form, StoreAcbSettings? entity, CancellationToken ct)
    {
        // Never render submitted secrets back into HTML, even after validation fails.
        ModelState.Remove(nameof(form.ClientSecret)); ModelState.Remove(nameof(form.CallbackApiKey));
        form.ClientSecret = null; form.CallbackApiKey = null;
        form.HasClientSecret = !string.IsNullOrEmpty(entity?.ClientSecretProtected);
        form.HasCallbackApiKey = !string.IsNullOrEmpty(entity?.CallbackApiKeyProtected);
        await PopulateAsync(ct);
        return View(nameof(Index), form);
    }
}
