using System.ComponentModel.DataAnnotations;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.CustomerMarketing;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Policy = CustomerServiceRights.View), Authorize(Policy = PermissionCodes.CustomerService.MarketingConsent)]
[AutoValidateAntiforgeryToken, Route("admin/customer-service/marketing-consent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerMarketingConsentController(CustomerServiceAccess access, AppDbContext db, CustomerMarketingConsentWriter consentWriter, CustomerChannelRegistry registry) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var who = await access.Staff(PermissionCodes.CustomerService.MarketingConsent, ct);
        var query = (q ?? "").Trim();
        var customers = db.Customers.AsNoTracking().Where(c => c.StoreId == who.StoreId && c.IsActive && c.Phone != null && c.Phone != "");
        if (query.Length > 0)
            customers = customers.Where(c => c.Name.Contains(query) || c.Phone!.Contains(query) || c.Code!.Contains(query));

        var rows = await customers
            .OrderBy(c => c.Name)
            .Take(100)
            .Select(c => new { c.Id, c.Name, Phone = c.Phone!, Group = c.CustomerGroup,
                Granted = db.CustomerMarketingConsents.Where(x => x.StoreId == who.StoreId && x.CustomerId == c.Id && x.Channel == "Zalo")
                    .Select(x => (bool?)x.IsGranted).FirstOrDefault(),
                ChangedAtUtc = db.CustomerMarketingConsents.Where(x => x.StoreId == who.StoreId && x.CustomerId == c.Id && x.Channel == "Zalo")
                    .Select(x => (DateTime?)x.ChangedAtUtc).FirstOrDefault() })
            .ToListAsync(ct);
        var configured = CurrentConnections(who.StoreId);
        var bindings = await db.InvoiceRecipientBindings.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == who.StoreId && x.RevokedAtUtc == null)
            .Select(x => new { x.CustomerId, x.ConnectionId, x.ConnectionBinding, x.SenderProfileId }).ToListAsync(ct);
        var items = rows.Select(c => new CustomerMarketingConsentRow(c.Id, c.Name, c.Phone, c.Group, c.Granted, c.ChangedAtUtc,
            bindings.Any(b => b.CustomerId == c.Id && configured.Any(k => k.Id == b.ConnectionId && k.Binding == b.ConnectionBinding && k.ProfileId == b.SenderProfileId)))).ToArray();
        var store = await access.Store(ct);
        return View("~/Areas/Admin/Views/CustomerMarketingConsent/Index.cshtml", new CustomerMarketingConsentPage(store.Name, query, items, TempData["Notice"] as string));
    }

    [HttpPost("{customerId:int}")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Save(int customerId, CustomerMarketingConsentForm form, CancellationToken ct)
    {
        var who = await access.Staff(PermissionCodes.CustomerService.MarketingConsent, ct);
        if (!ModelState.IsValid || form.Channel != "Zalo" || form.Source is not ("CustomerRequest" or "PaperForm" or "PhoneCall"))
            return BadRequest("Thông tin đồng ý nhận quảng cáo chưa hợp lệ.");
        if (form.IsGranted && (string.IsNullOrWhiteSpace(form.EvidenceReference) || !form.ConfirmedEvidence))
            return BadRequest("Khi bật nhận quảng cáo, cần nhập nguồn bằng chứng và xác nhận đã kiểm tra.");

        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId && c.StoreId == who.StoreId && c.IsActive && c.Phone != null && c.Phone != "", ct);
        if (customer is null) return NotFound("Không tìm thấy khách có số điện thoại trong cửa hàng hiện tại.");
        var configured = CurrentConnections(who.StoreId);
        var bindings = await db.InvoiceRecipientBindings.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == who.StoreId && x.CustomerId == customerId && x.RevokedAtUtc == null)
            .Select(x => new { x.ConnectionId, x.ConnectionBinding, x.SenderProfileId }).ToListAsync(ct);
        if (form.IsGranted && !bindings.Any(b => configured.Any(k => k.Id == b.ConnectionId && k.Binding == b.ConnectionBinding && k.ProfileId == b.SenderProfileId)))
            return Conflict("Cần liên kết Zalo hiện hành đã xác minh cho khách trước khi đăng ký nhận quảng cáo.");

        await consentWriter.SetAsync(who.StoreId, customerId, form.IsGranted, form.Source, Normalize(form.EvidenceReference), who.UserId, ct);
        await access.Recheck(who, PermissionCodes.CustomerService.MarketingConsent, ct);
        TempData["Notice"] = form.IsGranted
            ? $"Đã lưu đồng ý nhận quảng cáo Zalo cho {customer.Name}."
            : $"Đã lưu từ chối nhận quảng cáo Zalo cho {customer.Name}.";
        return RedirectToAction(nameof(Index), new { q = Request.Query["q"].ToString() });
    }

    private static string? Normalize(string? value)
    {
        var clean = new string((value ?? "").Where(c => !char.IsControl(c)).Take(250).ToArray()).Trim();
        return clean.Length == 0 ? null : clean;
    }

    private (string Id, string Binding, Guid ProfileId)[] CurrentConnections(int storeId) => registry.All()
        .Where(c => c.StoreId == storeId && c.Provider == "zalomarketing" && Guid.TryParse(c.AccountId, out _))
        .Select(c => (c.Id, c.Binding, Guid.Parse(c.AccountId))).ToArray();
}

public sealed record CustomerMarketingConsentRow(int CustomerId, string Name, string Phone, string Group, bool? Granted, DateTime? ChangedAtUtc, bool HasActiveBinding);
public sealed record CustomerMarketingConsentPage(string StoreName, string Query, IReadOnlyList<CustomerMarketingConsentRow> Items, string? Notice);
public sealed class CustomerMarketingConsentForm
{
    [Required, StringLength(20)] public string Channel { get; set; } = "Zalo";
    public bool IsGranted { get; set; }
    [Required, StringLength(40)] public string Source { get; set; } = "CustomerRequest";
    [StringLength(250)] public string? EvidenceReference { get; set; }
    public bool ConfirmedEvidence { get; set; }
}
