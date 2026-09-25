using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Printing;
using GaoApp.Tests.Configuration;
using GaoApp.Web.Services.Printing;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ProductLabelSqlServerTests
{
    private const string Url = "/admin/label-printing/";
    private static string Version(JsonElement json) => json.GetProperty("rowVersion").GetString()!;
    private static int Id(JsonElement json) => json.GetProperty("id").GetInt32();

    [Fact]
    public async Task Upgrade_is_additive_and_preserves_existing_stock_and_catalog()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync("20260910012000_AddStoreReceiptIdentity");
        var seed = await database.SeedInventoryCatalogAsync();
        await database.MigrateAsync();
        await using var db = database.CreateTenantContext(seed.StoreId);
        Assert.Equal(20m, (await db.ProductVariants.SingleAsync()).Price);
        Assert.Empty(await db.Set<ProductLabelTask>().ToListAsync());
        Assert.Empty(await db.Set<ProductLabelJob>().ToListAsync());
    }

    [Fact]
    public async Task Templates_printers_and_receipt_tasks_enforce_permissions_csrf_tenant_and_versions()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.System.ProductLabel.Print));
        using var outsider = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using var denied = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        var design = new ProductLabelDesign { WidthMm = 50, HeightMm = 30, Columns = 2, QuantityMode = "received" };
        using (var response = await employee.Http.PostAsJsonAsync(Url + "templates", new SaveLabelTemplate(design, null))) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await denied.Http.GetAsync(Url + "tasks")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var saved = await manager.JsonAsync(HttpMethod.Post, Url + "templates", new SaveLabelTemplate(design, null));
        Assert.Equal(2, saved.GetProperty("design").GetProperty("columns").GetInt32());
        Assert.Equal("received", saved.GetProperty("design").GetProperty("quantityMode").GetString());
        using (var response = await manager.Http.PostAsJsonAsync(Url + "templates", new SaveLabelTemplate(design with { Columns = 3 }, null))) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await outsider.Http.PutAsJsonAsync(Url + "templates/" + Id(saved), new SaveLabelTemplate(design, Version(saved)))) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await manager.JsonAsync(HttpMethod.Put, Url + "templates/" + Id(saved), new SaveLabelTemplate(design with { Columns = 1 }, Version(saved)));
        using (var response = await manager.Http.PutAsJsonAsync(Url + "templates/" + Id(saved), new SaveLabelTemplate(design, Version(saved)))) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var token = manager.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single(); manager.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var response = await manager.Http.PostAsJsonAsync(Url + "templates", new SaveLabelTemplate(design, null))) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        manager.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        var receiptId = await SeedReceipt(app, store);
        using (var response = await outsider.Http.PostAsync(Url + "receipts/" + receiptId, null)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var task = await employee.JsonAsync(HttpMethod.Post, Url + "receipts/" + receiptId);
        Assert.Equal(Id(task), Id(await employee.JsonAsync(HttpMethod.Post, Url + "receipts/" + receiptId)));
        using (var response = await outsider.Http.GetAsync(Url + "tasks/" + Id(task))) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, (await outsider.JsonAsync(HttpMethod.Get, Url + "tasks")).GetArrayLength());
    }

    [Fact]
    public async Task Staff_print_workspace_and_admin_settings_are_separate_and_enforce_permissions()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var staff = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.System.ProductLabel.Print));
        using var config = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.System.ProductLabel.Manage));
        using var denied = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        const string settingsUrl = "/admin/label-printing-settings";
        var printHtml = await staff.Http.GetStringAsync(Url);
        Assert.Contains("data-page=\"print\"", printHtml); Assert.Contains("id=\"taskLines\"", printHtml);
        Assert.DoesNotContain("id=\"templateFields\"", printHtml); Assert.DoesNotContain("id=\"printerForm\"", printHtml);
        Assert.DoesNotContain("href=\"" + settingsUrl + "\"", printHtml);
        using (var response = await staff.Http.GetAsync(settingsUrl)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await denied.Http.GetAsync(settingsUrl)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var settingsHtml = await config.Http.GetStringAsync(settingsUrl);
        Assert.Contains("data-page=\"settings\"", settingsHtml); Assert.Contains("id=\"templateFields\"", settingsHtml);
        Assert.Contains("id=\"printerForm\"", settingsHtml); Assert.DoesNotContain("id=\"taskLines\"", settingsHtml);
        Assert.DoesNotContain("id=\"allHistory\"", settingsHtml); Assert.DoesNotContain("id=\"printTest\"", settingsHtml);
        foreach (var path in new[] { "", "tasks", "jobs", "tasks/1" })
        { using var response = await config.Http.GetAsync(Url + path); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); }
        foreach (var path in new[] { "jobs", "receipts/1", "tasks/1/refresh", "tasks/1/complete", "jobs/1/confirm", "jobs/1/cancel" })
        { using var response = await config.Http.PostAsJsonAsync(Url + path, new { }); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); }
        using (var response = await config.Http.PutAsJsonAsync(Url + "tasks/1/plan", new { })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var template = await config.JsonAsync(HttpMethod.Post, Url + "templates", new SaveLabelTemplate(new ProductLabelDesign { Layout = "price-tag" }, null));
        var printer = await config.JsonAsync(HttpMethod.Post, Url + "printers", new SaveLabelPrinter { Name = "Kho", WindowsPrinterName = "TEST USB" });
        Assert.Equal("Giá nền đen", template.GetProperty("layoutName").GetString());
        Assert.Equal(1, (await staff.JsonAsync(HttpMethod.Get, Url + "templates")).GetArrayLength());
        Assert.Equal(1, (await staff.JsonAsync(HttpMethod.Get, Url + "printers")).GetArrayLength());
        using (var response = await staff.Http.PutAsJsonAsync(Url + "templates/" + Id(template), new SaveLabelTemplate(new(), Version(template)))) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await staff.Http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, Url + "templates/" + Id(template)) { Content = JsonContent.Create(new LabelVersion(Version(template))) })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await staff.Http.PostAsJsonAsync(Url + "printers", new SaveLabelPrinter { Name = "Denied", WindowsPrinterName = "Denied" })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await staff.Http.PutAsJsonAsync(Url + "printers/" + Id(printer), new SaveLabelPrinter { Name = "Denied", WindowsPrinterName = "TEST USB", RowVersion = Version(printer) })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await staff.Http.GetAsync(Url + "installed-printers")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await config.Http.GetAsync(Url + "layouts/price-tag/preview")) response.EnsureSuccessStatusCode();

        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Single(await verify.AdminMenuItems.Where(x => x.Url == "/admin/label-printing" && x.PermissionCode == PermissionCodes.System.ProductLabel.Print).ToListAsync());
        Assert.Single(await verify.AdminMenuItems.Where(x => x.Url == settingsUrl && x.PermissionCode == PermissionCodes.System.ProductLabel.Manage).ToListAsync());
    }

    [Fact]
    public async Task Base_unit_price_partial_print_reprint_concurrency_and_completion_preserve_stock()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0]; var account = await app.AddAccountAsync(store, "*");
        using var client = await app.LoginAsync(account);
        var receipt = await SeedReceipt(app, store);
        var template = await client.JsonAsync(HttpMethod.Post, Url + "templates", new SaveLabelTemplate(new ProductLabelDesign { QuantityMode = "received" }, null));
        var printer = await client.JsonAsync(HttpMethod.Post, Url + "printers", new SaveLabelPrinter { Name = "Test USB", WindowsPrinterName = "MOCK-XPRINTER" });
        var taskId = Id(await client.JsonAsync(HttpMethod.Post, Url + "receipts/" + receipt));
        var task = await client.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        var product = task.GetProperty("lines")[0].GetProperty("product");
        Assert.Equal(48, product.GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal(12345, product.GetProperty("price").GetDecimal());
        Assert.Equal("8938505974194", product.GetProperty("barcode").GetString());
        Assert.Equal("Chai", product.GetProperty("unit").GetString());
        var quantities = new List<LabelQuantity> { new(store.VariantId, 48) };
        task = await client.JsonAsync(HttpMethod.Put, Url + $"tasks/{taskId}/plan", new LabelPlanRequest(Id(template), quantities, Version(task)));
        var request = new LabelJobRequest(taskId, Id(template), Id(printer), [new(store.VariantId, 20)], Guid.NewGuid(), Version(task), Version(template));
        var same = await Task.WhenAll(client.Http.PostAsJsonAsync(Url + "jobs", request), client.Http.PostAsJsonAsync(Url + "jobs", request));
        var created = new List<int>();
        foreach (var response in same) { response.EnsureSuccessStatusCode(); created.Add(Id(await response.Content.ReadFromJsonAsync<JsonElement>())); response.Dispose(); }
        Assert.Equal(created[0], created[1]); int firstJob = created[0];
        using (var blocked = await client.Http.PostAsJsonAsync(Url + "jobs", request with { RequestId = Guid.NewGuid() })) Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using (var blocked = await client.Http.PostAsJsonAsync(Url + $"tasks/{taskId}/complete", new LabelVersion(Version(task)))) Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var transport = new FakeTransport();
        async Task<bool> Dispatch() { await using var db = app.Database.CreateTenantContext(store.StoreId); return await new LabelPrintDispatcher(db, transport).DispatchAsync(Id(printer), default); }
        var dispatches = await Task.WhenAll(Dispatch(), Dispatch()); Assert.Equal(1, dispatches.Count(x => x)); Assert.Equal(1, transport.Calls);
        var first = (await client.JsonAsync(HttpMethod.Get, Url + "jobs"))[0];
        Assert.Equal(2, first.GetProperty("status").GetInt32());
        await client.JsonAsync(HttpMethod.Post, Url + $"jobs/{firstJob}/confirm", new LabelConfirmRequest(Version(first), [new(store.VariantId, 18)], "2 tem lỗi"));
        using (var twice = await client.Http.PostAsJsonAsync(Url + $"jobs/{firstJob}/confirm", new LabelConfirmRequest(Version(first), [new(store.VariantId, 18)], "2 tem lỗi"))) Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        task = await client.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId); Assert.Equal(18, task.GetProperty("lines")[0].GetProperty("printed").GetInt32());
        var reprint = request with { RequestId = Guid.NewGuid(), RowVersion = Version(task), Lines = [new(store.VariantId, 1)], IsReprint = true, Reason = "Dán hỏng" };
        var reprintJob = Id(await client.JsonAsync(HttpMethod.Post, Url + "jobs", reprint)); await Dispatch();
        var latest = (await client.JsonAsync(HttpMethod.Get, Url + "jobs"))[0];
        await client.JsonAsync(HttpMethod.Post, Url + $"jobs/{reprintJob}/confirm", new LabelConfirmRequest(Version(latest), [new(store.VariantId, 1)], null));
        task = await client.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId); Assert.Equal(18, task.GetProperty("lines")[0].GetProperty("printed").GetInt32());
        var remaining = request with { RequestId = Guid.NewGuid(), RowVersion = Version(task), Lines = [new(store.VariantId, 30)] };
        var lastJob = Id(await client.JsonAsync(HttpMethod.Post, Url + "jobs", remaining)); await Dispatch();
        latest = (await client.JsonAsync(HttpMethod.Get, Url + "jobs"))[0];
        await client.JsonAsync(HttpMethod.Post, Url + $"jobs/{lastJob}/confirm", new LabelConfirmRequest(Version(latest), [new(store.VariantId, 30)], null));
        task = await client.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        await client.JsonAsync(HttpMethod.Post, Url + $"tasks/{taskId}/complete", new LabelVersion(Version(task)));
        Assert.True((await client.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId)).GetProperty("completed").GetBoolean());
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(StockDocumentStatus.Draft, (await verify.Set<StockDocument>().SingleAsync(x => x.Id == receipt)).Status);
        Assert.Equal(100, (await verify.InventoryBalances.SingleAsync()).OnHandQty);
        Assert.Equal(account.UserId, (await verify.Set<ProductLabelJob>().SingleAsync(x => x.Id == firstJob)).CreatedBy);
    }

    [Fact]
    public async Task Changed_receipt_or_price_blocks_stale_print_and_uncertain_dispatch_is_never_replayed()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var receiptId = await SeedReceipt(app, store);
        var template = await client.JsonAsync(HttpMethod.Post, Url + "templates", new SaveLabelTemplate(new(), null));
        var printer = await client.JsonAsync(HttpMethod.Post, Url + "printers", new SaveLabelPrinter { Name = "Mock", WindowsPrinterName = "Mock" });
        int taskId = Id(await client.JsonAsync(HttpMethod.Post, Url + "receipts/" + receiptId));
        var task = await client.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        task = await client.JsonAsync(HttpMethod.Put, Url + $"tasks/{taskId}/plan", new LabelPlanRequest(Id(template), [new(store.VariantId, 1)], Version(task)));
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        { var unit = await db.ProductUnitConversions.SingleAsync(x => x.IsBaseUnit); unit.Price = 14000; await db.SaveChangesAsync(); }
        var req = new LabelJobRequest(taskId, Id(template), Id(printer), [new(store.VariantId, 1)], Guid.NewGuid(), Version(task), Version(template));
        using (var stale = await client.Http.PostAsJsonAsync(Url + "jobs", req)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        task = await client.JsonAsync(HttpMethod.Post, Url + $"tasks/{taskId}/refresh", new LabelVersion(Version(task)));
        Assert.Equal(14000, task.GetProperty("lines")[0].GetProperty("product").GetProperty("price").GetDecimal());
        req = req with { RowVersion = Version(task), RequestId = Guid.NewGuid() };
        int jobId = Id(await client.JsonAsync(HttpMethod.Post, Url + "jobs", req));
        var transport = new FakeTransport { Fail = true };
        for (var n = 0; n < 2; n++)
        { await using var db = app.Database.CreateTenantContext(store.StoreId); await new LabelPrintDispatcher(db, transport).DispatchAsync(Id(printer), default); }
        Assert.Equal(1, transport.Calls);
        var job = (await client.JsonAsync(HttpMethod.Get, Url + "jobs"))[0]; Assert.Equal(3, job.GetProperty("status").GetInt32());
        await client.JsonAsync(HttpMethod.Post, Url + $"jobs/{jobId}/confirm", new LabelConfirmRequest(Version(job), [new(store.VariantId, 0)], "Không nhận được tem; đã kiểm tra hàng đợi"));
        task = await client.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        var queued = await client.JsonAsync(HttpMethod.Post, Url + "jobs", req with { RowVersion = Version(task), RequestId = Guid.NewGuid() });
        job = (await client.JsonAsync(HttpMethod.Get, Url + "jobs"))[0];
        await client.JsonAsync(HttpMethod.Post, Url + $"jobs/{Id(queued)}/cancel", new LabelVersion(Version(job)));
        Assert.Equal(5, (await client.JsonAsync(HttpMethod.Get, Url + "jobs"))[0].GetProperty("status").GetInt32());
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task Legacy_templates_use_auto_for_new_jobs_and_preview_without_rewriting_queued_payloads()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var receipt = await SeedReceipt(app, store);
        var printer = await client.JsonAsync(HttpMethod.Post, Url + "printers", new SaveLabelPrinter { Name = "Mock", WindowsPrinterName = "Mock" });
        int templateId, legacyJobId;
        var oldPayload = new LabelPrintPayload(new() { BarcodeFormat = "EAN8" }, new("Mock", "Mock", 203, 108, 0, 0),
            [new(ProductLabelRenderer.Sample with { Barcode = "96385074" }, 1)]);
        string oldJson = LabelJson.Write(oldPayload);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.Set<ProductVariantUnitBarcode>().SingleAsync()).Barcode = "SKU001";
            var oldTemplate = new ProductLabelTemplate { StoreId = store.StoreId, Name = "Mẫu cũ", DefinitionJson = "{\"name\":\"Mẫu cũ\",\"barcodeFormat\":\"EAN13\"}" };
            var oldJob = new ProductLabelJob { StoreId = store.StoreId, PrinterId = Id(printer), RequestId = Guid.NewGuid(), RequestHash = "legacy",
                PayloadJson = oldJson, Quantity = 1, RequestedByName = "Legacy operator" };
            db.Add(oldTemplate); db.Add(oldJob); await db.SaveChangesAsync(); templateId = oldTemplate.Id; legacyJobId = oldJob.Id;
        }
        var template = (await client.JsonAsync(HttpMethod.Get, Url + "templates"))[0];
        Assert.Equal("AUTO", template.GetProperty("design").GetProperty("barcodeFormat").GetString());
        Assert.Equal("standard", template.GetProperty("design").GetProperty("layout").GetString());
        using (var preview = await client.Http.PostAsJsonAsync(Url + "preview", new { design = new ProductLabelDesign { BarcodeFormat = "EAN13", Layout = "price-tag" }, product = ProductLabelRenderer.Sample with { Barcode = "SKU001" } }))
        { preview.EnsureSuccessStatusCode(); Assert.Equal("image/png", preview.Content.Headers.ContentType?.MediaType); }
        var taskId = Id(await client.JsonAsync(HttpMethod.Post, Url + "receipts/" + receipt));
        var task = await client.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        task = await client.JsonAsync(HttpMethod.Put, Url + $"tasks/{taskId}/plan", new LabelPlanRequest(templateId, [new(store.VariantId, 1)], Version(task)));
        var request = new LabelJobRequest(taskId, templateId, Id(printer), [new(store.VariantId, 1)], Guid.NewGuid(), Version(task), Version(template));
        var job = await client.JsonAsync(HttpMethod.Post, Url + "jobs", request);
        var saved = await client.JsonAsync(HttpMethod.Put, Url + "templates/" + templateId,
            new SaveLabelTemplate(new ProductLabelDesign { BarcodeFormat = "EAN8", Layout = "price-first", QuantityMode = "received", WidthMm = 50, HeightMm = 30 }, Version(template)));
        Assert.Equal("AUTO", saved.GetProperty("design").GetProperty("barcodeFormat").GetString());
        Assert.Equal("price-first", saved.GetProperty("design").GetProperty("layout").GetString());
        Assert.Equal(Id(job), Id(await client.JsonAsync(HttpMethod.Post, Url + "jobs", request)));
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(oldJson, (await verify.Set<ProductLabelJob>().SingleAsync(x => x.Id == legacyJobId)).PayloadJson);
        var newPayload = LabelJson.Read<LabelPrintPayload>((await verify.Set<ProductLabelJob>().SingleAsync(x => x.Id == Id(job))).PayloadJson);
        Assert.Equal("AUTO", newPayload.Design.BarcodeFormat); Assert.Equal("standard", newPayload.Design.Layout);
        Assert.Equal("SKU001", newPayload.Items[0].Product.Barcode);
        Assert.NotEmpty(ProductLabelRenderer.Commands(newPayload).ToArray());
        Assert.NotEmpty(ProductLabelRenderer.Commands(LabelJson.Read<LabelPrintPayload>(oldJson)).ToArray());
    }

    internal static async Task<int> SeedReceipt(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var variant = await db.ProductVariants.Include(x => x.Product).ThenInclude(x => x.BaseUnit).SingleAsync(x => x.Id == store.VariantId);
        variant.Product.BaseUnit.Name = "Chai";
        var conversion = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id, UnitId = variant.Product.BaseUnitId, IsBaseUnit = true, Factor = 1, IsActive = true, Price = 12345 };
        db.Add(conversion); await db.SaveChangesAsync();
        db.Add(new ProductVariantUnitBarcode { StoreId = store.StoreId, ProductUnitConversionId = conversion.Id, Barcode = "8938505974194", IsPrimary = true, IsActive = true });
        var doc = new StockDocument { StoreId = store.StoreId, DocumentNo = "TEM-" + Guid.NewGuid().ToString("N")[..8], WarehouseId = store.WarehouseId,
            Lines = [new StockDocumentLine { ProductVariantId = variant.Id, Quantity = 2, Factor = 24, BaseQuantity = 48, LineNo = 1, ProductNameSnapshot = "Sữa tươi không đường", UnitNameSnapshot = "Thùng" }] };
        db.Add(doc); await db.SaveChangesAsync(); return doc.Id;
    }
    private sealed class FakeTransport : ILabelPrintTransport
    {
        private int calls; public int Calls => calls; public bool Fail { get; init; }
        public int Send(string printerName, string documentName, IEnumerable<byte[]> commands)
        { Interlocked.Increment(ref calls); Assert.NotEmpty(commands.ToArray()); if (Fail) throw new IOException("USB disconnected"); return 101; }
    }
}
