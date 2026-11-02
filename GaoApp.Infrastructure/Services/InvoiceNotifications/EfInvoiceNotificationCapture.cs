using GaoApp.Application.InvoiceNotifications;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.InvoiceNotifications;

public sealed class EfInvoiceNotificationCapture(AppDbContext db,Microsoft.Extensions.Options.IOptions<InvoiceNotificationOptions>? options=null,Microsoft.Extensions.Options.IOptionsMonitor<InvoiceNotificationOptions>? monitor=null):IInvoiceNotificationCapture
{
    public async Task CaptureFinalizedOrderAsync(Order order,decimal? currentPoints,CancellationToken ct)
    {
        if((monitor?.CurrentValue.Enabled??options?.Value.Enabled)!=true)return;
        if(db.Database.IsRelational()&&db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Invoice notification capture requires its owning business transaction.");
        await StageAsync(db,await InvoiceNotificationSnapshots.OrderAsync(db,order,currentPoints,ct),db.CurrentUserId,ct);
    }
    internal static async Task StageAsync(AppDbContext db,InvoiceDocumentSnapshot snapshot,int? actor,CancellationToken ct)
    {
        if(snapshot.SourceId<=0||InvoiceNotificationSnapshots.Phone(snapshot.CustomerPhone)!="0398946174")return;
        if(db.InvoiceNotificationOutbox.Local.Any(x=>x.StoreId==snapshot.StoreId&&x.SourceType==snapshot.SourceType&&x.SourceId==snapshot.SourceId&&x.SourceVersion==snapshot.SourceVersion&&x.Kind==snapshot.Kind)||
            await db.InvoiceNotificationOutbox.AnyAsync(x=>x.StoreId==snapshot.StoreId&&x.SourceType==snapshot.SourceType&&x.SourceId==snapshot.SourceId&&x.SourceVersion==snapshot.SourceVersion&&x.Kind==snapshot.Kind,ct))return;
        var binding=snapshot.CustomerId is {} customer?await db.InvoiceRecipientBindings.SingleOrDefaultAsync(x=>x.StoreId==snapshot.StoreId&&x.CustomerId==customer&&x.RevokedAtUtc==null,ct):null;
        var reason=snapshot.UnsupportedReason??(binding is null?"recipient_not_verified":InvoiceNotificationSnapshots.Phone(snapshot.CustomerPhone)!=binding.Phone?"customer_phone_changed":null);
        var id=Guid.NewGuid();var json=InvoiceNotificationSnapshots.Json(snapshot);var now=DateTime.UtcNow;
        db.InvoiceNotificationOutbox.Add(new InvoiceNotificationOutbox
        {
            Id=id,StoreId=snapshot.StoreId,CustomerId=snapshot.CustomerId,SourceType=snapshot.SourceType,SourceId=snapshot.SourceId,SourceVersion=snapshot.SourceVersion,
            Kind=snapshot.Kind,Code=snapshot.Code,SnapshotJson=json,SnapshotSha256=InvoiceNotificationSnapshots.Hash(json),RequestId=id,IdempotencyKey="gao-invoice-"+id.ToString("N"),
            BindingId=binding?.Id,BindingVersion=binding?.Version,BindingSnapshotJson=binding is null?null:InvoiceNotificationSnapshots.Json(EfInvoiceNotificationStore.Binding(binding)),
            State=reason is null?"Pending":"Blocked",Detail=reason,CreatedAtUtc=now,NextAttemptAtUtc=now,CreatedByUserId=actor
        });
    }
}
