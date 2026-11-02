using System.Net;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Migrations;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseLineNotesSqlServerTests
{
    [Fact]
    public async Task Line_notes_survive_edit_review_conversion_and_supplier_print_as_independent_snapshots()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var actor = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        int conversion, supplier, legal;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
            var purchaseUnit = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id,
                UnitId = variant.Product.BaseUnitId, Factor = 1, IsBaseUnit = true, IsActive = true };
            db.ProductUnitConversions.Add(purchaseUnit); await db.SaveChangesAsync(); conversion = purchaseUnit.Id;
            supplier = await db.Suppliers.Select(x => x.Id).FirstAsync();
            legal = (await db.Warehouses.SingleAsync(x => x.Id == store.WarehouseId)).LegalEntityId;
            foreach (var type in new[] { typeof(PurchaseRequestLine), typeof(PurchaseOrderLine) })
            {
                var property = db.Model.FindEntityType(type)!.FindProperty("Note")!;
                Assert.True(property.IsNullable); Assert.Equal(1000, property.GetMaxLength());
                var snapshotProperty = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model.FindEntityType(type.FullName!)!.FindProperty("Note")!;
                Assert.Equal(1000, snapshotProperty.GetMaxLength());
            }
        }
        var note = "Chỉ nhận hàng còn hạn dùng từ 6 tháng.\nGiao nguyên kiện.";
        var manualNote = "Màu xanh <img src=x onerror=alert(1)>";
        var form = new Dictionary<string, string> { ["Title"] = "Yêu cầu có ghi chú mặt hàng", ["Note"] = "Ghi chú chung riêng",
            ["Lines[0].ItemKind"] = "1", ["Lines[0].ProductVariantId"] = store.VariantId.ToString(),
            ["Lines[0].ProductUnitConversionId"] = conversion.ToString(), ["Lines[0].Quantity"] = "2", ["Lines[0].Note"] = "  " + note + "  ",
            ["Lines[1].ItemKind"] = "2", ["Lines[1].ProductName"] = "Hàng tự nhập", ["Lines[1].UnitName"] = "cái",
            ["Lines[1].Quantity"] = "3", ["Lines[1].Note"] = manualNote };
        async Task Post(string url, Dictionary<string, string> fields)
        {
            using var response = await actor.Http.PostAsync(url, new FormUrlEncodedContent(fields));
            Assert.True(response.StatusCode == HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        }
        await Post("/admin/purchase-requests/save", form);
        int id; int[] lineIds; string version;
        async Task Refresh()
        {
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            var request = await db.PurchaseRequests.AsNoTracking().Include(x => x.Lines).SingleAsync();
            id = request.Id; lineIds = request.Lines.OrderBy(x => x.LineNo).Select(x => x.Id).ToArray();
            version = Convert.ToBase64String(request.RowVersion);
        }
        id = 0; lineIds = []; version = ""; await Refresh();
        var quick = await actor.JsonAsync(HttpMethod.Get, "/admin/purchase-requests/quick-view?requestId=" + id);
        Assert.Equal(note, quick.GetProperty("lines")[0].GetProperty("note").GetString());
        Assert.Equal(manualNote, quick.GetProperty("lines")[1].GetProperty("note").GetString());
        Assert.Contains(note, WebUtility.HtmlDecode(await actor.Http.GetStringAsync($"/admin/purchase-requests/{id}/edit")));

        form["Id"] = id.ToString(); form["RowVersion"] = version;
        form["Lines[0].Id"] = lineIds[0].ToString(); form["Lines[1].Id"] = lineIds[1].ToString();
        form["Lines[0].Note"] = new string('a', 1001);
        using (var rejected = await actor.Http.PostAsync("/admin/purchase-requests/save", new FormUrlEncodedContent(form)))
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode); // Validation redisplays the edit form.
        var previous = version; await Refresh(); Assert.Equal(previous, version);
        form["Lines[0].Note"] = "Yêu cầu bao bì mới.\n" + note;
        form["Lines[1].Note"] = "  ";
        await Post("/admin/purchase-requests/save", form); await Refresh();
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            Assert.Null((await db.PurchaseRequestLines.SingleAsync(x => x.Id == lineIds[1])).Note);
        form["RowVersion"] = version; form["Lines[1].Note"] = manualNote;
        await Post("/admin/purchase-requests/save", form); await Refresh();
        await Post($"/admin/purchase-requests/{id}/submit", new() { ["RowVersion"] = version }); await Refresh();
        await Post($"/admin/purchase-requests/{id}/approve", new() { ["RowVersion"] = version }); await Refresh();
        var preparedHtml = await actor.Http.GetStringAsync($"/admin/purchase-requests/{id}/prepare");
        Assert.Contains(form["Lines[0].Note"], WebUtility.HtmlDecode(preparedHtml)); Assert.DoesNotContain(manualNote, preparedHtml);
        var conversionForm = new Dictionary<string, string> { ["RowVersion"] = version,
            ["Orders[0].ClientGroupKey"] = "line-notes-" + Guid.NewGuid().ToString("N"),
            ["Orders[0].SupplierId"] = supplier.ToString(), ["Orders[0].ExpectedWarehouseId"] = store.WarehouseId.ToString(),
            ["Orders[0].LegalEntityId"] = legal.ToString(), ["Orders[0].OrderDate"] = GaoApp.Application.Common.Helpers.VietnamTime.Today.ToString("yyyy-MM-dd"),
            ["Orders[0].Lines[0].PurchaseRequestLineId"] = lineIds[0].ToString(), ["Orders[0].Lines[0].Quantity"] = "2",
            ["Orders[0].Lines[1].PurchaseRequestLineId"] = lineIds[1].ToString(), ["Orders[0].Lines[1].Quantity"] = "3" };
        await Post($"/admin/purchase-requests/{id}/convert", conversionForm);
        int orderId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var order = await db.PurchaseOrders.Include(x => x.Lines).SingleAsync(x => x.SourcePurchaseRequestId == id); orderId = order.Id;
            Assert.Equal(form["Lines[0].Note"], order.Lines.Single(x => x.SourcePurchaseRequestLineId == lineIds[0]).Note);
            Assert.Equal(manualNote, order.Lines.Single(x => x.SourcePurchaseRequestLineId == lineIds[1]).Note);
            (await db.PurchaseRequestLines.SingleAsync(x => x.Id == lineIds[0])).Note = "Changed source snapshot in test";
            await db.SaveChangesAsync();
        }
        var printed = await actor.Http.GetStringAsync($"/admin/purchase-orders/{orderId}/print?autoPrint=false");
        Assert.Contains(form["Lines[0].Note"], WebUtility.HtmlDecode(printed)); Assert.Contains(manualNote, WebUtility.HtmlDecode(printed));
        Assert.DoesNotContain(manualNote, printed); Assert.DoesNotContain("Changed source snapshot in test", printed);
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using var hidden = await foreign.Http.GetAsync("/admin/purchase-requests/quick-view?requestId=" + id);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }
}
