using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Security;

public sealed class CustomerInvoiceNotificationAdminServiceTests
{
    private static readonly CustomerStaff Staff=new(1,1,"TEST OPERATOR",true,true,true);
    [Fact]
    public async Task Source_picker_keeps_each_legal_invoice_ID_and_blocks_foreign_legacy_group_and_draft_sources()
    {
        await using var fixture=await Fixture.Create();
        var sources=await fixture.Service.SourcesAsync(Staff,11,CancellationToken.None);
        Assert.Contains(sources,row=>row.SourceType=="Invoice"&&row.SourceId==101&&row.Kind=="OfficialPdf"&&row.CanQueue&&row.LegalOwner=="TEST LEGAL ONE");
        Assert.Contains(sources,row=>row.SourceType=="Invoice"&&row.SourceId==102&&row.Kind=="OfficialPdf"&&row.CanQueue&&row.LegalOwner=="TEST LEGAL TWO");
        Assert.Contains(sources,row=>row.SourceId==101&&row.Kind=="OfficialXml"&&row.CanQueue&&row.Label.Contains("XML gốc"));
        Assert.DoesNotContain(sources,row=>row.SourceId==201);
        Assert.DoesNotContain("FOREIGN LEGAL",JsonSerializer.Serialize(sources));
        foreach(var id in new[]{103,104,105,106})Assert.All(sources.Where(row=>row.SourceId==id),row=>{Assert.False(row.CanQueue);Assert.NotNull(row.BlockedReason);});
        var foreign=await Assert.ThrowsAsync<AssistantException>(()=>fixture.Service.SourcesAsync(Staff,22,CancellationToken.None));Assert.Equal(404,foreign.Status);
    }

