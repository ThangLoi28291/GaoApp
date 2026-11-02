using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using ChatbotLocal.Integration;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Controllers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AssistantCall
{
    public Guid? RequestId { get; set; }
    public Guid? ResourceId { get; set; }
    public int? DocumentId { get; set; }
    public JsonElement? Body { get; set; }
    public int Limit { get; set; } = 100;
    public string? Cursor { get; set; }
    public bool IncludeClarification { get; set; }
    public bool IncludeProducts { get; set; }
    public bool IncludeFeedback { get; set; }
    public string? Screen { get; set; }
}
[Authorize, AutoValidateAntiforgeryToken, RequestSizeLimit(65536)]
[Route("admin/api/assistant")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssistantController(AssistantAccess access, AssistantJwt jwt, IOptionsMonitor<AssistantOptions> options,
    IWebHostEnvironment environment, ILogger<AssistantController> logger, AssistantCatalogService catalog, ResourceAdmission admission) : ControllerBase
{
    [HttpGet("context")]
    public async Task<IActionResult> Context([FromQuery] int? documentId, CancellationToken ct, [FromQuery] string? screen = null)
    {
        try
        {
            if (!options.CurrentValue.Enabled) throw AssistantException.Disabled();
            var who = await access.CurrentAsync(documentId, ct);
            using var lease = admission.Enter(ResourceGroup.AssistantRead, ResourceSubject.User(who.UserId), AssistantAccess.Id(who.Scope.StoreId));
            if (!lease.IsAcquired) return ResourceProtectionResponse.Result(HttpContext, lease);
            var storeName = await access.CurrentStoreNameAsync(who, ct);
            if (screen is not null)
            {
                var currentScreen = await access.ScreenAsync(who, screen, documentId, ct);
                return Ok(new { contractVersion = "1.0", identity = who.UserId.ToString(), displayName = who.DisplayName,
                    scope = who.Scope, storeName, permissions = who.Permissions, screen = currentScreen });
            }
            return Ok(new { contractVersion = "1.0", identity = who.UserId.ToString(), displayName = who.DisplayName,
                scope = who.Scope, storeName, permissions = who.Permissions });
        }
        catch (AssistantException ex) { return StatusCode(ex.Status, ex.Body(Guid.Empty)); }
    }
    // Read-only handoff data follows the receiving-screen permission, not barcode review permissions.
    [HttpGet("receiving/{documentId:int}/products/{variantId:int}/units")]
    public async Task<IActionResult> ReceivingUnits(int documentId, int variantId, [FromQuery] string? documentVersion, CancellationToken ct)
    {
        try
        {
            if (!options.CurrentValue.Enabled || !options.CurrentValue.ReceivingEnabled) throw AssistantException.Disabled();
            var who = await access.CurrentAsync(documentId, ct);
            var before = await access.ScreenAsync(who, "receiving", documentId, ct);
            using var lease = admission.Enter(ResourceGroup.AssistantRead, ResourceSubject.User(who.UserId), AssistantAccess.Id(who.Scope.StoreId));
            if (!lease.IsAcquired) return ResourceProtectionResponse.Result(HttpContext, lease);
            if (!before.CanApply || documentVersion is null || before.DocumentVersion != documentVersion)
                throw new AssistantException(409, "receiving_document_changed", "Phiếu nhập đã thay đổi. Mình tải lại phiếu rồi tạo đề xuất mới nhé.");
            var variant = await catalog.FindAsync(who, variantId.ToString(), ct);
            var retail = who.Permissions.Contains(AssistantPermissions.Retail);
            var source = AssistantCatalogService.Build(variant, retail);
            decimal Number(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            var items = source.Units.Where(u => u.Unit.CanReceive).Select(u => new {
                productId = variant.ProductId, productVariantId = variant.Id, productName = source.Product.Name, sku = variant.Sku,
                unitId = AssistantAccess.Id(u.Unit.UnitId), productUnitConversionId = u.Unit.ConversionId is { } id ? (int?)AssistantAccess.Id(id) : null,
                unitName = u.Unit.Name, baseUnitName = source.BaseUnit.Name, factor = Number(u.Unit.Factor), isBaseUnit = u.Unit.IsBaseUnit,
                price = retail && u.Retail.Status == "available" && u.Retail.Amount is { } amount ? (decimal?)Number(amount) : null,
                priceStatus = retail ? u.Retail.Status : "forbidden", imageUrl = (string?)null
            }).ToArray();
            var current = await access.CurrentAsync(documentId, ct);
            var after = await access.ScreenAsync(current, "receiving", documentId, ct);
            if (who.UserId != current.UserId || who.Scope != current.Scope) throw AssistantException.Denied();
            if (retail) AssistantAccess.Require(current.Permissions, AssistantPermissions.Retail);
            if (!after.CanApply || before.Key != after.Key)
                throw new AssistantException(409, "receiving_document_changed", "Phiếu nhập vừa thay đổi. Mình kiểm tra lại phiếu nhé.");
            return Ok(new { documentId, documentVersion = after.DocumentVersion, items });
        }
        catch (AssistantException ex) { return StatusCode(ex.Status, ex.Body(Guid.Empty)); }
    }
    [HttpPost("call/{operation}")]
    public async Task<IActionResult> Call(string operation, [FromBody] JsonElement input, CancellationToken ct)
    {
        var id = Guid.Empty;
        try
        {
            if (!ModelState.IsValid) throw AssistantException.Invalid();
            AssistantJwt.Unique(input);
            AssistantCall call;
            try { call = input.Deserialize<AssistantCall>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw AssistantException.Invalid(); }
            catch (JsonException) { throw AssistantException.Invalid(); }
            id = call.RequestId ?? Guid.Empty;
            if (!Enum.TryParse<AssistantOperation>(operation, false, out var action) || !Enum.IsDefined(action)) throw AssistantException.Invalid();
            if(action is AssistantOperation.Monitor or AssistantOperation.ResolveFeedback)throw AssistantException.Invalid();
            if ((call.IncludeClarification || call.IncludeProducts || call.IncludeFeedback) && action != AssistantOperation.History) throw AssistantException.Invalid();
            if (call.Screen is not null && (call.Screen is not ("receiving" or "product_create")
                || action is not (AssistantOperation.Capabilities or AssistantOperation.RequestStatus or AssistantOperation.CancelRequest)
                    && action != (call.Screen == "receiving" ? AssistantOperation.Receiving : AssistantOperation.NormalizeName))) throw AssistantException.Invalid();
            var config = options.CurrentValue;
            if (!config.Enabled) throw AssistantException.Disabled();
            if (action == AssistantOperation.Receiving && !config.ReceivingEnabled || action == AssistantOperation.NormalizeName && !config.NormalizeEnabled)
                throw new AssistantException(503, "feature_disabled", "Chức năng hỗ trợ này đang tạm tắt.");
            if (action == AssistantOperation.Receiving && call.DocumentId is null) throw AssistantException.Invalid();
            var who = await access.CurrentAsync(call.DocumentId, ct);
            var required = action switch {
                AssistantOperation.Receiving => AssistantPermissions.Receiving,
                AssistantOperation.NormalizeName => AssistantPermissions.NormalizeName,
                AssistantOperation.Quote => AssistantPermissions.Retail,
                AssistantOperation.Capabilities or AssistantOperation.RequestStatus or AssistantOperation.CancelRequest => AssistantPermissions.Use,
                _ => AssistantPermissions.Chat };
            AssistantAccess.Require(who.Permissions, required);
            using var lease = admission.Enter(ResourceEndpointPolicy.AssistantOperationGroup(action), ResourceSubject.User(who.UserId), AssistantAccess.Id(who.Scope.StoreId));
            if (!lease.IsAcquired) return ResourceProtectionResponse.Result(HttpContext, lease, id);
            AssistantScreenContext? screenBefore = null;
            if (call.Screen is not null || action == AssistantOperation.Receiving)
            {
                screenBefore = await access.ScreenAsync(who, call.Screen ?? "receiving", call.DocumentId, ct);
                if (action == AssistantOperation.Receiving && !screenBefore.CanApply)
                    throw new AssistantException(409, "receiving_document_locked", screenBefore.Message!);
            }
            if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var address)
                || config.TimeoutSeconds is < 1 or > 120) throw new AssistantException(503, "assistant_not_configured", "Chưa cấu hình địa chỉ trợ lý.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
            using var client = new ChatbotLocalClient(address, environment.IsDevelopment());
            var reply = await client.CallAsync(action, jwt.Sign(who), call.RequestId, call.Body, call.ResourceId,
                call.Limit, call.Cursor, timeout.Token, call.IncludeClarification, call.IncludeProducts, call.IncludeFeedback);
            var current = await access.CurrentAsync(call.DocumentId, ct);
            if (current.Scope != who.Scope || current.UserId != who.UserId) throw AssistantException.Denied();
            AssistantAccess.Require(current.Permissions, required);
            if (screenBefore is not null)
            {
                var screenAfter = await access.ScreenAsync(current, screenBefore.Kind, call.DocumentId, ct);
                if (action == AssistantOperation.Receiving && (!screenAfter.CanApply || screenBefore.Key != screenAfter.Key))
                    throw new AssistantException(409, "receiving_document_changed", "Phiếu nhập vừa thay đổi. Mình kiểm tra phiếu rồi gửi lại đề xuất nhé.");
            }
            AssistantJwt.Unique(reply.Body);
            if (reply.Body.ValueKind != JsonValueKind.Object || !reply.Body.TryGetProperty("contractVersion", out var version) || version.GetString() != "1.0")
                throw new AssistantException(502, "invalid_assistant_response", "Phản hồi trợ lý chưa đúng hợp đồng.");
            if (reply.IsSuccess && (reply.Body.TryGetProperty("scope", out var scope)
                ? scope.Deserialize<AssistantScope>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) != who.Scope
                : action is AssistantOperation.Receiving or AssistantOperation.NormalizeName or AssistantOperation.Chat or AssistantOperation.RequestStatus))
                throw new AssistantException(502, "source_scope_mismatch", "Phản hồi trợ lý không thuộc cửa hàng hiện tại.");
            if (reply.IsSuccess && call.RequestId is { } requestId && action is not (AssistantOperation.CancelRequest)
                && (!reply.Body.TryGetProperty("requestId", out var request) || request.GetGuid() != requestId))
                throw new AssistantException(502, "invalid_assistant_response", "Phản hồi trợ lý không khớp lượt yêu cầu.");
            if (reply.IsSuccess && call.IncludeProducts && reply.Body.TryGetProperty("productDisplay", out _))
            {
                var enriched = await catalog.DisplayAsync(current, reply.Body, timeout.Token);
                var after = await access.CurrentAsync(call.DocumentId, ct);
                if (after.Scope != who.Scope || after.UserId != who.UserId) throw AssistantException.Denied();
                AssistantAccess.Require(after.Permissions, required);
                AssistantAccess.Require(after.Permissions, enriched.RequiredPermissions);
                if (enriched.Display is not null)
                    enriched.Display["canReadWholesale"] = after.Permissions.Contains(AssistantPermissions.Retail) && after.Permissions.Contains(AssistantPermissions.Wholesale);
                var response = JsonNode.Parse(reply.Body.GetRawText())!.AsObject();
                response["productDisplay"] = enriched.Display;
                return StatusCode(reply.HttpStatus, response);
            }
            return StatusCode(reply.HttpStatus, reply.Body);
        }
        catch (AssistantException ex) { return StatusCode(ex.Status, ex.Body(id)); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return StatusCode(504, new AssistantException(504, "assistant_timeout", "Trợ lý đang phản hồi chậm. Bạn có thể kiểm tra lại lượt này hoặc thao tác thủ công.").Body(id)); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or ArgumentException or JsonException or InvalidOperationException or FormatException)
        {
            logger.LogWarning("Assistant integration failed: {ExceptionType}; RequestId {RequestId}", ex.GetType().Name, id);
            return StatusCode(503, new AssistantException(503, "assistant_unavailable", "Chưa kết nối được trợ lý. Bạn vẫn có thể thao tác thủ công.").Body(id));
        }
    }
}
