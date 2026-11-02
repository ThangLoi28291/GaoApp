using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using GaoApp.Web.Services.Assistant;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.CustomerService;

public sealed class CustomerChannelOptions
{
    public const string Section="CustomerChannels";
    public bool Enabled {get;set;}=true;
    public bool WorkerEnabled {get;set;}=true;
    public string CredentialDirectory {get;set;}="";
    public List<string> PublicRoots {get;set;}=[];
    public List<CustomerChannelConnection> Connections {get;set;}=[];
    public bool Valid()=>!Enabled||(Connections.Count<=50&&Connections.All(c=>c.Valid())&&Connections.Select(c=>c.Id).Distinct(StringComparer.Ordinal).Count()==Connections.Count
        &&Connections.Select(c=>c.Provider+":"+c.AccountKey).Distinct(StringComparer.Ordinal).Count()==Connections.Count);
}
public sealed class CustomerChannelConnection
{
    public string Id {get;set;}="";
    public int StoreId {get;set;}
    public string Provider {get;set;}=""; // facebook, zalomarketing
    public string AccountId {get;set;}=""; // Page ID or the dedicated ZaloMarketing sender-profile ID
    public bool Enabled {get;set;}
    public bool AutoReply {get;set;}
    public CustomerLiveReplyPilot LiveReplyPilot {get;set;}=new();
    public string AccessTokenFile {get;set;}="";
    internal string? ManagedAccessToken {get;set;}
    public string AppSecretFile {get;set;}="";
    public string VerifyTokenFile {get;set;}="";
    public string GraphVersion {get;set;}="";
    public string BaseUrl {get;set;}="";
    public bool Valid()=>StoreId>0&&System.Text.RegularExpressions.Regex.IsMatch(Id,@"\A[a-z0-9][a-z0-9-]{2,47}\z")
        &&LiveReplyPilot.Valid(this)&&Provider is "facebook" or "zalomarketing"&&AccountId.Length is >0 and <=100&&!AccountId.Any(char.IsControl)
        &&(!Enabled||!string.IsNullOrWhiteSpace(AccessTokenFile)&&
            (Provider=="facebook"?AccountId.All(char.IsAsciiDigit)&&System.Text.RegularExpressions.Regex.IsMatch(GraphVersion,@"\Av[1-9][0-9]?\.[0-9]+\z")&&!string.IsNullOrWhiteSpace(AppSecretFile)&&!string.IsNullOrWhiteSpace(VerifyTokenFile):Guid.TryParse(AccountId,out var sender)&&sender!=Guid.Empty&&Uri.TryCreate(BaseUrl,UriKind.Absolute,out var u)&&u.UserInfo==""&&u.Query==""&&u.Fragment==""));
    public string AccountKey=>Provider=="zalomarketing"&&Guid.TryParse(AccountId,out var id)?id.ToString("D"):AccountId;
    public string Binding=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Id}\n{StoreId}\n{Provider}\n{AccountKey}\n{BaseUrl}")));
    public int TextLimit=>Provider=="facebook"?2000:4000;
    public string Label=>Provider=="facebook"?"Facebook Messenger":"Zalo";
}
public sealed class CustomerChannelRegistry(IOptionsMonitor<CustomerChannelOptions> options,IWebHostEnvironment environment,IConfiguration? configuration=null)
{
    private CustomerChannelOptions Current(){try{var current=options.CurrentValue;CustomerChannelCredentialSafety.Validate(current,environment,configuration,readEnabledFiles:false);return current;}catch(Exception e)when(e is InvalidOperationException or OptionsValidationException){throw Unavailable();}}
    private static AssistantException Unavailable()=>new(503,"customer_channel_credential_unavailable","Chưa đọc được cấu hình bảo mật của kênh.");
    public bool Enabled=>Current().Enabled;
    public bool WorkerEnabled{get{var current=Current();return current.Enabled&&current.WorkerEnabled;}}
    public CustomerChannelConnection[] All(){var current=Current();return current.Enabled?current.Connections.ToArray():[];}
    public CustomerChannelConnection Get(string id)=>All().SingleOrDefault(c=>c.Id==id)??throw new AssistantException(404,"customer_channel_not_found","Không tìm thấy kênh được cấu hình.");
    public string Secret(string path){try{var current=Current();if(!current.Enabled)throw Unavailable();var managed=current.Connections.SingleOrDefault(c=>c.Provider=="zalomarketing"&&c.AccessTokenFile==path&&c.ManagedAccessToken is not null);return managed?.ManagedAccessToken??CustomerChannelCredentialSafety.Read(path,current,environment,configuration);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Security.SecurityException){throw Unavailable();}}
    public Uri ZaloOrigin(CustomerChannelConnection connection){if(!Current().Enabled)throw Unavailable();if(!Uri.TryCreate(connection.BaseUrl,UriKind.Absolute,out var u)||u.UserInfo!=""||u.Query!=""||u.Fragment!=""||u.Scheme!="https"&&!(environment.IsDevelopment()&&u.Scheme=="http"&&u.IsLoopback))throw new AssistantException(503,"customer_channel_url_invalid","Địa chỉ kết nối ZaloMarketing chưa hợp lệ.");return new Uri(u.AbsoluteUri.TrimEnd('/')+"/");}
}
public sealed record CustomerChannelDelivery(string State,string? Code=null);
public sealed record CustomerChannelIncoming(string ExternalId,string Peer,string Text,DateTimeOffset SentAt,string Reference,bool NeedsHuman=false,string? DisplayName=null,string ConversationKind="Direct",CustomerZaloMessage? Zalo=null);
public sealed record CustomerChannelTurn(CustomerBridgeIdentity Who,string Message,string State,string Connection);
public sealed record CustomerChannelPrivateTurn(Guid RequestId,Guid ConversationId,Guid VisitorId,int StoreId,string Message);
public sealed record CustomerChannelDispatch(Guid Id,Guid ConversationId,string Connection,string Binding,string Peer,string Reference,string Text,string Kind,int? Actor,string State,string? JobId,DateTimeOffset CreatedAt,int Attempts,CustomerOutgoingAsset? Media=null,string? AssistantPolicyVersion=null,string? AssistantSettingsVersion=null,Guid? IdentityGeneration=null);
public sealed record CustomerChannelState(bool Paused,long Version,int Cursor,bool Baseline,DateTimeOffset? StartedAt,DateTimeOffset? LastSuccess,string? Error,DateTimeOffset? LastReceived=null,bool SyncIncomplete=false,string? SyncPeer=null,int BufferedPages=0);
public sealed record CustomerChannelResult(string State,string? ExternalId=null,string? JobId=null,string? Code=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]public sealed record CustomerChannelControl(bool Paused,long Version);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]public sealed record CustomerChannelReview(Guid RequestId,string Action,bool ConfirmedNotSent);
