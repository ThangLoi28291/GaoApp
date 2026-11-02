using System.Security.Cryptography;
using GaoApp.Web.Services.Assistant;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

namespace GaoApp.Web.Services.CustomerService;

public sealed partial class CustomerServiceStore
{
    private CustomerChannelState ChannelState(SqliteConnection db,SqliteTransaction tx,CustomerChannelConnection c) {
        Execute(db,tx,"INSERT INTO CskhChannelControls(Connection,Binding) VALUES($connection,$binding) ON CONFLICT(Connection) DO NOTHING",("$connection",c.Id),("$binding",c.Binding));
        using var cmd=Command(db,tx,"SELECT Binding,Paused,Version,Cursor,Baseline,Started,LastSuccess,Error FROM CskhChannelControls WHERE Connection=$connection",("$connection",c.Id));using var r=cmd.ExecuteReader();
        if(!r.Read()||r.GetString(0)!=c.Binding)throw new AssistantException(409,"customer_channel_binding_changed","Kênh đã đổi tài khoản/cửa hàng. Cần khai báo mã kết nối mới để tách dữ liệu.");
        DateTimeOffset? Date(int i)=>r.IsDBNull(i)?null:DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(i));
        return new(r.GetInt64(1)==1,r.GetInt64(2),r.GetInt32(3),r.GetInt64(4)==1,Date(5),Date(6),r.IsDBNull(7)?null:r.GetString(7));
    }
    public CustomerChannelState ChannelState(CustomerChannelConnection c)=>Run((db,tx)=>WithZaloSyncState(db,tx,c,ChannelState(db,tx,c)));
    public void PauseChannel(CustomerChannelConnection c,bool paused,long version)=>Run((db,tx)=>{
        if(ChannelState(db,tx,c).Version!=version)throw Conflict();Execute(db,tx,"UPDATE CskhChannelControls SET Paused=$paused,Version=Version+1 WHERE Connection=$connection",("$paused",paused?1:0),("$connection",c.Id));return true;
    });
    public void ChannelPoll(CustomerChannelConnection c,int cursor,bool baseline,bool success,string? error=null)=>Run((db,tx)=>{
        ChannelState(db,tx,c);Execute(db,tx,"UPDATE CskhChannelControls SET Started=COALESCE(Started,$now),Cursor=$cursor,Baseline=$baseline,LastSuccess=CASE WHEN $success=1 THEN $now ELSE LastSuccess END,Error=$error WHERE Connection=$connection",("$now",Now),("$cursor",cursor),("$baseline",baseline?1:0),("$success",success?1:0),("$error",error),("$connection",c.Id));return true;
    });
    private bool Receipt(SqliteConnection db,SqliteTransaction tx,string connection,string externalId,object content) {
        var id=Hash(externalId);var hash=Hash(content);
        Execute(db,tx,"DELETE FROM CskhChannelReceipts WHERE Connection=$c AND ExternalId=$id AND Expires<=$now",("$c",connection),("$id",id),("$now",Now));
        var previous=Scalar(db,tx,"SELECT Hash FROM CskhChannelReceipts WHERE Connection=$c AND ExternalId=$id",("$c",connection),("$id",id)) as string;
        if(previous is not null){if(previous!=hash)throw new AssistantException(409,"customer_channel_event_conflict","Mã sự kiện đã có nội dung khác.");return false;}
        if((long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhChannelReceipts WHERE Connection=$c",("$c",connection))!>=100000)throw new AssistantException(429,"customer_channel_capacity","Kênh đã đạt giới hạn sự kiện lưu.");
        Execute(db,tx,"INSERT INTO CskhChannelReceipts VALUES($c,$id,$hash,$until)",("$c",connection),("$id",id),("$hash",hash),("$until",Now+7L*86400000));return true;
    }
    public void SeedChannelReceipt(CustomerChannelConnection c,CustomerChannelIncoming input)=>Run((db,tx)=>{ChannelState(db,tx,c);Receipt(db,tx,c.Id,input.ExternalId,input);return true;});
    public bool ChannelSeen(CustomerChannelConnection c,string externalId)=>RunRead((db,tx)=>Scalar(db,tx,"SELECT Hash FROM CskhChannelReceipts WHERE Connection=$c AND ExternalId=$id AND Expires>$now",("$c",c.Id),("$id",Hash(externalId)),("$now",Now)) is string);
    public Guid? ImportChannel(CustomerChannelConnection connection,CustomerChannelIncoming input)=>Run((db,tx)=>{
        var control=ChannelState(db,tx,connection);if(!connection.Enabled||control.Paused||!Settings(db,tx,connection.StoreId).Enabled)return null;
        if(!Text(input.ExternalId,250)||!Text(input.Peer,200)||!Text(input.Reference,250)||!Text(input.Text,8000)||input.SentAt>clock.GetUtcNow().AddMinutes(5)||input.SentAt<clock.GetUtcNow().AddDays(-7))throw AssistantException.Invalid();
        if(!Receipt(db,tx,connection.Id,input.ExternalId,input))return null;
        if(connection.Provider=="zalomarketing")Execute(db,tx,"INSERT INTO CskhZaloSync(Connection,Binding,LastReceived) VALUES($c,$b,$now) ON CONFLICT(Connection) DO UPDATE SET LastReceived=excluded.LastReceived",("$c",connection.Id),("$b",connection.Binding),("$now",Now));
        else Execute(db,tx,"UPDATE CskhChannelControls SET LastSuccess=$now,Error=NULL WHERE Connection=$connection",("$now",Now),("$connection",connection.Id));
        var previous=Scalar(db,tx,"SELECT t.Conversation FROM CskhChannelThreads t JOIN CskhConversations c ON c.Id=t.Conversation WHERE t.Connection=$connection AND t.Binding=$binding AND t.Peer=$peer AND c.Store=$store AND c.Mode!='closed' AND c.Expires>$now ORDER BY c.Created DESC LIMIT 1",("$connection",connection.Id),("$binding",connection.Binding),("$peer",input.Peer),("$store",connection.StoreId),("$now",Now)) as string;
        if(input.ConversationKind is not ("Direct" or "Group")||input.ConversationKind=="Group"&&connection.Provider!="zalomarketing")throw AssistantException.Invalid();
        Guid id;var mode=connection.AutoReply&&input.ConversationKind!="Group"?"bot":"waiting";
        if(previous is not null&&(long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhMessages WHERE Conversation=$id",("$id",previous))!>=options.CurrentValue.MaximumMessagesPerConversation-4) {
            var old=Read(db,tx,connection.StoreId,Guid.Parse(previous));if(old.Mode!="bot")mode="waiting";
            Execute(db,tx,"UPDATE CskhConversations SET Mode='closed',Reason='channel_thread_full',Owner=NULL,OwnerName=NULL,ProcessingId=NULL,ProcessingUntil=NULL,Version=Version+1,Updated=$now WHERE Id=$id",("$id",previous),("$now",Now));
            Execute(db,tx,"UPDATE CskhChannelInbox SET State='done' WHERE Conversation=$id AND State IN('pending','processing')",("$id",previous));previous=null;
        }
        if(previous is null) {
            if((long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhConversations WHERE Store=$store",("$store",connection.StoreId))!>=options.CurrentValue.MaximumConversationsPerStore)throw new AssistantException(503,"customer_capacity","Hộp thư đã đầy.");
            id=Guid.NewGuid();var visitor=Guid.NewGuid();
            Execute(db,tx,"INSERT INTO CskhConversations(Id,Store,Visitor,Name,Contact,Mode,Version,Created,Updated,Expires,Unread) VALUES($id,$store,$visitor,$name,NULL,$mode,1,$now,$now,$expires,0)",("$id",id.ToString()),("$store",connection.StoreId),("$visitor",visitor.ToString()),("$name",Text(input.DisplayName,80)?input.DisplayName!.Trim():"Khách "+connection.Label+" · "+input.Peer[^Math.Min(4,input.Peer.Length)..]),("$mode",mode),("$now",Now),("$expires",clock.GetUtcNow().AddDays(options.CurrentValue.RetentionDays).ToUnixTimeMilliseconds()));
            Execute(db,tx,"INSERT INTO CskhChannelThreads VALUES($id,$connection,$binding,$provider,$peer,$reference,$at)",("$id",id.ToString()),("$connection",connection.Id),("$binding",connection.Binding),("$provider",connection.Provider),("$peer",input.Peer),("$reference",input.Reference),("$at",input.SentAt.ToUnixTimeMilliseconds()));
            Add(db,tx,id,"system","Hội thoại từ "+connection.Label+". Danh tính trên kênh chưa xác minh hồ sơ mua hàng; nhân viên chỉ gửi trả lời khi đã tiếp nhận.");
        }else id=Guid.Parse(previous);
        var c=Read(db,tx,connection.StoreId,id);var last=(long)Scalar(db,tx,"SELECT LastInbound FROM CskhChannelThreads WHERE Conversation=$id",("$id",id.ToString()))!;
        var older=input.SentAt.ToUnixTimeMilliseconds()<last;var longText=input.Text.Length>2000;
        var message=longText?input.Text[..1900]+"\n[Nội dung dài; nhân viên cần xem đầy đủ tại kênh gốc.]":input.Text;
        Execute(db,tx,"INSERT INTO CskhChannelThreadDetails(Conversation,Kind) VALUES($id,$kind) ON CONFLICT(Conversation) DO UPDATE SET Kind=excluded.Kind",("$id",id.ToString()),("$kind",input.ConversationKind));
        var sourceMessage=Add(db,tx,id,"customer",message);
        SaveZaloSource(db,tx,sourceMessage,connection,input);
        var privateTurn=connection.Provider=="zalomarketing"&&input.ConversationKind=="Direct"&&(CustomerBusinessRead.PrivateQuery(message)||CustomerBusinessRead.VerificationCodeCandidate(message));
        var handoff=CustomerAiBridge.Handoff(message) is not null;
        var human=input.ConversationKind=="Group"||input.NeedsHuman||input.Zalo is {Kind:not "Text"}||input.Zalo?.Attachments.Length>0||older||longText||handoff&&(!privateTurn||CustomerAiBridge.ExplicitHandoff(message))||CustomerBusinessRead.PrivateQuery(message)&&!privateTurn;
        Execute(db,tx,"UPDATE CskhChannelThreads SET Reference=CASE WHEN $at>=LastInbound THEN $reference ELSE Reference END,LastInbound=MAX(LastInbound,$at) WHERE Conversation=$id",("$reference",input.Reference),("$at",input.SentAt.ToUnixTimeMilliseconds()),("$id",id.ToString()));
        Execute(db,tx,"UPDATE CskhConversations SET Mode=CASE WHEN $private=1 OR Mode='bot' AND $human=1 THEN 'waiting' ELSE Mode END,Reason=CASE WHEN $human=1 THEN 'channel_needs_human' WHEN $private=1 THEN 'customer_private_read' ELSE Reason END,Version=Version+1,Updated=$now,Unread=Unread+1,ProcessingId=CASE WHEN $human=1 OR $private=1 THEN NULL ELSE ProcessingId END,ProcessingUntil=CASE WHEN $human=1 OR $private=1 THEN NULL ELSE ProcessingUntil END WHERE Id=$id",("$id",id.ToString()),("$now",Now),("$human",human?1:0),("$private",privateTurn?1:0));
        if(human&&connection.AutoReply&&c.Mode=="bot"&&!input.NeedsHuman&&!older&&!longText)Add(db,tx,id,"assistant","Cửa hàng đã nhận yêu cầu. Mình chờ nhân viên tiếp nhận và trả lời tại đây nhé.",channelKind:"handoff");
        if(human)Execute(db,tx,"UPDATE CskhChannelInbox SET State='done' WHERE Conversation=$id AND State IN('pending','processing')",("$id",id.ToString()));
        var task=Guid.NewGuid();var inboxState=privateTurn&&!human&&connection.AutoReply?"private":!human&&c.Mode=="bot"?"pending":"done";Execute(db,tx,"INSERT INTO CskhChannelInbox VALUES($task,$id,$message,$state,$now)",("$task",task.ToString()),("$id",id.ToString()),("$message",message),("$state",inboxState),("$now",Now));return (Guid?)id;
    });
    private void QueueChannelReply(SqliteConnection db,SqliteTransaction tx,Guid conversation,Guid message,string text,string kind,int? actor,string? assistantPolicyVersion=null,string? assistantSettingsVersion=null,Guid? privateIdentityGeneration=null) {
        using var cmd=Command(db,tx,"SELECT Connection,Binding,Peer,Reference,Provider FROM CskhChannelThreads WHERE Conversation=$id",("$id",conversation.ToString()));
        string connection,binding,peer,reference,provider;using(var r=cmd.ExecuteReader()){if(!r.Read())return;connection=r.GetString(0);binding=r.GetString(1);peer=r.GetString(2);reference=r.GetString(3);provider=r.GetString(4);}
        var limit=provider=="facebook"?2000:4000;if(text.EnumerateRunes().Count()>limit)throw new AssistantException(422,"customer_channel_message_too_long","Tin nhắn vượt giới hạn của kênh. Vui lòng rút gọn trước khi gửi.");
        string? privatePayload=null;if(kind=="private"){try{privatePayload=PrivateReplies?.Protect(text);}catch(CryptographicException){privatePayload=null;}if(privatePayload is null)throw new AssistantException(503,"private_reply_storage_unavailable","Chưa thể lưu câu trả lời riêng an toàn. Vui lòng nhờ nhân viên hỗ trợ.");}
        Execute(db,tx,"INSERT INTO CskhChannelOutbox(Id,Conversation,Message,Connection,Binding,Peer,Reference,Kind,Actor,State,Available,Created,Updated,AssistantPolicyVersion,AssistantSettingsVersion,PrivateIdentityGeneration,PrivatePayload) VALUES($out,$id,$message,$connection,$binding,$peer,$reference,$kind,$actor,'pending',$now,$now,$now,$policy,$settings,$identity,$payload)",("$out",Guid.NewGuid().ToString()),("$id",conversation.ToString()),("$message",message.ToString()),("$connection",connection),("$binding",binding),("$peer",peer),("$reference",reference),("$kind",kind),("$actor",actor),("$now",Now),("$policy",kind=="assistant"?assistantPolicyVersion:null),("$settings",kind=="assistant"?assistantSettingsVersion:null),("$identity",privateIdentityGeneration?.ToString()),("$payload",privatePayload));
    }
    public CustomerChannelPrivateTurn? TakeChannelPrivateTurn(CustomerChannelConnection connection)=>Run((db,tx)=>{
        if(connection.LiveReplyPilot.Enabled||connection.Provider!="zalomarketing"||!connection.Enabled||!connection.AutoReply||ChannelState(db,tx,connection).Paused||!Settings(db,tx,connection.StoreId).Enabled||ZaloSyncBlocked(db,tx,connection))return null;
        Execute(db,tx,"UPDATE CskhChannelInbox SET State='private' WHERE State='processing' AND Created<=$cutoff AND EXISTS(SELECT 1 FROM CskhChannelThreads t WHERE t.Conversation=CskhChannelInbox.Conversation AND t.Connection=$connection AND t.Binding=$binding)",("$cutoff",Now-120000),("$connection",connection.Id),("$binding",connection.Binding));
        using var cmd=Command(db,tx,"SELECT i.Id,i.Conversation,i.Message,c.Visitor FROM CskhChannelInbox i JOIN CskhConversations c ON c.Id=i.Conversation JOIN CskhChannelThreads t ON t.Conversation=c.Id JOIN CskhChannelThreadDetails d ON d.Conversation=c.Id WHERE i.State='private' AND c.Expires>$now AND c.Mode!='closed' AND d.Kind='Direct' AND t.Provider='zalomarketing' AND t.Connection=$connection AND t.Binding=$binding AND c.Store=$store ORDER BY i.Created,i.rowid LIMIT 1",("$now",Now),("$connection",connection.Id),("$binding",connection.Binding),("$store",connection.StoreId));
        CustomerChannelPrivateTurn turn;using(var r=cmd.ExecuteReader()){if(!r.Read())return null;turn=new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),Guid.Parse(r.GetString(3)),connection.StoreId,r.GetString(2));}
        Execute(db,tx,"UPDATE CskhChannelInbox SET State='processing' WHERE Id=$id AND State='private'",("$id",turn.RequestId.ToString()));return turn;
    });
    public void CompleteChannelPrivateTurn(CustomerChannelPrivateTurn turn,string? reply,Guid? identityGeneration)=>Run((db,tx)=>{
        if(ExecuteChanged(db,tx,"UPDATE CskhChannelInbox SET State='done' WHERE Id=$id AND Conversation=$conversation AND State='processing'",("$id",turn.RequestId.ToString()),("$conversation",turn.ConversationId.ToString()))==0)return true;
        if(Scalar(db,tx,"SELECT 1 FROM CskhConversations WHERE Id=$id AND Store=$store AND Expires>$now AND Mode NOT IN('closed','human')",("$id",turn.ConversationId.ToString()),("$store",turn.StoreId),("$now",Now)) is null)return true;
        if(!string.IsNullOrWhiteSpace(reply))Add(db,tx,turn.ConversationId,"assistant",reply,channelKind:"private",privateIdentityGeneration:identityGeneration);
        Execute(db,tx,"UPDATE CskhConversations SET Version=Version+1,Updated=$now WHERE Id=$id",("$now",Now),("$id",turn.ConversationId.ToString()));return true;
    });
    public CustomerChannelTurn? TakeChannelTurn(CustomerChannelConnection connection)=>Run((db,tx)=>{
        if(!connection.Enabled||!connection.AutoReply||ChannelState(db,tx,connection).Paused||!Settings(db,tx,connection.StoreId).Enabled||ZaloSyncBlocked(db,tx,connection)&&!ZaloLivePilotReady(db,tx,connection))return null;
        foreach(var recoveryId in Ids(db,tx,"SELECT c.Id FROM CskhChannelThreads t JOIN CskhConversations c ON c.Id=t.Conversation WHERE t.Connection=$connection AND t.Binding=$binding AND c.Expires>$now AND c.Mode='bot' AND c.ProcessingId IS NOT NULL AND c.ProcessingUntil<=$now LIMIT $batch",("$connection",connection.Id),("$binding",connection.Binding),("$now",Now),("$batch",options.CurrentValue.CleanupBatchSize)))RecoverConversation(db,tx,recoveryId);
        using var cmd=Command(db,tx,"SELECT i.Id,i.Conversation,i.Message,c.Visitor,c.AiState FROM CskhChannelInbox i JOIN CskhConversations c ON c.Id=i.Conversation JOIN CskhChannelThreads t ON t.Conversation=c.Id WHERE i.State='pending' AND c.Expires>$now AND c.Mode='bot' AND NOT EXISTS(SELECT 1 FROM CskhChannelThreadDetails d WHERE d.Conversation=c.Id AND d.Kind='Group') AND c.ProcessingId IS NULL AND t.Connection=$connection AND t.Binding=$binding AND c.Store=$store AND ($pilot=0 OR t.Peer=$pilotPeer AND CAST(t.Reference AS INTEGER)>$pilotAfter AND t.LastInbound>=$pilotStarted) ORDER BY i.Created,i.rowid LIMIT 1",("$connection",connection.Id),("$binding",connection.Binding),("$store",connection.StoreId),("$now",Now),("$pilot",connection.LiveReplyPilot.Enabled?1:0),("$pilotPeer",connection.LiveReplyPilot.ConversationId.ToString("D")),("$pilotAfter",connection.LiveReplyPilot.AfterIncomingSequence),("$pilotStarted",connection.LiveReplyPilot.StartedAtUtc.ToUnixTimeMilliseconds()));
        Guid request,id,visitor;string message,state;using(var r=cmd.ExecuteReader()){if(!r.Read())return null;request=Guid.Parse(r.GetString(0));id=Guid.Parse(r.GetString(1));message=r.GetString(2);visitor=Guid.Parse(r.GetString(3));state=r.GetString(4);}
        Execute(db,tx,"UPDATE CskhChannelInbox SET State='processing' WHERE Id=$request",("$request",request.ToString()));
        Execute(db,tx,"UPDATE CskhConversations SET ProcessingId=$request,ProcessingUntil=$until WHERE Id=$id",("$request",request.ToString()),("$until",Now+120000),("$id",id.ToString()));return new CustomerChannelTurn(new(visitor,connection.StoreId,id,request),message,state,connection.Id);
    });
    public CustomerChannelDispatch? TakeChannelDispatch(CustomerChannelConnection connection)=>Run((db,tx)=>{
        if(!connection.Enabled||ChannelState(db,tx,connection).Paused||!Settings(db,tx,connection.StoreId).Enabled)return null;
        RecoverOutbox(db,tx,connection.Id);
        Execute(db,tx,"UPDATE CskhChannelOutbox SET State='cancelled',Code='human_took_over',Updated=$now WHERE Connection=$connection AND Kind='assistant' AND State IN('pending','retry') AND EXISTS(SELECT 1 FROM CskhConversations c WHERE c.Id=CskhChannelOutbox.Conversation AND c.Mode!='bot')",("$now",Now),("$connection",connection.Id));
        using var cmd=Command(db,tx,"SELECT o.Id,o.Conversation,o.Binding,o.Peer,o.Reference,m.Text,o.Kind,o.Actor,o.State,o.Job,o.Created,o.Attempts,o.Message,o.AssistantPolicyVersion,o.AssistantSettingsVersion,o.PrivateIdentityGeneration,o.PrivatePayload FROM CskhChannelOutbox o JOIN CskhMessages m ON m.Id=o.Message JOIN CskhConversations c ON c.Id=o.Conversation WHERE o.Connection=$connection AND o.Binding=$binding AND c.Store=$store AND c.Expires>$now AND o.State IN('pending','retry','accepted') AND o.Available<=$now AND ($syncBlocked=0 OR o.Kind!='assistant' OR o.Job IS NOT NULL) AND ($pilot=0 OR o.Kind='agent' OR o.Job IS NOT NULL OR o.Peer=$pilotPeer AND CAST(o.Reference AS INTEGER)>$pilotAfter AND o.Created>=$pilotStarted) AND NOT EXISTS(SELECT 1 FROM CskhChannelOutbox earlier WHERE earlier.Connection=o.Connection AND earlier.Binding=o.Binding AND earlier.Peer=o.Peer AND earlier.State IN('pending','retry','accepted','sending','checking','review','failed') AND earlier.rowid<o.rowid AND EXISTS(SELECT 1 FROM CskhConversations live WHERE live.Id=earlier.Conversation AND live.Expires>$now)) ORDER BY o.Available,o.rowid LIMIT 1",("$connection",connection.Id),("$binding",connection.Binding),("$store",connection.StoreId),("$now",Now),("$syncBlocked",ZaloSyncBlocked(db,tx,connection)&&!ZaloLivePilotReady(db,tx,connection)?1:0),("$pilot",connection.LiveReplyPilot.Enabled?1:0),("$pilotPeer",connection.LiveReplyPilot.ConversationId.ToString("D")),("$pilotAfter",connection.LiveReplyPilot.AfterIncomingSequence),("$pilotStarted",connection.LiveReplyPilot.StartedAtUtc.ToUnixTimeMilliseconds()));CustomerChannelDispatch item;Guid message;
        using(var r=cmd.ExecuteReader()){if(!r.Read())return null;var kind=r.GetString(6);var text=r.GetString(5);if(kind=="private"){try{text=PrivateReplies?.Unprotect(r.IsDBNull(16)?"":r.GetString(16))??throw new CryptographicException();}catch(CryptographicException){throw new AssistantException(503,"private_reply_storage_unavailable","Chưa thể mở câu trả lời riêng đã mã hóa. Vui lòng nhờ nhân viên hỗ trợ.");}}item=new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),connection.Id,r.GetString(2),r.GetString(3),r.GetString(4),text,kind,r.IsDBNull(7)?null:r.GetInt32(7),r.GetString(8),r.IsDBNull(9)?null:r.GetString(9),DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(10)),r.GetInt32(11),AssistantPolicyVersion:r.IsDBNull(13)?null:r.GetString(13),AssistantSettingsVersion:r.IsDBNull(14)?null:r.GetString(14),IdentityGeneration:r.IsDBNull(15)?null:Guid.Parse(r.GetString(15)));message=Guid.Parse(r.GetString(12));}
        if(item.JobId is null)item=item with{Media=ReadOutgoingAsset(db,tx,message)};
        Execute(db,tx,"UPDATE CskhChannelOutbox SET State=CASE WHEN Job IS NULL THEN 'sending' ELSE 'checking' END,Attempts=Attempts+1,Updated=$now WHERE Id=$id",("$id",item.Id.ToString()),("$now",Now));return item;
    });
    public void FinishChannelDispatch(CustomerChannelDispatch dispatch,CustomerChannelResult result)=>Run((db,tx)=>{
        if(result.State is not ("sent" or "accepted" or "review" or "failed" or "retry" or "cancelled"))throw AssistantException.Invalid();
        Execute(db,tx,"UPDATE CskhChannelOutbox SET State=$state,ExternalId=COALESCE($external,ExternalId),Job=COALESCE($job,Job),Code=$code,Available=$next,Updated=$now WHERE Id=$id AND State IN('sending','checking')",("$state",result.State),("$external",result.ExternalId),("$job",result.JobId),("$code",result.Code),("$next",Now+15000),("$now",Now),("$id",dispatch.Id.ToString()));
        if(result.State is "review" or "failed"&&dispatch.Kind=="assistant")Execute(db,tx,"UPDATE CskhConversations SET Mode='waiting',Reason='channel_delivery_review',ProcessingId=NULL,ProcessingUntil=NULL,Unread=Unread+1 WHERE Id=$id AND Mode='bot'",("$id",dispatch.ConversationId.ToString()));
        if(result.State=="cancelled"&&dispatch.Kind=="assistant"&&result.Code is "assistant_settings_changed" or "assistant_policy_changed" or "assistant_context_unavailable")Execute(db,tx,"UPDATE CskhConversations SET Mode='waiting',Reason=$reason,ProcessingId=NULL,ProcessingUntil=NULL,Unread=Unread+1,Updated=$now WHERE Id=$id AND Mode='bot'",("$reason",result.Code),("$now",Now),("$id",dispatch.ConversationId.ToString()));
        Execute(db,tx,"UPDATE CskhConversations SET Version=Version+1 WHERE Id=$id",("$id",dispatch.ConversationId.ToString()));return true;
    });
    public string? ChannelDispatchBlock(CustomerChannelConnection connection,CustomerChannelDispatch item)=>Run((db,tx)=>{
        if(!connection.Enabled||connection.Binding!=item.Binding||ChannelState(db,tx,connection).Paused||!Settings(db,tx,connection.StoreId).Enabled)return "channel_paused";
        var c=Read(db,tx,connection.StoreId,item.ConversationId);
        if(item.JobId is not null)return null; // Read-only reconciliation of an already accepted external job.
        if(item.Kind is "assistant" or "private"&&connection.LiveReplyPilot.Enabled&&!connection.LiveReplyPilot.Allows(item.Peer,item.Reference,item.CreatedAt))return "live_pilot_recipient_not_allowed";
        if(item.Kind=="private"&&connection.LiveReplyPilot.Enabled)return "live_pilot_public_text_only";
        if(item.Kind=="assistant"&&ZaloSyncBlocked(db,tx,connection)&&!ZaloLivePilotReady(db,tx,connection))return "channel_sync_incomplete";
        if(item.Kind!="agent"&&!connection.AutoReply)return "auto_reply_disabled";
        if(item.Kind=="assistant"&&c.Mode!="bot")return "human_took_over";
        if(item.Kind=="private"&&item.IdentityGeneration is Guid generation&&Scalar(db,tx,"SELECT 1 FROM CskhIdentity i JOIN CskhChannelThreads t ON t.Conversation=i.Conversation WHERE i.Conversation=$id AND i.IssueRequest=$generation AND i.GrantUntil>$now AND i.CodeHash IS NULL AND i.Connection=$connection AND i.Binding=$binding AND i.Peer=$peer AND i.VerifiedPhone IS NOT NULL AND t.Connection=$connection AND t.Binding=$binding AND t.Peer=$peer AND t.Provider='zalomarketing'",("$id",item.ConversationId.ToString()),("$generation",generation.ToString()),("$now",Now),("$connection",connection.Id),("$binding",connection.Binding),("$peer",item.Peer)) is null)return "customer_identity_changed";
        if(connection.Provider=="facebook"&&(long)Scalar(db,tx,"SELECT LastInbound FROM CskhChannelThreads WHERE Conversation=$id",("$id",item.ConversationId.ToString()))!<=Now-86400000)return "reply_window_expired";
        return null;
    });
    public object ChannelDeliveries(CustomerStaff staff,string connection)=>Run((db,tx)=>{
        using var cmd=Command(db,tx,"SELECT o.Id,o.Conversation,o.State,o.Code,o.Created,o.Updated,o.Job FROM CskhChannelOutbox o JOIN CskhConversations c ON c.Id=o.Conversation WHERE c.Store=$store AND c.Expires>$now AND o.Connection=$connection ORDER BY o.Created DESC,o.rowid DESC LIMIT 50",("$store",staff.StoreId),("$connection",connection),("$now",Now));using var r=cmd.ExecuteReader();var rows=new List<object>();while(r.Read())rows.Add(new{id=r.GetString(0),conversationId=r.GetString(1),state=r.GetString(2),code=r.IsDBNull(3)?null:r.GetString(3),createdAt=DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(4)),updatedAt=DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(5)),hasJob=!r.IsDBNull(6)});return rows;
    });
    public void ReviewChannelDispatch(CustomerStaff staff,CustomerChannelConnection connection,Guid id,CustomerChannelReview input)=>Run((db,tx)=>{
        if(!staff.CanManage||connection.StoreId!=staff.StoreId)throw AssistantException.Denied();ChannelState(db,tx,connection);
        using var cmd=Command(db,tx,"SELECT o.Conversation,o.State,o.Job FROM CskhChannelOutbox o JOIN CskhConversations c ON c.Id=o.Conversation WHERE o.Id=$id AND o.Connection=$connection AND c.Store=$store",("$id",id.ToString()),("$connection",connection.Id),("$store",staff.StoreId));Guid conversation;string state;bool hasJob;
        using(var r=cmd.ExecuteReader()){if(!r.Read())throw Missing();conversation=Guid.Parse(r.GetString(0));state=r.GetString(1);hasJob=!r.IsDBNull(2);}
        if(Replay(db,tx,conversation,input.RequestId,"channel-manager:"+staff.UserId,new{id,input.Action,input.ConfirmedNotSent}))return true;
        var next=input.Action switch{"mark_sent" when state is "review" or "failed"=>"sent","discard" when state is "review" or "failed" or "pending" or "retry"=>"cancelled","check" when hasJob&&state is "review" or "failed"=>"accepted","retry" when input.ConfirmedNotSent&&(state is "review" or "failed")&&!hasJob=>"pending",_=>throw Conflict()};
        Execute(db,tx,"UPDATE CskhChannelOutbox SET State=$state,Attempts=0,Code='manager_reviewed',Available=$now,Updated=$now WHERE Id=$id",("$state",next),("$now",Now),("$id",id.ToString()));
        Execute(db,tx,"UPDATE CskhConversations SET Version=Version+1 WHERE Id=$id",("$id",conversation.ToString()));return true;
    });
}
