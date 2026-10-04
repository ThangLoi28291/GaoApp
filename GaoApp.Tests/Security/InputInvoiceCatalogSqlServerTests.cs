using System.Net;
using System.Net.Http.Json;
using System.Text;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Services.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class InputInvoiceCatalogSqlServerTests
{
    [Fact]
    public async Task Catalog_indexes_all_files_filters_reviews_links_and_enforces_tenant_permissions()
    {
        await using var app = await FullApplicationFixture.StartWithInvoiceLibraryAsync();
        var store = app.Stores[0];
        await SeedAsync(app, store, 205);
        // A valid XML belonging to a different seller was filed in this supplier folder.
        // The receipt picker blocks linking it; the catalog must read its actual source partition.
        await File.WriteAllTextAsync(Path.Combine(app.InvoiceLibraryRoot, "2026", "0312770607", "10", "wrong-seller.xml"),
            Xml("WRONG-FOLDER").Replace("0312770607", "0100520429"));
        var sourcePdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% source partition fixture\n%%EOF");
        await File.WriteAllBytesAsync(Path.Combine(app.InvoiceLibraryRoot, "2026", "0312770607", "10", "wrong-seller.pdf"), sourcePdf);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        const string url = "/admin/input-invoices";
        using (var denied = await viewer.Http.PostAsync(url + "/sync", null)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var synced = await manager.JsonAsync(HttpMethod.Post, url + "/sync?year=2026&month=10");
        Assert.Equal(206, synced.GetProperty("added").GetInt32());
        Assert.Equal(1, synced.GetProperty("outsideScope").GetInt32());
        Assert.Equal(1, synced.GetProperty("invalid").GetInt32());
        Assert.Equal(1, synced.GetProperty("folderMismatches").GetInt32());
        var list = await manager.JsonAsync(HttpMethod.Get, url + "/list?year=2026&month=10");
        Assert.Equal(206, list.GetProperty("total").GetInt32());
        var mismatchedList = await manager.JsonAsync(HttpMethod.Get, url + "/list?search=WRONG-FOLDER");
        var mismatched = Assert.Single(mismatchedList.GetProperty("rows").EnumerateArray());
        Assert.True(mismatched.GetProperty("folderMismatch").GetBoolean());
        var mismatchedDetail = await manager.JsonAsync(HttpMethod.Get, url + $"/{mismatched.GetProperty("id").GetInt32()}");
        Assert.Equal("0312770607", mismatchedDetail.GetProperty("sourceSupplierTaxCode").GetString());
        Assert.Equal("0100520429", mismatchedDetail.GetProperty("sellerTaxCode").GetString());
        using (var pdf = await manager.Http.GetAsync(url + $"/{mismatched.GetProperty("id").GetInt32()}/pdf"))
        { Assert.Equal(HttpStatusCode.OK, pdf.StatusCode); Assert.Equal(sourcePdf, await pdf.Content.ReadAsByteArrayAsync()); }
        Assert.Equal(30, list.GetProperty("rows").GetArrayLength());
        var id = list.GetProperty("rows")[0].GetProperty("id").GetInt32();
        var details = await manager.JsonAsync(HttpMethod.Get, url + $"/{id}");
        var version = details.GetProperty("rowVersion").GetString();
        foreach (var suffix in new[] { "", "/preview", "/pdf", "/xml" })
        { using var denied = await foreign.Http.GetAsync(url + $"/{id}" + suffix); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); }
        using (var denied = await viewer.Http.PostAsJsonAsync(url + $"/{id}/review", new { status = 1, rowVersion = version })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var invalid = await manager.Http.PostAsJsonAsync(url + $"/{id}/review", new { status = 3, rowVersion = version })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await manager.JsonAsync(HttpMethod.Post, url + $"/{id}/review", new { status = 2, note = "Chi phí điện cửa hàng", rowVersion = version });
        using (var stale = await manager.Http.PostAsJsonAsync(url + $"/{id}/review", new { status = 1, rowVersion = version })) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        details = await manager.JsonAsync(HttpMethod.Get, url + $"/{id}");
        Assert.Equal(2, details.GetProperty("reviewStatus").GetInt32());
        Assert.Single(details.GetProperty("history").EnumerateArray());
        Assert.Empty(details.GetProperty("receipts").EnumerateArray());
        var expense = await manager.JsonAsync(HttpMethod.Get, url + "/list?review=2&linked=false");
        Assert.Equal(1, expense.GetProperty("total").GetInt32());
        var second = await manager.JsonAsync(HttpMethod.Post, url + "/sync?year=2026&month=10");
        Assert.Equal(0, second.GetProperty("added").GetInt32());
        Assert.Equal(206, second.GetProperty("existing").GetInt32());
        using(var xml = await manager.Http.GetAsync(url + $"/{id}/xml")) { Assert.Equal(HttpStatusCode.OK, xml.StatusCode); Assert.NotNull(xml.Content.Headers.ContentDisposition); }
        var preview = await manager.Http.GetStringAsync(url + $"/{id}/preview"); Assert.Contains("Nhà cung cấp thử", WebUtility.HtmlDecode(preview));
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var entry = await db.InputInvoiceLibraryEntries.SingleAsync(x => x.Id == id);
            var head = new InputInvoiceHead { StoreId = store.StoreId, SellerTaxCode = entry.SellerTaxCode, BuyerTaxCode = entry.BuyerTaxCode,
                InvoiceSeries = entry.Series, InvoiceNumber = entry.Number, InvoiceDate = entry.InvoiceDate };
            InputInvoiceIdentityPolicy.ApplyRequiredIdentity(head);
            var receipt = new StockDocument { StoreId = store.StoreId, DocumentNo = "CATALOG-RECEIPT", WarehouseId = store.WarehouseId };
            db.AddRange(head, receipt); await db.SaveChangesAsync();
            db.Add(new StockDocumentInputInvoiceMap { StoreId = store.StoreId, StockDocumentId = receipt.Id, InputInvoiceHeadId = head.Id }); await db.SaveChangesAsync();
        }
        Assert.Equal(1, (await manager.JsonAsync(HttpMethod.Get, url + "/list?linked=true")).GetProperty("total").GetInt32());
        Assert.Single((await manager.JsonAsync(HttpMethod.Get, url + $"/{id}")).GetProperty("receipts").EnumerateArray());
        Assert.Equal(0, (await foreign.JsonAsync(HttpMethod.Get, url + "/list")).GetProperty("total").GetInt32());
        using var upload = new MultipartFormDataContent(); upload.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(Xml("new", "9999999999"))), "xml", "test.xml");
        using(var denied = await manager.Http.PostAsync(url + "/upload", upload)) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using var validUpload = new MultipartFormDataContent(); validUpload.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(Xml("UPLOAD"))), "xml", "test.xml");
        using(var uploaded = await manager.Http.PostAsync(url + "/upload", validUpload)) Assert.True(uploaded.IsSuccessStatusCode, await uploaded.Content.ReadAsStringAsync());
        var uploadedList = await manager.JsonAsync(HttpMethod.Get, url + "/list?search=UPLOAD");
        var uploadId = uploadedList.GetProperty("rows")[0].GetProperty("id").GetInt32();
        using var withPdf = new MultipartFormDataContent();
        withPdf.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(Xml("UPLOAD"))), "xml", "test.xml");
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n% synthetic library transport fixture\n%%EOF");
        withPdf.Add(new ByteArrayContent(pdfBytes), "pdf", "test.pdf");
        using(var uploaded = await manager.Http.PostAsync(url + "/upload", withPdf)) Assert.True(uploaded.IsSuccessStatusCode, await uploaded.Content.ReadAsStringAsync());
        using(var pdf = await manager.Http.GetAsync(url + $"/{uploadId}/pdf"))
        { Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType); Assert.Equal(pdfBytes, await pdf.Content.ReadAsByteArrayAsync()); }
        var filesBefore = Directory.GetFiles(app.InvoiceLibraryRoot, "*", SearchOption.AllDirectories).Length;
        using var conflictingUpload = new MultipartFormDataContent();
        conflictingUpload.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(Xml("UPLOAD").Replace("108000", "109000"))), "xml", "test.xml");
        using(var conflictResponse = await manager.Http.PostAsync(url + "/upload", conflictingUpload))
        { Assert.True(conflictResponse.IsSuccessStatusCode); Assert.Contains("\"conflicts\":1", await conflictResponse.Content.ReadAsStringAsync()); }
        Assert.Equal(filesBefore, Directory.GetFiles(app.InvoiceLibraryRoot, "*", SearchOption.AllDirectories).Length);
        // Changed source may not inherit an earlier review or silently replace the catalog record.
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var entry = await db.InputInvoiceLibraryEntries.SingleAsync(x => x.Id == id);
            var stem = entry.DocumentKey.Split(':')[2];
            var path = Path.Combine(app.InvoiceLibraryRoot, "2026", "0312770607", "10", stem + ".xml");
            await File.AppendAllTextAsync(path, "\n");
        }
        using(var changed = await manager.Http.GetAsync(url + $"/{id}/preview")) Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        var conflict = await manager.JsonAsync(HttpMethod.Post, url + "/sync?year=2026&month=10");
        Assert.Equal(1, conflict.GetProperty("conflicts").GetInt32());
    }

    [Theory]
    [InlineData("1", 1)] [InlineData("2", 2)] [InlineData("9", 3)]
    public void Relationship_is_read_from_header_not_item_fields(string value, int kind)
    {
        var bytes = Encoding.UTF8.GetBytes(Xml("1", relation: $"<TTHDLQuan><TCHDon>{value}</TCHDon><KHHDCLQuan>C26OLD</KHHDCLQuan><SHDCLQuan>42</SHDCLQuan></TTHDLQuan>"));
        var result = InputInvoiceCatalogService.ReadRelation(bytes);
        Assert.Equal(kind, result.Kind); Assert.Equal("C26OLD · 42", result.Related);
    }
    [Fact]
    public void Relation_parser_rejects_external_entities() => Assert.Throws<System.Xml.XmlException>(() =>
        InputInvoiceCatalogService.ReadRelation(Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///c:/windows/win.ini'>]><HDon>&e;</HDon>")));

    internal static async Task SeedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int count = 8)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        db.LegalEntities.Add(new LegalEntity { StoreId = store.StoreId, Code = "XMLBUYER", Name = "Gạo Mart", LegalName = "Hộ kinh doanh Gạo Mart", TaxCode = "070091008655", SalePriority = 99 });
        await db.SaveChangesAsync();
        var folder = Path.Combine(app.InvoiceLibraryRoot, "2026", "0312770607", "10"); Directory.CreateDirectory(folder);
        for(var i = 1; i <= count; i++) await File.WriteAllTextAsync(Path.Combine(folder, $"invoice-{i}.xml"), Xml(i.ToString("00000000"), relation: i == 2 ? "<TTHDLQuan><TCHDon>1</TCHDon><SHDCLQuan>00000001</SHDCLQuan></TTHDLQuan>" : ""));
        await File.WriteAllTextAsync(Path.Combine(folder, "foreign.xml"), Xml("FOREIGN", "9999999999"));
        await File.WriteAllTextAsync(Path.Combine(folder, "invalid.xml"), "<invalid>");
    }
    internal static string Xml(string number, string buyer = "070091008655", string relation = "") => $$"""
        <HDon><DLHDon><TTChung><KHMSHDon>1</KHMSHDon><KHHDon>C26MVP</KHHDon><SHDon>{{number}}</SHDon><NLap>2026-10-01</NLap>{{relation}}</TTChung><NDHDon><NBan><Ten>Nhà cung cấp thử</Ten><MST>0312770607</MST></NBan><NMua><Ten>Hộ kinh doanh Gạo Mart</Ten><MST>{{buyer}}</MST></NMua><DSHHDVu><HHDVu><STT>1</STT><THHDVu>Chi phí cửa hàng</THHDVu><DVTinh>Lần</DVTinh><SLuong>1</SLuong><DGia>100000</DGia><ThTien>100000</ThTien><TSuat>8%</TSuat></HHDVu></DSHHDVu><TToan><TgTCThue>100000</TgTCThue><TgTThue>8000</TgTThue><TgTTTBSo>108000</TgTTTBSo></TToan></NDHDon></DLHDon></HDon>
        """;
}
