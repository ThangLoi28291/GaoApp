using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Web.HealthChecks;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Tests.Security;

public sealed class AssistantStorageContinuityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GaoC1", Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(root, "old-web", "App_Data", "Assistant");
    private string SourceDb => Path.Combine(Source, "customer-service.db");
    private readonly AssistantIntegrationTests.FixedClock clock = new();
    private readonly CustomerStaff staff = new(1, 1, "TEST staff", true, true, true);
    private readonly IDataProtector protector=new EphemeralDataProtectionProvider().CreateProtector("C1-test");
    private CustomerIdentityIssue? proof;
    private string? proofCode;
    public AssistantStorageContinuityTests() { Directory.CreateDirectory(Source); Directory.CreateDirectory(Path.Combine(root,"new-web")); }
    private AssistantIntegrationTests.EnvironmentStub Env(string directory, bool production = false) => new(directory)
    { EnvironmentName = production ? "Production" : "Development", WebRootPath = Path.Combine(directory,"wwwroot") };
    private CustomerServiceStore SourceStore() => new(Env(Path.Combine(root,"old-web")),new AssistantIntegrationTests.Monitor<CustomerServiceOptions>(new()),clock);
    private JsonElement Prepare(bool migrate = true, string name = "persistent") => JsonSerializer.SerializeToElement(AssistantStorageMaintenance.Prepare(new(
        migrate ? "migrate" : "initialize", DestinationDirectory:Path.Combine(root,name),SourceDatabase:migrate?SourceDb:"",SourceSettingsDirectory:migrate?Source:"",WritersQuiesced:true)));
    private static string Property(JsonElement value,string key) => value.GetProperty(key).GetString()!;
    private IConfiguration Config(JsonElement prepared) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Assistant:StorageId"]=Property(prepared,"storageId"),["Assistant:StoreSettingsDirectory"]=Property(prepared,"settingsDirectory"),
        ["CustomerService:DatabasePath"]=Property(prepared,"databasePath"),["Storage:UploadRoot"]=Path.Combine(root,"uploads")}).Build();
    private CustomerServiceStore Live(JsonElement prepared, string release = "new-web") => new(Env(Path.Combine(root,release),true),
        new AssistantIntegrationTests.Monitor<CustomerServiceOptions>(new(){DatabasePath=Property(prepared,"databasePath"),StorageId=Property(prepared,"storageId")}),clock);
    private Guid Seed()
    {
        var source=SourceStore();source.Configure(1,true,0);source.Configure(2,true,0);
        var chat=source.Start(1,Guid.NewGuid(),new(Guid.NewGuid(),"TEST customer",null,true));
        chat=source.StaffCommand(staff,chat.Id,new(Guid.NewGuid(),"claim",chat.Version));
        proof=new(Guid.NewGuid(),chat.Version,123,"in_person","TEST confirmed reference",true);
        proofCode=source.IssueIdentity(staff,chat.Id,proof,protector).Code;
        source.Start(2,Guid.NewGuid(),new(Guid.NewGuid(),"TEST other store",null,true));
        var channel=new CustomerChannelConnection{Id="zalo-c1-test",StoreId=1,Provider="zalomarketing",Enabled=true,AccountId=Guid.NewGuid().ToString(),BaseUrl="https://source.example.test/"};
        var inbound=source.ImportChannel(channel,new("c1-inbound","c1-peer","TEST incoming",clock.Now,"1"))!.Value;
        var row=source.StaffGet(staff,inbound);row=source.StaffCommand(staff,inbound,new(Guid.NewGuid(),"claim",row.Version));
        source.StaffCommand(staff,inbound,new(Guid.NewGuid(),"reply",row.Version,"TEST queued reply"));
        source.ChannelPoll(channel,4,true,true);source.PauseChannel(channel,true,source.ChannelState(channel).Version);
        source.Revoke(new(Guid.NewGuid(),1,clock.Now.AddDays(7)));
        File.WriteAllText(Path.Combine(Source,"store-1.json"),"{\"test\":\"settings\"}");
        File.WriteAllText(Path.Combine(Source,"store-1-policies.json"),"{\"test\":\"policy\"}");
        File.WriteAllText(Path.Combine(Source,"store-1-search-knowledge.json"),"{\"test\":\"knowledge\"}");
        return chat.Id;
    }

    [Fact] public void Legacy_WAL_migration_keeps_history_identity_outbox_receipts_settings_and_source_unchanged()
    {
        var conversation=Seed();
        using var writer=new SqliteConnection("Data Source="+SourceDb+";Pooling=False");writer.Open();
        using(var command=writer.CreateCommand()){command.CommandText="PRAGMA wal_autocheckpoint=0; UPDATE CskhConversations SET Name='TEST committed WAL' WHERE Id=$id";command.Parameters.AddWithValue("$id",conversation.ToString());command.ExecuteNonQuery();}
        Assert.True(File.Exists(SourceDb+"-wal"));
        var before=AssistantStorageMaintenance.Capture(SourceDb,Source);
        var prepared=Prepare();var after=AssistantStorageMaintenance.Capture(Property(prepared,"databasePath"),Property(prepared,"settingsDirectory"));
        Assert.Equal(before.Tables,after.Tables);Assert.Equal(before.Files,after.Files);
        Assert.Equal(before.Tables,AssistantStorageMaintenance.Tables(SourceDb));
        foreach(var table in new[]{"CskhIdentity","CskhIdentityAudit","CskhChannelOutbox","CskhChannelReceipts","CskhChannelControls","CskhRevoked"})Assert.True(after.Tables[table].Rows>0);
        Assert.True(File.Exists(Path.Combine(root,"persistent","source-backup","evidence.json")));
        AssistantStorageSafety.Validate(Env(Path.Combine(root,"new-web"),true),Config(prepared));
        Assert.Equal("TEST committed WAL",Live(prepared).StaffGet(staff,conversation).Name);
        Assert.Equal(proofCode,Live(prepared).IssueIdentity(staff,conversation,proof!,protector).Code);
        Assert.Equal(conversation,Live(prepared,"rollback-web").StaffGet(staff,conversation).Id);
        Assert.Equal(404,Assert.Throws<AssistantException>(()=>Live(prepared).StaffGet(staff with{StoreId=2},conversation)).Status);
    }

    [Fact] public void Initialize_is_explicit_and_never_overwrites_an_existing_destination()
    {
        Assert.Throws<InvalidOperationException>(()=>AssistantStorageMaintenance.Prepare(new("initialize",DestinationDirectory:Path.Combine(root,"new"))));
        var prepared=Prepare(false);AssistantStorageSafety.Validate(Env(Path.Combine(root,"new-web"),true),Config(prepared));
        Assert.Throws<IOException>(()=>Prepare(false));
        Assert.Empty(Live(prepared).Inbox(staff,"all"));
        Assert.Throws<ArgumentException>(()=>AssistantStorageMaintenance.Prepare(new("initialize",DestinationDirectory:Path.Combine(root,"bad"),SourceDatabase:SourceDb,WritersQuiesced:true)));
    }

    [Fact] public void Missing_legacy_or_corrupt_database_cannot_silently_initialize_a_replacement()
    {
        Assert.Throws<InvalidOperationException>(()=>Prepare());
        File.WriteAllText(SourceDb,"TEST NOT SQLITE");var original=File.ReadAllBytes(SourceDb);
        Assert.Throws<SqliteException>(()=>Prepare(name:"corrupt-target"));Assert.Equal(original,File.ReadAllBytes(SourceDb));
    }

    [Fact] public void Settings_only_upgrade_is_explicit_preserves_AI_files_and_refuses_existing_database_or_prepared_marker()
    {
        File.WriteAllText(Path.Combine(Source,"store-1.json"),"TEST legacy assistant configuration");
        var request=new AssistantStorageRequest("migrate-settings",DestinationDirectory:Path.Combine(root,"settings-only"),SourceDatabase:SourceDb,SourceSettingsDirectory:Source,WritersQuiesced:true);
        var prepared=JsonSerializer.SerializeToElement(AssistantStorageMaintenance.Prepare(request));
        Assert.Equal("TEST legacy assistant configuration",File.ReadAllText(Path.Combine(Property(prepared,"settingsDirectory"),"store-1.json")));
        Assert.False(File.Exists(SourceDb));AssistantStorageSafety.Validate(Env(Path.Combine(root,"new-web"),true),Config(prepared));
        SourceStore().Settings(1);Assert.Throws<InvalidOperationException>(()=>AssistantStorageMaintenance.Prepare(request with{DestinationDirectory=Path.Combine(root,"reject-db")}));
        File.Delete(SourceDb);File.WriteAllText(Path.Combine(Source,AssistantStorageSafety.Marker),"TEST prepared storage cannot be replaced");
        Assert.Throws<InvalidOperationException>(()=>AssistantStorageMaintenance.Prepare(request with{DestinationDirectory=Path.Combine(root,"reject-marker")}));
    }

    [Fact] public async Task Production_rejects_default_public_wrong_identity_and_missing_database_and_readiness_fails()
    {
        var env=Env(Path.Combine(root,"new-web"),true);
        Assert.Throws<InvalidOperationException>(()=>AssistantStorageSafety.Validate(env,new ConfigurationBuilder().Build()));
        Assert.False(Directory.Exists(Path.Combine(env.ContentRootPath,"App_Data")));
        var prepared=Prepare(false);var config=Config(prepared);AssistantStorageSafety.Validate(env,config);
        var wrong=Guid.NewGuid().ToString();config["Assistant:StorageId"]=wrong;Assert.Throws<InvalidOperationException>(()=>AssistantStorageSafety.Validate(env,config));
        config["Assistant:StorageId"]=Property(prepared,"storageId");config["Storage:UploadRoot"]=Path.Combine(root,"persistent");
        Assert.Throws<InvalidOperationException>(()=>AssistantStorageSafety.Validate(env,config));config["Storage:UploadRoot"]=Path.Combine(root,"uploads");
        Assert.Throws<InvalidOperationException>(()=>AssistantStorageSafety.Absolute("relative/data.db","DatabasePath"));
        Assert.Throws<InvalidOperationException>(()=>AssistantStorageSafety.Private(Path.Combine(env.WebRootPath,"data.db"),env));
        var healthy=await new AssistantStorageHealthCheck(env,config).CheckHealthAsync(new());Assert.Equal(HealthStatus.Healthy,healthy.Status);
        File.Delete(Property(prepared,"databasePath"));
        Assert.Equal(503,Assert.Throws<AssistantException>(()=>Live(prepared).Settings(1)).Status);Assert.False(File.Exists(Property(prepared,"databasePath")));
        Assert.Equal(HealthStatus.Unhealthy,(await new AssistantStorageHealthCheck(env,config).CheckHealthAsync(new())).Status);
    }

    [Fact] public async Task Deployment_hold_blocks_CSKH_and_all_assistant_file_writes_but_allows_readonly_health_verification()
    {
        var prepared=Prepare(false);var directory=Property(prepared,"settingsDirectory");var database=Property(prepared,"databasePath");
        var settingsHold=Path.Combine(directory,AssistantStorageSafety.Hold);var databaseHold=Path.Combine(Path.GetDirectoryName(database)!,AssistantStorageSafety.Hold);
        File.WriteAllText(settingsHold,"TEST hold");File.WriteAllText(databaseHold,"TEST hold");
        Assert.Equal(503,Assert.Throws<AssistantException>(()=>Live(prepared).Configure(1,true,0)).Status);
        var env=Env(Path.Combine(root,"new-web"),true);var options=new AssistantIntegrationTests.Monitor<AssistantOptions>(new(){StoreSettingsDirectory=directory,StorageId=Property(prepared,"storageId")});
        var settings=new AssistantStoreSettings(env,options,clock,NullLogger<AssistantStoreSettings>.Instance);
        await Assert.ThrowsAsync<AssistantException>(()=>settings.SaveAsync(1,1,new(1),"initial",default));
        await Assert.ThrowsAsync<AssistantException>(()=>new AssistantPolicyKnowledge(env,options,clock).ReadAsync(1,default));
        await Assert.ThrowsAsync<AssistantException>(()=>new AssistantSearchKnowledge(env,options,clock).ReadAsync(1,default));
        AssistantStorageMaintenance.Check(new("check",DatabasePath:database,SettingsDirectory:directory,StorageId:Property(prepared,"storageId")));
        Assert.Equal(HealthStatus.Healthy,(await new AssistantStorageHealthCheck(env,Config(prepared)).CheckHealthAsync(new())).Status);
        File.Delete(settingsHold);File.Delete(databaseHold);Assert.True(Live(prepared).Configure(1,true,0).Enabled);
    }

    [Fact] public void Snapshot_detects_changed_rows_and_files_and_never_restores_over_new_data()
    {
        Seed();var prepared=Prepare();var database=Property(prepared,"databasePath");var settings=Property(prepared,"settingsDirectory");
        var request=new AssistantStorageRequest("snapshot",database,settings,Path.Combine(root,"snapshot"),WritersQuiesced:true,StorageId:Property(prepared,"storageId"));
        AssistantStorageMaintenance.Snapshot(request);AssistantStorageMaintenance.Verify(request with{Operation="verify"});
        Assert.Throws<InvalidOperationException>(()=>AssistantStorageMaintenance.Snapshot(request));
        File.WriteAllText(Path.Combine(settings,"store-1.json"),"TEST changed settings");
        Assert.Throws<InvalidOperationException>(()=>AssistantStorageMaintenance.Verify(request with{Operation="verify"}));
        File.Copy(Path.Combine(Source,"store-1.json"),Path.Combine(settings,"store-1.json"),true);
        var live=Live(prepared);var newer=live.Start(1,Guid.NewGuid(),new(Guid.NewGuid(),"TEST after release",null,true));
        Assert.Throws<InvalidOperationException>(()=>AssistantStorageMaintenance.Verify(request with{Operation="verify"}));
        Assert.Equal(newer.Id,live.StaffGet(staff,newer.Id).Id);
    }

    [Fact] public void Offline_CLI_is_strict_bounded_sanitized_and_rejects_requests_without_writers_quiesced()
    {
        foreach(var request in new[]{"{}","{\"operation\":\"unknown\"}","{\"operation\":\"initialize\",\"destinationDirectory\":\"TEST_SENSITIVE_PATH\"}",new string('x',32769),"{\"operation\":\"check\",\"unexpected\":true}"})
        {
            using var output=new StringWriter();using var error=new StringWriter();Assert.Equal(2,AssistantStorageMaintenance.Run(new StringReader(request),output,error));
            Assert.Empty(output.ToString());Assert.DoesNotContain("TEST_SENSITIVE_PATH",error.ToString());
        }
    }

    public void Dispose()
    {
        var expected=Path.Combine(Path.GetTempPath(),"GaoC1")+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(root).StartsWith(expected,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileName(root),"N",out _))throw new InvalidOperationException("Unsafe test cleanup.");
        Directory.Delete(root,true);
    }
}
