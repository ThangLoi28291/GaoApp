using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Printing;
using GaoApp.Web.Services.Printing;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

public sealed partial class ProductLabelSqlServerTests
{
    internal static async Task<int> SeedBarcodeReceipt(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        var receipt = await SeedProgressReceipt(app, store);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var codes = await db.Set<ProductVariantUnitBarcode>().OrderBy(x => x.Id).ToListAsync();
        codes[0].Barcode = "0984712540111";
        codes[1].Barcode = "0984712539663";
        db.Add(new ProductVariantUnitBarcode { StoreId = store.StoreId, ProductUnitConversionId = codes[0].ProductUnitConversionId,
            Barcode = "8938505974194", IsPrimary = false, IsActive = true });
        await db.SaveChangesAsync();
        return receipt;
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task Barcode_preparation_reuses_creates_preserves_aliases_progress_and_prints_without_duplicates()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var receipt = await SeedBarcodeReceipt(app, store);
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var staff = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.System.ProductLabel.Print));
        using var denied = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        using var outsider = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        var printer = await admin.JsonAsync(HttpMethod.Post, Url + "printers", new SaveLabelPrinter { Name = "Tem nhỏ", WindowsPrinterName = "FAKE ONLY" });
        var template = await admin.JsonAsync(HttpMethod.Post, Url + "templates", new SaveLabelTemplate(new() { PrinterId = Id(printer) }, null));
        var taskId = Id(await staff.JsonAsync(HttpMethod.Post, Url + "receipts/" + receipt));
        var task = await staff.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        var variants = task.GetProperty("lines").EnumerateArray().Select(x => x.GetProperty("product").GetProperty("variantId").GetInt32()).ToList();
        // A previously skipped line remains skipped even though its default barcode changes.
        task = await staff.JsonAsync(HttpMethod.Post, Url + $"tasks/{taskId}/resolve", new LabelLineResolution(Version(task), Guid.NewGuid(), [variants[1]], false, "Đã có tem"));
        var request = new LabelBarcodeRequest(Id(template), Version(template), taskId, Version(task), variants, null);
        var large = await admin.JsonAsync(HttpMethod.Post, Url + "templates", new SaveLabelTemplate(new() { PrinterId = Id(printer), WidthMm = 50, HeightMm = 30, Columns = 1 }, null));
        var largePlan = (await staff.JsonAsync(HttpMethod.Post, Url + "barcodes/check", request with { TemplateId = Id(large), TemplateVersion = Version(large) })).Deserialize<LabelBarcodePlan>(LabelJson.Options)!;
        Assert.Empty(largePlan.Issues); // A barcode already fitting the selected paper must not be changed.
        var plan = (await staff.JsonAsync(HttpMethod.Post, Url + "barcodes/check", request)).Deserialize<LabelBarcodePlan>(LabelJson.Options)!;
        Assert.Equal(2, plan.Issues.Count); Assert.Single(plan.Issues.Where(x => x.Reuse));
        Assert.Equal("8938505974194", plan.Issues.Single(x => x.Reuse).NewBarcode);
        Assert.Equal("EAN13", ProductLabelRenderer.DetectBarcodeFormat(plan.Issues.Single(x => !x.Reuse).NewBarcode));
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(3, await db.Set<ProductVariantUnitBarcode>().CountAsync());
            Assert.Empty(await db.Set<ProductVariantBarcodeHistory>().ToListAsync());
            Assert.Empty(await db.Set<ProductLabelJob>().ToListAsync()); // Check/cancel has no mutations.
        }
        request = request with { PlanToken = plan.Token };
        using (var response = await denied.Http.PostAsJsonAsync(Url + "barcodes/prepare", request)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await outsider.Http.PostAsJsonAsync(Url + "barcodes/prepare", request)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await staff.Http.PostAsJsonAsync(Url + "barcodes/prepare", request with { PlanToken = "tampered" })) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var token = staff.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        staff.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var response = await staff.Http.PostAsJsonAsync(Url + "barcodes/prepare", request)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        staff.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        // Two cashiers confirming the same snapshot must allocate only once.
        var attempts = await Task.WhenAll(staff.Http.PostAsJsonAsync(Url + "barcodes/prepare", request), staff.Http.PostAsJsonAsync(Url + "barcodes/prepare", request));
        Assert.Single(attempts.Where(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Single(attempts.Where(x => x.StatusCode == HttpStatusCode.Conflict));
        var result = await attempts.Single(x => x.IsSuccessStatusCode).Content.ReadFromJsonAsync<JsonElement>();
        foreach (var response in attempts) response.Dispose();
        task = result.GetProperty("task");
        Assert.False(task.GetProperty("sourceChanged").GetBoolean());
        Assert.Equal(1, task.GetProperty("progress").GetProperty("pending").GetInt32());
        Assert.Equal(1, task.GetProperty("progress").GetProperty("skipped").GetInt32());
        using (var retry = await staff.Http.PostAsJsonAsync(Url + "barcodes/prepare", request)) Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        var ready = (await staff.JsonAsync(HttpMethod.Post, Url + "barcodes/check", request with { RowVersion = Version(task) })).Deserialize<LabelBarcodePlan>(LabelJson.Options)!;
        Assert.Empty(ready.Issues);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var codes = await db.Set<ProductVariantUnitBarcode>().ToListAsync();
            Assert.Equal(4, codes.Count); Assert.All(codes, b => Assert.True(b.IsActive));
            Assert.All(codes.Where(x => x.Barcode.StartsWith("098")), x => Assert.False(x.IsPrimary));
            Assert.Equal(2, codes.Count(x => x.IsPrimary));
            var history = await db.Set<ProductVariantBarcodeHistory>().ToListAsync();
            Assert.Equal(2, history.Count); Assert.All(history, x => { Assert.NotNull(x.ChangedByUserId); Assert.Contains("giữ mã cũ", x.Reason!); });
            Assert.Empty(await db.Set<ProductLabelJob>().ToListAsync());
        }
        foreach (var old in new[] { "0984712540111", "0984712539663" })
            Assert.Single((await staff.JsonAsync(HttpMethod.Get, Url + "products?q=" + old)).EnumerateArray());
        var job = await staff.JsonAsync(HttpMethod.Post, Url + "jobs", new LabelJobRequest(taskId, Id(template), Id(printer), variants.Select(v => new LabelQuantity(v, 2)).ToList(), Guid.NewGuid(), Version(task), Version(template)) { ProductProgress = true });
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var payload = LabelJson.Read<LabelPrintPayload>((await db.Set<ProductLabelJob>().SingleAsync()).PayloadJson);
            Assert.All(payload.Items, i => Assert.Contains(plan.Issues, p => p.NewBarcode == i.Product.Barcode));
            Assert.NotEmpty(ProductLabelRenderer.Commands(payload).ToArray());
            await new LabelPrintDispatcher(db, new FakeTransport()).DispatchAsync(Id(printer), default);
        }
        task = await staff.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        Assert.Equal(1, task.GetProperty("progress").GetProperty("handled").GetInt32());
        Assert.Equal(1, task.GetProperty("progress").GetProperty("skipped").GetInt32());
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task Barcode_quick_preparation_targets_exact_unit_and_rejects_stale_confirmation()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        await SeedReceipt(app, store);
        int packId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var pack = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = store.VariantId, Factor = 6, Price = 70000,
                Unit = new Unit { StoreId = store.StoreId, Code = "BCPACK", Name = "Lốc", IsActive = true }, IsActive = true };
            db.Add(pack); await db.SaveChangesAsync(); packId = pack.Id;
            db.Add(new ProductVariantUnitBarcode { StoreId = store.StoreId, ProductUnitConversionId = packId, Barcode = "0984712540111", IsActive = true });
            await db.SaveChangesAsync();
        }
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var printer = await client.JsonAsync(HttpMethod.Post, Url + "printers", new SaveLabelPrinter { Name = "Small", WindowsPrinterName = "FAKE" });
        var template = await client.JsonAsync(HttpMethod.Post, Url + "templates", new SaveLabelTemplate(new() { PrinterId = Id(printer) }, null));
        async Task<QuickLabelOption> Option() => (await client.JsonAsync(HttpMethod.Get, Url + "products?variantId=" + store.VariantId)).Deserialize<List<QuickLabelOption>>(LabelJson.Options)!.Single(x => x.ConversionId == packId);
        var option = await Option();
        var request = new LabelBarcodeRequest(Id(template), Version(template), null, null, [], [new(packId, 3, option.Fingerprint)]);
        var plan = (await client.JsonAsync(HttpMethod.Post, Url + "barcodes/check", request)).Deserialize<LabelBarcodePlan>(LabelJson.Options)!;
        // Editing a price between the prompt and confirmation must not silently accept it.
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        { (await db.ProductUnitConversions.SingleAsync(x => x.Id == packId)).Price = 71000; await db.SaveChangesAsync(); }
        using (var stale = await client.Http.PostAsJsonAsync(Url + "barcodes/prepare", request with { PlanToken = plan.Token })) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        option = await Option(); request = request with { QuickLines = [new(packId, 3, option.Fingerprint)] };
        plan = (await client.JsonAsync(HttpMethod.Post, Url + "barcodes/check", request)).Deserialize<LabelBarcodePlan>(LabelJson.Options)!;
        var result = await client.JsonAsync(HttpMethod.Post, Url + "barcodes/prepare", request with { PlanToken = plan.Token });
        option = result.GetProperty("options").Deserialize<List<QuickLabelOption>>(LabelJson.Options)!.Single();
        Assert.Equal(71000, option.Product.Price); Assert.Equal(plan.Issues[0].NewBarcode, option.Product.Barcode);
        await client.JsonAsync(HttpMethod.Post, Url + "jobs", new LabelJobRequest(null, Id(template), Id(printer), [], Guid.NewGuid(), null, Version(template)) { QuickLines = [new(packId, 3, option.Fingerprint)] });
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        var baseUnit = await verify.ProductUnitConversions.SingleAsync(x => x.IsBaseUnit);
        Assert.Equal("8938505974194", (await verify.Set<ProductVariantUnitBarcode>().SingleAsync(x => x.ProductUnitConversionId == baseUnit.Id && x.IsPrimary)).Barcode);
        Assert.Equal(2, await verify.Set<ProductVariantUnitBarcode>().CountAsync(x => x.ProductUnitConversionId == packId && x.IsActive));
        Assert.Empty(await verify.Set<ProductLabelTask>().ToListAsync());
    }
}
