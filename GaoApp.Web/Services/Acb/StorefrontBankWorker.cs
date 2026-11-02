using GaoApp.Application.Common;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

// Runs in the bank credential owner's host. The public customer process never starts this worker.
public sealed class StorefrontBankWorker(IServiceScopeFactory scopes,IConfiguration configuration,ILogger<StorefrontBankWorker> log,TimeProvider? clock=null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!configuration.GetValue<bool>("StorefrontBanking:WorkerEnabled"))return;
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var discovery=scopes.CreateAsyncScope();var db=discovery.ServiceProvider.GetRequiredService<AppDbContext>();
                using var grant=discovery.ServiceProvider.GetRequiredService<TenantContext>().BeginSystemAccess(SystemDataAccessPurpose.BankCallbackDiscovery);
                var now=(clock??TimeProvider.System).GetUtcNow().UtcDateTime;
                var due=await db.Set<StorefrontQrProcessing>().AsNoTracking().Where(x=>x.NextAttemptAtUtc<=now&&x.Store!=null&&x.Store.IsActive)
                    .OrderBy(x=>x.NextAttemptAtUtc).ThenBy(x=>x.Id).Take(50).Select(x=>new{x.StoreId,x.PaymentIntentId,x.Store!.SubDomainNormalized}).ToListAsync(stoppingToken);
                foreach(var row in due)
                {
                    await using var scope=scopes.CreateAsyncScope();scope.ServiceProvider.GetRequiredService<ITenantContextWriter>().SetStore(row.StoreId,row.SubDomainNormalized);
                    await scope.ServiceProvider.GetRequiredService<IStorefrontBankProcessor>().ProcessAsync(row.PaymentIntentId,stoppingToken);
                }
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){log.LogWarning("Storefront bank queue could not be processed; retrying. No bank payload is logged.");}
            try{await Task.Delay(TimeSpan.FromSeconds(10),stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
        }
    }
}
