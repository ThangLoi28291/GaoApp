using GaoApp.Application.Common.Security;
using GaoApp.Web.Security;
using GaoApp.Web.Services.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public sealed record SearchAuditPage(string StoreName, SearchDocument[] Documents, SearchCorrection[] Corrections, string Query, CatalogRankedPage? Results);

[Area("Admin"), Authorize(Policy = PermissionCodes.System.Setting.Update), AutoValidateAntiforgeryToken, Route("admin/assistant/search")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssistantSearchAdminController(AssistantAccess access, AssistantCatalogService catalog, AssistantSearchKnowledge knowledge, ResourceAdmission admission) : Controller
{
    private async Task<AssistantIdentity> Current(CancellationToken ct) {
        var who = await access.CurrentAsync(null, ct); AssistantAccess.Require(who.Permissions, AssistantPermissions.Catalog);
        var monitor = await access.MonitorAsync(ct); if (who.UserId != monitor.UserId || who.Scope != monitor.Scope) throw AssistantException.Denied(); return who;
    }
    private async Task Recheck(AssistantIdentity who, CancellationToken ct) {
        var current = await Current(ct); if (who.UserId != current.UserId || who.Scope != current.Scope) throw AssistantException.Denied();
    }
    [HttpGet("")]
    public async Task<IActionResult> Index(string? query = null, CancellationToken ct = default)
    {
        try {
            var who = await Current(ct);
            using var lease = admission.Enter(ResourceGroup.AssistantRead, ResourceSubject.User(who.UserId), AssistantAccess.Id(who.Scope.StoreId));
            if (!lease.IsAcquired) return ResourceProtectionResponse.Result(HttpContext, lease);
            if (query?.Length > 200 || query?.Any(char.IsControl) == true) return BadRequest();
            var documents = await catalog.SearchDocumentsAsync(who, ct);
            var corrections = await knowledge.ReadAsync(AssistantAccess.Id(who.Scope.StoreId), ct);
            var results = string.IsNullOrWhiteSpace(query) ? null : AssistantCatalogSearch.Rank(documents, new() { Query = query, Limit = 20 }, who.Scope.StoreId);
            var name = await access.CurrentStoreNameAsync(who, ct); await Recheck(who, ct);
            return View("~/Areas/Admin/Views/AssistantSearch/Index.cshtml", new SearchAuditPage(name, documents, corrections.Entries.OrderByDescending(e => e.UpdatedAt).ToArray(), query ?? "", results));
        } catch (AssistantException ex) { return StatusCode(ex.Status, ex.Body(Guid.Empty)); }
    }
    [HttpGet("catalog.json")]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        try {
            var who = await Current(ct);
            using var lease = admission.Enter(ResourceGroup.AssistantRead, ResourceSubject.User(who.UserId), AssistantAccess.Id(who.Scope.StoreId));
            if (!lease.IsAcquired) return ResourceProtectionResponse.Result(HttpContext, lease);
            var documents = await catalog.SearchDocumentsAsync(who, ct); await Recheck(who, ct);
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, sourceKind = "gaoapp_export", storeId = who.Scope.StoreId,
                documents = documents.Select(d => new { d.Id, d.ProductId, d.Name, d.Sku, d.Attributes, d.Aliases }) }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true });
            return File(bytes, "application/json", $"gaoapp-search-catalog-{who.Scope.StoreId}.json");
        } catch (AssistantException ex) { return StatusCode(ex.Status, ex.Body(Guid.Empty)); }
    }
    [HttpGet("approved-cases.json")]
    public async Task<IActionResult> ApprovedCases(CancellationToken ct)
    {
        try {
            var who = await Current(ct);
            using var lease = admission.Enter(ResourceGroup.AssistantRead, ResourceSubject.User(who.UserId), AssistantAccess.Id(who.Scope.StoreId));
            if (!lease.IsAcquired) return ResourceProtectionResponse.Result(HttpContext, lease);
            var documents = await catalog.SearchDocumentsAsync(who, ct);
            var active = documents.ToDictionary(d => d.Id, d => d.ProductId);
            var knowledgeRows = await knowledge.ReadAsync(AssistantAccess.Id(who.Scope.StoreId), ct);
            var groups = knowledgeRows.Entries.Where(e => e.Status == "approved" && active.GetValueOrDefault(e.VariantId) == e.ProductId)
                .GroupBy(e => AssistantCatalogSearch.Key(e.Query)).OrderBy(g => g.Key).ToArray();
            await Recheck(who, ct);
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, sourceKind = "manual_verified", storeId = who.Scope.StoreId,
                purpose = "Approved teaching examples, tune split only; independent holdout questions must be supplied separately.",
                cases = groups.Select((g, i) => new { id = "approved-" + (i + 1), query = g.First().Query,
                    kind = g.Select(e => e.VariantId).Distinct().Count() > 1 ? "ambiguous" : "confirm",
                    expectedVariantIds = g.Select(e => e.VariantId).Distinct().Order().ToArray(), split = "tune" }) },
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true });
            return File(bytes, "application/json", $"gaoapp-search-approved-cases-{who.Scope.StoreId}.json");
        } catch (AssistantException ex) { return StatusCode(ex.Status, ex.Body(Guid.Empty)); }
    }
    [HttpPost("propose")]
    public async Task<IActionResult> Propose(string query, int variantId, Guid requestId, CancellationToken ct)
    {
        try {
            var who = await Current(ct);
            using var lease = admission.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(who.UserId), AssistantAccess.Id(who.Scope.StoreId));
            if (!lease.IsAcquired) return ResourceProtectionResponse.Result(HttpContext, lease);
            if (!AssistantSearchKnowledge.ValidQuery(query) || variantId <= 0 || requestId == Guid.Empty) return BadRequest();
            var variant = await catalog.FindAsync(who, variantId.ToString(), ct); await Recheck(who, ct);
            await knowledge.ProposeAsync(AssistantAccess.Id(who.Scope.StoreId), who.UserId, query, variant.ProductId, variant.Id, requestId, null, null, ct);
            TempData["SearchResult"] = "Đã thêm đề xuất. Kiểm tra mã hàng và duyệt ở danh sách bên dưới.";
        } catch (AssistantException ex) { TempData["SearchResult"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
    [HttpPost("review")]
    public async Task<IActionResult> Review(Guid id, string version, string decision, Guid requestId, CancellationToken ct)
    {
        try {
            var who = await Current(ct);
            using var lease = admission.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(who.UserId), AssistantAccess.Id(who.Scope.StoreId));
            if (!lease.IsAcquired) return ResourceProtectionResponse.Result(HttpContext, lease);
            var store = AssistantAccess.Id(who.Scope.StoreId);
            var row = (await knowledge.ReadAsync(store, ct)).Entries.SingleOrDefault(e => e.Id == id)
                ?? throw new AssistantException(404, "correction_not_found", "Không tìm thấy đề xuất.");
            CatalogAttributes? attributes = null;
            if (decision == "approve") {
                var variant = await catalog.FindAsync(who, row.VariantId.ToString(), ct);
                if (variant.ProductId != row.ProductId) throw AssistantException.Invalid();
                attributes = AssistantCatalogService.Build(variant, false).Attributes;
            }
            await Recheck(who, ct);
            await knowledge.ReviewAsync(store, who.UserId, id, version, decision, requestId, attributes, ct);
            TempData["SearchResult"] = decision == "approve" ? "Đã duyệt cách gọi cho cửa hàng hiện tại." : "Đã từ chối hoặc thu hồi cách gọi.";
        } catch (AssistantException ex) { TempData["SearchResult"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}