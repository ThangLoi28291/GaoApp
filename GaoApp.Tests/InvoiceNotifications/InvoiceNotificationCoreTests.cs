using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.InvoiceNotifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.InvoiceNotifications;

public sealed class InvoiceNotificationCoreTests
{
    [Fact]
    public async Task BindingRequiresTenantCustomerPhoneIncomingProofAndAuthenticatedActor()
    {
        await using var f=await Fixture.Create();
        var proof=f.Proof();var id=Guid.NewGuid();
        var b=await f.Service.VerifyBindingAsync(proof,id,default);
        Assert.Equal(b.Id,(await f.Service.VerifyBindingAsync(proof,id,default)).Id);
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.VerifyBindingAsync(proof with{Phone="0900000000"},Guid.NewGuid(),default));
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.VerifyBindingAsync(proof with{NativePeer="123"},Guid.NewGuid(),default));
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.VerifyBindingAsync(proof with{IncomingSequence=0},Guid.NewGuid(),default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.VerifyBindingAsync(proof with{StoreId=99},Guid.NewGuid(),default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.VerifyBindingAsync(proof with{ActorUserId=88},Guid.NewGuid(),default));
        Assert.Single(await f.Db.InvoiceRecipientBindings.ToListAsync());
        Assert.Single(await f.Db.InvoiceNotificationAudit.ToListAsync());
    }
    [Fact]
    public async Task CapturePreservesFractionalMoneyPointsPromotionsPaymentsAndDeduplicates()
    {
        await using var f=await Fixture.Create();await f.Bind();
        var capture=f.Capture;
        await capture.CaptureFinalizedOrderAsync(f.Order,12.75m,default);await f.Db.SaveChangesAsync();
        await capture.CaptureFinalizedOrderAsync(f.Order,999m,default);await f.Db.SaveChangesAsync();
        var row=Assert.Single(await f.Db.InvoiceNotificationOutbox.ToListAsync());
        var detail=await f.Service.FindAsync(1,row.Id,default);
        Assert.Equal(12.75m,detail!.Snapshot.CurrentPoints);
        Assert.Equal(99.25m,detail.Snapshot.GrandTotal);Assert.Equal(20.25m,detail.Snapshot.DepositAmount);
        Assert.Equal(3.25m,Assert.Single(detail.Snapshot.Lines).PromotionDiscount);
        Assert.Equal(1.5m,Assert.Single(detail.Snapshot.Lines).ComboAllocatedDiscount);
        Assert.Equal(1.125m,Assert.Single(detail.Snapshot.Lines).Quantity);
        Assert.Equal(79m,Assert.Single(detail.Snapshot.Payments).Amount);
        row.SnapshotJson="{}";await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Db.SaveChangesAsync());
    }
    [Fact]
    public async Task NullPointsRemainUnknownAndUtcSourceVersionSurvivesSqlDateKind()
    {
        await using var f=await Fixture.Create();await f.Bind();
        var capture=f.Capture;
        await capture.CaptureFinalizedOrderAsync(f.Order,null,default);await f.Db.SaveChangesAsync();
        var row=Assert.Single(await f.Db.InvoiceNotificationOutbox.ToListAsync());
        Assert.Null((await f.Service.FindAsync(1,row.Id,default))!.Snapshot.CurrentPoints);
        f.Order.Subtotal=120.00m;f.Order.PaidTotal=99.250m;
        f.Order.CompletedAtUtc=DateTime.SpecifyKind(f.Order.CompletedAtUtc!.Value,DateTimeKind.Unspecified);
        f.Db.Entry(f.Order).Property(x=>x.Subtotal).IsModified=true;f.Db.Entry(f.Order).Property(x=>x.CompletedAtUtc).IsModified=true;await f.Db.SaveChangesAsync();
        var b=Assert.Single(await f.Service.BindingsAsync(1,2,default));
        var id=await f.Service.EnqueueAsync(new(1,2,"Order",3,"Receipt",Guid.NewGuid(),7,b.Id,b.Version,row.SourceVersion),default);
        Assert.Equal(row.Id,id);
    }
    [Fact]
    public async Task DisabledPilotCannotClaimAndRevocationCancelsUnsentEvidence()
    {
        await using var f=await Fixture.Create(false);var b=await f.Bind();
        await f.Capture.CaptureFinalizedOrderAsync(f.Order,null,default);await f.Db.SaveChangesAsync();
        Assert.Empty(await f.Db.InvoiceNotificationOutbox.ToListAsync());
        await f.Service.EnqueueAsync(await f.Command(b.Id,b.Version),default);
        Assert.Null(await f.Service.ClaimAsync(default));
        await f.Service.RevokeBindingAsync(1,b.Id,b.Version,7,"Customer requested revocation",default);
        Assert.Equal("Cancelled",(await f.Db.InvoiceNotificationOutbox.SingleAsync()).State);
        Assert.Equal(2,(await f.Db.InvoiceRecipientBindings.SingleAsync()).Version);
        var revoked=await f.Command(b.Id,1);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.EnqueueAsync(revoked,default));
    }
    [Fact]
    public async Task ClaimRetryPreparedAndAcceptedKeepExactlyOneImmutableRequestAndJob()
    {
        await using var f=await Fixture.Create();var b=await f.Bind();
        var id=await f.Service.EnqueueAsync(await f.Command(b.Id,b.Version),default);
        var first=Assert.IsType<InvoiceNotificationWorkItem>(await f.Service.ClaimAsync(default));
        Assert.Equal(id,first.Id);Assert.Null(f.Db.Database.CurrentTransaction);
        var bundle=new InvoicePreparedBundle([new("Text","Synthetic invoice",null,null,null,null,null)],new string('A',64));
        first=await f.Service.SavePreparedAsync(first,bundle,default);
        await f.Service.RecordRetryAsync(first,"Synthetic pre-accept timeout",TimeSpan.Zero,default);
        var second=Assert.IsType<InvoiceNotificationWorkItem>(await f.Service.ClaimAsync(default));
        Assert.Equal(first.RequestId,second.RequestId);Assert.Equal(first.IdempotencyKey,second.IdempotencyKey);
        Assert.Equal(bundle.Fingerprint,second.Prepared!.Fingerprint);
        Assert.Equal(bundle.Parts.Select(p=>p.Text),second.Prepared.Parts.Select(p=>p.Text));
        Assert.True(await f.Service.RevalidateAsync(second,b.ConnectionBinding,default));
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.RecordAcceptedAsync(second,Guid.NewGuid(),default));
        await f.Service.RecordAcceptedAsync(second,second.RequestId,default);f.Clock.Advance(TimeSpan.FromSeconds(3));
        var poll=Assert.IsType<InvoiceNotificationWorkItem>(await f.Service.ClaimAsync(default));
        Assert.Equal("Accepted",poll.State);Assert.Equal(second.RequestId,poll.ZaloJobId);
        await f.Service.RecordPartsOutcomeAsync(poll,"Sent","{\"confirmedParts\":1}",default);
        Assert.Null(await f.Service.ClaimAsync(default));
        Assert.Equal("Sent",(await f.Db.InvoiceNotificationOutbox.SingleAsync()).State);
    }
    [Fact]
    public async Task ChangedSourceAndExpiredUnknownRequestNeverCreateANewKey()
    {
        await using var f=await Fixture.Create();var b=await f.Bind();
        await f.Service.EnqueueAsync(await f.Command(b.Id,b.Version),default);
        var first=Assert.IsType<InvoiceNotificationWorkItem>(await f.Service.ClaimAsync(default));
        await f.Service.RecordRetryAsync(first,"Synthetic unknown acceptance",TimeSpan.Zero,default);
        f.Clock.Advance(TimeSpan.FromHours(24));
        Assert.Null(await f.Service.ClaimAsync(default));
        Assert.Equal(first.RequestId,(await f.Db.InvoiceNotificationOutbox.SingleAsync()).RequestId);
        Assert.Equal("NeedsReview",(await f.Db.InvoiceNotificationOutbox.SingleAsync()).State);
        Assert.Null(await f.Service.ClaimAsync(default));
    }
    [Fact]
    public async Task SupplierSourcesAndChangedBindingCannotEnterRetailOutbox()
    {
        await using var f=await Fixture.Create();var b=await f.Bind();
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.EnqueueAsync(new(1,2,"InputInvoice",3,"OfficialPdf",Guid.NewGuid(),7,b.Id,b.Version,new string('A',64)),default));
        var foreign=await f.Command(Guid.NewGuid(),b.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.EnqueueAsync(foreign,default));
        Assert.Empty(await f.Db.InvoiceNotificationOutbox.ToListAsync());
    }
    [Fact]
    public async Task OfficialArtifactProofStagesSameSaveAndCancelledInvoiceStaysBlocked()
    {
        await using var f=await Fixture.Create();await f.Bind();
        var invoice=new InvoiceHead{Id=10,StoreId=1,OrderId=3,InvoiceNumber="SYN",ProviderInvoiceNo="SYN",ProviderStatus=InvoiceProviderStatus.Issued,IssuedAtUtc=DateTime.UtcNow,RowVersion=new byte[8]};
        f.Db.InvoiceHeads.Add(invoice);await f.Db.SaveChangesAsync();
        invoice.PdfFilePath="private/synthetic.pdf";invoice.OfficialPdfFileName="synthetic.pdf";invoice.OfficialPdfStatus=InvoiceFileDownloadStatus.Downloaded;
        invoice.OfficialPdfSha256=new string('B',64);invoice.OfficialPdfByteLength=3;
        await f.Db.SaveChangesAsync();
        var ready=Assert.Single(await f.Db.InvoiceNotificationOutbox.ToListAsync());Assert.Equal("Pending",ready.State);
        Assert.Equal(new string('B',64),(await f.Service.FindAsync(1,ready.Id,default))!.Snapshot.Artifact!.Sha256);
        invoice.ProviderStatus=InvoiceProviderStatus.Cancelled;await f.Db.SaveChangesAsync();
        Assert.Contains(await f.Db.InvoiceNotificationOutbox.ToListAsync(),x=>x.State=="Blocked"&&x.Detail=="invoice_cancelled");
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("revoked")]
    [InlineData("expired")]
    public async Task LostAcceptanceCanOnlyAdoptTheExactKnownRequestForCancellation(string cause)
    {
        await using var f=await Fixture.Create();var b=await f.Bind();
        await f.Service.EnqueueAsync(await f.Command(b.Id,b.Version),default);
        var item=Assert.IsType<InvoiceNotificationWorkItem>(await f.Service.ClaimAsync(default));
        item=await f.Service.SavePreparedAsync(item,new([new("Text","Synthetic",null,null,null,null,null)],new string('A',64)),default);
        await f.Service.RecordRetryAsync(item,"Synthetic lost acceptance",TimeSpan.Zero,default);
        if(cause=="disabled")f.Monitor.CurrentValue.Enabled=false;
        if(cause=="revoked")await f.Service.RevokeBindingAsync(1,b.Id,b.Version,7,"Synthetic revoked",default);
        if(cause=="expired")f.Clock.Advance(TimeSpan.FromHours(24));
        var recovery=Assert.IsType<InvoiceNotificationWorkItem>(await f.Service.ClaimAsync(default));
        Assert.False(await f.Service.IsAuthorizedAsync(recovery,b.ConnectionBinding,default));
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.AttachDiscoveredAcceptedAsync(recovery,Guid.NewGuid(),default));
        var attached=await f.Service.AttachDiscoveredAcceptedAsync(recovery,recovery.RequestId,default);
        Assert.Equal(recovery.LeaseToken,attached.LeaseToken);Assert.Equal(recovery.RequestId,attached.ZaloJobId);
        Assert.Equal("Accepted",attached.State);
        await f.Service.RecordPartsOutcomeAsync(attached,"Cancelled","{\"code\":\"synthetic_cancel_confirmed\"}",default);
        Assert.Null(await f.Service.ClaimAsync(default));
    }

    [Fact]
    public async Task ManualSourcePreviewVersionIsMandatoryAndChangedDocumentIsRejected()
    {
        await using var f=await Fixture.Create();var b=await f.Bind();
        var command=await f.Command(b.Id,b.Version);
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.EnqueueAsync(command with{ExpectedSourceVersion=""},default));
        f.Order.GrandTotal+=1m;await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.EnqueueAsync(command,default));
        Assert.Empty(await f.Db.InvoiceNotificationOutbox.ToListAsync());
    }

    [Fact]
    public async Task RevocationPreservesActivePossiblePostLeaseForLateAcceptedAcknowledgement()
    {
        await using var f=await Fixture.Create();var b=await f.Bind();
        await f.Service.EnqueueAsync(await f.Command(b.Id,b.Version),default);
        var item=Assert.IsType<InvoiceNotificationWorkItem>(await f.Service.ClaimAsync(default));
        item=await f.Service.SavePreparedAsync(item,new([new("Text","Synthetic",null,null,null,null,null)],new string('A',64)),default);
        await f.Service.RevokeBindingAsync(1,b.Id,b.Version,7,"Synthetic revoked during HTTP",default);
        Assert.Equal(item.LeaseToken,(await f.Db.InvoiceNotificationOutbox.SingleAsync()).LeaseToken);
        Assert.Null(await f.Service.ClaimAsync(default));
        await f.Service.RecordAcceptedAsync(item,item.RequestId,default);f.Clock.Advance(TimeSpan.FromSeconds(3));
        var poll=Assert.IsType<InvoiceNotificationWorkItem>(await f.Service.ClaimAsync(default));
        Assert.Equal(item.RequestId,poll.ZaloJobId);Assert.False(await f.Service.IsAuthorizedAsync(poll,b.ConnectionBinding,default));
    }

    [Theory]
    [InlineData("amount")][InlineData("status")][InlineData("phone")][InlineData("customer")][InlineData("store")][InlineData("line")][InlineData("payment")]
    public async Task FinalAuthorizationReadsExternalSourceChangesAfterInitialCheck(string field)
    {
        await using var f=await Fixture.Create();var b=await f.Bind();
        await f.Service.EnqueueAsync(await f.Command(b.Id,b.Version),default);
        var item=Assert.IsType<InvoiceNotificationWorkItem>(await f.Service.ClaimAsync(default));
        Assert.True(await f.Service.IsAuthorizedAsync(item,b.ConnectionBinding,default));
        await using(var other=f.Fresh())
        {
            if(field is "amount" or "status") {var order=await other.Orders.SingleAsync(x=>x.Id==3);if(field=="amount")order.GrandTotal+=1m;else order.Status=OrderStatus.Voided;}
            if(field is "phone" or "customer"){var customer=await other.Customers.SingleAsync(x=>x.Id==2);if(field=="phone")customer.Phone="0900000000";else customer.IsActive=false;}
            if(field=="store")(await other.Stores.SingleAsync(x=>x.Id==1)).IsActive=false;
            if(field=="line")(await other.OrderLines.SingleAsync(x=>x.Id==4)).LineTotal+=1m;
            if(field=="payment")(await other.OrderPayments.SingleAsync(x=>x.Id==5)).Amount+=1m;
            await other.SaveChangesAsync();
        }
        Assert.False(await f.Service.IsAuthorizedAsync(item,b.ConnectionBinding,default));
    }
    [Fact]
    public async Task OfficialPdfVersionSurvivesOtherFileAndCreditProgressButDetectsChangedArtifact()
    {
        await using var f=await Fixture.Create();var b=await f.Bind();
        var invoice=new InvoiceHead{Id=10,StoreId=1,OrderId=3,ProviderInvoiceNo="SYN",ProviderStatus=InvoiceProviderStatus.Issued,IssuedAtUtc=DateTime.UtcNow,RowVersion=new byte[8],
            PdfFilePath="private/synthetic.pdf",OfficialPdfFileName="synthetic.pdf",OfficialPdfStatus=InvoiceFileDownloadStatus.Downloaded,OfficialPdfSha256=new string('B',64),OfficialPdfByteLength=3};
        f.Db.InvoiceHeads.Add(invoice);await f.Db.SaveChangesAsync();
        var preview=await f.Service.PreviewSourceAsync(1,2,"Invoice",10,"OfficialPdf",default);
        await f.Service.EnqueueAsync(new(1,2,"Invoice",10,"OfficialPdf",Guid.NewGuid(),7,b.Id,b.Version,preview.SourceVersion),default);
        var item=Assert.IsType<InvoiceNotificationWorkItem>(await f.Service.ClaimAsync(default));
        Assert.True(await f.Service.IsAuthorizedAsync(item,b.ConnectionBinding,default));
        await using(var other=f.Fresh())
        {
            var changed=await other.InvoiceHeads.SingleAsync(x=>x.Id==10);changed.ProviderStatus=InvoiceProviderStatus.ZipDownloaded;
            changed.OfficialZipXmlStatus=InvoiceFileDownloadStatus.Downloaded;changed.OfficialZipXmlSha256=new string('C',64);changed.OfficialZipXmlByteLength=5;changed.OfficialZipXmlFileName="synthetic.zip";changed.ZipFilePath="private/synthetic.zip";
            var order=await other.Orders.SingleAsync(x=>x.Id==3);order.PaidTotal+=1m;order.BalanceDue-=1m;order.PaymentStatus=PaymentStatus.PartiallyPaid;
            (await other.OrderPayments.SingleAsync(x=>x.Id==5)).Amount+=1m;
            await other.SaveChangesAsync();
        }
        Assert.Equal(preview.SourceVersion,(await f.Service.PreviewSourceAsync(1,2,"Invoice",10,"OfficialPdf",default)).SourceVersion);
        Assert.True(await f.Service.IsAuthorizedAsync(item,b.ConnectionBinding,default));
        await using(var other=f.Fresh()){(await other.InvoiceHeads.SingleAsync(x=>x.Id==10)).OfficialPdfSha256=new string('D',64);await other.SaveChangesAsync();}
        Assert.False(await f.Service.IsAuthorizedAsync(item,b.ConnectionBinding,default));
    }
    [Fact]
    public async Task ExternalRevocationPreservesActiveLeaseAndLateAcceptanceCanRefreshDurableRow()
    {
        await using var f=await Fixture.Create();var b=await f.Bind();await f.Service.EnqueueAsync(await f.Command(b.Id,b.Version),default);
        var item=Assert.IsType<InvoiceNotificationWorkItem>(await f.Service.ClaimAsync(default));
        item=await f.Service.SavePreparedAsync(item,new([new("Text","Synthetic",null,null,null,null,null)],new string('A',64)),default);
        Assert.True(await f.Service.IsAuthorizedAsync(item,b.ConnectionBinding,default));
        await using(var other=f.Fresh())await new EfInvoiceNotificationStore(other,Options.Create(new InvoiceNotificationOptions{Enabled=true}),f.Clock).RevokeBindingAsync(1,b.Id,b.Version,7,"Synthetic external revoke",default);
        Assert.False(await f.Service.IsAuthorizedAsync(item,b.ConnectionBinding,default));
        await f.Service.RecordAcceptedAsync(item,item.RequestId,default);
        Assert.Equal("Accepted",(await f.Db.InvoiceNotificationOutbox.AsNoTracking().SingleAsync()).State);
    }

    [Theory]
    [InlineData("OfficialLink")][InlineData("CorrectionLink")]
    public async Task OfficialLinkAuthorizesOnlyItsExactPdfAndBindingWithoutActorOrLease(string kind)
    {
        await using var f=await Fixture.Create();var b=await f.Bind();await SeedLinkInvoice(f,kind=="CorrectionLink");
        var links=new LinkMonitor("https://invoices.example.test/gao/");var service=f.LinkService(links);
        var preview=await service.PreviewSourceAsync(1,2,"Invoice",10,kind,default);Assert.Null(preview.UnsupportedReason);
        var id=await service.EnqueueAsync(new(1,2,"Invoice",10,kind,Guid.NewGuid(),7,b.Id,b.Version,preview.SourceVersion),default);
        var grant=Grant(f,b,preview,id);
        await using var anonymous=new AppDbContext(f.DatabaseOptions,new Tenant(),new AnonymousUser());
        var reader=new EfInvoiceNotificationStore(anonymous,Options.Create(new InvoiceNotificationOptions{Enabled=true}),f.Clock,f.Monitor,links);
        var access=await reader.AuthorizeAsync(grant,default);Assert.NotNull(access);
        Assert.Equal("private/synthetic.pdf",access!.Snapshot.Artifact!.StoragePath);Assert.Equal(b.Id,access.Binding.Id);
        Assert.Empty(anonymous.ChangeTracker.Entries());Assert.Null(anonymous.Database.CurrentTransaction);
        Assert.Null(await reader.AuthorizeAsync(grant with{StoreId=99},default));
        Assert.Null(await reader.AuthorizeAsync(grant with{CustomerId=99},default));
        Assert.Null(await reader.AuthorizeAsync(grant with{NotificationId=Guid.NewGuid()},default));
        Assert.Null(await reader.AuthorizeAsync(grant with{SourceVersion=new string('A',64)},default));
        Assert.Null(await reader.AuthorizeAsync(grant with{Audience="https://another.example.test/"},default));
        Assert.Null(await reader.AuthorizeAsync(grant with{ExpiresAtUtc=f.Clock.GetUtcNow().AddSeconds(-1)},default));
        Assert.Null(await reader.AuthorizeAsync(grant with{ExpiresAtUtc=f.Clock.GetUtcNow().AddHours(73)},default));
        Assert.Null(await reader.AuthorizeAsync(grant with{Pdf=grant.Pdf with{Sha256=new string('C',64)}},default));
        Assert.Null(await reader.AuthorizeAsync(grant with{Pdf=grant.Pdf with{FileName="../synthetic.pdf"}},default));
    }
    [Fact]
    public async Task LinkIsUnavailableByDefaultAndPublicAudienceChangesInvalidatePreparedSource()
    {
        await using var f=await Fixture.Create();var b=await f.Bind();await SeedLinkInvoice(f,false);
        var unavailable=await f.Service.PreviewSourceAsync(1,2,"Invoice",10,"OfficialLink",default);
        Assert.Equal("official_link_not_configured",unavailable.UnsupportedReason);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.EnqueueAsync(new(1,2,"Invoice",10,"OfficialLink",Guid.NewGuid(),7,b.Id,b.Version,unavailable.SourceVersion),default));
        var links=new LinkMonitor("https://invoices.example.test/gao/");var service=f.LinkService(links);
        var preview=await service.PreviewSourceAsync(1,2,"Invoice",10,"OfficialLink",default);
        var id=await service.EnqueueAsync(new(1,2,"Invoice",10,"OfficialLink",Guid.NewGuid(),7,b.Id,b.Version,preview.SourceVersion),default);
        var item=Assert.IsType<InvoiceNotificationWorkItem>(await service.ClaimAsync(default));
        Assert.True(await service.IsAuthorizedAsync(item,b.ConnectionBinding,default));
        var grant=Grant(f,b,preview,id);Assert.NotNull(await service.AuthorizeAsync(grant,default));
        links.CurrentValue.PublicBaseUrl="https://changed.example.test/gao/";
        Assert.NotEqual(preview.SourceVersion,(await service.PreviewSourceAsync(1,2,"Invoice",10,"OfficialLink",default)).SourceVersion);
        Assert.False(await service.IsAuthorizedAsync(item,b.ConnectionBinding,default));Assert.Null(await service.AuthorizeAsync(grant,default));
    }
    [Theory]
    [InlineData("phone")][InlineData("customer")][InlineData("store")][InlineData("pdf")][InlineData("cancelled")][InlineData("revoked")][InlineData("disabled")]
    public async Task PublicLinkRejectsFreshExternalRevocationOrSourceChanges(string change)
    {
        await using var f=await Fixture.Create();var b=await f.Bind();await SeedLinkInvoice(f,false);
        var service=f.LinkService(new LinkMonitor("https://invoices.example.test/gao/"));
        var preview=await service.PreviewSourceAsync(1,2,"Invoice",10,"OfficialLink",default);
        var id=await service.EnqueueAsync(new(1,2,"Invoice",10,"OfficialLink",Guid.NewGuid(),7,b.Id,b.Version,preview.SourceVersion),default);var grant=Grant(f,b,preview,id);
        Assert.NotNull(await service.AuthorizeAsync(grant,default));
        if(change=="disabled")f.Monitor.CurrentValue.Enabled=false;
        else
        {
            await using var other=f.Fresh();
            if(change is "phone" or "customer"){var customer=await other.Customers.SingleAsync(x=>x.Id==2);if(change=="phone")customer.Phone="0900000000";else customer.IsActive=false;}
            if(change=="store")(await other.Stores.SingleAsync(x=>x.Id==1)).IsActive=false;
            if(change is "pdf" or "cancelled"){var invoice=await other.InvoiceHeads.SingleAsync(x=>x.Id==10);if(change=="pdf")invoice.OfficialPdfSha256=new string('C',64);else invoice.ProviderStatus=InvoiceProviderStatus.Cancelled;}
            if(change=="revoked")await new EfInvoiceNotificationStore(other,Options.Create(new InvoiceNotificationOptions{Enabled=true}),f.Clock).RevokeBindingAsync(1,b.Id,b.Version,7,"Synthetic revoke",default);
            else await other.SaveChangesAsync();
        }
        Assert.Null(await service.AuthorizeAsync(grant,default));
    }
    private static InvoiceOfficialDocumentLinkGrant Grant(Fixture f,InvoiceRecipientBindingSnapshot b,InvoiceNotificationSourcePreview preview,Guid id)=>
        new(1,id,1,2,"Invoice",10,preview.SourceVersion,preview.Kind,b.Id,b.Version,"https://invoices.example.test/gao/",f.Clock.GetUtcNow().AddHours(1),new("synthetic.pdf","application/pdf",3,new string('B',64)));
    private static async Task SeedLinkInvoice(Fixture f,bool correction)
    {
        if(correction)f.Db.InvoiceHeads.Add(new InvoiceHead{Id=9,StoreId=1,OrderId=3,ProviderInvoiceNo="SYN-ORIGINAL",ProviderStatus=InvoiceProviderStatus.Issued,IssuedAtUtc=DateTime.UtcNow,RowVersion=new byte[8]});
        f.Db.InvoiceHeads.Add(new InvoiceHead{Id=10,StoreId=1,OrderId=3,ProviderInvoiceNo="SYN",ProviderStatus=InvoiceProviderStatus.Issued,IssuedAtUtc=DateTime.UtcNow,RowVersion=new byte[8],
            OriginalInvoiceHeadId=correction?9:null,OriginalInvoiceNo=correction?"SYN-ORIGINAL":null,CorrectionType=correction?InvoiceCorrectionType.Replacement:null,
            PdfFilePath="private/synthetic.pdf",OfficialPdfFileName="synthetic.pdf",OfficialPdfStatus=InvoiceFileDownloadStatus.Downloaded,OfficialPdfSha256=new string('B',64),OfficialPdfByteLength=3});
        await f.Db.SaveChangesAsync();
    }

    private sealed class Fixture: IAsyncDisposable
    {
        public required AppDbContext Db{get;init;}
        public required DbContextOptions<AppDbContext> DatabaseOptions{get;init;}
        public AppDbContext Fresh()=>new(DatabaseOptions,new Tenant(),new User());
        public required EfInvoiceNotificationStore Service{get;init;}
        public required Order Order{get;init;}
        public required Clock Clock{get;init;}
        public required Monitor Monitor{get;init;}
        public bool Enabled{get;init;}
        public EfInvoiceNotificationStore LinkService(LinkMonitor links)=>new(Db,Options.Create(new InvoiceNotificationOptions{Enabled=Enabled}),Clock,Monitor,links);
        public EfInvoiceNotificationCapture Capture=>new(Db,Options.Create(new InvoiceNotificationOptions{Enabled=Enabled}));
        public InvoiceRecipientBindingProof Proof()=>new(1,2,"pilot-connection",new string('C',64),Guid.Parse("11111111-1111-1111-1111-111111111111"),Guid.Parse("22222222-2222-2222-2222-222222222222"),123,"sdk-v1:123","0398946174",7,"in_person","Synthetic phone owner verification");
        public Task<InvoiceRecipientBindingSnapshot> Bind()=>Service.VerifyBindingAsync(Proof(),Guid.NewGuid(),default);
        public async Task<InvoiceNotificationEnqueueCommand> Command(Guid binding,int version)=>new(1,2,"Order",3,"Receipt",Guid.NewGuid(),7,binding,version,(await Service.PreviewSourceAsync(1,2,"Order",3,"Receipt",default)).SourceVersion);
        public static async Task<Fixture>Create(bool enabled=true)
        {
            var databaseOptions=new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("P3Synthetic_"+Guid.NewGuid()).Options;
            var db=new AppDbContext(databaseOptions,new Tenant(),new User(),Options.Create(new InvoiceNotificationOptions{Enabled=enabled}));
            var order=new Order{Id=3,StoreId=1,CustomerId=2,POSShiftId=1,OrderNumber="SYN-3",Status=OrderStatus.Completed,PaymentStatus=PaymentStatus.Paid,CompletedAtUtc=DateTime.UtcNow,
                Subtotal=120m,DiscountTotal=20.75m,GrandTotal=99.25m,PaidTotal=99.25m,DepositAmount=20.25m,
                Lines=[new OrderLine{Id=4,StoreId=1,OrderId=3,ItemName="Synthetic item",Quantity=1.125m,UnitPrice=100m,LineDiscount=10m,PromotionDiscount=3.25m,ComboAllocatedDiscount=1.5m,LineTotal=99.25m}],
                Payments=[new OrderPayment{Id=5,StoreId=1,OrderId=3,Method=PaymentMethod.Cash,Amount=79m}]};
            db.AddRange(new Store{Id=1,Name="Synthetic shop",SubDomain="p3-synthetic",SubDomainNormalized="P3-SYNTHETIC"},new Customer{Id=2,StoreId=1,Name="Synthetic customer",Phone="0398946174"},order);
            foreach(var entry in db.ChangeTracker.Entries().Where(e=>e.Metadata.FindProperty("RowVersion") is not null))entry.Property("RowVersion").CurrentValue=new byte[8];
            await db.SaveChangesAsync();var clock=new Clock();var monitor=new Monitor(new InvoiceNotificationOptions{Enabled=enabled});
            return new(){Db=db,DatabaseOptions=databaseOptions,Order=order,Clock=clock,Monitor=monitor,Enabled=enabled,Service=new EfInvoiceNotificationStore(db,Options.Create(new InvoiceNotificationOptions{Enabled=enabled}),clock,monitor)};
        }
        public ValueTask DisposeAsync()=>Db.DisposeAsync();
    }
    private sealed class Tenant:ITenantContext{public int? StoreId=>1;public bool IsHostAdmin=>false;public string? Subdomain=>"p3-synthetic";}
    private sealed class User:ICurrentUser{public int? UserId=>7;public string? UserName=>"Synthetic operator";public int? TerminalId=>null;public string? TerminalCode=>null;public bool IsAuthenticated=>true;}
    private sealed class AnonymousUser:ICurrentUser{public int? UserId=>null;public string? UserName=>null;public int? TerminalId=>null;public string? TerminalCode=>null;public bool IsAuthenticated=>false;}
    private sealed class LinkMonitor(string url):IOptionsMonitor<InvoiceOfficialDocumentLinkOptions>{public InvoiceOfficialDocumentLinkOptions CurrentValue{get;}=new(){PublicBaseUrl=url};public InvoiceOfficialDocumentLinkOptions Get(string? name)=>CurrentValue;public IDisposable? OnChange(Action<InvoiceOfficialDocumentLinkOptions,string?> listener)=>null;}
    private sealed class Monitor(InvoiceNotificationOptions settings):IOptionsMonitor<InvoiceNotificationOptions>{public InvoiceNotificationOptions CurrentValue=>settings;public InvoiceNotificationOptions Get(string? name)=>settings;public IDisposable? OnChange(Action<InvoiceNotificationOptions,string?> listener)=>null;}
    private sealed class Clock:TimeProvider{private TimeSpan offset=TimeSpan.FromSeconds(1);public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow+offset;public void Advance(TimeSpan duration)=>offset+=duration;}
}
