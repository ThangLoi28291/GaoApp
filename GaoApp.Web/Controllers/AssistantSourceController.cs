using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Controllers;

[ApiController, AllowAnonymous, IgnoreAntiforgeryToken, RequestSizeLimit(65536)]
[Route("api/v1/assistant")]
[ServiceFilter(typeof(AssistantSourceFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssistantSourceController(AssistantCatalogService catalog, TimeProvider clock) : ControllerBase
{
    private AssistantIdentity Identity => (AssistantIdentity)HttpContext.Items["AssistantIdentity"]!;
    private Guid RequestId => (Guid)HttpContext.Items["AssistantRequestId"]!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [HttpPost("context/authorize")]
    public IActionResult Authorize([FromBody] JsonElement body)
    {
        var input = body.Deserialize<AuthorizationRequest>(Json) ?? throw AssistantException.Invalid();
        if (input.RequiredPermissions is null || input.RequiredPermissions.Length is < 1 or > 30
            || input.RequiredPermissions.Any(p => !AssistantPermissions.Known.Contains(p))
            || input.RequiredPermissions.Distinct().Count() != input.RequiredPermissions.Length) throw AssistantException.Invalid();
        var permissionVersion = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', Identity.Permissions.Order()))));
        return Ok(new { contractVersion = "1.0", requestId = RequestId, allowed = input.RequiredPermissions.All(Identity.Permissions.Contains),
            scope = Identity.Scope, permissions = Identity.Permissions, permissionVersion, verifiedAt = clock.GetUtcNow() });
    }
    [HttpPost("catalog/aliases/resolve")]
    public async Task<IActionResult> Alias([FromBody] JsonElement body, CancellationToken ct)
    {
        var input = body.Deserialize<CatalogAliasQuery>(Json) ?? throw AssistantException.Invalid();
        var query = await catalog.ApprovedAliasAsync(Identity, input.Query, ct);
        return Ok(new { contractVersion = "1.0", requestId = RequestId, scope = Identity.Scope, matched = query is not null, query });
    }
    [HttpPost("catalog/search")]
    public async Task<IActionResult> Search([FromBody] JsonElement body, CancellationToken ct)
    {
        var input = body.Deserialize<CatalogSearch>(Json) ?? throw AssistantException.Invalid();
        var result = await catalog.SearchAsync(Identity, input, ct);
        return Ok(new { contractVersion = "1.0", requestId = RequestId, scope = Identity.Scope, source = catalog.Source(),
            variants = result.Variants, nextCursor = result.Cursor, search = result.Search });
    }
    [HttpGet("catalog/variants/{variantId}")]
    public async Task<IActionResult> Variant(string variantId, CancellationToken ct)
    {
        var variant = await catalog.FindAsync(Identity, variantId, ct);
        return Ok(new { contractVersion = "1.0", requestId = RequestId, scope = Identity.Scope, source = catalog.Source(),
            variant = AssistantCatalogService.Build(variant, Identity.Permissions.Contains(AssistantPermissions.Retail)) });
    }
    [HttpPost("quotes/lookup")]
    public async Task<IActionResult> Quote([FromBody] JsonElement body, CancellationToken ct)
    {
        var input = body.Deserialize<QuoteLookup>(Json) ?? throw AssistantException.Invalid();
        var quotes = await catalog.QuoteAsync(Identity, input, ct);
        return Ok(new { contractVersion = "1.0", requestId = RequestId, scope = Identity.Scope, source = catalog.Source(), quotes });
    }
}
