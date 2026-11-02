using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Web.Services.Assistant;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.CustomerService;

// One dedicated SQLite file, durable across IIS recycle. Every state transition uses an immediate transaction.
// Tenant, visitor ownership, request replay and the AI lease are checked together; no browser state is trusted.
public sealed partial class CustomerServiceStore(IWebHostEnvironment environment, IOptionsMonitor<CustomerServiceOptions> options, TimeProvider clock,ILogger<CustomerServiceStore>? logger=null,IDataProtectionProvider? protection=null)
{
    private long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();
    private IDataProtector? PrivateReplies=>protection?.CreateProtector("GaoApp.CustomerService.PrivateChannelReply.v1");
    private readonly object initialize = new();
    private string? initialized;
    internal void InitializeForMaintenance() { using var db=Open(); }
    private SqliteConnection Open() {
        var path=Path.GetFullPath(options.CurrentValue.DatabasePath,environment.ContentRootPath);
        AssistantStorageSafety.Available(Path.GetDirectoryName(path)!);
        if(!environment.IsDevelopment()) {
            try { AssistantStorageSafety.Database(options.CurrentValue.DatabasePath,options.CurrentValue.StorageId,environment); }
            catch(Exception e) when(e is InvalidOperationException or SqliteException or IOException or UnauthorizedAccessException) {
                throw new AssistantException(503,"customer_persistent_storage_unavailable","Chưa xác minh được dữ liệu CSKH đã cấu hình. Vui lòng kiểm tra triển khai.");
            }
        }
        if(environment.IsDevelopment())Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=environment.IsDevelopment()?SqliteOpenMode.ReadWriteCreate:SqliteOpenMode.ReadWrite,Pooling=false,DefaultTimeout=5,ForeignKeys=true}.ToString());db.Open();
        lock(initialize) { if(initialized!=path) {
            using var cmd=db.CreateCommand();cmd.CommandText="""
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS CskhSettings(Store INTEGER PRIMARY KEY,Enabled INTEGER NOT NULL,Version INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS CskhConversations(
                    Id TEXT PRIMARY KEY,Store INTEGER NOT NULL,Visitor TEXT NOT NULL,Name TEXT NOT NULL,Contact TEXT,
                    Mode TEXT NOT NULL,Owner INTEGER,OwnerName TEXT,Version INTEGER NOT NULL,Created INTEGER NOT NULL,Updated INTEGER NOT NULL,Expires INTEGER NOT NULL,
                    Unread INTEGER NOT NULL,Reason TEXT,AiState TEXT NOT NULL DEFAULT '',ProcessingId TEXT,ProcessingUntil INTEGER);
                CREATE INDEX IF NOT EXISTS IX_CskhStore ON CskhConversations(Store,Updated DESC);
                CREATE INDEX IF NOT EXISTS IX_CskhVisitor ON CskhConversations(Store,Visitor,Updated DESC);
                CREATE INDEX IF NOT EXISTS IX_CskhExpiry ON CskhConversations(Expires);
                CREATE INDEX IF NOT EXISTS IX_CskhLease ON CskhConversations(ProcessingUntil) WHERE ProcessingId IS NOT NULL;
                CREATE TABLE IF NOT EXISTS CskhMessages(Id TEXT PRIMARY KEY,Conversation TEXT NOT NULL REFERENCES CskhConversations(Id) ON DELETE CASCADE,
                    Role TEXT NOT NULL,Text TEXT NOT NULL,Created INTEGER NOT NULL,AgentName TEXT);
                CREATE INDEX IF NOT EXISTS IX_CskhMessages ON CskhMessages(Conversation,Created);
                CREATE TABLE IF NOT EXISTS CskhRequests(Conversation TEXT NOT NULL REFERENCES CskhConversations(Id) ON DELETE CASCADE,
                    Id TEXT NOT NULL,Actor TEXT NOT NULL,Hash TEXT NOT NULL,PRIMARY KEY(Conversation,Id));
                CREATE TABLE IF NOT EXISTS CskhStarts(Store INTEGER NOT NULL,Visitor TEXT NOT NULL,Request TEXT NOT NULL,Hash TEXT NOT NULL,Conversation TEXT NOT NULL REFERENCES CskhConversations(Id) ON DELETE CASCADE,
                    PRIMARY KEY(Store,Visitor,Request));
                CREATE TABLE IF NOT EXISTS CskhRevoked(Store INTEGER NOT NULL,Visitor TEXT NOT NULL,Expires INTEGER NOT NULL,PRIMARY KEY(Store,Visitor));
                CREATE TABLE IF NOT EXISTS CskhIdentity(Conversation TEXT PRIMARY KEY REFERENCES CskhConversations(Id) ON DELETE CASCADE,
                    Customer INTEGER NOT NULL,Issuer INTEGER NOT NULL,Method TEXT NOT NULL,Reference TEXT NOT NULL,
                    IssueRequest TEXT NOT NULL,CodeHash TEXT,ProtectedCode TEXT,CodeUntil INTEGER NOT NULL,GrantUntil INTEGER NOT NULL,Failures INTEGER NOT NULL,
                    Connection TEXT,Binding TEXT,Peer TEXT,VerifiedPhone TEXT);
                CREATE TABLE IF NOT EXISTS CskhIdentityAudit(Id INTEGER PRIMARY KEY,Conversation TEXT NOT NULL REFERENCES CskhConversations(Id) ON DELETE CASCADE,
                    Action TEXT NOT NULL,Actor INTEGER,Customer INTEGER,Created INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_CskhIdentityAudit ON CskhIdentityAudit(Conversation,Id);
                CREATE TABLE IF NOT EXISTS CskhChannelControls(Connection TEXT PRIMARY KEY,Binding TEXT NOT NULL,Paused INTEGER NOT NULL DEFAULT 0,Version INTEGER NOT NULL DEFAULT 0,Cursor INTEGER NOT NULL DEFAULT 1,Baseline INTEGER NOT NULL DEFAULT 0,Started INTEGER,LastSuccess INTEGER,Error TEXT);
                CREATE TABLE IF NOT EXISTS CskhChannelThreads(Conversation TEXT PRIMARY KEY REFERENCES CskhConversations(Id) ON DELETE CASCADE,Connection TEXT NOT NULL,Binding TEXT NOT NULL,Provider TEXT NOT NULL,Peer TEXT NOT NULL,Reference TEXT NOT NULL,LastInbound INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_CskhChannelPeer ON CskhChannelThreads(Connection,Binding,Peer);
                CREATE TABLE IF NOT EXISTS CskhChannelReceipts(Connection TEXT NOT NULL,ExternalId TEXT NOT NULL,Hash TEXT NOT NULL,Expires INTEGER NOT NULL,PRIMARY KEY(Connection,ExternalId));
                CREATE INDEX IF NOT EXISTS IX_CskhChannelReceiptExpiry ON CskhChannelReceipts(Expires);
                CREATE TABLE IF NOT EXISTS CskhChannelInbox(Id TEXT PRIMARY KEY,Conversation TEXT NOT NULL REFERENCES CskhConversations(Id) ON DELETE CASCADE,Message TEXT NOT NULL,State TEXT NOT NULL,Created INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS CskhChannelOutbox(Id TEXT PRIMARY KEY,Conversation TEXT NOT NULL REFERENCES CskhConversations(Id) ON DELETE CASCADE,Message TEXT NOT NULL UNIQUE REFERENCES CskhMessages(Id) ON DELETE CASCADE,Connection TEXT NOT NULL,Binding TEXT NOT NULL,Peer TEXT NOT NULL,Reference TEXT NOT NULL,Kind TEXT NOT NULL,Actor INTEGER,State TEXT NOT NULL,Job TEXT,ExternalId TEXT,Code TEXT,Attempts INTEGER NOT NULL DEFAULT 0,Available INTEGER NOT NULL,Created INTEGER NOT NULL,Updated INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_CskhChannelSend ON CskhChannelOutbox(State,Available);
                CREATE TABLE IF NOT EXISTS CskhQualityFeedback(Id TEXT PRIMARY KEY,Conversation TEXT NOT NULL REFERENCES CskhConversations(Id) ON DELETE CASCADE,
                    Message TEXT NOT NULL REFERENCES CskhMessages(Id) ON DELETE CASCADE,Actor TEXT NOT NULL,Kind TEXT NOT NULL,Status TEXT NOT NULL,Version TEXT NOT NULL,
                    Created INTEGER NOT NULL,Updated INTEGER NOT NULL,Resolution TEXT,ReviewedBy INTEGER,UNIQUE(Message,Actor));
                CREATE INDEX IF NOT EXISTS IX_CskhQualityQueue ON CskhQualityFeedback(Status,Updated DESC);
                CREATE TABLE IF NOT EXISTS CskhEvaluationCases(Id TEXT PRIMARY KEY,Store INTEGER NOT NULL,Query TEXT NOT NULL,Kind TEXT NOT NULL,Expected TEXT NOT NULL,
                    Split TEXT NOT NULL,State TEXT NOT NULL,Version TEXT NOT NULL,Created INTEGER NOT NULL,Updated INTEGER NOT NULL,Actor INTEGER NOT NULL,FromFeedback INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_CskhEvaluationStore ON CskhEvaluationCases(Store,Updated DESC);
                CREATE TABLE IF NOT EXISTS CskhEvaluationRequests(Store INTEGER NOT NULL,Actor INTEGER NOT NULL,Id TEXT NOT NULL,Hash TEXT NOT NULL,Result TEXT NOT NULL,Expires INTEGER NOT NULL,PRIMARY KEY(Store,Actor,Id));
                CREATE TABLE IF NOT EXISTS CskhEvaluationTeaching(Store INTEGER NOT NULL,QueryHash TEXT NOT NULL,PRIMARY KEY(Store,QueryHash));
                CREATE TABLE IF NOT EXISTS CskhEvaluationRuns(Id TEXT PRIMARY KEY,Store INTEGER NOT NULL,Actor INTEGER NOT NULL,Created INTEGER NOT NULL,Report TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_CskhEvaluationRuns ON CskhEvaluationRuns(Store,Created DESC);
                CREATE TABLE IF NOT EXISTS CskhChannelThreadDetails(Conversation TEXT PRIMARY KEY REFERENCES CskhConversations(Id) ON DELETE CASCADE,Kind TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS CskhChannelMessageSources(Message TEXT PRIMARY KEY REFERENCES CskhMessages(Id) ON DELETE CASCADE,Connection TEXT NOT NULL,ExternalId TEXT NOT NULL,Details TEXT NOT NULL,UNIQUE(Connection,ExternalId));
                CREATE TABLE IF NOT EXISTS CskhChannelHistoryMedia(Id TEXT PRIMARY KEY,Conversation TEXT NOT NULL REFERENCES CskhConversations(Id) ON DELETE CASCADE,Connection TEXT NOT NULL,ExternalId TEXT NOT NULL,Observed INTEGER NOT NULL,Outgoing INTEGER NOT NULL,Details TEXT NOT NULL,UNIQUE(Connection,ExternalId));
                CREATE TABLE IF NOT EXISTS CskhOutgoingAssets(Message TEXT PRIMARY KEY REFERENCES CskhMessages(Id) ON DELETE CASCADE,
                    Kind TEXT NOT NULL,FileName TEXT NOT NULL,ContentType TEXT NOT NULL,Length INTEGER NOT NULL,Sha256 TEXT NOT NULL,Bytes BLOB NOT NULL);
                CREATE TABLE IF NOT EXISTS CskhZaloSync(Connection TEXT PRIMARY KEY REFERENCES CskhChannelControls(Connection),Binding TEXT NOT NULL,Work TEXT,Lease TEXT,LeaseUntil INTEGER,LastReceived INTEGER,LastCompleted INTEGER,WorkStarted INTEGER,Peer TEXT,Pages INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE IF NOT EXISTS CskhZaloCheckpoints(Connection TEXT NOT NULL REFERENCES CskhChannelControls(Connection),Peer TEXT NOT NULL,Anchor INTEGER,Verified INTEGER NOT NULL,PRIMARY KEY(Connection,Peer));
                CREATE INDEX IF NOT EXISTS IX_CskhZaloCheckpointExpiry ON CskhZaloCheckpoints(Verified);
                CREATE TABLE IF NOT EXISTS CskhVisitorSessions(Store INTEGER NOT NULL,Visitor TEXT NOT NULL,Expires INTEGER NOT NULL,Issued INTEGER NOT NULL,Revoked INTEGER NOT NULL DEFAULT 0,PRIMARY KEY(Store,Visitor));
                CREATE INDEX IF NOT EXISTS IX_CskhVisitorExpiry ON CskhVisitorSessions(Expires);
                CREATE INDEX IF NOT EXISTS IX_CskhVisitorIssued ON CskhVisitorSessions(Issued);
                CREATE INDEX IF NOT EXISTS IX_CskhVisitorStoreIssued ON CskhVisitorSessions(Store,Issued);
                CREATE TABLE IF NOT EXISTS CskhSessionUsage(Scope INTEGER PRIMARY KEY,Count INTEGER NOT NULL CHECK(Count>=0),LegacyUntil INTEGER);
                CREATE TABLE IF NOT EXISTS CskhConversationSessions(Conversation TEXT PRIMARY KEY REFERENCES CskhConversations(Id) ON DELETE CASCADE,Expires INTEGER NOT NULL);
                CREATE TRIGGER IF NOT EXISTS CskhSessionInserted AFTER INSERT ON CskhVisitorSessions WHEN NOT EXISTS(SELECT 1 FROM CskhRevoked WHERE Store=NEW.Store AND Visitor=NEW.Visitor) BEGIN UPDATE CskhSessionUsage SET Count=Count+1 WHERE Scope IN(0,NEW.Store); END;
                CREATE TRIGGER IF NOT EXISTS CskhSessionDeleted AFTER DELETE ON CskhVisitorSessions WHEN NOT EXISTS(SELECT 1 FROM CskhRevoked WHERE Store=OLD.Store AND Visitor=OLD.Visitor) BEGIN UPDATE CskhSessionUsage SET Count=Count-1 WHERE Scope IN(0,OLD.Store); END;
                CREATE TRIGGER IF NOT EXISTS CskhRevokedInserted AFTER INSERT ON CskhRevoked WHEN NOT EXISTS(SELECT 1 FROM CskhVisitorSessions WHERE Store=NEW.Store AND Visitor=NEW.Visitor) BEGIN UPDATE CskhSessionUsage SET Count=Count+1 WHERE Scope IN(0,NEW.Store); END;
                CREATE TRIGGER IF NOT EXISTS CskhRevokedDeleted AFTER DELETE ON CskhRevoked WHEN NOT EXISTS(SELECT 1 FROM CskhVisitorSessions WHERE Store=OLD.Store AND Visitor=OLD.Visitor) BEGIN UPDATE CskhSessionUsage SET Count=Count-1 WHERE Scope IN(0,OLD.Store); END;
                CREATE TRIGGER IF NOT EXISTS CskhSessionKeyImmutable BEFORE UPDATE OF Store,Visitor ON CskhVisitorSessions WHEN NEW.Store!=OLD.Store OR NEW.Visitor!=OLD.Visitor BEGIN SELECT RAISE(ABORT,'immutable visitor identity'); END;
                CREATE TRIGGER IF NOT EXISTS CskhRevokedKeyImmutable BEFORE UPDATE OF Store,Visitor ON CskhRevoked WHEN NEW.Store!=OLD.Store OR NEW.Visitor!=OLD.Visitor BEGIN SELECT RAISE(ABORT,'immutable visitor identity'); END;
                CREATE INDEX IF NOT EXISTS IX_CskhStartsConversation ON CskhStarts(Conversation);
                CREATE INDEX IF NOT EXISTS IX_CskhRevokedExpiry ON CskhRevoked(Expires);
                CREATE INDEX IF NOT EXISTS IX_CskhEvaluationRequestExpiry ON CskhEvaluationRequests(Expires);
                CREATE INDEX IF NOT EXISTS IX_CskhEvaluationRunExpiry ON CskhEvaluationRuns(Created);
                CREATE INDEX IF NOT EXISTS IX_CskhHistoryTimeline ON CskhChannelHistoryMedia(Conversation,Observed,Id);
                CREATE INDEX IF NOT EXISTS IX_CskhIdentityCodeExpiry ON CskhIdentity(CodeUntil) WHERE CodeHash IS NOT NULL OR ProtectedCode IS NOT NULL;
                CREATE INDEX IF NOT EXISTS IX_CskhChannelInboxState ON CskhChannelInbox(Conversation,State,Created);
                CREATE INDEX IF NOT EXISTS IX_CskhChannelOutboxRecovery ON CskhChannelOutbox(Connection,State,Updated);
                CREATE INDEX IF NOT EXISTS IX_CskhChannelOutboxLease ON CskhChannelOutbox(Updated) WHERE State IN('sending','checking');
                CREATE INDEX IF NOT EXISTS IX_CskhChannelInboxRecovery ON CskhChannelInbox(State,Conversation);
                CREATE INDEX IF NOT EXISTS IX_CskhChannelOutboxPeer ON CskhChannelOutbox(Connection,Binding,Peer,State);
                CREATE INDEX IF NOT EXISTS IX_CskhChannelOutboxReady ON CskhChannelOutbox(Connection,Binding,Available) WHERE State IN('pending','retry','accepted');
                CREATE INDEX IF NOT EXISTS IX_CskhChannelOutboxConversation ON CskhChannelOutbox(Conversation);
                CREATE INDEX IF NOT EXISTS IX_CskhQualityConversation ON CskhQualityFeedback(Conversation);
                PRAGMA user_version=11;
                """;cmd.ExecuteNonQuery();
            EnsureColumn(db,"CskhChannelOutbox","AssistantPolicyVersion");
            EnsureColumn(db,"CskhChannelOutbox","AssistantSettingsVersion");
            EnsureColumn(db,"CskhIdentity","Connection");EnsureColumn(db,"CskhIdentity","Binding");EnsureColumn(db,"CskhIdentity","Peer");EnsureColumn(db,"CskhIdentity","VerifiedPhone");
            EnsureColumn(db,"CskhChannelOutbox","PrivateIdentityGeneration");
            EnsureColumn(db,"CskhChannelOutbox","PrivatePayload");
            initialized=path;
        }}
        return db;
    }
    private static void EnsureColumn(SqliteConnection db,string table,string column) {
        using var check=db.CreateCommand();check.CommandText=$"PRAGMA table_info({table})";using var reader=check.ExecuteReader();
        while(reader.Read())if(string.Equals(reader.GetString(1),column,StringComparison.OrdinalIgnoreCase))return;
        reader.Close();using var alter=db.CreateCommand();alter.CommandText=$"ALTER TABLE {table} ADD COLUMN {column} TEXT";alter.ExecuteNonQuery();
    }
    private T Run<T>(Func<SqliteConnection,SqliteTransaction,T> action,bool readOnly=false,[System.Runtime.CompilerServices.CallerMemberName]string operation="") {
        var started=System.Diagnostics.Stopwatch.GetTimestamp();
        try { using var db=Open();using var tx=db.BeginTransaction(deferred:readOnly);
            var result=action(db,tx);tx.Commit();return result;
        } catch(Exception e) when(e is SqliteException or IOException or UnauthorizedAccessException) { StorageFailure(e);throw new AssistantException(503,"customer_store_unavailable","Chưa đọc hoặc lưu được hội thoại. Vui lòng thử lại."); }
        finally {StorageDuration(operation,started);}
    }
    private static SqliteCommand Command(SqliteConnection db,SqliteTransaction tx,string sql,params (string,object?)[] args) {
        var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value??DBNull.Value);return cmd;
    }
    private static void Execute(SqliteConnection db,SqliteTransaction tx,string sql,params (string,object?)[] args) {using var cmd=Command(db,tx,sql,args);cmd.ExecuteNonQuery();}
    private static int ExecuteChanged(SqliteConnection db,SqliteTransaction tx,string sql,params (string,object?)[] args) {using var cmd=Command(db,tx,sql,args);return cmd.ExecuteNonQuery();}
    private static object? Scalar(SqliteConnection db,SqliteTransaction tx,string sql,params (string,object?)[] args) {using var cmd=Command(db,tx,sql,args);return cmd.ExecuteScalar();}
    private static string Hash(object payload)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    private static bool Text(string? value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=max&&!value.Any(c=>char.IsControl(c)&&c is not ('\n' or '\r' or '\t'));
    private static AssistantException Missing()=>new(404,"customer_conversation_not_found","Không tìm thấy hội thoại được phép xem.");
    private static AssistantException Conflict()=>new(409,"customer_conversation_conflict","Hội thoại vừa thay đổi. Tải lại trước khi tiếp tục.");
    private static void Valid(int store,Guid visitor) {if(store<=0||visitor==Guid.Empty)throw AssistantException.Denied();}
    private CustomerStoreSettings Settings(SqliteConnection db,SqliteTransaction tx,int store) {
        using var cmd=Command(db,tx,"SELECT Enabled,Version FROM CskhSettings WHERE Store=$store",("$store",store));using var r=cmd.ExecuteReader();return r.Read()?new(r.GetInt64(0)==1,r.GetInt64(1)):new(false,0);
    }
    public CustomerStoreSettings Settings(int store)=>Run((db,tx)=>Settings(db,tx,store),readOnly:true);
    public CustomerStoreSettings Configure(int store,bool enabled,long version)=>Run((db,tx)=>{
        if(store<=0||Settings(db,tx,store).Version!=version)throw Conflict();
        Execute(db,tx,"INSERT INTO CskhSettings(Store,Enabled,Version) VALUES($store,$enabled,$version) ON CONFLICT(Store) DO UPDATE SET Enabled=$enabled,Version=$version",("$store",store),("$enabled",enabled?1:0),("$version",version+1));return new CustomerStoreSettings(enabled,version+1);
    });
    private void RequireEnabled(SqliteConnection db,SqliteTransaction tx,int store) {if(!Settings(db,tx,store).Enabled)throw new AssistantException(404,"customer_service_disabled","Cửa hàng chưa bật chat chăm sóc khách hàng.");}
    private Guid Add(SqliteConnection db,SqliteTransaction tx,Guid id,string role,string text,string? agent=null,int? actor=null,string? channelKind=null,string? assistantPolicyVersion=null,string? assistantSettingsVersion=null,Guid? privateIdentityGeneration=null) {
        var count=(long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhMessages WHERE Conversation=$id",("$id",id.ToString()))!;
        var limit=options.CurrentValue.MaximumMessagesPerConversation;
        if(role=="system"&&count>=limit+10)return Guid.Empty;
        if(role!="system"&&count>=(role=="customer"?limit-2:limit))
            throw new AssistantException(409,"customer_conversation_full","Hội thoại đã đủ số tin lưu. Vui lòng bắt đầu hội thoại mới.");
        var message=Guid.NewGuid();var kind=channelKind??role;var transcriptText=kind=="private"?"Đã gửi thông tin riêng đã xác minh qua kênh.":text;
        Execute(db,tx,"INSERT INTO CskhMessages VALUES($message,$id,$role,$text,$at,$agent)",("$message",message.ToString()),("$id",id.ToString()),("$role",role),("$text",transcriptText),("$at",Now),("$agent",agent));
        if(role is "agent" or "assistant")QueueChannelReply(db,tx,id,message,text,kind,actor,assistantPolicyVersion,assistantSettingsVersion,privateIdentityGeneration);
        return message;
    }
    private CustomerConversation Read(SqliteConnection db,SqliteTransaction tx,int store,Guid id,Guid? visitor=null,bool includeMessages=true) {
        RecoverConversation(db,tx,id,store);
        using var cmd=Command(db,tx,"SELECT Visitor,Name,Contact,Mode,Owner,OwnerName,Version,Created,Updated,Expires,Unread,Reason FROM CskhConversations WHERE Id=$id AND Store=$store AND Expires>$now",("$id",id.ToString()),("$store",store),("$now",Now));
        CustomerConversation c;
        using(var r=cmd.ExecuteReader()) {if(!r.Read()||visitor is not null&&r.GetString(0)!=visitor.Value.ToString())throw Missing();
            c=new(id,store,Guid.Parse(r.GetString(0)),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:r.GetInt32(4),r.IsDBNull(5)?null:r.GetString(5),r.GetInt64(6),DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(7)),DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(8)),DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(9)),r.GetInt32(10),r.IsDBNull(11)?null:r.GetString(11),[]);}
        if(c.Mode=="bot"&&Scalar(db,tx,"SELECT ProcessingUntil FROM CskhConversations WHERE Id=$id AND ProcessingId IS NOT NULL",("$id",id.ToString())) is long until&&until<=Now) {
            Execute(db,tx,"UPDATE CskhConversations SET Mode='waiting',Reason='ai_interrupted',ProcessingId=NULL,ProcessingUntil=NULL,Version=Version+1,Updated=$now,Unread=Unread+1 WHERE Id=$id",("$id",id.ToString()),("$now",Now));
            Add(db,tx,id,"system","Trợ lý chưa hoàn tất lượt trước. Hội thoại đã được chuyển vào hàng chờ nhân viên.");return Read(db,tx,store,id,visitor);
        }
        var channel=Scalar(db,tx,"SELECT Provider FROM CskhChannelThreads WHERE Conversation=$id",("$id",id.ToString())) as string;
        c=c with{Source=channel??"web",ConversationKind=ThreadKind(db,tx,id)};
        if(!includeMessages)return c;
        using var messages=Command(db,tx,"SELECT m.Id,m.Role,m.Text,m.Created,m.AgentName,o.State,o.Code,s.Details,a.Kind,a.FileName,a.ContentType,a.Length FROM CskhMessages m LEFT JOIN CskhChannelOutbox o ON o.Message=m.Id LEFT JOIN CskhChannelMessageSources s ON s.Message=m.Id LEFT JOIN CskhOutgoingAssets a ON a.Message=m.Id WHERE m.Conversation=$id ORDER BY m.Created,m.rowid",("$id",id.ToString()));using var reader=messages.ExecuteReader();var rows=new List<CustomerMessage>();
        while(reader.Read())rows.Add(new(Guid.Parse(reader.GetString(0)),reader.GetString(1),reader.GetString(2),DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),reader.IsDBNull(4)?null:reader.GetString(4),reader.IsDBNull(5)?null:new CustomerChannelDelivery(reader.GetString(5),reader.IsDBNull(6)?null:reader.GetString(6)),reader.IsDBNull(7)?null:JsonSerializer.Deserialize<CustomerZaloMessage>(reader.GetString(7),CustomerZaloMedia.Json),reader.IsDBNull(8)?null:new CustomerOutgoingAttachment(reader.GetString(8),reader.GetString(9),reader.GetString(10),reader.GetInt64(11))));
        return c with{Messages=rows.ToArray(),ZaloHistory=channel=="zalomarketing"?ReadZaloHistory(db,tx,id):null};
    }
    private bool Replay(SqliteConnection db,SqliteTransaction tx,Guid id,Guid request,string actor,object payload) {
        if(request==Guid.Empty)throw AssistantException.Invalid();var hash=Hash(payload);
        using var cmd=Command(db,tx,"SELECT Actor,Hash FROM CskhRequests WHERE Conversation=$id AND Id=$request",("$id",id.ToString()),("$request",request.ToString()));
        using(var r=cmd.ExecuteReader()){if(r.Read()){if(r.GetString(0)!=actor||r.GetString(1)!=hash)throw Conflict();return true;}}
        if((long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhRequests WHERE Conversation=$id",("$id",id.ToString()))!>=1000)throw new AssistantException(429,"customer_operation_limit","Hội thoại đã đạt giới hạn thao tác. Vui lòng bắt đầu hội thoại mới.");
        Execute(db,tx,"INSERT INTO CskhRequests VALUES($id,$request,$actor,$hash)",("$id",id.ToString()),("$request",request.ToString()),("$actor",actor),("$hash",hash));return false;
    }
    public CustomerConversation Start(int store,Guid visitor,CustomerStart input,DateTimeOffset? sessionExpiry=null) {
        Valid(store,visitor);if(input.RequestId==Guid.Empty||!input.Consent||!Text(input.Name,80)||input.Contact?.Length>100||input.Contact?.Any(char.IsControl)==true)throw AssistantException.Invalid();
        return Run((db,tx)=>{if(sessionExpiry<=clock.GetUtcNow())throw SessionExpired();RequireVisitor(db,tx,store,visitor);RequireEnabled(db,tx,store);var hash=Hash(new{input.Name,input.Contact,input.Consent});
            using(var cmd=Command(db,tx,"SELECT Hash,Conversation FROM CskhStarts WHERE Store=$store AND Visitor=$visitor AND Request=$request",("$store",store),("$visitor",visitor.ToString()),("$request",input.RequestId.ToString())))
            using(var r=cmd.ExecuteReader()){if(r.Read()){if(r.GetString(0)!=hash)throw Conflict();var id=Guid.Parse(r.GetString(1));r.Close();return Read(db,tx,store,id,visitor);}}
            if((long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhConversations WHERE Store=$store",("$store",store))!>=options.CurrentValue.MaximumConversationsPerStore)throw new AssistantException(503,"customer_capacity","Hộp thư đang đầy. Vui lòng liên hệ cửa hàng trực tiếp.");
            if((long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhConversations WHERE Store=$store AND Visitor=$visitor AND Mode!='closed'",("$store",store),("$visitor",visitor.ToString()))!>=3)throw new AssistantException(409,"customer_active_limit","Mình đang có hội thoại chưa kết thúc. Tiếp tục hội thoại hiện tại giúp em.");
            var next=Guid.NewGuid();Execute(db,tx,"INSERT INTO CskhConversations(Id,Store,Visitor,Name,Contact,Mode,Version,Created,Updated,Expires,Unread) VALUES($id,$store,$visitor,$name,$contact,'bot',1,$now,$now,$expires,0)",("$id",next.ToString()),("$store",store),("$visitor",visitor.ToString()),("$name",input.Name.Trim()),("$contact",string.IsNullOrWhiteSpace(input.Contact)?null:input.Contact.Trim()),("$now",Now),("$expires",clock.GetUtcNow().AddDays(options.CurrentValue.RetentionDays).ToUnixTimeMilliseconds()));
            Execute(db,tx,"INSERT INTO CskhStarts VALUES($store,$visitor,$request,$hash,$id)",("$store",store),("$visitor",visitor.ToString()),("$request",input.RequestId.ToString()),("$hash",hash),("$id",next.ToString()));
            if(Scalar(db,tx,"SELECT Expires FROM CskhVisitorSessions WHERE Store=$store AND Visitor=$visitor",("$store",store),("$visitor",visitor.ToString())) is long visitorUntil)
                Execute(db,tx,"INSERT INTO CskhConversationSessions VALUES($id,$expires)",("$id",next.ToString()),("$expires",visitorUntil));
            Add(db,tx,next,"system","Xin chào! Em là trợ lý tự động của cửa hàng. Mình có thể hỏi sản phẩm, giá lẻ, chính sách hoặc yêu cầu gặp nhân viên.");return Read(db,tx,store,next,visitor);
        });
    }
    public CustomerConversation Get(int store,Guid visitor,Guid id)=>Run((db,tx)=>{RequireVisitor(db,tx,store,visitor);RequireEnabled(db,tx,store);return Read(db,tx,store,id,visitor);});
    public CustomerConversation[] Mine(int store,Guid visitor)=>Run((db,tx)=>{RequireVisitor(db,tx,store,visitor);RequireEnabled(db,tx,store);var ids=Ids(db,tx,"SELECT Id FROM CskhConversations WHERE Store=$store AND Visitor=$visitor AND Expires>$now ORDER BY Updated DESC LIMIT 20",("$store",store),("$visitor",visitor.ToString()),("$now",Now));return ids.Select(id=>Read(db,tx,store,id,visitor,includeMessages:false)).ToArray();});
    private static Guid[] Ids(SqliteConnection db,SqliteTransaction tx,string sql,params (string,object?)[] args) {using var cmd=Command(db,tx,sql,args);using var r=cmd.ExecuteReader();var ids=new List<Guid>();while(r.Read())ids.Add(Guid.Parse(r.GetString(0)));return ids.ToArray();}
    public CustomerTurn Begin(int store,Guid visitor,Guid id,CustomerSend input,string? handoffReason=null)=>Run((db,tx)=>{
        RequireVisitor(db,tx,store,visitor);RequireEnabled(db,tx,store);if(!Text(input.Message,2000))throw AssistantException.Invalid();var c=Read(db,tx,store,id,visitor);
        if(Replay(db,tx,id,input.RequestId,"visitor:"+visitor,new{action="message",input.Message}))return new(c,false,"",input.RequestId);
        if(c.Mode=="closed")throw new AssistantException(409,"customer_conversation_closed","Hội thoại đã kết thúc. Mình bắt đầu hội thoại mới nhé.");
        if(Scalar(db,tx,"SELECT ProcessingId FROM CskhConversations WHERE Id=$id",("$id",id.ToString())) is string)throw new AssistantException(409,"customer_busy","Lượt trước đang xử lý. Đợi phản hồi hoặc chọn gặp nhân viên.");
        Add(db,tx,id,"customer",input.Message.Trim());var ai=c.Mode=="bot"&&handoffReason is null;
        Execute(db,tx,"UPDATE CskhConversations SET Mode=$mode,Reason=$reason,Version=Version+1,Updated=$now,Unread=Unread+1,ProcessingId=$request,ProcessingUntil=$until WHERE Id=$id",("$mode",handoffReason is not null&&c.Mode=="bot"?"waiting":c.Mode),("$reason",handoffReason??c.HandoffReason),("$now",Now),("$request",ai?input.RequestId.ToString():null),("$until",ai?Now+120000:null),("$id",id.ToString()));
        if(handoffReason is not null&&c.Mode=="bot")Add(db,tx,id,"system","Đã gửi yêu cầu gặp nhân viên. Cửa hàng sẽ trả lời tại đây khi có người tiếp nhận.");
        var state=ai?Scalar(db,tx,"SELECT AiState FROM CskhConversations WHERE Id=$id",("$id",id.ToString())) as string??"":"";
        return new CustomerTurn(Read(db,tx,store,id,visitor),ai,state,input.RequestId);
    });
    public bool BridgeAllowed(CustomerBridgeIdentity who)=>Run((db,tx)=>{
        if(!Settings(db,tx,who.StoreId).Enabled||!VisitorAllowed(db,tx,who.StoreId,who.VisitorId))return false;
        return (long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhConversations WHERE Id=$id AND Store=$store AND Visitor=$visitor AND Mode='bot' AND ProcessingId=$turn AND ProcessingUntil>$now AND Expires>$now",("$id",who.ConversationId.ToString()),("$store",who.StoreId),("$visitor",who.VisitorId.ToString()),("$turn",who.TurnId.ToString()),("$now",Now))! ==1;
    });
    public CustomerConversation Complete(CustomerBridgeIdentity who,string? reply,string? state,string? failure,string? assistantPolicyVersion=null,string? assistantSettingsVersion=null)=>Run((db,tx)=>{
        var c=Read(db,tx,who.StoreId,who.ConversationId,who.VisitorId);
        if(c.Mode!="bot"||Scalar(db,tx,"SELECT ProcessingId FROM CskhConversations WHERE Id=$id",("$id",who.ConversationId.ToString())) as string!=who.TurnId.ToString())return c;
        if(failure is null&&(!Text(reply,24000)||state?.Length>262144||state is null))failure="invalid_ai_response";
        if(failure is null){Add(db,tx,c.Id,"assistant",reply!,channelKind:"assistant",assistantPolicyVersion:assistantPolicyVersion,assistantSettingsVersion:assistantSettingsVersion);Execute(db,tx,"UPDATE CskhConversations SET AiState=$state,Version=Version+1,Updated=$now,ProcessingId=NULL,ProcessingUntil=NULL WHERE Id=$id",("$state",state),("$now",Now),("$id",c.Id.ToString()));}
        else{if(failure=="needs_human"&&Text(reply,24000))Add(db,tx,c.Id,"assistant",reply!,channelKind:"handoff");else if(c.Source!="web")Add(db,tx,c.Id,"assistant","Cửa hàng đã nhận yêu cầu. Trợ lý đang chuyển sang nhân viên để hỗ trợ mình tại đây.",channelKind:"handoff");Add(db,tx,c.Id,"system","Trợ lý chưa thể trả lời chắc chắn. Hội thoại đã được chuyển sang nhân viên; mình có thể để lại thông tin cần hỗ trợ.");Execute(db,tx,"UPDATE CskhConversations SET Mode='waiting',Reason=$reason,Version=Version+1,Updated=$now,Unread=Unread+1,ProcessingId=NULL,ProcessingUntil=NULL WHERE Id=$id",("$reason",failure),("$now",Now),("$id",c.Id.ToString()));}
        Execute(db,tx,"UPDATE CskhChannelInbox SET State='done' WHERE Id=$turn",("$turn",who.TurnId.ToString()));
        return Read(db,tx,who.StoreId,c.Id,who.VisitorId);
    });
    public CustomerConversation Handoff(int store,Guid visitor,Guid id,Guid request)=>Run((db,tx)=>{
        RequireVisitor(db,tx,store,visitor);RequireEnabled(db,tx,store);var c=Read(db,tx,store,id,visitor);if(Replay(db,tx,id,request,"visitor:"+visitor,new{action="handoff"}))return c;
        if(c.Mode=="closed")throw Conflict();if(c.Mode=="bot"){Execute(db,tx,"UPDATE CskhConversations SET Mode='waiting',Reason='customer_request',Version=Version+1,Updated=$now,Unread=Unread+1,ProcessingId=NULL,ProcessingUntil=NULL WHERE Id=$id",("$now",Now),("$id",id.ToString()));Add(db,tx,id,"system","Đã gửi yêu cầu gặp nhân viên. Cửa hàng sẽ trả lời tại đây khi có người tiếp nhận.");}return Read(db,tx,store,id,visitor);
    });
    public CustomerConversation[] Inbox(CustomerStaff staff,string mode,int limit=50,int page=0,string source="all")=>Run((db,tx)=>{
        if(source is not ("all" or "web" or "facebook" or "zalomarketing" or "zalo-direct" or "zalo-group")||mode is not ("all" or "waiting" or "human" or "bot" or "closed")||limit is <1 or >100||page is <0 or >200)throw AssistantException.Invalid();
        var ids=Ids(db,tx,"""
            SELECT c.Id FROM CskhConversations c LEFT JOIN CskhChannelThreads t ON t.Conversation=c.Id
            LEFT JOIN CskhChannelThreadDetails d ON d.Conversation=c.Id
            WHERE c.Store=$store AND c.Expires>$now AND ($mode='all' OR $mode='waiting' AND (c.Mode='waiting' OR c.Mode='bot' AND c.ProcessingId IS NOT NULL AND c.ProcessingUntil<=$now) OR $mode='bot' AND c.Mode='bot' AND (c.ProcessingId IS NULL OR c.ProcessingUntil>$now) OR $mode NOT IN('waiting','bot') AND c.Mode=$mode) AND
            ($source='all' OR $source='web' AND t.Conversation IS NULL OR $source='facebook' AND t.Provider='facebook'
             OR $source='zalomarketing' AND t.Provider='zalomarketing'
             OR $source='zalo-direct' AND t.Provider='zalomarketing' AND COALESCE(d.Kind,'Direct')='Direct'
             OR $source='zalo-group' AND t.Provider='zalomarketing' AND d.Kind='Group')
            ORDER BY CASE c.Mode WHEN 'waiting' THEN 0 WHEN 'human' THEN 1 ELSE 2 END,c.Updated DESC,c.Id LIMIT $limit OFFSET $skip
            """,("$store",staff.StoreId),("$mode",mode),("$source",source),("$limit",limit),("$skip",page*50),("$now",Now));return ids.Select(id=>Read(db,tx,staff.StoreId,id,includeMessages:false)).ToArray();
    });
    public CustomerConversation StaffGet(CustomerStaff staff,Guid id)=>Run((db,tx)=>Read(db,tx,staff.StoreId,id));
    public CustomerConversation StaffCommand(CustomerStaff staff,Guid id,CustomerCommand input)=>Run((db,tx)=>{
        var c=Read(db,tx,staff.StoreId,id);if(Replay(db,tx,id,input.RequestId,"agent:"+staff.UserId,new{input.Action,input.Version,input.Message}))return c;
        if(c.Version!=input.Version)throw Conflict();if(input.Action is "claim" or "reply" && !staff.CanReply)throw AssistantException.Denied();
        if(input.Action=="claim") {
            if(c.Mode=="closed"||c.Owner is not null&&c.Owner!=staff.UserId)throw Conflict();
            Execute(db,tx,"UPDATE CskhConversations SET Mode='human',Owner=$owner,OwnerName=$name,Version=Version+1,Updated=$now,Unread=0,ProcessingId=NULL,ProcessingUntil=NULL WHERE Id=$id",("$owner",staff.UserId),("$name",staff.Name),("$now",Now),("$id",id.ToString()));Add(db,tx,id,"system",staff.Name+" đã tiếp nhận hội thoại.");
        } else if(input.Action=="reply") {
            if(c.Mode!="human"||c.Owner!=staff.UserId||!Text(input.Message,4000))throw Conflict();Add(db,tx,id,"agent",input.Message!.Trim(),staff.Name,staff.UserId);
            Execute(db,tx,"UPDATE CskhConversations SET Version=Version+1,Updated=$now,Unread=0 WHERE Id=$id",("$now",Now),("$id",id.ToString()));
        } else if(input.Action is "release" or "close" or "bot") {
            if(c.Mode=="closed"||c.Owner!=staff.UserId&&!staff.CanManage)throw AssistantException.Denied();
            if(input.Action=="bot"&&c.ConversationKind=="Group")throw new AssistantException(409,"customer_group_staff_only","Hội thoại nhóm do nhân viên hỗ trợ.");
            var next=input.Action=="close"?"closed":input.Action=="bot"?"bot":"waiting";
            Execute(db,tx,"UPDATE CskhConversations SET Mode=$mode,Owner=NULL,OwnerName=NULL,Version=Version+1,Updated=$now,Unread=0,AiState='',ProcessingId=NULL,ProcessingUntil=NULL WHERE Id=$id",("$mode",next),("$now",Now),("$id",id.ToString()));
            Add(db,tx,id,"system",input.Action=="close"?"Nhân viên đã kết thúc hội thoại. Cảm ơn mình đã liên hệ cửa hàng.":input.Action=="bot"?"Nhân viên đã chuyển lại trợ lý tự động. Mình nêu lại sản phẩm hoặc câu hỏi để em hỗ trợ nhé.":"Hội thoại được trả lại hàng chờ nhân viên.");
        }else throw AssistantException.Invalid();
        if(input.Action!="reply")Execute(db,tx,"UPDATE CskhChannelInbox SET State='done' WHERE Conversation=$id AND State IN('pending','processing')",("$id",id.ToString()));
        return Read(db,tx,staff.StoreId,id);
    });
}
