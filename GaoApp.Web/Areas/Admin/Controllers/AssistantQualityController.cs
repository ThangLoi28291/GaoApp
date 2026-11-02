using System.Text.Json;
using System.Text.Json.Serialization;
using ChatbotLocal.Integration;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Areas.Admin.Controllers;

public sealed record QualityRequests([property:JsonRequired] long Attempts,[property:JsonRequired] long Completed,[property:JsonRequired] long Accepted,[property:JsonRequired] long Failed,[property:JsonRequired] long Cancelled,[property:JsonRequired] long? P50Ms,[property:JsonRequired] long? P95Ms,[property:JsonRequired] int LatencySamples,[property:JsonRequired] bool SampleLimited);
public sealed record QualityAiGroup([property:JsonRequired] string Provider,[property:JsonRequired] string Model,[property:JsonRequired] string? Project,[property:JsonRequired] int? Slot,[property:JsonRequired] long Attempts,[property:JsonRequired] long Succeeded,[property:JsonRequired] long Failed,[property:JsonRequired] long Interrupted,[property:JsonRequired] long? KnownTokens,[property:JsonRequired] long UnknownTokens,[property:JsonRequired] string? CooldownKind,[property:JsonRequired] DateTimeOffset? CooldownUntil);
public sealed record QualityError([property:JsonRequired] string Code,[property:JsonRequired] long Count);
public sealed record QualityFeedback([property:JsonRequired] Guid Id,[property:JsonRequired] string Owner,[property:JsonRequired] Guid ConversationId,[property:JsonRequired] string MessageId,[property:JsonRequired] string Kind,[property:JsonRequired] string Status,[property:JsonRequired] string Version,[property:JsonRequired] DateTimeOffset UpdatedAt);
public sealed record QualityReport([property:JsonRequired] DateTimeOffset GeneratedAt,[property:JsonRequired] DateTimeOffset From,[property:JsonRequired] int RetentionDays,[property:JsonRequired] DateTimeOffset ScopedSince,[property:JsonRequired] QualityRequests Requests,[property:JsonRequired] QualityAiGroup[] Ai,[property:JsonRequired] QualityError[] Errors,[property:JsonRequired] long OpenFeedback,[property:JsonRequired] long ResolvedFeedback,[property:JsonRequired] QualityFeedback[] Feedback);
public sealed record AssistantQualityPage(string StoreName,int Days,AssistantStoreSwitches? Switches,QualityReport? Report,string? Error,string? Code);

[Area("Admin"),Route("admin/assistant/quality"),Authorize(Policy=PermissionCodes.System.Setting.Update),AutoValidateAntiforgeryToken]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class AssistantQualityController(AssistantAccess access,AssistantStoreSettings settings,AssistantJwt jwt,
    IOptionsMonitor<AssistantOptions> options,IWebHostEnvironment environment,ILogger<AssistantQualityController> logger):Controller
{
    private async Task<AssistantReply> Call(AssistantOperation op,AssistantIdentity who,CancellationToken ct,int days=1,Guid? requestId=null,Guid? resourceId=null,object? body=null)
    {
        var config=options.CurrentValue;if(!config.Enabled)throw AssistantException.Disabled();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds,1,20)));
        using var client=new ChatbotLocalClient(new Uri(config.BaseUrl),environment.IsDevelopment());
        var reply=await client.CallAsync(op,jwt.Sign(who),requestId,body,resourceId,cancellationToken:timeout.Token,reportDays:days);
        var current=await access.MonitorAsync(ct);if(current.UserId!=who.UserId||current.Scope!=who.Scope)throw AssistantException.Denied();
        if(!reply.IsSuccess)throw new AssistantException(reply.HttpStatus,"monitor_source_unavailable","Chưa đọc được báo cáo hoặc xác nhận thao tác. Tải lại trước khi thử lại.");
        AssistantJwt.Unique(reply.Body);
        if(reply.Body.GetProperty("contractVersion").GetString()!="1.0"||reply.Body.GetProperty("scope").Deserialize<AssistantScope>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!=who.Scope)
            throw new AssistantException(502,"source_scope_mismatch","Báo cáo không thuộc cửa hàng hiện tại.");
        if(requestId is { } id&&reply.Body.GetProperty("requestId").GetGuid()!=id)throw AssistantException.Invalid();return reply;
    }
    [HttpGet("")]
    public async Task<IActionResult> Index(int days=1,CancellationToken ct=default)
    {
        if(days is not (1 or 7))return BadRequest();
        var who=await access.MonitorAsync(ct);var name=await access.CurrentStoreNameAsync(who,ct);AssistantStoreSwitches? switches=null;
        QualityReport? report=null;string? error=null,code=null;
        try{
            switches=await settings.ReadAsync(AssistantAccess.Id(who.Scope.StoreId),ct);
            var reply=await Call(AssistantOperation.Monitor,who,ct,days);
            report=reply.Body.GetProperty("report").Deserialize<QualityReport>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if(report is null||report.Ai is null||report.Ai.Length>100||report.Errors is null||report.Errors.Length>20||report.Feedback is null||report.Feedback.Length>50||report.Requests is null
                ||report.RetentionDays is <1 or >30||report.GeneratedAt>DateTimeOffset.UtcNow.AddMinutes(1)||report.GeneratedAt<DateTimeOffset.UtcNow.AddMinutes(-5)
                ||report.Feedback.Any(f=>f.Kind is not ("wrong_product" or "wrong_price" or "unclear")||f.Status is not ("open" or "resolved")))throw AssistantException.Invalid();
        }catch(AssistantException ex){report=null;error=ex.Message;code=ex.Code;}
        catch(Exception ex)when(ex is HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or OperationCanceledException&& !ct.IsCancellationRequested){
            report=null;error="Chưa kết nối được ChatbotLocal hoặc xác minh quyền. Có thể tiếp tục thao tác thủ công và tải lại báo cáo.";code="monitor_unavailable";
            logger.LogWarning("Assistant quality unavailable: {Type}",ex.GetType().Name);
        }
        return View(new AssistantQualityPage(name,days,switches,report,error,code));
    }
    [HttpPost("resolve")]
    public async Task<IActionResult> Resolve(Guid id,string version,Guid requestId,int days=1,CancellationToken ct=default)
    {
        if(id==Guid.Empty||requestId==Guid.Empty||!Guid.TryParseExact(version,"N",out _)||days is not (1 or 7))return BadRequest();
        var who=await access.MonitorAsync(ct);
        try{var reply=await Call(AssistantOperation.ResolveFeedback,who,ct,days,requestId,id,new{version});
            TempData["AssistantQualityResult"]=reply.IsPending?"Đang xử lý. Tải lại danh sách để kiểm tra trước khi thử tiếp.":"Đã đánh dấu phản hồi được xử lý.";
        }catch(Exception ex)when(ex is AssistantException or HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or OperationCanceledException&& !ct.IsCancellationRequested){
            TempData["AssistantQualityResult"]="Chưa xác nhận được thao tác. Tải lại danh sách và kiểm tra trạng thái trước khi thử lại.";
            logger.LogWarning("Assistant quality review unavailable: {Type}",ex.GetType().Name);
        }
        return RedirectToAction(nameof(Index),new{days});
    }
}
