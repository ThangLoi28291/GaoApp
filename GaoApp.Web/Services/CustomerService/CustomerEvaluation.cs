using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GaoApp.Web.Services.Assistant;
namespace GaoApp.Web.Services.CustomerService;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerEvaluationInput([property:JsonRequired] Guid RequestId,[property:JsonRequired] Guid Id,[property:JsonRequired] string? Version,
    [property:JsonRequired] string Query,[property:JsonRequired] string Kind,[property:JsonRequired] int[] ExpectedVariantIds,[property:JsonRequired] string Split,
    [property:JsonRequired] string State,[property:JsonRequired] bool Independent,[property:JsonRequired] bool Sanitized,Guid? FeedbackId=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerEvaluationRunInput([property:JsonRequired] Guid RequestId,[property:JsonRequired] string SuiteVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerEvaluationDelete([property:JsonRequired] Guid RequestId,[property:JsonRequired] string Version);
public sealed record CustomerEvaluationCase(Guid Id,string Query,string Kind,int[] ExpectedVariantIds,string Split,string State,string Version,DateTimeOffset UpdatedAt,bool FromFeedback);
public sealed record CustomerEvaluationObservation(Guid Id,string Version,string Query,string Kind,int[] ExpectedIds,int[] ActualIds,string Split,string? ExcludedFromHoldout,
    bool RequiresConfirmation,bool Confident,bool WrongConfident,bool Critical,bool Passed,double ElapsedMs);
public sealed record CustomerEvaluationMetrics(int Cases,int Passed,int Critical,int ExactCases,double? Top1Accuracy,double? RecallAt5Any,int ConfidentAnswers,int WrongConfidentAnswers,double? PassRate);
public sealed record CustomerEvaluationReport(Guid Id,DateTimeOffset GeneratedAt,string Strategy,string SuiteVersion,string CatalogRevision,int DocumentCount,
    string Gate,CustomerEvaluationMetrics Tune,CustomerEvaluationMetrics Holdout,CustomerEvaluationObservation[] Observations);

// Exercises the same name ranker as catalog retrieval. This is not a model judge or an end-to-end chat score.
public static class CustomerEvaluation
{
    public static bool ValidQuery(string? query)=>!string.IsNullOrWhiteSpace(query)&&query.Length<=200&&!query.Any(char.IsControl)&&AssistantCatalogSearch.Canonical(query).Length>0;
    public static string SuiteVersion(IEnumerable<CustomerEvaluationCase> cases)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cases.OrderBy(c=>c.Id).Select(c=>new{c.Id,c.Version,c.Query,c.Kind,c.ExpectedVariantIds,c.Split,c.State})))));
    public static CustomerEvaluationReport Run(CustomerEvaluationCase[] cases,SearchDocument[] documents,string store,IReadOnlySet<string> teachingKeys,TimeProvider clock,CancellationToken ct){
        if(cases.Length is <1 or >100)throw new AssistantException(422,"evaluation_case_limit","Bộ chạy cần 1–100 câu đang dùng. Có thể xuất dữ liệu để chạy bộ lớn bằng công cụ offline.");
        var deadline=Stopwatch.StartNew();var rows=new List<CustomerEvaluationObservation>();var known=documents.Select(d=>d.Id).ToHashSet();string revision="";
        var trained=documents.SelectMany(d=>d.Aliases).Select(AssistantCatalogSearch.Key).Concat(teachingKeys).ToHashSet(StringComparer.Ordinal);
        foreach(var c in cases.OrderBy(c=>c.Id)){
            ct.ThrowIfCancellationRequested();if(deadline.Elapsed>TimeSpan.FromSeconds(10))throw new AssistantException(503,"evaluation_time_limit","Bộ kiểm tra vượt 10 giây. Chia bộ câu hỏi hoặc dùng công cụ offline; chưa lưu kết quả một phần.");
            var timer=Stopwatch.StartNew();var page=AssistantCatalogSearch.Rank(documents,new(){Query=c.Query,Limit=5},store);timer.Stop();revision=page.Search.CatalogRevision;
            var actual=page.Hits.Select(h=>h.Document.Id).ToArray();var confident=actual.Length==1&&page.Cursor is null&&!page.Search.RequiresConfirmation;
            var missing=c.ExpectedVariantIds.Any(id=>!known.Contains(id));
            var passed=!missing&&(c.Kind switch{
                "not_found"=>actual.Length==0,
                "ambiguous"=>!confident&&c.ExpectedVariantIds.All(actual.Contains),
                "confirm"=>page.Search.RequiresConfirmation&&actual.Any(c.ExpectedVariantIds.Contains),
                _=>actual.Length>0&&c.ExpectedVariantIds.Contains(actual[0])
            });
            var wrong=confident&&!c.ExpectedVariantIds.Contains(actual[0]);
            var critical=wrong||c.Kind=="exact"&&!passed;
            var contaminated=c.FromFeedback||trained.Contains(AssistantCatalogSearch.Key(c.Query));
            rows.Add(new(c.Id,c.Version,c.Query,c.Kind,c.ExpectedVariantIds,actual,c.Split=="tune"||contaminated?"tune":"holdout",
                c.Split=="holdout"&&contaminated?"teaching_overlap":null,page.Search.RequiresConfirmation,confident,wrong,critical,passed,timer.Elapsed.TotalMilliseconds));
        }
        if(deadline.Elapsed>TimeSpan.FromSeconds(10))throw new AssistantException(503,"evaluation_time_limit","Bộ kiểm tra vượt 10 giây; chưa lưu kết quả một phần.");
        var tune=Metrics(rows.Where(c=>c.Split=="tune"));var holdout=Metrics(rows.Where(c=>c.Split=="holdout"));
        var gate=holdout.Cases<20?"insufficient_holdout":holdout.Critical>0||holdout.PassRate<.95?"needs_review":"retrieval_passed";
        return new(Guid.NewGuid(),clock.GetUtcNow(),AssistantCatalogSearch.Strategy,SuiteVersion(cases),revision,documents.Length,gate,tune,holdout,rows.ToArray());
    }
    public static CustomerEvaluationMetrics Metrics(IEnumerable<CustomerEvaluationObservation> observations){
        var rows=observations.ToArray();var exact=rows.Where(c=>c.Kind=="exact").ToArray();var matching=rows.Where(c=>c.Kind!="not_found").ToArray();
        double? Rate(int a,int b)=>b==0?null:Math.Round((double)a/b,4);
        return new(rows.Length,rows.Count(c=>c.Passed),rows.Count(c=>c.Critical),exact.Length,Rate(exact.Count(c=>c.ActualIds.Length>0&&c.ExpectedIds.Contains(c.ActualIds[0])),exact.Length),
            Rate(matching.Count(c=>c.ActualIds.Any(c.ExpectedIds.Contains)),matching.Length),rows.Count(c=>c.Confident),rows.Count(c=>c.WrongConfident),Rate(rows.Count(c=>c.Passed),rows.Length));
    }
}
