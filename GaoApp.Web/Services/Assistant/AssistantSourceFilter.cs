using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using System.Text.Json;
using GaoApp.Web.Security;

namespace GaoApp.Web.Services.Assistant;

public sealed class AssistantSourceFilter(AssistantJwt jwt, AssistantAccess access, IOptionsMonitor<AssistantOptions> options, ILogger<AssistantSourceFilter> logger, ResourceAdmission admission)
    : IAsyncActionFilter, IAsyncResourceFilter, IOrderedFilter
{
    public int Order => -3000;
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var requestId = Guid.Empty;
        try
        {
            if (!options.CurrentValue.Enabled) throw AssistantException.Disabled();
            var request = context.HttpContext.Request;
            if (request.Headers["X-Request-Id"].Count != 1 || !Guid.TryParse(request.Headers["X-Request-Id"], out requestId) || requestId == Guid.Empty) throw AssistantException.Invalid();
            var header = request.Headers.Authorization;
            if (header.Count != 1 || !header[0]!.StartsWith("Bearer ", StringComparison.Ordinal))
                throw new AssistantException(401, "unauthenticated", "Thiếu thông tin kết nối được xác thực.");
            var identity = jwt.ValidateDelegation(header[0]![7..]);
            identity = await access.RecheckAsync(identity, request.HttpContext.RequestAborted);
            AssistantAccess.Require(identity.Permissions, GaoApp.Application.Common.Security.AssistantPermissions.Use);
            context.HttpContext.Items["AssistantIdentity"] = identity;
            context.HttpContext.Items["AssistantRequestId"] = requestId;
            using var lease = admission.Enter(ResourceGroup.AssistantSource, ResourceSubject.User(identity.UserId), AssistantAccess.Id(identity.Scope.StoreId));
            if (!lease.IsAcquired) { context.Result = ResourceProtectionResponse.Result(context.HttpContext, lease, requestId); return; }
            await next();
        }
        catch (AssistantException error) { context.Result = Result(error, requestId); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogWarning("Assistant source unavailable: {ExceptionType}; RequestId {RequestId}", ex.GetType().Name, requestId); context.Result = Result(Unavailable(), requestId); }
    }
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var requestId = context.HttpContext.Items["AssistantRequestId"] is Guid id ? id : Guid.Empty;
        try
        {
            if (!context.ModelState.IsValid) throw AssistantException.Invalid();
            foreach (var value in context.ActionArguments.Values.OfType<JsonElement>()) AssistantJwt.Unique(value);
            var executed = await next();
            if (executed.Exception is AssistantException error) { executed.ExceptionHandled = true; executed.Result = Result(error, requestId); }
            else if (executed.Exception is JsonException or FormatException) { executed.ExceptionHandled = true; executed.Result = Result(AssistantException.Invalid(), requestId); }
            else if (executed.Exception is { } failure && failure is not OperationCanceledException)
            { logger.LogWarning("Assistant source unavailable: {ExceptionType}; RequestId {RequestId}", failure.GetType().Name, requestId); executed.ExceptionHandled = true; executed.Result = Result(Unavailable(), requestId); }
        }
        catch (AssistantException error) { context.Result = Result(error, requestId); }
        catch (JsonException) { context.Result = Result(AssistantException.Invalid(), requestId); }
    }
    private static AssistantException Unavailable() => new(503, "source_unavailable", "Chưa đọc được dữ liệu GaoApp. Hãy thử lại sau.");
    private static ObjectResult Result(AssistantException error, Guid id) => new(error.Body(id)) { StatusCode = error.Status };
}
