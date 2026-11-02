using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Assistant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

public sealed partial class AssistantIntegrationTests
{
    private static async Task<StockDocument> SeedScreenReceipt(Fixture f)
    {
        var legal = new LegalEntity { Id=501, StoreId=1, Code="SCREEN", Name="TEST", LegalName="TEST", IsActive=true, RowVersion=[1] };
        var warehouse = new Warehouse { Id=501, StoreId=1, LegalEntityId=501, LegalEntity=legal, Code="SCREEN", Name="TEST", IsActive=true, RowVersion=[1] };
        var doc = new StockDocument { Id=501, StoreId=1, WarehouseId=501, Warehouse=warehouse, DocumentNo="PN-TEST", Status=StockDocumentStatus.Draft, RowVersion=[1] };
        f.Db.StockDocuments.Add(doc); await f.Db.SaveChangesAsync(); return doc;
    }

    [Theory]
    [InlineData(StockDocumentStatus.Draft,true)]
    [InlineData(StockDocumentStatus.Rejected,true)]
    [InlineData(StockDocumentStatus.PendingApproval,false)]
    [InlineData(StockDocumentStatus.Confirmed,false)]
    [InlineData(StockDocumentStatus.Cancelled,false)]
    public async Task Screen_receiving_allows_only_editable_scoped_receipts(StockDocumentStatus status, bool canApply)
    {
        using var f=new Fixture(); await f.Seed(); var doc=await SeedScreenReceipt(f);
        doc.Status=status; await f.Db.SaveChangesAsync(); var who=await f.Access.CurrentAsync(doc.Id,default);
        var screen=await f.Access.ScreenAsync(who,"receiving",doc.Id,default);
        Assert.Equal(canApply,screen.CanApply); Assert.Equal(doc.Id,screen.DocumentId);
        Assert.Equal(Convert.ToBase64String(doc.RowVersion),screen.DocumentVersion);
        Assert.Contains("PN-TEST",screen.Title); Assert.Equal(canApply,screen.Message is null);
    }

    [Fact]
    public async Task Screen_receiving_detects_row_version_and_status_changes_without_writing_the_document()
    {
        using var f=new Fixture(); await f.Seed(); var doc=await SeedScreenReceipt(f);
        var who=await f.Access.CurrentAsync(doc.Id,default); var first=await f.Access.ScreenAsync(who,"receiving",doc.Id,default);
        doc.RowVersion=[2]; await f.Db.SaveChangesAsync(); var changed=await f.Access.ScreenAsync(who,"receiving",doc.Id,default);
        Assert.NotEqual(first.Key,changed.Key); Assert.True(changed.CanApply);
        doc.Status=StockDocumentStatus.PendingApproval; await f.Db.SaveChangesAsync(); var locked=await f.Access.ScreenAsync(who,"receiving",doc.Id,default);
        Assert.NotEqual(changed.Key,locked.Key); Assert.False(locked.CanApply);
        Assert.DoesNotContain(f.Db.ChangeTracker.Entries(),e=>e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Fact]
    public async Task Screen_receiving_rejects_foreign_store_wrong_warehouse_and_non_receipt_documents()
    {
        using var f=new Fixture(); await f.Seed(); var doc=await SeedScreenReceipt(f); var who=await f.Access.CurrentAsync(doc.Id,default);
        Assert.Equal(404,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.ScreenAsync(who with {Scope=new("2","501","501")},"receiving",doc.Id,default))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.ScreenAsync(who with {Scope=new("1","999","501")},"receiving",doc.Id,default))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.ScreenAsync(who with {Scope=new("1","501","999")},"receiving",doc.Id,default))).Status);
        doc.Type=StockDocumentType.Issue; await f.Db.SaveChangesAsync();
        Assert.Equal(404,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.ScreenAsync(who,"receiving",doc.Id,default))).Status);
        doc.Type=StockDocumentType.Receipt; doc.IsDeleted=true; await f.Db.SaveChangesAsync();
        Assert.Equal(404,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.ScreenAsync(who,"receiving",doc.Id,default))).Status);
    }

    [Fact]
    public async Task Screen_requires_feature_and_catalog_rights_and_rejects_arbitrary_screen_names()
    {
        using var f=new Fixture(); await f.Seed(); var doc=await SeedScreenReceipt(f); var who=await f.Access.CurrentAsync(doc.Id,default);
        foreach(var missing in new[]{AssistantPermissions.Receiving,AssistantPermissions.Catalog})
            Assert.Equal(403,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.ScreenAsync(who with {Permissions=who.Permissions.Where(p=>p!=missing).ToArray()},"receiving",doc.Id,default))).Status);
        Assert.Equal(400,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.ScreenAsync(who,"arbitrary",null,default))).Status);
        Assert.Equal(400,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.ScreenAsync(who,"receiving",null,default))).Status);
    }

    [Fact]
    public async Task Product_creation_screen_requires_normalization_rights_and_has_no_document_scope()
    {
        using var f=new Fixture(); await f.Seed(); var who=await f.Access.CurrentAsync(null,default);
        var screen=await f.Access.ScreenAsync(who,"product_create",null,default); Assert.True(screen.CanApply); Assert.Null(screen.DocumentId);
        foreach(var missing in new[]{AssistantPermissions.NormalizeName,AssistantPermissions.Catalog})
            Assert.Equal(403,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.ScreenAsync(who with {Permissions=who.Permissions.Where(p=>p!=missing).ToArray()},"product_create",null,default))).Status);
        Assert.Equal(400,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.ScreenAsync(who,"product_create",501,default))).Status);
    }
}
