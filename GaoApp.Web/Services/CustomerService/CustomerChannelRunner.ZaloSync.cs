using System.Globalization;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Services.CustomerMarketing;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.CustomerService;

public sealed partial class CustomerChannelRunner
{
    // A work budget, not a daily message allowance. The next poll resumes the same
    // private SQLite cursor. No partially fetched range is presented as complete.
    private const int ZaloRequestsPerRun=8;
    private async Task PollZalo(CustomerChannelConnection c,CancellationToken ct) {
        if(c.LiveReplyPilot.Enabled){await PollZaloLivePilot(c,ct);return;}
        var lease=storage.AcquireZaloSync(c);if(lease is null)return;
        try {
            var work=storage.ReadZaloSync(c,lease.Value)??new CustomerZaloSyncWork{Started=clock.GetUtcNow().ToUnixTimeMilliseconds()};
            if(work.Started<clock.GetUtcNow().AddDays(-7).ToUnixTimeMilliseconds())throw new CustomerZaloSyncException("zalo_sync_history_expired");
            storage.SaveZaloSync(c,lease.Value,work);
            var state=storage.ChannelState(c);var budget=ZaloRequestsPerRun;
            // Recheck readiness on every resumed batch. Waiting permits a staff-only
            // view of retained observations; it never proves complete remote history.
            var page=await transport.ZaloGet(c,$"api/v1/inbox/conversations?page={state.Cursor}",ct);budget--;
            if(!page.TryGetProperty("sync",out var sync))throw new JsonException();
            var sourceStatus=sync.GetProperty("status").GetString();
            if(!sync.GetProperty("enabled").GetBoolean()||sourceStatus is not ("Ready" or "Waiting"))throw new CustomerZaloSyncException("zalo_source_not_ready");
            work.SourceReady&=sourceStatus=="Ready";
            if(work.Peers is null) {
                var summaries=page.GetProperty("conversations");if(summaries.GetArrayLength()>25)throw new JsonException();
                work.Peers=summaries.EnumerateArray().Select(s=>new CustomerZaloSyncPeer(s.GetProperty("id").GetGuid(),s.GetProperty("conversationKind").GetString()??"")).ToArray();
                if(work.Peers.Any(p=>p.Id==Guid.Empty||p.Kind is not ("Direct" or "Group"))||work.Peers.Select(p=>p.Id).Distinct().Count()!=work.Peers.Length)throw new JsonException();
                work.More=page.GetProperty("hasNextPage").GetBoolean();storage.SaveZaloSync(c,lease.Value,work);
            }
            while(work.Position<work.Peers.Length) {
                ct.ThrowIfCancellationRequested();c=registry.Get(c.Id);storage.CheckZaloSync(c,lease.Value);
                var peer=work.Peers[work.Position];
                var history=work.History??=new(){Anchor=storage.ZaloSyncAnchor(c,peer.Id)};
                while(!history.Covered) {
                    if(budget==0){storage.SaveZaloSync(c,lease.Value,work);return;}
                    var before=history.Pages.Count==0?(long?)null:history.Pages[^1].GetProperty("messages")[0].GetProperty("sequence").GetInt64();
                    var detail=await transport.ZaloGet(c,$"api/v1/inbox/conversations/{peer.Id:D}"+(before is null?"":"?before="+before.Value.ToString(CultureInfo.InvariantCulture)),ct);budget--;
                    ValidateZaloPage(detail,peer,history);
                    var messages=detail.GetProperty("messages");var more=detail.GetProperty("hasOlder").GetBoolean();
                    var found=history.Anchor is {} anchor&&messages.EnumerateArray().Any(m=>m.GetProperty("sequence").GetInt64()==anchor);
                    history.AnchorFound|=found;
                    // Baseline/window exclusions are explicit policy boundaries, not proof that
                    // the source supplied an established checkpoint which has disappeared.
                    var policyBoundary=(history.Anchor is null||history.AnchorFound)&&messages.GetArrayLength()>0&&messages.EnumerateArray().All(m=>
                        m.GetProperty("observedAtUtc").GetDateTimeOffset()<clock.GetUtcNow().AddDays(-7)||work.SourceReady&&peer.Kind=="Direct"&&m.GetProperty("observedAtUtc").GetDateTimeOffset()<state.StartedAt);
                    if(!history.AnchorFound&&!more&&history.Anchor is not null)throw new CustomerZaloSyncException("zalo_sync_anchor_missing");
                    if(more&&messages.GetArrayLength()==0)throw new CustomerZaloSyncException("zalo_sync_cursor_not_advancing");
                    if(history.Pages.Count==0) {
                        history.LatestRangeCovered=found||history.Anchor is null&&!more;
                        var position=-1;foreach(var m in messages.EnumerateArray()){position++;var sequence=m.GetProperty("sequence").GetInt64();if(sequence==history.Anchor||!m.GetProperty("isOutgoing").GetBoolean()&&storage.ChannelSeen(c,peer.Id.ToString("D")+":"+sequence))history.LastKnownPosition=position;}
                    }
                    // Continue through the retained window even after finding the checkpoint:
                    // the source can restore unseen frames behind an already known message.
                    history.Pages.Add(detail);history.Covered=!more||policyBoundary;
                    if(history.Pages.Count>2000)throw new CustomerZaloSyncException("zalo_sync_buffer_capacity");
                    storage.SaveZaloSync(c,lease.Value,work);
                }
                // Fetch the entire missing interval first; import oldest-to-newest so reply
                // references remain correct even when source observations share a timestamp.
                while(history.ImportedPages<history.Pages.Count) {
                    var index=history.Pages.Count-1-history.ImportedPages;
                    await ImportZaloPage(c,lease.Value,peer,history.Pages[index],state,
                        !work.SourceReady||!history.LatestRangeCovered||!state.Baseline||index!=0,index==0?history.LastKnownPosition:int.MaxValue,work.SourceReady,ct);
                    history.ImportedPages++;storage.SaveZaloSync(c,lease.Value,work);
                }
                var latest=history.Pages[0].GetProperty("messages");var newest=latest.GetArrayLength()==0?history.Anchor:latest[latest.GetArrayLength()-1].GetProperty("sequence").GetInt64();
                var incoming=history.Pages.SelectMany(p=>p.GetProperty("messages").EnumerateArray().Reverse()).Where(m=>!m.GetProperty("isOutgoing").GetBoolean()).Select(m=>new CustomerZaloSyncReference(m.GetProperty("sequence").GetInt64(),m.GetProperty("observedAtUtc").GetDateTimeOffset())).FirstOrDefault();
                work.History=null;work.Position++;storage.FinishZaloPeer(c,lease.Value,peer.Id,newest,work,incoming);
            }
            storage.FinishZaloPage(registry.Get(c.Id),lease.Value,work.More,work.SourceReady);
        } finally {storage.ReleaseZaloSync(c,lease.Value);}
    }
    private static void ValidateZaloPage(JsonElement detail,CustomerZaloSyncPeer peer,CustomerZaloSyncHistory history) {
        if(detail.GetProperty("id").GetGuid()!=peer.Id||detail.GetProperty("kind").GetString()!=peer.Kind)throw new JsonException();
        var messages=detail.GetProperty("messages");if(messages.GetArrayLength()>50)throw new JsonException();
        var seen=history.Pages.SelectMany(p=>p.GetProperty("messages").EnumerateArray()).Select(m=>m.GetProperty("sequence").GetInt64()).ToHashSet();
        foreach(var m in messages.EnumerateArray()) {
            var sequence=m.GetProperty("sequence").GetInt64();
            if(sequence<=0||!seen.Add(sequence))throw new CustomerZaloSyncException("zalo_sync_cursor_not_advancing");
            _=m.GetProperty("observedAtUtc").GetDateTimeOffset();_=m.GetProperty("isOutgoing").GetBoolean();_=CustomerZaloMedia.Parse(m,sequence);
        }
    }
    private async Task ImportZaloPage(CustomerChannelConnection c,Guid lease,CustomerZaloSyncPeer peer,JsonElement detail,CustomerChannelState state,bool historyOnly,int lastKnown,bool sourceReady,CancellationToken ct) {
        var name=CustomerZaloMedia.Clean(detail.GetProperty("displayName").GetString(),80);var position=-1;
        foreach(var m in detail.GetProperty("messages").EnumerateArray()) {
            ct.ThrowIfCancellationRequested();c=registry.Get(c.Id);storage.CheckZaloSync(c,lease);position++;
            var sequence=m.GetProperty("sequence").GetInt64();var metadata=CustomerZaloMedia.Parse(m,sequence);
            var media=metadata.Kind!="Text"||metadata.Attachments.Length>0;var text=m.GetProperty("text").GetString();
            if(string.IsNullOrWhiteSpace(text))text=metadata.Kind switch {"Image"=>"[Hình ảnh]","File"=>"[Tệp đính kèm]","Voice"=>"[Tin nhắn thoại]","Video"=>"[Video]",_=>"[Nội dung đính kèm]"};
            if(text.Length>8000){text=text[..1900]+"\n[Nội dung dài; xem tại Zalo.]";media=true;}
            var incoming=new CustomerChannelIncoming(peer.Id.ToString("D")+":"+sequence,peer.Id.ToString("D"),text,m.GetProperty("observedAtUtc").GetDateTimeOffset(),sequence.ToString(CultureInfo.InvariantCulture),media||peer.Kind=="Group"||historyOnly||position<lastKnown,name,peer.Kind,metadata);
            if(m.GetProperty("isOutgoing").GetBoolean()){if(metadata.Attachments.Length>0)storage.SyncZaloHistory(c,incoming with{Zalo=metadata with{SenderName="Bạn (Zalo)"}},true);continue;}
            var isNewDirectMessage=sourceReady&&!historyOnly&&peer.Kind=="Direct"&&position>=lastKnown&&incoming.SentAt>=state.StartedAt;
            if(storage.ChannelSeen(c,incoming.ExternalId)){
                storage.RefreshZaloMessage(c,incoming);
                if(isNewDirectMessage&&CustomerMarketingOptOut.IsOptOut(incoming.Text))await RecordMarketingOptOutAsync(c,peer.Id,sequence,ct);
                continue;
            }
            // Staff may inspect retained live observations from before first activation.
            // Preserve the existing baseline exclusion for automated Ready sources.
            if(incoming.SentAt<clock.GetUtcNow().AddDays(-7)||sourceReady&&peer.Kind=="Direct"&&incoming.SentAt<state.StartedAt){storage.SeedChannelReceipt(c,incoming);continue;}
            if(storage.ImportChannel(c,incoming) is null)throw new CustomerZaloSyncPausedException();
            if(isNewDirectMessage&&CustomerMarketingOptOut.IsOptOut(incoming.Text))await RecordMarketingOptOutAsync(c,peer.Id,sequence,ct);
        }
    }

    private async Task RecordMarketingOptOutAsync(CustomerChannelConnection connection,Guid conversationId,long sequence,CancellationToken ct)
    {
        var customerId=await db.InvoiceRecipientBindings.IgnoreQueryFilters().AsNoTracking()
            .Where(x=>x.StoreId==connection.StoreId&&x.ConnectionId==connection.Id&&x.ConnectionBinding==connection.Binding&&
                x.SenderProfileId==Guid.Parse(connection.AccountId)&&x.ConversationId==conversationId&&x.RevokedAtUtc==null)
            .Select(x=>(int?)x.CustomerId).SingleOrDefaultAsync(ct);
        if(customerId is null)return;
        var evidence=$"zalo:{connection.Id}:{conversationId:N}:{sequence.ToString(CultureInfo.InvariantCulture)}";
        await new CustomerMarketingConsentWriter(db,clock).SetAsync(connection.StoreId,customerId.Value,false,"ZaloKeyword",evidence,null,ct);
    }
}
