using System.Diagnostics;
using System.Text.Json;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace GaoApp.Tests.Security;

public sealed class CustomerServiceStorageBoundsTests(Xunit.Abstractions.ITestOutputHelper output):IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"GaoC4",Guid.NewGuid().ToString("N"));
    private readonly AssistantIntegrationTests.FixedClock clock=new();
    private readonly AssistantIntegrationTests.Monitor<CustomerServiceOptions> options=new(new(){DatabasePath="test.db",VisitorSessionsPerMinutePerStore=10000,VisitorSessionsPerMinuteTotal=100000});
    private readonly EphemeralDataProtectionProvider protector=new();
    private readonly CustomerStaff staff=new(1,1,"TEST staff",true,true,true);
    private CustomerServiceStore Store()=>new(new AssistantIntegrationTests.EnvironmentStub(directory),options,clock);
    private CustomerSession Sessions(CustomerServiceStore s)=>new(protector,clock,options,new AssistantIntegrationTests.EnvironmentStub(directory),s);
    private CustomerTicket Ticket(int store=1)=>new(Guid.NewGuid(),store,clock.Now.AddDays(7));
    private SqliteConnection Open(){var db=new SqliteConnection("Data Source="+Path.Combine(directory,"test.db")+";Pooling=False;Foreign Keys=True");db.Open();return db;}
    private long Count(string table){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM "+table;return (long)cmd.ExecuteScalar()!;}
    private static void Execute(SqliteConnection db,string sql,params (string,object)[] args){using var cmd=db.CreateCommand();cmd.CommandText=sql;foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value);cmd.ExecuteNonQuery();}
    private DefaultHttpContext Cookie(CustomerTicket ticket) {var h=new DefaultHttpContext();h.Request.Scheme="https";h.Request.Headers.Cookie="GaoApp.Cskh.Visitor="+protector.CreateProtector("GaoApp.CustomerService.Visitor.v1").Protect(JsonSerializer.Serialize(ticket));return h;}

    [Fact]public void New_cookie_reserves_space_before_emission_and_ended_sessions_keep_their_slot() {
        options.CurrentValue.MaximumVisitorSessionsPerStore=2;options.CurrentValue.MaximumVisitorSessionsTotal=3;
        var s=Store();var sessions=Sessions(s);var first=new DefaultHttpContext();first.Request.Scheme="https";var t=sessions.Read(first,1,true);
        Assert.Contains("GaoApp.Cskh.Visitor",first.Response.Headers.SetCookie.ToString());Assert.Equal(1,Count("CskhVisitorSessions"));
        var second=Ticket();s.RegisterVisitorSession(second);sessions.End(Cookie(t),1);
        Assert.True(s.Revoked(1,t.VisitorId));Assert.Equal(2,s.StorageStatus().ReservedSessions);
        var denied=new DefaultHttpContext();denied.Request.Scheme="https";Assert.Equal(429,Assert.Throws<AssistantException>(()=>sessions.Read(denied,1,true)).Status);Assert.Equal(0,denied.Response.Headers.SetCookie.Count);
        Assert.Equal(second,sessions.Read(Cookie(second),1));sessions.End(Cookie(second),1);Assert.True(s.Revoked(1,second.VisitorId));
        Assert.Equal(401,Assert.Throws<AssistantException>(()=>sessions.Read(Cookie(t),1)).Status);Assert.Equal(2,Count("CskhVisitorSessions"));
    }
    [Fact]public void Three_hundred_visitors_without_any_conversation_cannot_exceed_the_configured_capacity() {
        options.CurrentValue.MaximumVisitorSessionsPerStore=300;options.CurrentValue.MaximumVisitorSessionsTotal=400;var s=Store();
        for(var i=0;i<300;i++){var ticket=Ticket();s.RegisterVisitorSession(ticket);s.Revoke(ticket);}
        Assert.Equal(300,Count("CskhVisitorSessions"));Assert.Equal(300,Count("CskhRevoked"));Assert.Equal(0,Count("CskhConversations"));Assert.Equal(300,s.StorageStatus().ReservedSessions);
        Assert.Equal("customer_session_capacity",Assert.Throws<AssistantException>(()=>Store().RegisterVisitorSession(Ticket())).Code);
    }
    [Fact]public void Store_and_global_capacity_are_independent_and_survive_a_new_store_instance() {
        options.CurrentValue.MaximumVisitorSessionsPerStore=2;options.CurrentValue.MaximumVisitorSessionsTotal=3;var s=Store();
        s.RegisterVisitorSession(Ticket());s.RegisterVisitorSession(Ticket());Assert.Throws<AssistantException>(()=>Store().RegisterVisitorSession(Ticket()));
        Store().RegisterVisitorSession(Ticket(2));Assert.Throws<AssistantException>(()=>Store().RegisterVisitorSession(Ticket(3)));Assert.Equal(3,Store().StorageStatus().ReservedSessions);
    }
    [Fact]public async Task Concurrent_admissions_cannot_overbook_the_last_slot() {
        options.CurrentValue.MaximumVisitorSessionsPerStore=1;options.CurrentValue.MaximumVisitorSessionsTotal=1;Store().Settings(1);
        var results=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>Task.Run(()=>{try{Store().RegisterVisitorSession(Ticket());return 200;}catch(AssistantException e){return e.Status;}})));
        Assert.Single(results,c=>c==200);Assert.Equal(11,results.Count(c=>c==429));Assert.Equal(1,Store().StorageStatus().ReservedSessions);
    }
    [Fact]public void Issuance_rate_is_shared_across_restart_and_logout_does_not_reset_it() {
        options.CurrentValue.VisitorSessionsPerMinutePerStore=2;options.CurrentValue.VisitorSessionsPerMinuteTotal=3;var s=Store();var t=Ticket();s.RegisterVisitorSession(t);s.Revoke(t);s.RegisterVisitorSession(Ticket());
        Assert.Equal("customer_session_rate_limit",Assert.Throws<AssistantException>(()=>Store().RegisterVisitorSession(Ticket())).Code);
        Store().RegisterVisitorSession(Ticket(2));Assert.Equal("customer_session_rate_limit",Assert.Throws<AssistantException>(()=>Store().RegisterVisitorSession(Ticket(3))).Code);
        clock.Now=clock.Now.AddSeconds(61);Store().RegisterVisitorSession(Ticket());Assert.Equal(4,Store().StorageStatus().ReservedSessions);
    }
    [Fact]public void Legacy_valid_cookies_are_registered_and_legacy_revoked_cookies_never_revive() {
        var s=Store();s.Settings(1);var active=Ticket();var ended=Ticket();
        using(var db=Open())Execute(db,"INSERT INTO CskhRevoked VALUES(1,$id,$expires)",("$id",ended.VisitorId.ToString()),("$expires",ended.ExpiresAt.ToUnixTimeMilliseconds()));
        Assert.Equal(active,Sessions(s).Read(Cookie(active),1));Assert.Equal(1,Count("CskhVisitorSessions"));Assert.Equal(2,s.StorageStatus().ReservedSessions);
        Assert.Equal(401,Assert.Throws<AssistantException>(()=>Sessions(Store()).Read(Cookie(ended),1)).Status);
        Sessions(Store()).End(Cookie(active),1);Sessions(Store()).End(Cookie(active),1);Assert.Equal(2,Store().StorageStatus().ReservedSessions);
        Assert.Equal(401,Assert.Throws<AssistantException>(()=>Sessions(Store()).Read(Cookie(active),1)).Status);
    }
    [Fact]public void Existing_unexpired_revocations_are_preserved_even_if_the_old_database_already_exceeds_the_new_limit() {
        var s=Store();s.Settings(1);var ended=Enumerable.Range(0,5).Select(_=>Ticket()).ToArray();using(var db=Open())foreach(var t in ended)Execute(db,"INSERT INTO CskhRevoked VALUES(1,$id,$expires)",("$id",t.VisitorId.ToString()),("$expires",t.ExpiresAt.ToUnixTimeMilliseconds()));
        options.CurrentValue.MaximumVisitorSessionsPerStore=2;options.CurrentValue.MaximumVisitorSessionsTotal=2;
        Assert.Throws<AssistantException>(()=>s.RegisterVisitorSession(Ticket()));s.Purge();Assert.Equal(5,Count("CskhRevoked"));Assert.All(ended,t=>Assert.True(Store().Revoked(1,t.VisitorId)));
    }
    [Fact]public void Failed_legacy_admission_does_not_claim_logout_or_remove_the_cookie() {
        options.CurrentValue.MaximumVisitorSessionsPerStore=1;options.CurrentValue.MaximumVisitorSessionsTotal=1;var s=Store();s.RegisterVisitorSession(Ticket());var old=Cookie(Ticket());
        Assert.Equal(429,Assert.Throws<AssistantException>(()=>Sessions(s).End(old,1)).Status);Assert.Equal(0,old.Response.Headers.SetCookie.Count);Assert.Equal(1,s.StorageStatus().ReservedSessions);
    }
    [Fact]public void Expiration_can_free_capacity_but_never_makes_old_signed_cookies_usable() {
        options.CurrentValue.MaximumVisitorSessionsPerStore=1;options.CurrentValue.MaximumVisitorSessionsTotal=1;var s=Store();var t=Ticket();s.RegisterVisitorSession(t);s.Revoke(t);clock.Now=clock.Now.AddDays(8);
        Assert.Equal(401,Assert.Throws<AssistantException>(()=>Sessions(Store()).Read(Cookie(t),1)).Status);Store().RegisterVisitorSession(Ticket());Assert.Equal(1,Store().StorageStatus().ReservedSessions);
        Assert.Equal(401,Assert.Throws<AssistantException>(()=>Sessions(Store()).Read(Cookie(t),1)).Status);
    }
    [Fact]public void Delayed_cleanup_does_not_expose_expired_transcripts_feedback_or_the_AI_bridge() {
        var s=Store();s.Configure(1,true,0);var t=Ticket();s.RegisterVisitorSession(t);var c=s.Start(1,t.VisitorId,new(Guid.NewGuid(),"TEST",null,true));var send=new CustomerSend(Guid.NewGuid(),"TEST sản phẩm");s.Begin(1,t.VisitorId,c.Id,send);
        clock.Now=clock.Now.AddDays(8);Assert.Equal(1,Count("CskhConversations"));Assert.Empty(s.Inbox(staff,"all"));Assert.Equal(404,Assert.Throws<AssistantException>(()=>s.StaffGet(staff,c.Id)).Status);
        Assert.False(s.BridgeAllowed(new(t.VisitorId,1,c.Id,send.RequestId)));Assert.Equal(401,Assert.Throws<AssistantException>(()=>s.Get(1,t.VisitorId,c.Id)).Status);Assert.Equal(1,Count("CskhConversations"));
    }
    [Fact]public void Logout_fences_requests_whose_cookie_was_validated_before_the_revoke_transaction() {
        var s=Store();s.Configure(1,true,0);var t=Ticket();s.RegisterVisitorSession(t);Assert.True(s.AcceptVisitorSession(t));s.Revoke(t);
        Assert.Equal(401,Assert.Throws<AssistantException>(()=>s.Start(1,t.VisitorId,new(Guid.NewGuid(),"TEST stale request",null,true))).Status);Assert.Equal(0,Count("CskhConversations"));
    }
    [Fact]public void Ordinary_read_checks_neither_delete_expired_rows_nor_wait_for_an_unrelated_writer() {
        var s=Store();s.Configure(1,true,0);using(var db=Open()){Execute(db,"INSERT INTO CskhRevoked VALUES(1,$id,1)",("$id",Guid.NewGuid().ToString()));using var tx=db.BeginTransaction();using var update=db.CreateCommand();update.Transaction=tx;update.CommandText="UPDATE CskhSettings SET Version=Version+1 WHERE Store=1";update.ExecuteNonQuery();
            Assert.True(s.Settings(1).Enabled);Assert.False(s.Revoked(1,Guid.NewGuid()));tx.Rollback();}
        Assert.Equal(1,Count("CskhRevoked"));
    }
    [Fact]public void Each_cleanup_transaction_limits_rows_and_resumes_after_restart() {
        options.CurrentValue.CleanupBatchSize=3;var s=Store();s.Settings(1);
        using(var db=Open())for(var i=0;i<10;i++)Execute(db,"INSERT INTO CskhRevoked VALUES(1,$id,1)",("$id",Guid.NewGuid().ToString()));
        var result=s.CleanupBatch();Assert.Equal(7,Count("CskhRevoked"));Assert.True(result.More);Assert.Equal(3,result.Changed);
        for(var i=0;i<4;i++)Store().CleanupBatch();Assert.Equal(0,Count("CskhRevoked"));
    }
    [Fact]public void A_cleanup_batch_deletes_only_one_expired_transcript_and_keeps_active_data() {
        var s=Store();s.Configure(1,true,0);var ids=Enumerable.Range(0,3).Select(_=>s.Start(1,Guid.NewGuid(),new(Guid.NewGuid(),"TEST expired",null,true)).Id).ToArray();clock.Now=clock.Now.AddDays(8);
        var fresh=s.Start(1,Guid.NewGuid(),new(Guid.NewGuid(),"TEST active",null,true));Assert.Equal(4,Count("CskhConversations"));var r=s.CleanupBatch();Assert.Equal(1,r.ExpiredConversations);Assert.Equal(3,Count("CskhConversations"));Assert.Equal(fresh.Id,Assert.Single(s.Inbox(staff,"all")).Id);
    }
    [Fact]public void Expiry_and_gallery_queries_use_the_intended_indexes() {
        Store().Settings(1);using var db=Open();
        foreach(var (sql,index) in new[]{("SELECT rowid FROM CskhRevoked WHERE Expires<=0 ORDER BY Expires LIMIT 100","IX_CskhRevokedExpiry"),("SELECT rowid FROM CskhEvaluationRequests WHERE Expires<=0 ORDER BY Expires LIMIT 100","IX_CskhEvaluationRequestExpiry"),("SELECT Id FROM CskhChannelHistoryMedia WHERE Conversation='TEST' ORDER BY Observed,Id LIMIT 50","IX_CskhHistoryTimeline"),("SELECT rowid FROM CskhVisitorSessions WHERE Expires<=0 ORDER BY Expires LIMIT 100","IX_CskhVisitorExpiry")}) {
            using var cmd=db.CreateCommand();cmd.CommandText="EXPLAIN QUERY PLAN "+sql;using var reader=cmd.ExecuteReader();var plan="";while(reader.Read())plan+=reader.GetString(3);Assert.Contains(index,plan);Assert.DoesNotContain("USE TEMP B-TREE",plan);
        }
    }
    [Fact]public void Union_counters_follow_ledger_and_revocation_cleanup_in_either_order() {
        var s=Store();var t=Ticket();s.RegisterVisitorSession(t);s.Revoke(t);Assert.Equal(1,s.StorageStatus().ReservedSessions);
        using(var db=Open()){Execute(db,"DELETE FROM CskhVisitorSessions");Assert.Equal(1,s.StorageStatus().ReservedSessions);Execute(db,"DELETE FROM CskhRevoked");}Assert.Equal(0,s.StorageStatus().ReservedSessions);
        var next=Ticket();s.RegisterVisitorSession(next);s.Revoke(next);using(var db=Open()){Execute(db,"DELETE FROM CskhRevoked");Assert.Equal(1,s.StorageStatus().ReservedSessions);Execute(db,"DELETE FROM CskhVisitorSessions");}Assert.Equal(0,s.StorageStatus().ReservedSessions);
    }
    [Fact]public void Capacity_options_reject_zero_invalid_windows_and_total_below_store() {
        var o=new CustomerServiceOptions();Assert.True(o.Valid());o.MaximumVisitorSessionsTotal=1;Assert.False(o.Valid());o=new(){CleanupBatchSize=0};Assert.False(o.Valid());o=new(){VisitorSessionsPerMinuteTotal=1};Assert.False(o.Valid());
    }
    [Fact]public void Twenty_thousand_session_records_do_not_turn_cookie_reads_into_global_cleanup() {
        var s=Store();s.Settings(1);using(var db=Open())using(var tx=db.BeginTransaction()) {
            using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO CskhRevoked VALUES(1,$id,$expires)";var id=cmd.Parameters.Add("$id",SqliteType.Text);var expires=cmd.Parameters.Add("$expires",SqliteType.Integer);
            for(var i=0;i<20000;i++){id.Value=Guid.NewGuid().ToString();expires.Value=i<10000?1:clock.Now.AddDays(7).ToUnixTimeMilliseconds();cmd.ExecuteNonQuery();}tx.Commit();
        }
        var watch=Stopwatch.StartNew();for(var i=0;i<100;i++)Assert.False(s.Revoked(1,Guid.NewGuid()));watch.Stop();var reads=watch.Elapsed.TotalMilliseconds;
        Assert.Equal(20000,Count("CskhRevoked"));watch.Restart();var cleaned=s.CleanupBatch();watch.Stop();Assert.Equal(100,cleaned.Changed);Assert.Equal(19900,Count("CskhRevoked"));
        output.WriteLine($"Synthetic 20,000 rows: 100 indexed checks = {reads:F2} ms; one 100-row cleanup = {watch.Elapsed.TotalMilliseconds:F2} ms. Local evidence, not a production latency guarantee.");
    }
    [Fact]public void Unregistered_new_format_cookies_cannot_be_recreated_as_legacy_sessions() {
        var s=Store();var unknown=Ticket() with{Version=2};Assert.False(s.AcceptVisitorSession(unknown));Assert.Equal(0,Count("CskhVisitorSessions"));
        s.RegisterVisitorSession(unknown);Assert.True(Store().AcceptVisitorSession(unknown));using(var db=Open())Execute(db,"DELETE FROM CskhVisitorSessions");Assert.False(Store().AcceptVisitorSession(unknown));
    }
    [Fact]public void Legacy_cookie_migration_window_is_persistent_and_does_not_reopen_after_restart() {
        var s=Store();Assert.True(s.AcceptVisitorSession(Ticket()));clock.Now=clock.Now.AddDays(8);
        Assert.False(Store().AcceptVisitorSession(Ticket()));var fresh=Ticket() with{Version=2};Store().RegisterVisitorSession(fresh);Assert.True(Store().AcceptVisitorSession(fresh));
    }
    [Fact]public void Expired_cookie_stays_fenced_after_ledger_cleanup_while_staff_history_keeps_its_retention() {
        var s=Store();s.Configure(1,true,0);var t=Ticket() with{Version=2};s.RegisterVisitorSession(t);clock.Now=clock.Now.AddDays(6);
        var c=s.Start(1,t.VisitorId,new(Guid.NewGuid(),"TEST late conversation",null,true),t.ExpiresAt);clock.Now=t.ExpiresAt.AddSeconds(-1);var request=new CustomerSend(Guid.NewGuid(),"TEST product");s.Begin(1,t.VisitorId,c.Id,request);
        clock.Now=clock.Now.AddSeconds(2);s.CleanupBatch();Assert.Equal(0,Count("CskhVisitorSessions"));Assert.Equal(1,Count("CskhConversations"));
        Assert.False(s.BridgeAllowed(new(t.VisitorId,1,c.Id,request.RequestId)));Assert.Equal(401,Assert.Throws<AssistantException>(()=>s.Get(1,t.VisitorId,c.Id)).Status);Assert.Equal(c.Id,s.StaffGet(staff,c.Id).Id);
        Assert.Equal(401,Assert.Throws<AssistantException>(()=>s.Start(1,Guid.NewGuid(),new(Guid.NewGuid(),"TEST delayed request",null,true),t.ExpiresAt)).Status);
    }
    [Fact]public void A_pre_upgrade_cookie_without_a_version_property_keeps_its_identity() {
        var t=Ticket();var http=new DefaultHttpContext();http.Request.Scheme="https";
        var legacy=JsonSerializer.Serialize(new{t.VisitorId,t.StoreId,t.ExpiresAt});http.Request.Headers.Cookie="GaoApp.Cskh.Visitor="+protector.CreateProtector("GaoApp.CustomerService.Visitor.v1").Protect(legacy);
        var read=Sessions(Store()).Read(http,1);Assert.Equal(t.VisitorId,read.VisitorId);Assert.Equal(1,read.Version);Assert.Equal(1,Count("CskhVisitorSessions"));
    }
    [Fact]public void Schema_seven_upgrade_adds_empty_accounting_and_preserves_existing_transcripts_and_revocations() {
        var s=Store();s.Configure(1,true,0);var visitor=Guid.NewGuid();var c=s.Start(1,visitor,new(Guid.NewGuid(),"TEST legacy",null,true));var revoked=Ticket();
        using(var db=Open()) {
            Execute(db,"INSERT INTO CskhRevoked VALUES(1,$id,$expires)",("$id",revoked.VisitorId.ToString()),("$expires",revoked.ExpiresAt.ToUnixTimeMilliseconds()));
            Execute(db,"DROP TRIGGER CskhSessionInserted;DROP TRIGGER CskhSessionDeleted;DROP TRIGGER CskhRevokedInserted;DROP TRIGGER CskhRevokedDeleted;DROP TRIGGER CskhSessionKeyImmutable;DROP TRIGGER CskhRevokedKeyImmutable;DROP TABLE CskhConversationSessions;DROP TABLE CskhVisitorSessions;DROP TABLE CskhSessionUsage;PRAGMA user_version=7;");
        }
        var before=AssistantStorageMaintenance.Tables(Path.Combine(directory,"test.db"));var next=Store();Assert.True(next.Settings(1).Enabled);
        var after=AssistantStorageMaintenance.Tables(Path.Combine(directory,"test.db"));foreach(var (name,evidence) in before)Assert.Equal(evidence,after[name]);
        Assert.Equal(0,after["CskhVisitorSessions"].Rows);Assert.Equal(0,after["CskhSessionUsage"].Rows);Assert.Equal(c.Id,next.StaffGet(staff,c.Id).Id);Assert.True(next.Revoked(1,revoked.VisitorId));
        using var check=Open();using var cmd=check.CreateCommand();cmd.CommandText="PRAGMA user_version";Assert.Equal(11L,cmd.ExecuteScalar());
    }
    public void Dispose(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
}
