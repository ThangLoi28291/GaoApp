using System.Text.Json;
using GaoApp.Web.Services.Assistant;
using Microsoft.Data.Sqlite;
namespace GaoApp.Web.Services.CustomerService;
public sealed partial class CustomerServiceStore
{
    private static readonly JsonSerializerOptions QualityJson=new(JsonSerializerDefaults.Web);
    private static string TeachingHash(string query)=>Hash(AssistantCatalogSearch.Key(query));
    private CustomerEvaluationCase[] EvaluationRows(SqliteConnection db,SqliteTransaction tx,int store){
        using var cmd=Command(db,tx,"SELECT Id,Query,Kind,Expected,Split,State,Version,Updated,FromFeedback FROM CskhEvaluationCases WHERE Store=$store ORDER BY Updated DESC,Id",("$store",store));using var r=cmd.ExecuteReader();var rows=new List<CustomerEvaluationCase>();
        while(r.Read())rows.Add(new(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),JsonSerializer.Deserialize<int[]>(r.GetString(3))!,r.GetString(4),r.GetString(5),r.GetString(6),DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(7)),r.GetInt64(8)==1));return rows.ToArray();
    }
    public CustomerEvaluationCase[] EvaluationCases(CustomerStaff who)=>Run((db,tx)=>{QualityManager(who);return EvaluationRows(db,tx,who.StoreId);});
    private string? EvaluationReplay(SqliteConnection db,SqliteTransaction tx,CustomerStaff who,Guid request,object payload){
        if(request==Guid.Empty)throw AssistantException.Invalid();
        Execute(db,tx,"DELETE FROM CskhEvaluationRequests WHERE Store=$store AND Actor=$actor AND Id=$id AND Expires<=$now",("$store",who.StoreId),("$actor",who.UserId),("$id",request.ToString()),("$now",Now));
        using var cmd=Command(db,tx,"SELECT Hash,Result FROM CskhEvaluationRequests WHERE Store=$store AND Actor=$actor AND Id=$id",("$store",who.StoreId),("$actor",who.UserId),("$id",request.ToString()));using var r=cmd.ExecuteReader();if(!r.Read())return null;if(r.GetString(0)!=Hash(payload))throw Conflict();return r.GetString(1);
    }
    private void EvaluationRemember(SqliteConnection db,SqliteTransaction tx,CustomerStaff who,Guid request,object payload,string result){
        if((long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhEvaluationRequests WHERE Store=$store",("$store",who.StoreId))!>=2000)throw new AssistantException(429,"evaluation_request_limit","Đã đạt giới hạn thao tác bộ kiểm tra trong 7 ngày.");
        Execute(db,tx,"INSERT INTO CskhEvaluationRequests VALUES($store,$actor,$id,$hash,$result,$expires)",("$store",who.StoreId),("$actor",who.UserId),("$id",request.ToString()),("$hash",Hash(payload)),("$result",result),("$expires",Now+7*86400000L));
    }
    private void Teach(SqliteConnection db,SqliteTransaction tx,int store,string query){
        var hash=TeachingHash(query);if(Scalar(db,tx,"SELECT QueryHash FROM CskhEvaluationTeaching WHERE Store=$store AND QueryHash=$hash",("$store",store),("$hash",hash)) is not null)return;
        if((long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhEvaluationTeaching WHERE Store=$store",("$store",store))!>=10000)throw new AssistantException(409,"evaluation_teaching_full","Sổ câu đã dùng để dạy đã đầy; cần quản trị kiểm tra trước khi mở rộng.");
        Execute(db,tx,"INSERT INTO CskhEvaluationTeaching VALUES($store,$hash)",("$store",store),("$hash",hash));
    }
    public void MarkEvaluationTeaching(CustomerStaff who,string query)=>Run((db,tx)=>{QualityManager(who);if(!AssistantSearchKnowledge.ValidQuery(query))throw AssistantException.Invalid();Teach(db,tx,who.StoreId,query);return true;});
    public CustomerEvaluationCase SaveEvaluationCase(CustomerStaff who,CustomerEvaluationInput input,IReadOnlySet<string> teachingKeys)=>Run((db,tx)=>{
        QualityManager(who);
        if(!input.Sanitized||!CustomerEvaluation.ValidQuery(input.Query)||input.Query.Length>200||input.Kind is not ("exact" or "ambiguous" or "confirm" or "not_found")
            ||input.Split is not ("tune" or "holdout")||input.State is not ("active" or "archived")||input.ExpectedVariantIds is null||input.ExpectedVariantIds.Any(id=>id<=0)
            ||input.Kind=="exact"&&input.ExpectedVariantIds.Length!=1||input.ExpectedVariantIds.Length>5||input.ExpectedVariantIds.Distinct().Count()!=input.ExpectedVariantIds.Length
            ||(input.Kind=="not_found"?input.ExpectedVariantIds.Length!=0:input.Kind=="ambiguous"?input.ExpectedVariantIds.Length<2:input.ExpectedVariantIds.Length==0)
            ||input.Split=="holdout"&&!input.Independent||input.FeedbackId==Guid.Empty)throw AssistantException.Invalid();
        var all=EvaluationRows(db,tx,who.StoreId);var payload=new{action="case",input};
        var replay=EvaluationReplay(db,tx,who,input.RequestId,payload);if(replay is not null)return all.SingleOrDefault(c=>c.Id.ToString()==replay)??throw Missing();
        var current=input.Id==Guid.Empty?null:all.SingleOrDefault(c=>c.Id==input.Id)??throw Missing();
        if(current is null&&input.Version is not null||current is not null&&current.Version!=input.Version)throw Conflict();
        var fromFeedback=current?.FromFeedback==true||input.FeedbackId is not null;
        if(input.FeedbackId is {} feedback)ReviewItem(db,tx,who.StoreId,feedback);
        var key=AssistantCatalogSearch.Key(input.Query);
        if(all.Any(c=>c.Id!=input.Id&&AssistantCatalogSearch.Key(c.Query)==key))throw new AssistantException(409,"evaluation_duplicate_query","Câu tương đương đã có trong bộ. Sửa câu đang lưu thay vì nhân đôi mẫu.");
        var taught=teachingKeys.Contains(key)||all.Any(c=>c.Split=="tune"&&AssistantCatalogSearch.Key(c.Query)==key)
            ||Scalar(db,tx,"SELECT QueryHash FROM CskhEvaluationTeaching WHERE Store=$store AND QueryHash=$hash",("$store",who.StoreId),("$hash",TeachingHash(input.Query))) is not null;
        if(input.Split=="holdout"&&(fromFeedback||current?.Split=="tune"||taught))throw new AssistantException(422,"evaluation_teaching_overlap","Câu đã dùng để dạy hoặc lấy từ phản hồi phải ở nhóm luyện, không được tính nghiệm thu độc lập.");
        if(current is null&&all.Length>=500||input.State=="active"&&all.Count(c=>c.State=="active"&&c.Id!=input.Id)>=100)throw new AssistantException(409,"evaluation_case_limit","Tối đa 500 câu lưu và 100 câu đang dùng. Lưu trữ bớt hoặc xuất bộ lớn để chạy offline.");
        if(input.Split=="tune")Teach(db,tx,who.StoreId,input.Query);
        var id=current?.Id??Guid.NewGuid();var version=Guid.NewGuid().ToString("N");
        Execute(db,tx,"""
            INSERT INTO CskhEvaluationCases VALUES($id,$store,$query,$kind,$expected,$split,$state,$version,$now,$now,$actor,$feedback)
            ON CONFLICT(Id) DO UPDATE SET Query=$query,Kind=$kind,Expected=$expected,Split=$split,State=$state,Version=$version,Updated=$now,Actor=$actor,FromFeedback=$feedback
            """,("$id",id.ToString()),("$store",who.StoreId),("$query",input.Query.Trim()),("$kind",input.Kind),("$expected",JsonSerializer.Serialize(input.ExpectedVariantIds.Order().ToArray())),("$split",input.Split),("$state",input.State),("$version",version),("$now",Now),("$actor",who.UserId),("$feedback",fromFeedback?1:0));
        EvaluationRemember(db,tx,who,input.RequestId,payload,id.ToString());return EvaluationRows(db,tx,who.StoreId).Single(c=>c.Id==id);
    });
    public bool DeleteEvaluationCase(CustomerStaff who,Guid id,CustomerEvaluationDelete input)=>Run((db,tx)=>{
        QualityManager(who);if(id==Guid.Empty||!Guid.TryParseExact(input.Version,"N",out _))throw AssistantException.Invalid();var payload=new{action="delete",id,input};
        if(EvaluationReplay(db,tx,who,input.RequestId,payload) is not null)return true;
        var current=EvaluationRows(db,tx,who.StoreId).SingleOrDefault(c=>c.Id==id)??throw Missing();if(current.Version!=input.Version||current.State!="archived")throw Conflict();
        var reports=Ids(db,tx,"SELECT Id FROM CskhEvaluationRuns WHERE Store=$store AND Created>$cutoff",("$store",who.StoreId),("$cutoff",Now-7L*86400000));
        foreach(var run in reports)if(EvaluationReport(db,tx,who.StoreId,run).Observations.Any(o=>o.Id==id))Execute(db,tx,"DELETE FROM CskhEvaluationRuns WHERE Store=$store AND Id=$id",("$store",who.StoreId),("$id",run.ToString()));
        Execute(db,tx,"DELETE FROM CskhEvaluationCases WHERE Store=$store AND Id=$id",("$store",who.StoreId),("$id",id.ToString()));
        EvaluationRemember(db,tx,who,input.RequestId,payload,"deleted:"+id);return true;
    });
    public CustomerEvaluationReport? ReplayEvaluationRun(CustomerStaff who,CustomerEvaluationRunInput input)=>Run((db,tx)=>{
        QualityManager(who);var previous=EvaluationReplay(db,tx,who,input.RequestId,new{action="run",input});return previous is null?null:EvaluationReport(db,tx,who.StoreId,Guid.Parse(previous));
    });
    private CustomerEvaluationReport EvaluationReport(SqliteConnection db,SqliteTransaction tx,int store,Guid id){
        var json=Scalar(db,tx,"SELECT Report FROM CskhEvaluationRuns WHERE Store=$store AND Id=$id AND Created>$cutoff",("$store",store),("$id",id.ToString()),("$cutoff",Now-7L*86400000)) as string??throw Missing();return JsonSerializer.Deserialize<CustomerEvaluationReport>(json,QualityJson)!;
    }
    public CustomerEvaluationReport[] EvaluationReports(CustomerStaff who)=>Run((db,tx)=>{
        QualityManager(who);return Ids(db,tx,"SELECT Id FROM CskhEvaluationRuns WHERE Store=$store AND Created>$cutoff ORDER BY Created DESC,rowid DESC LIMIT 10",("$store",who.StoreId),("$cutoff",Now-7L*86400000)).Select(id=>EvaluationReport(db,tx,who.StoreId,id)).ToArray();
    });
    public HashSet<string> EvaluationTeachingKeys(CustomerStaff who,IEnumerable<string> queries)=>Run((db,tx)=>{
        QualityManager(who);var all=EvaluationRows(db,tx,who.StoreId).Where(c=>c.Split=="tune").Select(c=>AssistantCatalogSearch.Key(c.Query)).ToHashSet(StringComparer.Ordinal);
        foreach(var query in queries)if(Scalar(db,tx,"SELECT QueryHash FROM CskhEvaluationTeaching WHERE Store=$store AND QueryHash=$hash",("$store",who.StoreId),("$hash",TeachingHash(query))) is not null)all.Add(AssistantCatalogSearch.Key(query));return all;
    });
    public CustomerEvaluationReport SaveEvaluationReport(CustomerStaff who,CustomerEvaluationRunInput input,CustomerEvaluationReport report)=>Run((db,tx)=>{
        QualityManager(who);var payload=new{action="run",input};var replay=EvaluationReplay(db,tx,who,input.RequestId,payload);if(replay is not null)return EvaluationReport(db,tx,who.StoreId,Guid.Parse(replay));
        var cases=EvaluationRows(db,tx,who.StoreId).Where(c=>c.State=="active").ToArray();
        if(report.SuiteVersion!=input.SuiteVersion||CustomerEvaluation.SuiteVersion(cases)!=input.SuiteVersion)throw new AssistantException(409,"evaluation_suite_changed","Bộ câu hỏi vừa đổi; tải lại trước khi chạy.");
        if(report.Observations.Any(o=>o.Split=="holdout"&&Scalar(db,tx,"SELECT QueryHash FROM CskhEvaluationTeaching WHERE Store=$store AND QueryHash=$hash",("$store",who.StoreId),("$hash",TeachingHash(o.Query))) is not null))throw Conflict();
        Execute(db,tx,"INSERT INTO CskhEvaluationRuns VALUES($id,$store,$actor,$now,$report)",("$id",report.Id.ToString()),("$store",who.StoreId),("$actor",who.UserId),("$now",Now),("$report",JsonSerializer.Serialize(report,QualityJson)));
        EvaluationRemember(db,tx,who,input.RequestId,payload,report.Id.ToString());
        Execute(db,tx,"DELETE FROM CskhEvaluationRuns WHERE Store=$store AND Id NOT IN (SELECT Id FROM CskhEvaluationRuns WHERE Store=$store ORDER BY Created DESC,rowid DESC LIMIT 10)",("$store",who.StoreId));return report;
    });
}
