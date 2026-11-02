using System.Text.Json;
using System.Text.Json.Serialization;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Controllers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PolicyLookup([property: JsonRequired] string[] Topics);
[ApiController, AllowAnonymous, IgnoreAntiforgeryToken, RequestSizeLimit(4096)]
[Route("api/v1/assistant/knowledge"), ServiceFilter(typeof(AssistantSourceFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssistantKnowledgeSourceController(AssistantPolicyKnowledge knowledge, AssistantAccess access, TimeProvider clock) : ControllerBase
{
    [HttpPost("lookup")]
    public async Task<IActionResult> Lookup([FromBody] JsonElement body, CancellationToken ct) {
        var who = (AssistantIdentity)HttpContext.Items["AssistantIdentity"]!;
        var request = (Guid)HttpContext.Items["AssistantRequestId"]!;
        AssistantAccess.Require(who.Permissions, AssistantPermissions.Use, AssistantPermissions.Chat);
        var input = body.Deserialize<PolicyLookup>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw AssistantException.Invalid();
        if (input.Topics is null || input.Topics.Length is < 1 or > 6 || input.Topics.Distinct().Count() != input.Topics.Length || input.Topics.Any(t => !AssistantPolicyKnowledge.Topics.Contains(t))) throw AssistantException.Invalid();
        var doc = await knowledge.ReadAsync(AssistantAccess.Id(who.Scope.StoreId), ct);
        var current = await access.RecheckAsync(who, ct); AssistantAccess.Require(current.Permissions, AssistantPermissions.Use, AssistantPermissions.Chat);
        var now = clock.GetUtcNow();
        var articles = input.Topics.Select(t => AssistantPolicyKnowledge.Current(doc, t, now)).Where(r => r is not null)
            .Select(r => new { revisionId = r!.Id, r.Topic, r.Title, r.Body, r.EffectiveFrom, effectiveUntil = End(doc, r) }).ToArray();
        return Ok(new { contractVersion = "1.0", requestId = request, scope = who.Scope,
            source = new { kind = "gaoapp", label = "Chính sách cửa hàng đã duyệt", revision = doc.Version, verifiedAt = now, isTest = false }, articles });
    }
    private static DateTimeOffset? End(PolicyDocument doc, PolicyRevision row) {
        var next = doc.Revisions.Where(r => r.Topic == row.Topic && r.Status is "approved" or "withdrawn" && r.EffectiveFrom > row.EffectiveFrom).Select(r => (DateTimeOffset?)r.EffectiveFrom).Min();
        return row.EffectiveUntil is null ? next : next is null ? row.EffectiveUntil : row.EffectiveUntil < next ? row.EffectiveUntil : next;
    }
}