using System.Text.Json;
using System.Text.Json.Serialization;
using ChatbotLocal.Integration;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Security;
using GaoApp.Web.Services.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Controllers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposeSearchCorrection(Guid RequestId, Guid ConversationId, string MessageId, string Query, int ProductId, int VariantId);

[Authorize, AutoValidateAntiforgeryToken, Route("admin/api/assistant/search"), RequestSizeLimit(8192)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssistantSearchController(AssistantAccess access, AssistantCatalogService catalog, AssistantSearchKnowledge knowledge,
    AssistantJwt jwt, IOptionsMonitor<AssistantOptions> options, IWebHostEnvironment environment, ResourceAdmission admission, TimeProvider clock) : ControllerBase
{
    [HttpGet("products")]
    public Task<IActionResult> Products(string query, CancellationToken ct) => Execute(async () => {
        var who = await Current(ct);
        using var lease = admission.Enter(ResourceGroup.AssistantRead, ResourceSubject.User(who.UserId), AssistantAccess.Id(who.Scope.StoreId));
        if (!lease.IsAcquired) return ResourceProtectionResponse.Result(HttpContext, lease);
        var found = await catalog.SearchAsync(who, new CatalogSearch { Query = query, Limit = 20 }, ct);
        await Recheck(who, ct);
        return Ok(new { contractVersion = "1.0", scope = who.Scope,
            items = found.Variants.Select(v => v.Product), hasMore = found.Cursor is not null, search = found.Search });
    });
    [HttpPost("corrections")]
    public Task<IActionResult> Propose([FromBody] JsonElement raw, CancellationToken ct) => Execute(async () => {
        AssistantJwt.Unique(raw);
        var input = raw.Deserialize<ProposeSearchCorrection>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw AssistantException.Invalid();
        if (input.RequestId == Guid.Empty || input.ConversationId == Guid.Empty || !long.TryParse(input.MessageId, out var message) || message <= 0
            || !AssistantSearchKnowledge.ValidQuery(input.Query) || input.ProductId <= 0 || input.VariantId <= 0) throw AssistantException.Invalid();
        var who = await Current(ct);
        using var lease = admission.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(who.UserId), AssistantAccess.Id(who.Scope.StoreId));
        if (!lease.IsAcquired) return ResourceProtectionResponse.Result(HttpContext, lease);
        await VerifyMessage(who, input.ConversationId, input.MessageId, ct);
        var variant = await catalog.FindAsync(who, input.VariantId.ToString(), ct);
        if (variant.ProductId != input.ProductId) throw AssistantException.Invalid();
        await Recheck(who, ct);
        var result = await knowledge.ProposeAsync(AssistantAccess.Id(who.Scope.StoreId), who.UserId, input.Query, input.ProductId, input.VariantId,
            input.RequestId, input.ConversationId, input.MessageId, ct);
        await Recheck(who, ct);
        return Ok(new { contractVersion = "1.0", requestId = input.RequestId, scope = who.Scope, correction = new { result.Id, result.Status },
            message = "Đã ghi sản phẩm đúng. Cách gọi đang chờ quản trị duyệt trước khi dùng để tìm kiếm." });
    });
    private async Task<AssistantIdentity> Current(CancellationToken ct) {
        if (!options.CurrentValue.Enabled) throw AssistantException.Disabled();
        var who = await access.CurrentAsync(null, ct); AssistantAccess.Require(who.Permissions, AssistantPermissions.Chat, AssistantPermissions.Catalog); return who;
    }
    private async Task Recheck(AssistantIdentity before, CancellationToken ct) {
        var after = await Current(ct); if (before.UserId != after.UserId || before.Scope != after.Scope) throw AssistantException.Denied();
    }
    private async Task VerifyMessage(AssistantIdentity who, Guid conversation, string message, CancellationToken ct)
    {
        var config = options.CurrentValue;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 1, 20)));
        using var client = new ChatbotLocalClient(new Uri(config.BaseUrl), environment.IsDevelopment());
        var reply = await client.CallAsync(AssistantOperation.History, jwt.Sign(who), resourceId: conversation, limit: 200, cancellationToken: timeout.Token);
        if (!reply.IsSuccess) throw new AssistantException(reply.HttpStatus is 401 or 403 ? 403 : 503, "correction_history_unavailable", "Chưa xác minh được lượt trả lời. Mình tải lại hội thoại trước khi sửa.");
        AssistantJwt.Unique(reply.Body);
        if (reply.Body.GetProperty("contractVersion").GetString() != "1.0" || reply.Body.GetProperty("conversationId").GetGuid() != conversation)
            throw AssistantException.Invalid();
        var items = reply.Body.GetProperty("items");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 200) throw AssistantException.Invalid();
        var matches = items.EnumerateArray().Where(m => m.GetProperty("messageId").GetString() == message && m.GetProperty("role").GetString() == "assistant").ToArray();
        if (matches.Length != 1 || matches[0].GetProperty("scope").Deserialize<AssistantScope>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) != who.Scope
            || matches[0].GetProperty("expiresAt").GetDateTimeOffset() <= clock.GetUtcNow())
            throw new AssistantException(404, "correction_message_not_found", "Lượt trả lời đã hết hạn hoặc không thuộc hội thoại hiện tại.");
    }
    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action) {
        try { return await action(); }
        catch (AssistantException ex) { return StatusCode(ex.Status, ex.Body(Guid.Empty)); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException) {
            return BadRequest(AssistantException.Invalid().Body(Guid.Empty));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException) {
            return StatusCode(503, new AssistantException(503, "search_unavailable", "Chưa kết nối được dữ liệu tìm kiếm. Mình thử lại sau nhé.").Body(Guid.Empty));
        }
    }
}