    [Fact]
    public async Task Selected_source_preview_returns_exact_version_and_safe_owner_for_manual_original_XML()
    {
        await using var fixture=await Fixture.Create();
        var preview=await fixture.Service.PreviewAsync(Staff,11,"Invoice",101,"OfficialXml",CancellationToken.None);
        Assert.Equal(new string('A',64),preview.SourceVersion);Assert.Equal(101,preview.SourceId);Assert.Equal("OfficialXml",preview.Kind);
        Assert.Equal("TEST LEGAL ONE",preview.LegalOwner);Assert.Contains("XML gốc",preview.Label);Assert.True(preview.CanQueue);
        Assert.DoesNotContain("storage",JsonSerializer.Serialize(preview),StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Exact_code_lookup_reaches_an_older_owned_order_and_keeps_all_its_legal_invoices()
    {
        await using var fixture=await Fixture.Create();var db=fixture.Db;
        db.Orders.Add(new Order{Id=30,StoreId=1,CustomerId=11,OrderNumber="OLDER-ORDER",CompletedAtUtc=DateTime.UtcNow.AddYears(-1),Status=OrderStatus.Completed});
        foreach(var index in Enumerable.Range(1,11))db.Orders.Add(new Order{Id=40+index,StoreId=1,CustomerId=11,OrderNumber="RECENT-"+index,CompletedAtUtc=DateTime.UtcNow.AddMinutes(-index),Status=OrderStatus.Completed});
        foreach(var id in new[]{301,302})db.InvoiceHeads.Add(new InvoiceHead{Id=id,StoreId=1,OrderId=30,LegalEntityId=id==301?1:2,InvoiceNumber=id==301?"OLDER-BILL":"SECOND-OWNER-BILL",ProviderStatus=InvoiceProviderStatus.LocalDraft});
        foreach(var entry in db.ChangeTracker.Entries())if(entry.Metadata.FindProperty("RowVersion") is not null)entry.Property("RowVersion").CurrentValue=new byte[8];
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var recent=await fixture.Service.SourcesAsync(Staff,11,CancellationToken.None);Assert.DoesNotContain(recent,row=>row.SourceId is 301 or 302);
        var older=await fixture.Service.LookupSourcesAsync(Staff,11,"OLDER-BILL",CancellationToken.None);
        Assert.Contains(older,row=>row.SourceType=="Invoice"&&row.SourceId==301);Assert.Contains(older,row=>row.SourceType=="Invoice"&&row.SourceId==302);
        Assert.Empty(await fixture.Service.LookupSourcesAsync(Staff,11,"FOREIGN-ORDER",CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_drops_private_paths_storage_keys_extra_wire_fields_and_preserves_receipt_strings(bool flatTransportOutcome)
    {
        await using var fixture=await Fixture.Create();var id=Guid.NewGuid();
        fixture.Store.Detail=new(new(id,1,11,"Invoice",101,"VERSION","OfficialPdf","TEST BILL","NeedsReview","source_or_recipient_changed",DateTime.UtcNow,null,Guid.NewGuid(),1),null,
            new InvoiceDocumentSnapshot{StoreId=1,CustomerId=11,SourceType="Invoice",SourceId=101,Code="TEST BILL",Status="Issued",GrandTotal=50000,
                Artifact=new("E:\\PRIVATE_STORAGE\\SECRET.pdf","TEST.pdf","application/pdf",50,new string('A',64))},
            new([new("Text","TEST BILL",null,null,null,null,null),new("File",null,"E:\\PRIVATE_STORAGE\\TEST.pdf","application/pdf",50,new string('B',64),"PRIVATE_STORAGE_SECRET_KEY")],"FINGERPRINT"),
            "{\"privateSecret\":\"SECRET\",\"deliveries\":[{\"parts\":[{\"index\":1,\"kind\":\"Text\",\"state\":\"Sent\",\"receiptId\":\"900719925474099312345\"},{\"index\":2,\"kind\":\"File\",\"state\":\"Uncertain\",\"detail\":\"timeout\"}]}]}");
        if(flatTransportOutcome)
        {
            using var document=JsonDocument.Parse(fixture.Store.Detail.PartsOutcomeJson!);
            fixture.Store.Detail=fixture.Store.Detail with{PartsOutcomeJson=document.RootElement.GetProperty("deliveries")[0].GetRawText()};
        }
        var detail=await fixture.Service.DetailAsync(Staff,id,CancellationToken.None);Assert.NotNull(detail);
        var json=JsonSerializer.Serialize(detail,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("PRIVATE_STORAGE",json);Assert.DoesNotContain("SECRET",json);Assert.DoesNotContain("storagePath",json);Assert.DoesNotContain("storageKey",json);Assert.DoesNotContain("privateSecret",json);
        Assert.Equal("900719925474099312345",detail.Parts.Single(row=>row.Index==1).ReceiptId);
        Assert.Equal("Uncertain",detail.Parts.Single(row=>row.Index==2).State);Assert.Equal("TEST.pdf",detail.Parts.Single(row=>row.Index==2).FileName);
        Assert.Contains("Nguồn chứng từ hoặc liên kết",detail.Item.Reason);
    }

    [Theory]
    [InlineData(false,"in_person")]
    [InlineData(true,"phone_match")]
    public async Task Binding_requires_explicit_attestation_and_an_allowed_real_verification_method(bool confirmed,string method)
    {
        await using var fixture=await Fixture.Create();
        var command=new CustomerInvoiceBindingCommand(Guid.NewGuid(),11,"zalo-test",Guid.NewGuid(),42,method,"TEST verification reference",confirmed);
        var error=await Assert.ThrowsAsync<AssistantException>(()=>fixture.Service.VerifyAsync(Staff,command,CancellationToken.None));Assert.Equal(400,error.Status);
        Assert.Equal(0,fixture.Store.Verifications);
    }

    [Fact]
    public async Task Monitor_converts_zero_based_UI_pages_to_one_based_durable_pages()
    {
        await using var fixture=await Fixture.Create();
        await fixture.Service.OutboxAsync(Staff,"NeedsReview",0,CancellationToken.None);Assert.Equal(1,fixture.Store.LastPage);Assert.Equal(1,fixture.Store.LastStore);
        await fixture.Service.OutboxAsync(Staff,"NeedsReview",1,CancellationToken.None);Assert.Equal(2,fixture.Store.LastPage);
    }

    [Fact]
    public async Task Verifier_with_reply_but_without_integration_can_read_receipts_but_cannot_discover_preview_detail_or_enqueue_official_invoices()
    {
        await using var fixture=await Fixture.Create(integration:false);var ct=CancellationToken.None;
        var sources=await fixture.Service.SourcesAsync(Staff,11,ct);Assert.NotEmpty(sources);Assert.All(sources,row=>Assert.Equal("Order",row.SourceType));
        Assert.Empty(await fixture.Service.LookupSourcesAsync(Staff,11,"TEST-101",ct));
        Assert.Equal("Receipt",(await fixture.Service.PreviewAsync(Staff,11,"Order",10,"Receipt",ct)).Kind);
        var invoicePreview=await Assert.ThrowsAsync<AssistantException>(()=>fixture.Service.PreviewAsync(Staff,11,"Invoice",101,"OfficialPdf",ct));Assert.Equal(403,invoicePreview.Status);
        var invoiceSend=await Assert.ThrowsAsync<AssistantException>(()=>fixture.Service.EnqueueAsync(Staff,new(Guid.NewGuid(),11,"Invoice",101,"OfficialPdf",Guid.NewGuid(),1,new string('A',64),true),ct));Assert.Equal(403,invoiceSend.Status);
        Assert.Equal(0,fixture.Store.Enqueues);
        var receipt=Guid.NewGuid();var invoice=Guid.NewGuid();
        fixture.Db.InvoiceNotificationOutbox.Add(new(){Id=receipt,StoreId=1,CustomerId=11,SourceType="Order",SourceId=10,Kind="Receipt",Code="VISIBLE RECEIPT",CreatedAtUtc=DateTime.UtcNow.AddDays(-1)});
        foreach(var index in Enumerable.Range(0,31))fixture.Db.InvoiceNotificationOutbox.Add(new(){Id=index==0?invoice:Guid.NewGuid(),StoreId=1,CustomerId=11,SourceType="Invoice",SourceId=101,Kind="OfficialPdf",Code="PRIVATE OFFICIAL BILL",CreatedAtUtc=DateTime.UtcNow});
        await fixture.Db.SaveChangesAsync();fixture.Db.ChangeTracker.Clear();
        var monitor=await fixture.Service.OutboxAsync(Staff,null,0,ct);Assert.Equal(receipt,Assert.Single(monitor).Id);Assert.Equal(0,fixture.Store.ListCalls);
        var invoiceDetail=await Assert.ThrowsAsync<AssistantException>(()=>fixture.Service.DetailAsync(Staff,invoice,ct));Assert.Equal(403,invoiceDetail.Status);Assert.Equal(0,fixture.Store.FindCalls);
        fixture.Store.Detail=new(new(receipt,1,11,"Order",10,"VERSION","Receipt","VISIBLE RECEIPT","Pending",null,DateTime.UtcNow,null,null,0),null,
            new InvoiceDocumentSnapshot{StoreId=1,CustomerId=11,SourceType="Order",SourceId=10,Kind="Receipt",GrandTotal=50000},null,null);
        Assert.Equal("Receipt",(await fixture.Service.DetailAsync(Staff,receipt,ct))!.Item.Kind);
    }

    [Fact]
    public async Task Official_invoice_access_is_rechecked_after_source_read_and_host_admin_keeps_the_existing_bypass()
    {
        await using(var fixture=await Fixture.Create())
        {
            fixture.Store.BeforePreview=async()=>
            {
                var revoked=await fixture.Db.Set<RolePermission>().Where(row=>row.Permission.Code==PermissionCodes.System.Integration.Manage).ToArrayAsync();
                fixture.Db.RemoveRange(revoked);await fixture.Db.SaveChangesAsync();fixture.Db.ChangeTracker.Clear();
            };
            var error=await Assert.ThrowsAsync<AssistantException>(()=>fixture.Service.PreviewAsync(Staff,11,"Invoice",101,"OfficialPdf",CancellationToken.None));Assert.Equal(403,error.Status);
        }
        await using var host=await Fixture.Create(integration:false,hostAdmin:true);
        Assert.Equal("OfficialPdf",(await host.Service.PreviewAsync(Staff,11,"Invoice",101,"OfficialPdf",CancellationToken.None)).Kind);
    }

    [Theory]
    [InlineData(null,false)]
    [InlineData("https://invoice-test.example/",true)]
    public async Task Official_link_is_explicitly_blocked_without_a_public_origin_while_existing_files_remain_available(string? origin,bool configured)
    {
        await using var fixture=await Fixture.Create(linkBaseUrl:origin);
        var rows=await fixture.Service.SourcesAsync(Staff,11,CancellationToken.None);
        var link=Assert.Single(rows,row=>row.SourceId==101&&row.Kind=="OfficialLink");Assert.Equal(configured,link.CanQueue);Assert.Contains("liên kết",link.Label);
        Assert.True(Assert.Single(rows,row=>row.SourceId==101&&row.Kind=="OfficialPdf").CanQueue);
        var preview=await fixture.Service.PreviewAsync(Staff,11,"Invoice",101,"OfficialLink",CancellationToken.None);Assert.Equal(configured,preview.CanQueue);
        Assert.Equal(new string('A',64),preview.SourceVersion);
        if(!configured)
        {
            Assert.Equal("Chưa cấu hình địa chỉ xem hóa đơn.",link.BlockedReason);Assert.Equal(link.BlockedReason,preview.BlockedReason);
            var error=await Assert.ThrowsAsync<AssistantException>(()=>fixture.Service.EnqueueAsync(Staff,new(Guid.NewGuid(),11,"Invoice",101,"OfficialLink",Guid.NewGuid(),1,new string('A',64),true),CancellationToken.None));
            Assert.Equal(409,error.Status);Assert.Equal(0,fixture.Store.Enqueues);
        }
    }

    [Fact]
    public async Task Official_link_needs_the_official_PDF_proof_even_when_the_ZIP_is_ready()
    {
        await using var fixture=await Fixture.Create(linkBaseUrl:"https://invoice-test.example/");
        var invoice=await fixture.Db.InvoiceHeads.SingleAsync(row=>row.Id==101);invoice.OfficialPdfSha256=null;
        await fixture.Db.SaveChangesAsync();fixture.Db.ChangeTracker.Clear();
        var rows=await fixture.Service.SourcesAsync(Staff,11,CancellationToken.None);
        Assert.False(Assert.Single(rows,row=>row.SourceId==101&&row.Kind=="OfficialLink").CanQueue);
        Assert.True(Assert.Single(rows,row=>row.SourceId==101&&row.Kind=="OfficialZipXml").CanQueue);
    }

    private sealed class Fixture(AppDbContext db,FakeStore store,CustomerInvoiceNotificationAdminService service):IAsyncDisposable
    {
        public AppDbContext Db=>db;public FakeStore Store=>store;public CustomerInvoiceNotificationAdminService Service=>service;
        public static async Task<Fixture> Create(bool integration=true,bool hostAdmin=false,string? linkBaseUrl=null)
        {
            var tenant=new Tenant();var user=new UserContext();var name="InvoiceAdminService_"+Guid.NewGuid().ToString("N");var memory=new InMemoryDatabaseRoot();
            var options=new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name,memory).Options;
            var db=new AppDbContext(options,tenant,user);
            db.Stores.AddRange(new Store{Id=1,Name="TEST STORE",SubDomain="test",SubDomainNormalized="TEST"},new Store{Id=2,Name="TEST FOREIGN",SubDomain="other",SubDomainNormalized="OTHER"});
            db.Users.Add(new User{Id=1,UserName="TEST",PasswordHash="SYNTHETIC-TEST-ONLY",IsHostAdmin=hostAdmin});
            var rights=new[]{CustomerServiceRights.View,CustomerServiceRights.Reply,CustomerServiceRights.Verify,PermissionCodes.Catalog.Customer.View,PermissionCodes.Pos.Order.View}
                .Concat(integration?[PermissionCodes.System.Integration.Manage]:Array.Empty<string>()).ToArray();
            db.Set<Permission>().AddRange(rights.Select((code,index)=>new Permission{Id=index+1,Code=code,Name=code,GroupName="TEST"}));
            var role=new Role{Id=1,StoreId=1,Code="TEST-OPERATOR",Name="TEST OPERATOR",RolePermissions=rights.Select((_,index)=>new RolePermission{PermissionId=index+1}).ToList()};
            db.Set<Role>().Add(role);db.UserInStores.Add(new UserInStore{Id=1,StoreId=1,UserId=1,Role=role,IsActive=true});
            db.Customers.AddRange(new Customer{Id=11,StoreId=1,Name="TEST CUSTOMER",Code="TEST11",Phone="0398946174"},new Customer{Id=22,StoreId=2,Name="FOREIGN CUSTOMER",Code="TEST22",Phone="0398946174"});
            db.LegalEntities.AddRange(new LegalEntity{Id=1,StoreId=1,Code="L1",Name="TEST LEGAL ONE",LegalName="TEST LEGAL ONE"},new LegalEntity{Id=2,StoreId=1,Code="L2",Name="TEST LEGAL TWO",LegalName="TEST LEGAL TWO"},new LegalEntity{Id=3,StoreId=2,Code="L3",Name="FOREIGN LEGAL",LegalName="FOREIGN LEGAL"});
            db.Orders.AddRange(new Order{Id=10,StoreId=1,CustomerId=11,OrderNumber="TEST-ORDER",CompletedAtUtc=DateTime.UtcNow,Status=OrderStatus.Completed,GrandTotal=50000},new Order{Id=20,StoreId=2,CustomerId=22,OrderNumber="FOREIGN-ORDER",CompletedAtUtc=DateTime.UtcNow,Status=OrderStatus.Completed});
            InvoiceHead Invoice(int id,int legal=1)=>new(){Id=id,StoreId=1,OrderId=10,LegalEntityId=legal,InvoiceNumber="TEST-"+id,ProviderInvoiceNo="TEST-"+id,ProviderStatus=InvoiceProviderStatus.Issued,IssuedAtUtc=DateTime.UtcNow,GrandTotal=50000,
                OfficialPdfStatus=InvoiceFileDownloadStatus.Downloaded,OfficialPdfFileName="TEST.pdf",OfficialPdfByteLength=50,OfficialPdfSha256=new string('A',64),
                OfficialZipXmlStatus=InvoiceFileDownloadStatus.Downloaded,OfficialZipXmlFileName="TEST.zip",OfficialZipXmlByteLength=50,OfficialZipXmlSha256=new string('B',64)};
            var foreignOwner=Invoice(103,3);var legacy=Invoice(104);legacy.LegacyReadOnly=true;var group=Invoice(105);group.IsAutoInvoiceGroup=true;var draft=Invoice(106);draft.ProviderStatus=InvoiceProviderStatus.LocalDraft;
            var foreign=Invoice(201,3);foreign.StoreId=2;foreign.OrderId=20;
            db.InvoiceHeads.AddRange(Invoice(101),Invoice(102,2),foreignOwner,legacy,group,draft,foreign);
            foreach(var entry in db.ChangeTracker.Entries())if(entry.Metadata.FindProperty("RowVersion") is not null)entry.Property("RowVersion").CurrentValue=new byte[8];
            // Seed deliberately inconsistent legacy ownership into the fake provider only.
            // Production AppDbContext write guards must never be relaxed for this regression.
            var entities=db.ChangeTracker.Entries().Select(entry=>entry.Entity).ToArray();db.ChangeTracker.Clear();
            await using(var seed=new DbContext(new DbContextOptionsBuilder().UseInMemoryDatabase(name,memory).UseModel(db.Model).Options))
            {seed.AddRange(entities);await seed.SaveChangesAsync();}
            var store=new FakeStore();var service=new CustomerInvoiceNotificationAdminService(db,new CustomerServiceAccess(db,user,tenant),null!,null!,store,new LinkOptions(linkBaseUrl));
            return new(db,store,service);
        }
        public ValueTask DisposeAsync()=>db.DisposeAsync();
    }
    private sealed class Tenant:ITenantContext{public int? StoreId=>1;public bool IsHostAdmin=>true;public string? Subdomain=>"TEST";public bool CanReadAllStores=>true;public bool CanWriteAllStores=>true;}
    private sealed class UserContext:ICurrentUser{public int? UserId=>1;public string? UserName=>"TEST";public int? TerminalId=>null;public string? TerminalCode=>null;public bool IsAuthenticated=>true;}
    private sealed class LinkOptions(string? origin):IOptionsMonitor<InvoiceOfficialDocumentLinkOptions>
    {
        public InvoiceOfficialDocumentLinkOptions CurrentValue{get;}=new(){PublicBaseUrl=origin??""};
        public InvoiceOfficialDocumentLinkOptions Get(string? name)=>CurrentValue;
        public IDisposable? OnChange(Action<InvoiceOfficialDocumentLinkOptions,string?> listener)=>null;
    }
    private sealed class FakeStore:IInvoiceNotificationStore
    {
        public InvoiceNotificationDetail? Detail;public int LastPage,LastStore,Verifications,ListCalls,FindCalls,Enqueues;public Func<Task>? BeforePreview;
        public Task<InvoiceRecipientBindingSnapshot> VerifyBindingAsync(InvoiceRecipientBindingProof proof,Guid requestId,CancellationToken ct){Verifications++;throw new InvalidOperationException("Unexpected proof side effect.");}
        public Task RevokeBindingAsync(int storeId,Guid id,int version,int actor,string reason,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<InvoiceRecipientBindingSnapshot>> BindingsAsync(int storeId,int? customer,CancellationToken ct)=>Task.FromResult<IReadOnlyList<InvoiceRecipientBindingSnapshot>>([]);
        public Task<IReadOnlyList<InvoiceNotificationListItem>> ListAsync(int storeId,string? state,int page,CancellationToken ct){ListCalls++;LastPage=page;LastStore=storeId;return Task.FromResult<IReadOnlyList<InvoiceNotificationListItem>>([]);}
        public Task<InvoiceNotificationDetail?> FindAsync(int storeId,Guid id,CancellationToken ct){FindCalls++;return Task.FromResult(Detail);}
        public async Task<InvoiceNotificationSourcePreview> PreviewSourceAsync(int store,int customer,string sourceType,int sourceId,string kind,CancellationToken ct){if(BeforePreview is not null)await BeforePreview();return new(store,customer,sourceType,sourceId,kind,new string('A',64),"TEST BILL","Issued",50000,null);}
        public Task<Guid> EnqueueAsync(InvoiceNotificationEnqueueCommand command,CancellationToken ct){Enqueues++;throw new NotSupportedException();}
    }
}
