using System.Text.Json;
using GaoApp.Application.Common.Security;
using System.Text.Json.Serialization;
using GaoApp.Web.Security;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
namespace GaoApp.Web.Areas.Admin.Controllers;

public sealed record CustomerQualityPage(CustomerStaff Staff,string StoreName,int RetentionDays);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerQualityProposal([property:JsonRequired] Guid RequestId,[property:JsonRequired] string Query,[property:JsonRequired] int VariantId,[property:JsonRequired] bool Sanitized);
[Area("Admin"),Authorize(Policy=CustomerServiceRights.View),Authorize(Policy=CustomerServiceRights.Manage),AutoValidateAntiforgeryToken,
 Route("admin/customer-service/quality"),RequestSizeLimit(16384),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CustomerQualityController(CustomerServiceAccess access,CustomerServiceStore store,AssistantAccess assistant,
    AssistantCatalogService catalog,AssistantSearchKnowledge knowledge,ResourceAdmission admission,IOptionsMonitor<CustomerServiceOptions> options,TimeProvider clock):Controller
{
    private static readonly JsonSerializerOptions Wire=new(JsonSerializerDefaults.Web);
    private T Parse<T>(JsonElement body){if(!ModelState.IsValid||body.ValueKind!=JsonValueKind.Object)throw AssistantException.Invalid();AssistantJwt.Unique(body);return body.Deserialize<T>(Wire)??throw AssistantException.Invalid();}
    private ResourceLease Lease(CustomerStaff who,bool write)=>admission.Enter(write?ResourceGroup.AssistantWork:ResourceGroup.AssistantRead,ResourceSubject.User(who.UserId),who.StoreId);
    private async Task<IActionResult> Handle(bool write,Func<CustomerStaff,Task<IActionResult>> action,CancellationToken ct){
        try{var who=await access.Staff(CustomerServiceRights.Manage,ct);using var lease=Lease(who,write);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);return await action(who);}
        catch(AssistantException e){return StatusCode(e.Status,e.Body(Guid.Empty));}catch(JsonException){return BadRequest(AssistantException.Invalid().Body(Guid.Empty));}
    }
    private async Task<AssistantIdentity> CatalogIdentity(CustomerStaff who,CancellationToken ct){
        var current=await assistant.CurrentAsync(null,ct);AssistantAccess.Require(current.Permissions,AssistantPermissions.Catalog);
        var monitor=await assistant.MonitorAsync(ct);if(current.UserId!=who.UserId||current.Scope.StoreId!=who.StoreId.ToString()||current.Scope!=monitor.Scope||monitor.UserId!=who.UserId)throw AssistantException.Denied();return current;
    }
    private async Task<HashSet<string>> Teaching(CustomerStaff who,CancellationToken ct){
        var entries=(await knowledge.ReadAsync(who.StoreId,ct)).Entries;var cases=store.EvaluationCases(who);
        var keys=store.EvaluationTeachingKeys(who,cases.Select(c=>c.Query));foreach(var e in entries)keys.Add(AssistantCatalogSearch.Key(e.Query));return keys;
    }
    [HttpGet("")]
    public Task<IActionResult> Index(CancellationToken ct)=>Handle(false,async who=>{
        var current=await access.Store(ct);await access.Recheck(who,CustomerServiceRights.Manage,ct);return View("~/Areas/Admin/Views/CustomerQuality/Index.cshtml",new CustomerQualityPage(who,current.Name,options.CurrentValue.RetentionDays));},ct);
    [HttpGet("report")]
    public Task<IActionResult> Report(int days=7,string source="all",CancellationToken ct=default)=>Handle(false,async who=>{
        var report=store.QualityReport(who,days,source);await access.Recheck(who,CustomerServiceRights.Manage,ct);return Json(new{storeId=who.StoreId,report});},ct);
    [HttpGet("feedback")]
    public Task<IActionResult> Feedback(int days=7,string source="all",string status="open",int page=0,CancellationToken ct=default)=>Handle(false,async who=>{
        var queue=store.QualityQueue(who,days,source,status,page);await access.Recheck(who,CustomerServiceRights.Manage,ct);return Json(new{storeId=who.StoreId,queue});},ct);
    [HttpPost("feedback/{id:guid}/review")]
    public Task<IActionResult> Review(Guid id,[FromBody]JsonElement body,CancellationToken ct)=>Handle(true,async who=>{
        var input=Parse<CustomerQualityReview>(body);await access.Recheck(who,CustomerServiceRights.Manage,ct);return Json(store.ReviewQuality(who,id,input));},ct);
    [HttpPost("feedback/{id:guid}/propose")]
    public Task<IActionResult> Propose(Guid id,[FromBody]JsonElement body,CancellationToken ct)=>Handle(true,async who=>{
        var input=Parse<CustomerQualityProposal>(body);if(!input.Sanitized||!AssistantSearchKnowledge.ValidQuery(input.Query)||input.VariantId<=0||input.RequestId==Guid.Empty)throw AssistantException.Invalid();
        var row=store.QualityItem(who,id);var identity=await CatalogIdentity(who,ct);var variant=await catalog.FindAsync(identity,input.VariantId.ToString(),ct);
        await CatalogIdentity(who,ct);await access.Recheck(who,CustomerServiceRights.Manage,ct);
        store.MarkEvaluationTeaching(who,input.Query);
        var proposal=await knowledge.ProposeAsync(who.StoreId,who.UserId,input.Query,variant.ProductId,variant.Id,input.RequestId,row.ConversationId,row.MessageId.ToString(),ct);
        return Json(new{proposal.Id,proposal.Status,reviewPath=Url.Content("~/admin/assistant/search")});},ct);
    [HttpGet("cases")]
    public Task<IActionResult> Cases(CancellationToken ct)=>Handle(false,async who=>{
        var cases=store.EvaluationCases(who);await access.Recheck(who,CustomerServiceRights.Manage,ct);return Json(new{storeId=who.StoreId,suiteVersion=CustomerEvaluation.SuiteVersion(cases.Where(c=>c.State=="active")),items=cases});},ct);
    [HttpPost("cases")]
    public Task<IActionResult> SaveCase([FromBody]JsonElement body,CancellationToken ct)=>Handle(true,async who=>{
        var input=Parse<CustomerEvaluationInput>(body);if(input.ExpectedVariantIds is null||input.ExpectedVariantIds.Length>5)throw AssistantException.Invalid();
        var identity=await CatalogIdentity(who,ct);if(input.State=="active")foreach(var id in input.ExpectedVariantIds){if(id<=0)throw AssistantException.Invalid();await catalog.FindAsync(identity,id.ToString(),ct);}
        var teaching=await Teaching(who,ct);await CatalogIdentity(who,ct);await access.Recheck(who,CustomerServiceRights.Manage,ct);
        return Json(store.SaveEvaluationCase(who,input,teaching));},ct);
    [HttpPost("cases/{id:guid}/delete")]
    public Task<IActionResult> DeleteCase(Guid id,[FromBody]JsonElement body,CancellationToken ct)=>Handle(true,async who=>{
        var input=Parse<CustomerEvaluationDelete>(body);await CatalogIdentity(who,ct);await access.Recheck(who,CustomerServiceRights.Manage,ct);return Json(new{deleted=store.DeleteEvaluationCase(who,id,input)});},ct);
    [HttpGet("evaluations")]
    public Task<IActionResult> Evaluations(CancellationToken ct)=>Handle(false,async who=>{
        var reports=store.EvaluationReports(who);await access.Recheck(who,CustomerServiceRights.Manage,ct);return Json(new{storeId=who.StoreId,items=reports});},ct);
    [HttpPost("evaluations")]
    public Task<IActionResult> Run([FromBody]JsonElement body,CancellationToken ct)=>Handle(true,async who=>{
        var input=Parse<CustomerEvaluationRunInput>(body);if(string.IsNullOrEmpty(input.SuiteVersion)||input.SuiteVersion.Length!=64)throw AssistantException.Invalid();
        var identity=await CatalogIdentity(who,ct);var replay=store.ReplayEvaluationRun(who,input);if(replay is not null){await access.Recheck(who,CustomerServiceRights.Manage,ct);return Json(new{storeId=who.StoreId,report=replay});}
        var cases=store.EvaluationCases(who).Where(c=>c.State=="active").ToArray();if(CustomerEvaluation.SuiteVersion(cases)!=input.SuiteVersion)throw new AssistantException(409,"evaluation_suite_changed","Bộ câu hỏi vừa đổi; tải lại trước khi chạy.");
        var documents=await catalog.SearchDocumentsAsync(identity,ct);var teaching=await Teaching(who,ct);var report=CustomerEvaluation.Run(cases,documents,who.StoreId.ToString(),teaching,clock,ct);
        if(!teaching.SetEquals(await Teaching(who,ct)))throw new AssistantException(409,"evaluation_teaching_changed","Cách gọi vừa đổi; chạy lại bộ kiểm tra.");
        await CatalogIdentity(who,ct);await access.Recheck(who,CustomerServiceRights.Manage,ct);return Json(new{storeId=who.StoreId,report=store.SaveEvaluationReport(who,input,report)});},ct);
    [HttpGet("cases.json")]
    public Task<IActionResult> Export(CancellationToken ct)=>Handle(false,async who=>{
        await CatalogIdentity(who,ct);var cases=store.EvaluationCases(who).Where(c=>c.State=="active").ToArray();var teaching=await Teaching(who,ct);await access.Recheck(who,CustomerServiceRights.Manage,ct);
        return File(JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=1,sourceKind="manual_verified",storeId=who.StoreId.ToString(),
            cases=cases.Select(c=>new{id=c.Id.ToString(),query=c.Query,kind=c.Kind,expectedVariantIds=c.ExpectedVariantIds,split=c.Split=="tune"||c.FromFeedback||teaching.Contains(AssistantCatalogSearch.Key(c.Query))?"tune":"holdout"})},Wire),"application/json","cskh-evaluation-cases.json");},ct);
}
