using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using GaoApp.Web.Services.Assistant;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

namespace GaoApp.Web.Services.CustomerService;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerIdentityIssue(Guid RequestId,long Version,int CustomerId,string Method,string Reference,bool Confirmed);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerIdentityRedeem(Guid RequestId,string Code);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerIdentityRevoke(Guid RequestId);
public sealed record CustomerIdentityCode(string? Code,DateTimeOffset ExpiresAt,bool AlreadyUsed);
public sealed record CustomerIdentityLease(int CustomerId,int Issuer,Guid Generation,DateTimeOffset ExpiresAt,string? ProtectedVerifiedPhone=null);
public sealed record CustomerChannelIdentityTarget(string ConnectionId,string Binding,string Peer);

public sealed partial class CustomerServiceStore
{
    private static AssistantException ProofRequired()=>new(403,"customer_verification_required","Mình cần được nhân viên xác minh trước khi xem đơn mua và quyền lợi. Chọn Gặp nhân viên để được hướng dẫn.");
    private static string CodeHash(string code)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
    private void IdentityAudit(SqliteConnection db,SqliteTransaction tx,Guid id,string action,int? actor,int? customer) {
        Execute(db,tx,"INSERT INTO CskhIdentityAudit(Conversation,Action,Actor,Customer,Created) VALUES($id,$action,$actor,$customer,$now)",("$id",id.ToString()),("$action",action),("$actor",actor),("$customer",customer),("$now",Now));
        Execute(db,tx,"DELETE FROM CskhIdentityAudit WHERE Conversation=$id AND Id NOT IN (SELECT Id FROM CskhIdentityAudit WHERE Conversation=$id ORDER BY Id DESC LIMIT 400)",("$id",id.ToString()));
    }
    private CustomerConversation IdentityConversation(SqliteConnection db,SqliteTransaction tx,int store,Guid visitor,Guid id) {
        RequireVisitor(db,tx,store,visitor);RequireEnabled(db,tx,store);var c=Read(db,tx,store,id,visitor);
        if(c.Mode=="closed"||IsRevoked(db,tx,store,visitor))throw ProofRequired();return c;
    }
    public CustomerChannelIdentityTarget? IdentityChannelTarget(CustomerStaff staff,Guid id)=>Run<CustomerChannelIdentityTarget?>((db,tx)=>{
        var c=Read(db,tx,staff.StoreId,id);if(c.Source=="web")return null;
        if(c.Source!="zalomarketing"||c.ConversationKind!="Direct"||c.Mode!="human"||c.Owner!=staff.UserId)throw new AssistantException(403,"customer_channel_verification_unsupported","Chỉ cấp quyền tra cứu cho hội thoại Zalo cá nhân do nhân viên tiếp nhận và xác minh.");
        using var cmd=Command(db,tx,"SELECT Connection,Binding,Peer FROM CskhChannelThreads WHERE Conversation=$id AND Provider='zalomarketing'",("$id",id.ToString()));using var r=cmd.ExecuteReader();if(!r.Read())throw ProofRequired();return new(r.GetString(0),r.GetString(1),r.GetString(2));
    });
    public CustomerChannelIdentityTarget RequireChannelIdentityTarget(CustomerChannelConnection connection,int store,Guid visitor,Guid id)=>Run<CustomerChannelIdentityTarget>((db,tx)=>{
        if(connection.Provider!="zalomarketing"||!connection.Enabled||connection.StoreId!=store)throw ProofRequired();
        var c=IdentityConversation(db,tx,store,visitor,id);if(c.Source!="zalomarketing"||c.ConversationKind!="Direct")throw ProofRequired();
        using var cmd=Command(db,tx,"SELECT Connection,Binding,Peer FROM CskhChannelThreads WHERE Conversation=$id AND Connection=$connection AND Binding=$binding AND Provider='zalomarketing'",("$id",id.ToString()),("$connection",connection.Id),("$binding",connection.Binding));using var r=cmd.ExecuteReader();
        if(!r.Read())throw ProofRequired();return new(r.GetString(0),r.GetString(1),r.GetString(2));
    });
    public Guid? ActiveChannelIdentityGeneration(CustomerChannelConnection connection,Guid conversation,CustomerChannelIdentityTarget target)=>RunRead<Guid?>((db,tx)=>{
        if(target.ConnectionId!=connection.Id||target.Binding!=connection.Binding)return null;
        using var cmd=Command(db,tx,"SELECT i.IssueRequest FROM CskhIdentity i JOIN CskhChannelThreads t ON t.Conversation=i.Conversation WHERE i.Conversation=$id AND i.GrantUntil>$now AND i.CodeHash IS NULL AND i.Connection=$connection AND i.Binding=$binding AND i.Peer=$peer AND i.VerifiedPhone IS NOT NULL AND t.Connection=$connection AND t.Binding=$binding AND t.Peer=$peer AND t.Provider='zalomarketing'",("$id",conversation.ToString()),("$now",Now),("$connection",connection.Id),("$binding",connection.Binding),("$peer",target.Peer));var value=cmd.ExecuteScalar() as string;return value is null?null:Guid.Parse(value);
    });
    public bool ChannelIdentityActive(CustomerChannelConnection connection,Guid conversation,string peer,Guid generation)=>RunRead((db,tx)=>Scalar(db,tx,"SELECT 1 FROM CskhIdentity i JOIN CskhChannelThreads t ON t.Conversation=i.Conversation WHERE i.Conversation=$id AND i.IssueRequest=$generation AND i.GrantUntil>$now AND i.CodeHash IS NULL AND i.Connection=$connection AND i.Binding=$binding AND i.Peer=$peer AND i.VerifiedPhone IS NOT NULL AND t.Connection=$connection AND t.Binding=$binding AND t.Peer=$peer AND t.Provider='zalomarketing'",("$id",conversation.ToString()),("$generation",generation.ToString()),("$now",Now),("$connection",connection.Id),("$binding",connection.Binding),("$peer",peer)) is not null);
    public Guid ChannelVisitor(CustomerChannelConnection connection,Guid conversation)=>RunRead((db,tx)=>{
        using var cmd=Command(db,tx,"SELECT c.Visitor FROM CskhConversations c JOIN CskhChannelThreads t ON t.Conversation=c.Id WHERE c.Id=$id AND c.Store=$store AND t.Connection=$connection AND t.Binding=$binding AND t.Provider='zalomarketing'",("$id",conversation.ToString()),("$store",connection.StoreId),("$connection",connection.Id),("$binding",connection.Binding));var value=cmd.ExecuteScalar() as string;if(value is null)throw ProofRequired();return Guid.Parse(value);
    });
    public CustomerIdentityCode IssueIdentity(CustomerStaff staff,Guid id,CustomerIdentityIssue input,IDataProtector protector,string? protectedVerifiedPhone=null)=>Run((db,tx)=>{
        if(!staff.CanVerify||!staff.CanReply)throw AssistantException.Denied();var c=Read(db,tx,staff.StoreId,id);RequireEnabled(db,tx,staff.StoreId);
        CustomerChannelIdentityTarget? target=null;
        if(c.Source=="zalomarketing") {
            if(c.ConversationKind!="Direct"||protectedVerifiedPhone is null)throw new AssistantException(403,"customer_channel_verification_unsupported","Chỉ cấp quyền tra cứu cho hội thoại Zalo cá nhân đã xác minh số điện thoại hiện tại.");
            using var targetQuery=Command(db,tx,"SELECT Connection,Binding,Peer FROM CskhChannelThreads WHERE Conversation=$id AND Provider='zalomarketing'",("$id",id.ToString()));using var targetReader=targetQuery.ExecuteReader();if(!targetReader.Read())throw ProofRequired();target=new(targetReader.GetString(0),targetReader.GetString(1),targetReader.GetString(2));
        } else if(c.Source!="web")throw new AssistantException(403,"customer_channel_verification_unsupported","Chỉ hỗ trợ tra cứu riêng trên website hoặc hội thoại Zalo cá nhân đã xác minh.");
        if(c.Mode!="human"||c.Owner!=staff.UserId)throw Conflict();
        if(input.CustomerId<=0||!input.Confirmed||input.Method is not ("in_person" or "verified_callback")||!Text(input.Reference,120)||input.Reference.Trim().Length<8)throw AssistantException.Invalid();
        var replay=Replay(db,tx,id,input.RequestId,"identity-agent:"+staff.UserId,input);
        if(replay) {
            using var previous=Command(db,tx,"SELECT ProtectedCode,CodeUntil,GrantUntil FROM CskhIdentity WHERE Conversation=$id AND IssueRequest=$request",("$id",id.ToString()),("$request",input.RequestId.ToString()));using var r=previous.ExecuteReader();if(!r.Read())throw Conflict();
            return new CustomerIdentityCode(!r.IsDBNull(0)&&r.GetInt64(1)>Now?protector.Unprotect(r.GetString(0)):null,DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(1)),r.GetInt64(2)>0);
        }
        if(c.Version!=input.Version)throw Conflict();
        var code=Convert.ToHexString(RandomNumberGenerator.GetBytes(12));var until=Math.Min(Now+600000,c.ExpiresAt.ToUnixTimeMilliseconds());
        Execute(db,tx,"INSERT INTO CskhIdentity(Conversation,Customer,Issuer,Method,Reference,IssueRequest,CodeHash,ProtectedCode,CodeUntil,GrantUntil,Failures,Connection,Binding,Peer,VerifiedPhone) VALUES($id,$customer,$issuer,$method,$reference,$request,$hash,$secret,$until,0,0,$connection,$binding,$peer,$phone) ON CONFLICT(Conversation) DO UPDATE SET Customer=$customer,Issuer=$issuer,Method=$method,Reference=$reference,IssueRequest=$request,CodeHash=$hash,ProtectedCode=$secret,CodeUntil=$until,GrantUntil=0,Failures=0,Connection=$connection,Binding=$binding,Peer=$peer,VerifiedPhone=$phone",
            ("$id",id.ToString()),("$customer",input.CustomerId),("$issuer",staff.UserId),("$method",input.Method),("$reference",input.Reference.Trim()),("$request",input.RequestId.ToString()),("$hash",CodeHash(code)),("$secret",protector.Protect(code)),("$until",until),("$connection",target?.ConnectionId),("$binding",target?.Binding),("$peer",target?.Peer),("$phone",protectedVerifiedPhone));
        IdentityAudit(db,tx,id,"issued",staff.UserId,input.CustomerId);
        Execute(db,tx,"UPDATE CskhConversations SET Version=Version+1,Updated=$now WHERE Id=$id",("$now",Now),("$id",id.ToString()));
        return new CustomerIdentityCode(code,DateTimeOffset.FromUnixTimeMilliseconds(until),false);
    });
    public (int Customer,int Issuer,Guid Generation,string? ProtectedPhone) PendingIdentity(int store,Guid visitor,Guid id)=>Run((db,tx)=>{
        IdentityConversation(db,tx,store,visitor,id);using var cmd=Command(db,tx,"SELECT Customer,Issuer,IssueRequest,VerifiedPhone FROM CskhIdentity WHERE Conversation=$id AND ((CodeUntil>$now AND Failures<5 AND CodeHash IS NOT NULL) OR GrantUntil>$now)",("$id",id.ToString()),("$now",Now));using var r=cmd.ExecuteReader();if(!r.Read())throw ProofRequired();return(r.GetInt32(0),r.GetInt32(1),Guid.Parse(r.GetString(2)),r.IsDBNull(3)?null:r.GetString(3));
    });
    public bool RedeemIdentity(int store,Guid visitor,Guid id,CustomerIdentityRedeem input)=>Run((db,tx)=>{
        var c=IdentityConversation(db,tx,store,visitor,id);if(input.RequestId==Guid.Empty||input.Code is null||input.Code.Length>100)throw AssistantException.Invalid();
        // Failure returns normally so its attempt counter commits. Throwing inside Run would roll it back.
        using var cmd=Command(db,tx,"SELECT Customer,Issuer,CodeHash,CodeUntil,GrantUntil,Failures FROM CskhIdentity WHERE Conversation=$id",("$id",id.ToString()));
        int customer,issuer,failures;string? hash;long until,grant;
        using(var r=cmd.ExecuteReader()){if(!r.Read())return false;customer=r.GetInt32(0);issuer=r.GetInt32(1);hash=r.IsDBNull(2)?null:r.GetString(2);until=r.GetInt64(3);grant=r.GetInt64(4);failures=r.GetInt32(5);}
        if(c.Source=="zalomarketing") {
            using var target=Command(db,tx,"SELECT i.Connection,i.Binding,i.Peer,t.Connection,t.Binding,t.Peer,t.Provider FROM CskhIdentity i JOIN CskhChannelThreads t ON t.Conversation=i.Conversation WHERE i.Conversation=$id",("$id",id.ToString()));using var r=target.ExecuteReader();
            if(!r.Read()||r.IsDBNull(0)||r.IsDBNull(1)||r.IsDBNull(2)||r.GetString(0)!=r.GetString(3)||r.GetString(1)!=r.GetString(4)||r.GetString(2)!=r.GetString(5)||r.GetString(6)!="zalomarketing")return false;
        }
        var normalized=input.Code.Trim().Replace("-","").Replace(" ","").ToUpperInvariant();
        // Successful replay uses a hash of the request, never the original secret. A used code with another ID cannot reactivate access.
        if(grant>Now) {
            var existing=Scalar(db,tx,"SELECT Hash FROM CskhRequests WHERE Conversation=$id AND Id=$request AND Actor=$actor",("$id",id.ToString()),("$request",input.RequestId.ToString()),("$actor","identity-visitor:"+visitor)) as string;
            return existing==Hash(new{action="verify",code=CodeHash(normalized)});
        }
        if(hash is null||until<=Now||failures>=5)return false;
        if(!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash),Encoding.ASCII.GetBytes(CodeHash(normalized)))) {
            Execute(db,tx,"UPDATE CskhIdentity SET Failures=Failures+1,ProtectedCode=CASE WHEN Failures>=4 THEN NULL ELSE ProtectedCode END,CodeHash=CASE WHEN Failures>=4 THEN NULL ELSE CodeHash END WHERE Conversation=$id",("$id",id.ToString()));IdentityAudit(db,tx,id,"failed",null,null);return false;
        }
        if(Replay(db,tx,id,input.RequestId,"identity-visitor:"+visitor,new{action="verify",code=CodeHash(normalized)}))return false;
        Execute(db,tx,"UPDATE CskhIdentity SET GrantUntil=$until,CodeHash=NULL,ProtectedCode=NULL WHERE Conversation=$id",("$until",Math.Min(Now+1800000,c.ExpiresAt.ToUnixTimeMilliseconds())),("$id",id.ToString()));
        IdentityAudit(db,tx,id,"verified",issuer,customer);return true;
    });
    public CustomerIdentityLease RequireIdentity(int store,Guid visitor,Guid id,string? audit=null)=>Run((db,tx)=>{
        IdentityConversation(db,tx,store,visitor,id);using var cmd=Command(db,tx,"SELECT Customer,Issuer,IssueRequest,GrantUntil,VerifiedPhone FROM CskhIdentity WHERE Conversation=$id AND GrantUntil>$now",("$id",id.ToString()),("$now",Now));CustomerIdentityLease lease;
        using(var r=cmd.ExecuteReader()){if(!r.Read())throw ProofRequired();lease=new(r.GetInt32(0),r.GetInt32(1),Guid.Parse(r.GetString(2)),DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(3)),r.IsDBNull(4)?null:r.GetString(4));}
        if(audit is not null)IdentityAudit(db,tx,id,audit,null,lease.CustomerId);return lease;
    });
    public bool InvalidateChannelIdentity(CustomerChannelConnection connection,Guid conversation,string peer,Guid generation)=>Run((db,tx)=>{
        var changed=ExecuteChanged(db,tx,"UPDATE CskhIdentity SET GrantUntil=0,CodeHash=NULL,ProtectedCode=NULL WHERE Conversation=$id AND IssueRequest=$generation AND GrantUntil>0 AND Connection=$connection AND Binding=$binding AND Peer=$peer",("$id",conversation.ToString()),("$generation",generation.ToString()),("$connection",connection.Id),("$binding",connection.Binding),("$peer",peer));
        if(changed>0)IdentityAudit(db,tx,conversation,"phone_changed",null,null);return changed>0;
    });
    public void InvalidateIdentity(int store,Guid visitor,Guid id,Guid generation,string reason)=>Run((db,tx)=>{
        IdentityConversation(db,tx,store,visitor,id);
        var changed=ExecuteChanged(db,tx,"UPDATE CskhIdentity SET GrantUntil=0,CodeHash=NULL,ProtectedCode=NULL,CodeUntil=0 WHERE Conversation=$id AND IssueRequest=$generation AND (GrantUntil>0 OR CodeHash IS NOT NULL)",("$id",id.ToString()),("$generation",generation.ToString()));
        if(changed>0)IdentityAudit(db,tx,id,reason,null,null);return true;
    });
    public void RevokeIdentity(int store,Guid visitor,Guid id,Guid requestId,int? actor=null)=>Run((db,tx)=>{
        IdentityConversation(db,tx,store,visitor,id);if(Replay(db,tx,id,requestId,actor is null?"identity-visitor:"+visitor:"identity-agent:"+actor,new{action="revoke"}))return true;
        Execute(db,tx,"UPDATE CskhIdentity SET GrantUntil=0,CodeHash=NULL,ProtectedCode=NULL WHERE Conversation=$id",("$id",id.ToString()));IdentityAudit(db,tx,id,"revoked",actor,null);return true;
    });
}
