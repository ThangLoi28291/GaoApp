using System.Collections.Concurrent;
using System.Text.Json;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Assistant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.CustomerService;

public sealed partial class CustomerChannelRunner(CustomerChannelRegistry registry,CustomerServiceStore storage,CustomerChannelTransport transport,
    AppDbContext db,CustomerServiceAccess access,CustomerAiBridge ai,AssistantStoreSettings switches,TimeProvider clock,CustomerBusinessRead? privateReads=null)
{
    public async Task Run(string connectionId,bool poll,CancellationToken stop) {
        var connection=registry.Get(connectionId);
        if(!connection.Enabled||storage.ChannelState(connection).Paused||!storage.Settings(connection.StoreId).Enabled||
            !await db.Stores.IgnoreQueryFilters().AsNoTracking().AnyAsync(s=>s.Id==connection.StoreId&&s.IsActive&&!s.IsDeleted,stop))return;
        if(poll&&connection.Provider=="zalomarketing") {
            try{using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop);deadline.CancelAfter(TimeSpan.FromSeconds(30));await PollZalo(connection,deadline.Token);}
            catch(CustomerZaloSyncPausedException) { }
            catch(Exception e)when(e is not OperationCanceledException||!stop.IsCancellationRequested){var state=storage.ChannelState(connection);storage.ChannelPoll(connection,state.Cursor,state.Baseline,false,e is CustomerZaloSyncException gap?gap.Code:e is JsonException?"zalo_contract_or_profile_mismatch":"zalo_sync_unavailable");}
        }
        await Dispatch(connection,stop);
        var privateTurn=storage.TakeChannelPrivateTurn(registry.Get(connectionId));
        if(privateTurn is not null) {
            try{var result=privateReads is null?null:await privateReads.ChannelReply(registry.Get(connectionId),privateTurn,stop);storage.CompleteChannelPrivateTurn(privateTurn,result?.Reply,result?.Generation);}
            catch(OperationCanceledException)when(stop.IsCancellationRequested){throw;}
            catch(Exception){storage.CompleteChannelPrivateTurn(privateTurn,"Chưa đọc được thông tin an toàn lúc này. Vui lòng nhờ nhân viên kiểm tra giúp mình.",null);}
        }
        var turn=storage.TakeChannelTurn(registry.Get(connectionId));
        if(turn is not null) {
            try{
                var settings=await switches.ReadAsync(connection.StoreId,stop);
                if(!settings.Enabled||!settings.ChatEnabled)throw AssistantException.Disabled();
                var answer=await ai.Reply(turn.Who,turn.Message,turn.State,stop);
                var currentSettings=await switches.ReadAsync(connection.StoreId,stop);
                if(!currentSettings.Enabled||!currentSettings.ChatEnabled||currentSettings.Version!=settings.Version)
                    storage.Complete(turn.Who,null,null,"assistant_settings_changed");
                else if(!await ai.PolicyVersionCurrentAsync(connection.StoreId,answer.PolicyVersion,stop))
                    storage.Complete(turn.Who,null,null,"assistant_policy_changed");
                else if(answer.Reply.EnumerateRunes().Count()>connection.TextLimit)storage.Complete(turn.Who,null,null,"channel_reply_too_long");
                else storage.Complete(turn.Who,answer.Reply,answer.State,answer.NeedsHuman?"needs_human":null,answer.PolicyVersion,settings.Version);
            }catch(Exception e)when(e is HttpRequestException or OperationCanceledException or JsonException or AssistantException or IOException or InvalidOperationException){storage.Complete(turn.Who,null,null,"channel_ai_unavailable");}
        }
        await Dispatch(connection,stop);
    }
    private async Task Dispatch(CustomerChannelConnection connection,CancellationToken ct) {
        ct.ThrowIfCancellationRequested();var current=registry.Get(connection.Id);var item=storage.TakeChannelDispatch(current);if(item is null)return;
        try{
            if(item.JobId is null&&item.Actor is int actor&&!await access.ReplyAllowed(actor,current.StoreId,ct)){storage.FinishChannelDispatch(item,new("cancelled",Code:"reply_permission_revoked"));return;}
            current=registry.Get(connection.Id);var block=storage.ChannelDispatchBlock(current,item);
            if(block is not null){storage.FinishChannelDispatch(item,new(block=="reply_window_expired"?"failed":"cancelled",Code:block));return;}
            var assistantBlock=item.JobId is null?await AssistantDispatchBlock(current,item,ct):null;
            if(assistantBlock is not null){storage.FinishChannelDispatch(item,new("cancelled",Code:assistantBlock));return;}
            if(item.Kind=="private"&&item.IdentityGeneration is not null&&(privateReads is null||!await privateReads.ChannelReplyStillAuthorized(current,item,ct))){storage.FinishChannelDispatch(item,new("cancelled",Code:"customer_identity_changed"));return;}
            var result=await transport.Dispatch(current,item,ct);storage.FinishChannelDispatch(item,result);
        }catch(AssistantException){storage.FinishChannelDispatch(item,new("review",JobId:item.JobId,Code:"channel_configuration_unavailable"));}
        catch(Exception e)when(e is OperationCanceledException or HttpRequestException or JsonException or InvalidOperationException){storage.FinishChannelDispatch(item,new("review",JobId:item.JobId,Code:"outcome_unconfirmed"));}
    }

    private async Task<string?> AssistantDispatchBlock(CustomerChannelConnection connection,CustomerChannelDispatch item,CancellationToken ct) {
        if(item.Kind!="assistant")return null;
        if(string.IsNullOrWhiteSpace(item.AssistantPolicyVersion)||string.IsNullOrWhiteSpace(item.AssistantSettingsVersion))return "assistant_context_unavailable";
        try {
            var current=await switches.ReadAsync(connection.StoreId,ct);
            if(!current.Enabled||!current.ChatEnabled||current.Version!=item.AssistantSettingsVersion)return "assistant_settings_changed";
            return await ai.PolicyVersionCurrentAsync(connection.StoreId,item.AssistantPolicyVersion,ct)?null:"assistant_policy_changed";
        } catch(OperationCanceledException)when(ct.IsCancellationRequested) { throw; }
        catch(Exception e)when(e is AssistantException or IOException or UnauthorizedAccessException or JsonException) { return "assistant_context_unavailable"; }
    }

}
public sealed class CustomerChannelWorker(IServiceScopeFactory scopes,CustomerChannelRegistry registry,ILogger<CustomerChannelWorker> logger,TimeProvider clock):BackgroundService
{
    private readonly ConcurrentDictionary<string,DateTimeOffset> nextPoll=new(StringComparer.Ordinal);
    protected override async Task ExecuteAsync(CancellationToken stop) {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(2));
        while(await timer.WaitForNextTickAsync(stop)) {
            if(!registry.WorkerEnabled)continue;
            using var lanes=new SemaphoreSlim(2);
            await Task.WhenAll(registry.All().Where(c=>c.Enabled).Select(async c=>{
                await lanes.WaitAsync(stop);
                try{using var scope=scopes.CreateScope();scope.ServiceProvider.GetRequiredService<TenantContext>().SetStore(c.StoreId,"customer-channel");var poll=!nextPoll.TryGetValue(c.Id,out var next)||next<=clock.GetUtcNow();if(poll)nextPoll[c.Id]=clock.GetUtcNow().AddSeconds(30);await scope.ServiceProvider.GetRequiredService<CustomerChannelRunner>().Run(c.Id,poll,stop);}
                catch(Exception e)when(e is not OperationCanceledException||!stop.IsCancellationRequested){logger.LogWarning("Customer channel {Connection} unavailable: {Type}",c.Id,e.GetType().Name);}
                finally{lanes.Release();}
            }));
        }
    }
}
