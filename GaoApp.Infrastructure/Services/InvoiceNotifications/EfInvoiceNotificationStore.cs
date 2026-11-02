using System.Data;
using System.Text.Json;
using System.Text;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace GaoApp.Infrastructure.Services.InvoiceNotifications;

public sealed class EfInvoiceNotificationStore(AppDbContext db,IOptions<InvoiceNotificationOptions> options,TimeProvider? timeProvider=null,IOptionsMonitor<InvoiceNotificationOptions>? monitor=null,IOptionsMonitor<InvoiceOfficialDocumentLinkOptions>? officialLinks=null)
    :IInvoiceNotificationStore,IInvoiceNotificationOutbox,IInvoiceOfficialDocumentLinkAuthorizer
{
    private InvoiceNotificationOptions Settings=>monitor?.CurrentValue??options.Value;
    private DateTime Now=>(timeProvider??TimeProvider.System).GetUtcNow().UtcDateTime;
    private void Store(int id)
    {if(id<=0||(!db.CanReadAllStores&&db.CurrentStoreId!=id)||!db.CanWriteStoreData)throw new InvalidOperationException("Store scope is not authorized.");}
    private void Actor(int actor){if(actor<=0||!db.IsCurrentUserAuthenticated||db.CurrentUserId!=actor)throw new InvalidOperationException("Authenticated notification actor is required.");}
    private Task<IDbContextTransaction?> Transaction(CancellationToken ct)=>db.Database.IsRelational()?Begin(ct):Task.FromResult<IDbContextTransaction?>(null);
    private async Task<IDbContextTransaction?> Begin(CancellationToken ct)=>await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
    private async Task<bool> TryClaimLock(CancellationToken ct)
    {
        if(!db.Database.IsSqlServer())return true;
        var transaction=db.Database.CurrentTransaction??throw new InvalidOperationException("Claim lock requires its SQL transaction.");
        await using var command=db.Database.GetDbConnection().CreateCommand();
        command.Transaction=transaction.GetDbTransaction();
        command.CommandText="DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'GaoApp:InvoiceNotifications:Claim',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=0; SELECT @result;";
        var result=await command.ExecuteScalarAsync(ct);return result is not null&&Convert.ToInt32(result)>=0;
    }
    private void Audit(int store,string action,int? actor,Guid? binding=null,Guid? outbox=null,string detail="")=>db.InvoiceNotificationAudit.Add(new(){Id=Guid.NewGuid(),StoreId=store,Action=action,ActorUserId=actor,BindingId=binding,OutboxId=outbox,Detail=detail,CreatedAtUtc=Now});
    internal static InvoiceRecipientBindingSnapshot Binding(InvoiceRecipientBinding b)=>new(b.Id,b.StoreId,b.CustomerId,b.ConnectionId,b.ConnectionBinding,b.SenderProfileId,b.ConversationId,b.IncomingSequence,b.NativePeer,b.Phone,b.Version,b.VerifiedAtUtc,b.RevokedAtUtc);

    public async Task<InvoiceRecipientBindingSnapshot> VerifyBindingAsync(InvoiceRecipientBindingProof p,Guid requestId,CancellationToken ct)
    {
        Store(p.StoreId);Actor(p.ActorUserId);
        if(requestId==Guid.Empty||p.CustomerId<=0||p.ActorUserId<=0||p.SenderProfileId==Guid.Empty||p.ConversationId==Guid.Empty||p.IncomingSequence<=0||
            p.ConnectionId.Length is <3 or >48||p.ConnectionBinding.Length!=64||p.ConnectionBinding.Any(c=>!Uri.IsHexDigit(c))||
            !System.Text.RegularExpressions.Regex.IsMatch(p.NativePeer,@"\Asdk-v1:[1-9][0-9]{0,31}\z")||
            p.Method is not ("in_person" or "verified_callback" or "reference")||p.EvidenceReference.Length is <1 or >250||
            InvoiceNotificationSnapshots.Phone(p.Phone)!="0398946174")throw new ArgumentException("A verified pilot recipient and incoming message proof are required.");
        await using var tx=await Transaction(ct);
        var customer=await db.Customers.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==p.CustomerId&&x.StoreId==p.StoreId&&x.IsActive,ct);
        if(customer is null||InvoiceNotificationSnapshots.Phone(customer.Phone)!=InvoiceNotificationSnapshots.Phone(p.Phone))throw new InvalidOperationException("Customer phone does not match verified recipient.");
        var replay=await db.InvoiceRecipientBindings.AsNoTracking().SingleOrDefaultAsync(x=>x.StoreId==p.StoreId&&x.VerificationRequestId==requestId,ct);
        if(replay is not null)
        {
            if(replay.CustomerId!=p.CustomerId||replay.ConnectionId!=p.ConnectionId||replay.ConnectionBinding!=p.ConnectionBinding||replay.SenderProfileId!=p.SenderProfileId||
                replay.ConversationId!=p.ConversationId||replay.IncomingSequence!=p.IncomingSequence||replay.NativePeer!=p.NativePeer||replay.Phone!=InvoiceNotificationSnapshots.Phone(p.Phone)||replay.VerifiedByUserId!=p.ActorUserId||replay.VerificationMethod!=p.Method||replay.EvidenceReference!=p.EvidenceReference)
                throw new InvalidOperationException("Verification request has conflicting evidence.");
            if(tx is not null)await tx.CommitAsync(ct);return Binding(replay);
        }
        if(await db.InvoiceRecipientBindings.AnyAsync(x=>x.StoreId==p.StoreId&&x.CustomerId==p.CustomerId&&x.RevokedAtUtc==null,ct))
            throw new InvalidOperationException("Revoke the current binding before verifying a different recipient.");
        if(await db.InvoiceRecipientBindings.AnyAsync(x=>x.StoreId==p.StoreId&&x.ConnectionId==p.ConnectionId&&x.ConversationId==p.ConversationId&&x.CustomerId!=p.CustomerId&&x.RevokedAtUtc==null,ct))
            throw new InvalidOperationException("This conversation is already bound to another customer.");
        var b=new InvoiceRecipientBinding{Id=Guid.NewGuid(),StoreId=p.StoreId,CustomerId=p.CustomerId,VerificationRequestId=requestId,
            ConnectionId=p.ConnectionId,ConnectionBinding=p.ConnectionBinding,SenderProfileId=p.SenderProfileId,ConversationId=p.ConversationId,IncomingSequence=p.IncomingSequence,
            NativePeer=p.NativePeer,Phone=InvoiceNotificationSnapshots.Phone(p.Phone),Version=1,VerifiedAtUtc=Now,VerifiedByUserId=p.ActorUserId,VerificationMethod=p.Method,EvidenceReference=p.EvidenceReference};
        db.InvoiceRecipientBindings.Add(b);Audit(p.StoreId,"RecipientVerified",p.ActorUserId,b.Id);
        await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);return Binding(b);
    }
    public async Task RevokeBindingAsync(int storeId,Guid bindingId,int version,int actorUserId,string reason,CancellationToken ct)
    {
        Store(storeId);Actor(actorUserId);if(actorUserId<=0||string.IsNullOrWhiteSpace(reason)||reason.Length>250)throw new ArgumentException("Revocation reason and actor are required.");
        await using var tx=await Transaction(ct);
        var b=await db.InvoiceRecipientBindings.SingleOrDefaultAsync(x=>x.Id==bindingId&&x.StoreId==storeId,ct)??throw new KeyNotFoundException();
        if(b.RevokedAtUtc is not null){if(tx is not null)await tx.CommitAsync(ct);return;}
        if(b.Version!=version)throw new InvalidOperationException("Recipient binding changed.");
        b.RevokedAtUtc=Now;b.RevokedByUserId=actorUserId;b.RevocationReason=reason.Trim();b.Version++;
        foreach(var row in await db.InvoiceNotificationOutbox.Where(x=>x.StoreId==storeId&&x.BindingId==bindingId&&(x.State=="Pending"||x.State=="Dispatching")).ToListAsync(ct))
        {
            var activePossiblePost=row.PreparedJson is not null&&row.LeaseToken is not null&&row.LeaseExpiresAtUtc>Now;
            row.State=row.FirstAttemptAtUtc is null?"Cancelled":row.PreparedJson is not null?"Dispatching":"NeedsReview";row.Detail="recipient_revoked";
            // Preserve an active possible-POST lease so its late accepted result can still be durably recorded.
            if(!activePossiblePost){row.LeaseToken=null;row.LeaseExpiresAtUtc=null;}
            row.NextAttemptAtUtc=Now;row.UpdatedAtUtc=Now;
        }
        Audit(storeId,"RecipientRevoked",actorUserId,bindingId,detail:"Revoked version "+version);
        await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<InvoiceRecipientBindingSnapshot>> BindingsAsync(int storeId,int? customerId,CancellationToken ct)
    {Store(storeId);return (await db.InvoiceRecipientBindings.AsNoTracking().Where(x=>x.StoreId==storeId&&(!customerId.HasValue||x.CustomerId==customerId)).OrderByDescending(x=>x.VerifiedAtUtc).Take(200).ToListAsync(ct)).Select(Binding).ToArray();}
    private static InvoiceNotificationListItem ListItem(InvoiceNotificationOutbox r)=>new(r.Id,r.StoreId,r.CustomerId,r.SourceType,r.SourceId,r.SourceVersion,r.Kind,r.Code,r.State,r.Detail,r.CreatedAtUtc,r.UpdatedAtUtc,r.ZaloJobId,r.Attempts);
    public async Task<IReadOnlyList<InvoiceNotificationListItem>> ListAsync(int storeId,string? state,int page,CancellationToken ct)
    {Store(storeId);return (await db.InvoiceNotificationOutbox.AsNoTracking().Where(x=>x.StoreId==storeId&&(state==null||state==""||x.State==state)).OrderByDescending(x=>x.CreatedAtUtc).Skip(Math.Clamp(page-1,0,10000)*30).Take(30).ToListAsync(ct)).Select(ListItem).ToArray();}
    public async Task<InvoiceNotificationDetail?> FindAsync(int storeId,Guid id,CancellationToken ct)
    {
        Store(storeId);var r=await db.InvoiceNotificationOutbox.AsNoTracking().SingleOrDefaultAsync(x=>x.StoreId==storeId&&x.Id==id,ct);
        return r is null?null:new(ListItem(r),r.BindingSnapshotJson is null?null:InvoiceNotificationSnapshots.Parse<InvoiceRecipientBindingSnapshot>(r.BindingSnapshotJson),
            InvoiceNotificationSnapshots.Parse<InvoiceDocumentSnapshot>(r.SnapshotJson),r.PreparedJson is null?null:InvoiceNotificationSnapshots.Parse<InvoicePreparedBundle>(r.PreparedJson),r.PartsOutcomeJson);
    }
    public async Task<InvoiceNotificationSourcePreview> PreviewSourceAsync(int storeId,int customerId,string sourceType,int sourceId,string kind,CancellationToken ct)
    {
        Store(storeId);var snapshot=await SourceAsync(storeId,sourceType,sourceId,kind,ct);
        if(snapshot.CustomerId!=customerId)throw new InvalidOperationException("Document does not belong to the selected customer.");
        return new(storeId,customerId,sourceType,sourceId,kind,snapshot.SourceVersion,snapshot.Code,snapshot.Status,snapshot.GrandTotal,snapshot.UnsupportedReason,snapshot.LegalEntityId,snapshot.OriginalInvoiceId,snapshot.OriginalInvoiceCode);
    }
    public async Task<Guid> EnqueueAsync(InvoiceNotificationEnqueueCommand c,CancellationToken ct)
    {
        Store(c.StoreId);Actor(c.ActorUserId);if(c.RequestId==Guid.Empty||c.ActorUserId<=0||c.ExpectedBindingId==Guid.Empty||c.ExpectedBindingVersion<=0||string.IsNullOrEmpty(c.ExpectedSourceVersion)||c.ExpectedSourceVersion.Length!=64)throw new ArgumentException("Explicit source, recipient binding and request identities are required.");
        await using var tx=await Transaction(ct);
        var binding=await db.InvoiceRecipientBindings.SingleOrDefaultAsync(x=>x.StoreId==c.StoreId&&x.CustomerId==c.CustomerId&&x.Id==c.ExpectedBindingId&&x.Version==c.ExpectedBindingVersion&&x.RevokedAtUtc==null,ct)
            ??throw new InvalidOperationException("Recipient binding changed; review the recipient again.");
        var snapshot=await SourceAsync(c.StoreId,c.SourceType,c.SourceId,c.Kind,ct);
        if(snapshot.CustomerId!=c.CustomerId||snapshot.UnsupportedReason is not null||InvoiceNotificationSnapshots.Phone(snapshot.CustomerPhone)!=binding.Phone||
            c.ExpectedSourceVersion!=snapshot.SourceVersion)throw new InvalidOperationException("Document ownership, status or version is not ready for sending.");
        var replay=await db.InvoiceNotificationOutbox.SingleOrDefaultAsync(x=>x.StoreId==c.StoreId&&x.RequestId==c.RequestId,ct);
        if(replay is not null)
        {
            if(replay.SourceType!=c.SourceType||replay.SourceId!=c.SourceId||replay.Kind!=c.Kind||replay.SourceVersion!=snapshot.SourceVersion||replay.BindingId!=binding.Id||replay.BindingVersion!=binding.Version)throw new InvalidOperationException("Send request conflicts with previously accepted evidence.");
            if(tx is not null)await tx.CommitAsync(ct);return replay.Id;
        }
        var existing=await db.InvoiceNotificationOutbox.SingleOrDefaultAsync(x=>x.StoreId==c.StoreId&&x.SourceType==c.SourceType&&x.SourceId==c.SourceId&&x.SourceVersion==snapshot.SourceVersion&&x.Kind==c.Kind,ct);
        if(existing is not null)
        {
            if(existing.BindingId is not null&&(existing.BindingId!=binding.Id||existing.BindingVersion!=binding.Version))throw new InvalidOperationException("Document has durable evidence bound to another recipient version; review is required.");
            if(existing.BindingId is null&&existing.State=="Blocked")
            {existing.BindingId=binding.Id;existing.BindingVersion=binding.Version;existing.BindingSnapshotJson=InvoiceNotificationSnapshots.Json(Binding(binding));existing.State="Pending";existing.Detail=null;existing.UpdatedAtUtc=Now;Audit(c.StoreId,"DocumentRecipientAttached",c.ActorUserId,binding.Id,existing.Id);await db.SaveChangesAsync(ct);}
            if(tx is not null)await tx.CommitAsync(ct);return existing.Id;
        }
        await EfInvoiceNotificationCapture.StageAsync(db,snapshot,c.ActorUserId,ct);
        var row=db.InvoiceNotificationOutbox.Local.Single(x=>x.StoreId==c.StoreId&&x.SourceType==c.SourceType&&x.SourceId==c.SourceId&&x.SourceVersion==snapshot.SourceVersion&&x.Kind==c.Kind);
        row.RequestId=c.RequestId;Audit(c.StoreId,"DocumentQueued",c.ActorUserId,binding.Id,row.Id);
        await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);return row.Id;
    }
    private async Task<InvoiceDocumentSnapshot> SourceAsync(int store,string type,int id,string kind,CancellationToken ct)
    {
        if(type=="Order"&&kind=="Receipt")return await InvoiceNotificationSnapshots.OrderAsync(db,await db.Orders.AsNoTracking().Include(x=>x.Lines).Include(x=>x.Payments).SingleOrDefaultAsync(x=>x.Id==id&&x.StoreId==store,ct)??throw new KeyNotFoundException(),null,ct,true);
        if(type=="Invoice"&&kind is "OfficialPdf" or "OfficialZipXml" or "OfficialXml" or "OfficialLink" or "CorrectionPdf" or "CorrectionZipXml" or "CorrectionXml" or "CorrectionLink" or "DraftPreview")
        {
            var snapshot=await InvoiceNotificationSnapshots.InvoiceAsync(db,await db.InvoiceHeads.AsNoTracking().Include(x=>x.Details).SingleOrDefaultAsync(x=>x.Id==id&&x.StoreId==store,ct)??throw new KeyNotFoundException(),kind,ct,true);
            if(kind is "OfficialLink" or "CorrectionLink")
            {
                var audience=LinkAudience();
                snapshot=snapshot with{SourceVersion=InvoiceNotificationSnapshots.Hash(snapshot.SourceVersion+"\0"+(audience??"")),
                    UnsupportedReason=snapshot.UnsupportedReason??(audience is null?"official_link_not_configured":null)};
            }
            return snapshot;
        }
        throw new ArgumentException("Unsupported document source or kind; supplier input invoices cannot be sent as retail invoices.");
    }

    private string? LinkAudience()
    {
        var value=officialLinks?.CurrentValue.PublicBaseUrl;
        if(string.IsNullOrWhiteSpace(value)||value.Length>2048||value.Any(char.IsControl)||!Uri.TryCreate(value,UriKind.Absolute,out var parsed)||
            parsed.Scheme!="https"||parsed.UserInfo!=""||parsed.Query!=""||parsed.Fragment!=""||parsed.IsLoopback||parsed.HostNameType!=UriHostNameType.Dns||parsed.AbsolutePath.Contains("//",StringComparison.Ordinal))return null;
        return new Uri(parsed.AbsoluteUri.TrimEnd('/')+"/").AbsoluteUri;
    }

    public async Task<InvoiceOfficialDocumentLinkAccess?> AuthorizeAsync(InvoiceOfficialDocumentLinkGrant grant,CancellationToken ct)
    {
        var now=new DateTimeOffset(DateTime.SpecifyKind(Now,DateTimeKind.Utc));var audience=LinkAudience();
        if(grant is null||!Settings.Enabled||Settings.PilotPhone!="0398946174"||audience is null||grant.SchemaVersion!=1||grant.NotificationId==Guid.Empty||
            grant.StoreId<=0||db.CurrentStoreId!=grant.StoreId||!db.CanReadStoreData||grant.CustomerId<=0||grant.SourceType!="Invoice"||grant.SourceId<=0||
            grant.Kind is not ("OfficialLink" or "CorrectionLink")||grant.BindingId==Guid.Empty||grant.BindingVersion<=0||grant.Audience!=audience||
            grant.ExpiresAtUtc<=now||grant.ExpiresAtUtc>now.AddHours(72).AddMinutes(1)||grant.SourceVersion?.Length!=64||grant.SourceVersion.Any(c=>!Uri.IsHexDigit(c))||
            grant.Pdf is null||grant.Pdf.ContentType!="application/pdf"||grant.Pdf.ByteLength is <1 or >20L*1024*1024||grant.Pdf.Sha256?.Length!=64||grant.Pdf.Sha256.Any(c=>!Uri.IsHexDigit(c))||
            grant.Pdf.FileName is null||!Path.GetExtension(grant.Pdf.FileName).Equals(".pdf",StringComparison.OrdinalIgnoreCase))return null;
        try{if(InvoiceNotificationMediaPolicy.NormalizeFileName(grant.Pdf.FileName)!=grant.Pdf.FileName)return null;}
        catch(InvalidDataException){return null;}
        var row=await db.InvoiceNotificationOutbox.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==grant.NotificationId&&x.StoreId==grant.StoreId&&x.CustomerId==grant.CustomerId&&
            x.SourceType==grant.SourceType&&x.SourceId==grant.SourceId&&x.Kind==grant.Kind&&x.BindingId==grant.BindingId&&x.BindingVersion==grant.BindingVersion,ct);
        if(row is null||row.SourceVersion!=grant.SourceVersion||row.State is "Cancelled" or "Blocked" or "Failed"||row.BindingSnapshotJson is null||
            InvoiceNotificationSnapshots.Hash(row.SnapshotJson)!=row.SnapshotSha256)return null;
        var binding=await db.InvoiceRecipientBindings.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==grant.BindingId&&x.StoreId==grant.StoreId&&x.CustomerId==grant.CustomerId&&x.Version==grant.BindingVersion&&x.RevokedAtUtc==null,ct);
        if(binding is null||binding.Phone!="0398946174")return null;
        try
        {
            var original=InvoiceNotificationSnapshots.Parse<InvoiceRecipientBindingSnapshot>(row.BindingSnapshotJson);var current=Binding(binding);
            if(original!=current)return null;
            var snapshot=await SourceAsync(grant.StoreId,grant.SourceType,grant.SourceId,grant.Kind,ct);
            var pdf=snapshot.Artifact;
            if(snapshot.CustomerId!=grant.CustomerId||snapshot.SourceVersion!=grant.SourceVersion||snapshot.UnsupportedReason is not null||
                InvoiceNotificationSnapshots.Phone(snapshot.CustomerPhone)!=binding.Phone||pdf is null||pdf.ContentType!=grant.Pdf.ContentType||pdf.FileName!=grant.Pdf.FileName||
                pdf.ByteLength!=grant.Pdf.ByteLength||!pdf.Sha256.Equals(grant.Pdf.Sha256,StringComparison.OrdinalIgnoreCase))return null;
            return new(snapshot,current);
        }
        catch(Exception e)when(e is KeyNotFoundException or JsonException or InvalidDataException){return null;}
    }

    private InvoiceNotificationWorkItem Work(InvoiceNotificationOutbox r){
        if(InvoiceNotificationSnapshots.Hash(r.SnapshotJson)!=r.SnapshotSha256)throw new InvalidOperationException("Notification snapshot hash mismatch.");
        var snapshot=InvoiceNotificationSnapshots.Parse<InvoiceDocumentSnapshot>(r.SnapshotJson);
        if(snapshot.StoreId!=r.StoreId||snapshot.CustomerId!=r.CustomerId||snapshot.SourceType!=r.SourceType||snapshot.SourceId!=r.SourceId||snapshot.SourceVersion!=r.SourceVersion||snapshot.Kind!=r.Kind)
            throw new InvalidOperationException("Notification snapshot identity mismatch.");
        return new(r.Id,r.LeaseToken??Guid.Empty,r.StoreId,r.CustomerId??0,r.SourceType,r.SourceId,r.SourceVersion,r.Kind,r.RequestId,r.IdempotencyKey,r.State,r.CreatedAtUtc,
        InvoiceNotificationSnapshots.Parse<InvoiceRecipientBindingSnapshot>(r.BindingSnapshotJson??throw new InvalidOperationException("Recipient proof missing.")),
        InvoiceNotificationSnapshots.Parse<InvoiceDocumentSnapshot>(r.SnapshotJson),r.PreparedJson is null?null:InvoiceNotificationSnapshots.Parse<InvoicePreparedBundle>(r.PreparedJson),r.ZaloJobId,r.Attempts);
    }
    private async Task<InvoiceNotificationOutbox> Owned(InvoiceNotificationWorkItem item,CancellationToken ct)
    {
        var fresh=await db.InvoiceNotificationOutbox.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==item.Id&&x.StoreId==item.StoreId&&x.LeaseToken==item.LeaseToken&&x.LeaseExpiresAtUtc>Now,ct)
            ??throw new InvalidOperationException("Notification lease is stale.");
        return TrackFresh(fresh);
    }
    private InvoiceNotificationOutbox TrackFresh(InvoiceNotificationOutbox fresh)
    {
        var tracked=db.ChangeTracker.Entries<InvoiceNotificationOutbox>().SingleOrDefault(e=>e.Entity.Id==fresh.Id);
        if(tracked is null){db.InvoiceNotificationOutbox.Attach(fresh);return fresh;}
        if(tracked.State!=EntityState.Unchanged)throw new InvalidOperationException("Unsaved notification changes cannot be refreshed.");
        tracked.CurrentValues.SetValues(fresh);tracked.OriginalValues.SetValues(fresh);tracked.State=EntityState.Unchanged;return tracked.Entity;
    }
    public async Task<InvoiceNotificationWorkItem?> ClaimAsync(CancellationToken ct)
    {
        if(!db.CanWriteStoreData)return null;
        await using var tx=await Transaction(ct);var now=Now;
        if(!await TryClaimLock(ct)){if(tx is not null)await tx.CommitAsync(ct);return null;}
        var candidate=await db.InvoiceNotificationOutbox.AsNoTracking().Where(x=>x.BindingId!=null&&x.BindingSnapshotJson!=null&&x.CustomerId!=null&&x.NextAttemptAtUtc<=now&&
            ((Settings.Enabled&&Settings.PilotPhone=="0398946174"&&(x.State=="Pending"||x.State=="Dispatching"))||(x.State=="Accepted"&&x.ZaloJobId!=null)||((x.State=="Dispatching"||x.State=="Pending")&&x.PreparedJson!=null&&x.FirstAttemptAtUtc!=null))&&(x.LeaseToken==null||x.LeaseExpiresAtUtc<=now))
            .OrderBy(x=>x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if(candidate is null){if(tx is not null)await tx.CommitAsync(ct);return null;}
        candidate=TrackFresh(candidate);
        if(candidate.ZaloJobId is null&&candidate.FirstAttemptAtUtc<now.AddHours(-23)&&candidate.PreparedJson is null)
        {candidate.State="NeedsReview";candidate.Detail="idempotency_window_uncertain";candidate.LeaseToken=null;candidate.LeaseExpiresAtUtc=null;await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);return null;}
        candidate.LeaseToken=Guid.NewGuid();candidate.LeaseExpiresAtUtc=now.AddSeconds(Math.Clamp(Settings.LeaseSeconds,30,300));candidate.UpdatedAtUtc=now;
        if(candidate.ZaloJobId is null){candidate.State="Dispatching";candidate.FirstAttemptAtUtc??=now;candidate.Attempts++;}
        await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);return Work(candidate);
    }
    public async Task<InvoiceNotificationWorkItem> SavePreparedAsync(InvoiceNotificationWorkItem item,InvoicePreparedBundle bundle,CancellationToken ct)
    {
        var r=await Owned(item,ct);var json=InvoiceNotificationSnapshots.Json(bundle);
        if(bundle.Parts.Any(p=>p.Kind is not ("Text" or "Image" or "File") ||
            (p.Kind=="Text" ? string.IsNullOrWhiteSpace(p.Text)||p.Text.EnumerateRunes().Take(4001).Count()>4000||p.FileName is not null||p.ByteLength is not null||p.StorageKey is not null :
                p.Text is not null||p.FileName is null||p.FileName.Length>120||Path.GetFileName(p.FileName)!=p.FileName||p.ContentType is null||p.StorageKey is null||p.ByteLength is null or <=0||
                p.ByteLength>(p.Kind=="Image"?10L:20L)*1024*1024||p.Sha256?.Length!=64||p.Sha256.Any(c=>!Uri.IsHexDigit(c)))))
            throw new ArgumentException("Prepared parts must contain exactly one text or private asset each.");
        if(bundle.Parts.Count is <1 or >10||bundle.Parts.Sum(x=>x.ByteLength??0)>50L*1024*1024||bundle.Fingerprint.Length!=64||bundle.Fingerprint.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("Prepared bundle limits are invalid.");
        if(r.PreparedJson is not null&&r.PreparedJson!=json)throw new InvalidOperationException("Prepared bundle is immutable.");
        r.PreparedJson??=json;await db.SaveChangesAsync(ct);return Work(r);
    }
    public async Task<bool> IsAuthorizedAsync(InvoiceNotificationWorkItem item,string currentConnectionBinding,CancellationToken ct)
    {
        var row=await Owned(item,ct);var binding=await db.InvoiceRecipientBindings.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==item.Binding.Id&&x.StoreId==item.StoreId&&x.RevokedAtUtc==null,ct);
        var valid=Settings.Enabled&&Settings.PilotPhone=="0398946174"&&(row.ZaloJobId is not null||row.FirstAttemptAtUtc>=Now.AddHours(-23))&&binding is not null&&binding.Version==item.Binding.Version&&binding.Phone=="0398946174"&&
            binding.ConnectionBinding==currentConnectionBinding&&binding.ConnectionBinding==item.Binding.ConnectionBinding&&binding.SenderProfileId==item.Binding.SenderProfileId&&
            binding.ConversationId==item.Binding.ConversationId&&binding.IncomingSequence==item.Binding.IncomingSequence&&binding.NativePeer==item.Binding.NativePeer;
        if(valid)
        {
            try{var source=await SourceAsync(item.StoreId,item.SourceType,item.SourceId,item.Kind,ct);valid=source.CustomerId==item.CustomerId&&source.SourceVersion==item.SourceVersion&&source.UnsupportedReason is null&&InvoiceNotificationSnapshots.Phone(source.CustomerPhone)==binding!.Phone;}
            catch(KeyNotFoundException){valid=false;}
        }
        return valid;
    }
    public async Task<bool> RevalidateAsync(InvoiceNotificationWorkItem item,string currentConnectionBinding,CancellationToken ct)
    {
        var valid=await IsAuthorizedAsync(item,currentConnectionBinding,ct);
        var r=await Owned(item,ct);
        if(!valid){r.State=r.Attempts>1?"NeedsReview":"Blocked";r.Detail="source_or_recipient_changed";r.LeaseToken=null;r.LeaseExpiresAtUtc=null;r.UpdatedAtUtc=Now;Audit(r.StoreId,"DispatchBlocked",null,r.BindingId,r.Id,"source_or_recipient_changed");await db.SaveChangesAsync(ct);}
        return valid;
    }
    public async Task<InvoiceNotificationWorkItem> AttachDiscoveredAcceptedAsync(InvoiceNotificationWorkItem item,Guid jobId,CancellationToken ct)
    {
        if(jobId==Guid.Empty||jobId!=item.RequestId)throw new ArgumentException("Discovered job must match immutable request identity.");
        var row=await Owned(item,ct);
        if(row.PreparedJson is null||row.ZaloJobId is not null&&row.ZaloJobId!=jobId)
            throw new InvalidOperationException("Discovered job conflicts with durable request evidence.");
        row.ZaloJobId=jobId;row.State="Accepted";row.UpdatedAtUtc=Now;row.Detail="accepted_job_discovered";
        Audit(row.StoreId,"AcceptedJobDiscovered",null,row.BindingId,row.Id);await db.SaveChangesAsync(ct);return Work(row);
    }
    public async Task RecordAcceptedAsync(InvoiceNotificationWorkItem item,Guid zaloJobId,CancellationToken ct)
    {
        if(zaloJobId==Guid.Empty||zaloJobId!=item.RequestId)throw new ArgumentException("Accepted job must match the immutable request identity.");var r=await Owned(item,ct);
        if(r.ZaloJobId is not null&&r.ZaloJobId!=zaloJobId)throw new InvalidOperationException("Accepted Zalo job cannot change.");
        r.ZaloJobId=zaloJobId;r.State="Accepted";r.Detail=null;Release(r,TimeSpan.FromSeconds(2));Audit(r.StoreId,"ZaloJobAccepted",null,r.BindingId,r.Id);await db.SaveChangesAsync(ct);
    }
    public async Task RecordRetryAsync(InvoiceNotificationWorkItem item,string detail,TimeSpan delay,CancellationToken ct)
    {
        var r=await Owned(item,ct);r.State=r.ZaloJobId is not null?"Accepted":r.FirstAttemptAtUtc<Now.AddHours(-23)?"NeedsReview":"Pending";r.Detail=Safe(detail);Release(r,delay);await db.SaveChangesAsync(ct);
    }
    public Task RecordReviewAsync(InvoiceNotificationWorkItem item,string detail,CancellationToken ct)=>RecordPartsOutcomeAsync(item,"NeedsReview",InvoiceNotificationSnapshots.Json(new{code=Safe(detail)}),ct);
    public async Task RecordPartsOutcomeAsync(InvoiceNotificationWorkItem item,string state,string partsOutcomeJson,CancellationToken ct)
    {
        if(state is not ("Sent" or "PartiallySent" or "NeedsReview" or "Failed" or "Blocked" or "Cancelled"))throw new ArgumentException("Unsupported notification outcome.");
        using var json=JsonDocument.Parse(partsOutcomeJson);if(partsOutcomeJson.Length>256000)throw new ArgumentException("Outcome too large.");
        var r=await Owned(item,ct);r.State=state;r.PartsOutcomeJson=partsOutcomeJson;r.Detail=state;Release(r,TimeSpan.Zero);Audit(r.StoreId,"NotificationOutcome",null,r.BindingId,r.Id,state);await db.SaveChangesAsync(ct);
    }
    private void Release(InvoiceNotificationOutbox r,TimeSpan delay){r.LeaseToken=null;r.LeaseExpiresAtUtc=null;r.UpdatedAtUtc=Now;r.NextAttemptAtUtc=Now.AddSeconds(Math.Clamp(delay.TotalSeconds,0,3600));}
    private static string Safe(string value)=>new(value.Where(c=>!char.IsControl(c)).Take(250).ToArray());
}
