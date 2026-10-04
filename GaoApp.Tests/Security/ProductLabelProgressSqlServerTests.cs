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
    internal static async Task<int> SeedProgressReceipt(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        var id = await SeedReceipt(app, store);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var first = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
        first.ProductVariantName = "Sữa tươi không đường";
        db.Add(new ProductImage { StoreId = store.StoreId, ProductId = first.ProductId, IsPrimary = true,
            MediaAsset = new MediaAsset { StoreId = store.StoreId, StoragePath = "uploads/test-label-product.svg", ContentType = "image/svg+xml" } });
        var second = new ProductVariant { StoreId = store.StoreId, ProductId = first.ProductId, Sku = "TEM-SECOND", ProductVariantName = "Bánh gạo vị rong biển", IsActive = true, Price = 25000 };
        db.Add(second); await db.SaveChangesAsync();
        var unit = await db.ProductUnitConversions.SingleAsync(x => x.ProductVariantId == first.Id && x.IsBaseUnit);
        var conversion = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = second.Id, UnitId = unit.UnitId, IsBaseUnit = true, IsActive = true, Factor = 1, Price = 25000 };
        db.Add(conversion); await db.SaveChangesAsync();
        db.Add(new ProductVariantUnitBarcode { StoreId = store.StoreId, ProductUnitConversionId = conversion.Id, Barcode = "000123456", IsPrimary = true, IsActive = true });
        var doc = await db.Set<StockDocument>().Include(x => x.Lines).SingleAsync(x => x.Id == id);
        doc.DocumentTitle = "Nhập hàng Nguyễn buổi sáng";
        doc.Lines.Add(new StockDocumentLine { ProductVariantId = second.Id, Quantity = 10, Factor = 1, BaseQuantity = 10, LineNo = 2, ProductNameSnapshot = second.ProductVariantName, UnitNameSnapshot = "Chai" });
        await db.SaveChangesAsync(); return id;
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task Product_progress_print_reprint_skip_restore_and_search_preserve_history_and_stock()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var staff = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.System.ProductLabel.Print));
        using var outsider = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        var receipt = await SeedProgressReceipt(app, store);
        var printer = await admin.JsonAsync(HttpMethod.Post, Url + "printers", new SaveLabelPrinter { Name = "Máy kho", WindowsPrinterName = "FAKE ONLY" });
        var template = await admin.JsonAsync(HttpMethod.Post, Url + "templates", new SaveLabelTemplate(new() { PrinterId = Id(printer) }, null));
        var taskId = Id(await staff.JsonAsync(HttpMethod.Post, Url + "receipts/" + receipt));
        var task = await staff.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        var second = task.GetProperty("lines")[1].GetProperty("product").GetProperty("variantId").GetInt32();
        Assert.Equal("Nhập hàng Nguyễn buổi sáng", task.GetProperty("documentTitle").GetString());
        var request = new LabelJobRequest(taskId, Id(template), Id(printer), [new(store.VariantId, 2)], Guid.NewGuid(), Version(task), Version(template)) { ProductProgress = true };
        var jobId = Id(await staff.JsonAsync(HttpMethod.Post, Url + "jobs", request));
        Assert.Equal(jobId, Id(await staff.JsonAsync(HttpMethod.Post, Url + "jobs", request)));
        task = await staff.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        Assert.Equal(2, task.GetProperty("progress").GetProperty("pending").GetInt32());
        using (var blocked = await staff.Http.PostAsJsonAsync(Url + $"tasks/{taskId}/resolve", new LabelLineResolution(Version(task), Guid.NewGuid(), [second], false, "Đã có tem"))) Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var transport = new FakeTransport();
        async Task Dispatch()
        {
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            await new LabelPrintDispatcher(db, transport).DispatchAsync(Id(printer), default);
        }
        await Dispatch(); await Dispatch(); Assert.Equal(1, transport.Calls);
        task = await staff.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        Assert.Equal(1, task.GetProperty("progress").GetProperty("handled").GetInt32());
        Assert.Equal(6, task.GetProperty("jobs")[0].GetProperty("status").GetInt32());
        Assert.False(task.GetProperty("completed").GetBoolean());
        var skip = new LabelLineResolution(Version(task), Guid.NewGuid(), [second], false, "Đã có tem");
        using (var hidden = await outsider.Http.PostAsJsonAsync(Url + $"tasks/{taskId}/resolve", skip)) Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        task = await staff.JsonAsync(HttpMethod.Post, Url + $"tasks/{taskId}/resolve", skip);
        Assert.True(task.GetProperty("completed").GetBoolean());
        var completedAt = task.GetProperty("completedAtUtc").GetDateTime().Ticks;
        task = await staff.JsonAsync(HttpMethod.Post, Url + $"tasks/{taskId}/resolve", skip); // Network retry is harmless.
        Assert.Single(task.GetProperty("lines")[1].GetProperty("actions").EnumerateArray());
        using (var reused = await staff.Http.PostAsJsonAsync(Url + $"tasks/{taskId}/resolve", skip with { Reason = "Khác" })) Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        var search = await staff.JsonAsync(HttpMethod.Get, Url + "workspace?q=NGUYEN&state=completed");
        Assert.Single(search.GetProperty("items").EnumerateArray());
        Assert.Empty((await outsider.JsonAsync(HttpMethod.Get, Url + "workspace?state=all")).GetProperty("items").EnumerateArray());
        // Both a printed product and a skipped product can be reprinted; neither changes progress.
        await staff.JsonAsync(HttpMethod.Post, Url + "jobs", request with { RequestId = Guid.NewGuid(), RowVersion = Version(task), Lines = [new(store.VariantId, 3), new(second, 1)] });
        await Dispatch();
        task = await staff.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        Assert.Equal(completedAt, task.GetProperty("completedAtUtc").GetDateTime().Ticks);
        Assert.Equal(1, task.GetProperty("progress").GetProperty("handled").GetInt32());
        Assert.Equal(1, task.GetProperty("progress").GetProperty("skipped").GetInt32());
        Assert.Equal(5, task.GetProperty("lines")[0].GetProperty("sent").GetInt32());
        Assert.True(task.GetProperty("jobs")[0].GetProperty("isReprint").GetBoolean());
        task = await staff.JsonAsync(HttpMethod.Post, Url + $"tasks/{taskId}/resolve", new LabelLineResolution(Version(task), Guid.NewGuid(), [second], true, "Cần in lại tem mới"));
        Assert.False(task.GetProperty("completed").GetBoolean());
        Assert.Equal(2, task.GetProperty("lines")[1].GetProperty("actions").GetArrayLength());
        // One submission can contain both first-time and reprint products. Flags come from server state.
        await staff.JsonAsync(HttpMethod.Post, Url + "jobs", request with { RequestId = Guid.NewGuid(), RowVersion = Version(task), Lines = [new(store.VariantId, 1), new(second, 1)] });
        await Dispatch();
        task = await staff.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        Assert.True(task.GetProperty("completed").GetBoolean());
        Assert.Equal(2, task.GetProperty("progress").GetProperty("handled").GetInt32());
        Assert.False(task.GetProperty("jobs")[0].GetProperty("payload").GetProperty("items")[0].TryGetProperty("countsForProgress", out var flag) && flag.GetBoolean());
        Assert.True(task.GetProperty("jobs")[0].GetProperty("payload").GetProperty("items")[1].GetProperty("countsForProgress").GetBoolean());
        task = await staff.JsonAsync(HttpMethod.Post, Url + $"tasks/{taskId}/refresh", new LabelVersion(Version(task)));
        Assert.True(task.GetProperty("completed").GetBoolean());
        Assert.Equal(2, task.GetProperty("lines")[1].GetProperty("actions").GetArrayLength());
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(100, (await verify.InventoryBalances.SingleAsync()).OnHandQty);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task Product_progress_uncertain_send_stays_pending_until_positive_reconciliation()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var receipt = await SeedReceipt(app, store);
        var printer = await client.JsonAsync(HttpMethod.Post, Url + "printers", new SaveLabelPrinter { Name = "Mock", WindowsPrinterName = "Mock" });
        var template = await client.JsonAsync(HttpMethod.Post, Url + "templates", new SaveLabelTemplate(new() { PrinterId = Id(printer) }, null));
        var taskId = Id(await client.JsonAsync(HttpMethod.Post, Url + "receipts/" + receipt));
        var task = await client.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        var req = new LabelJobRequest(taskId, Id(template), Id(printer), [new(store.VariantId, 5)], Guid.NewGuid(), Version(task), Version(template)) { ProductProgress = true };
        await client.JsonAsync(HttpMethod.Post, Url + "jobs", req);
        var transport = new FakeTransport { Fail = true };
        for (int i = 0; i < 2; i++) { await using var db = app.Database.CreateTenantContext(store.StoreId); await new LabelPrintDispatcher(db, transport).DispatchAsync(Id(printer), default); }
        Assert.Equal(1, transport.Calls);
        task = await client.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        Assert.Equal(1, task.GetProperty("progress").GetProperty("pending").GetInt32());
        var job = task.GetProperty("jobs")[0];
        Assert.Equal(3, job.GetProperty("status").GetInt32());
        await client.JsonAsync(HttpMethod.Post, Url + $"jobs/{Id(job)}/confirm", new LabelConfirmRequest(Version(job), [new(store.VariantId, 2)], "Đã kiểm tra, dùng được 2 tem"));
        task = await client.JsonAsync(HttpMethod.Get, Url + "tasks/" + taskId);
        Assert.True(task.GetProperty("completed").GetBoolean());
        using (var duplicate = await client.Http.PostAsJsonAsync(Url + $"jobs/{Id(job)}/confirm", new LabelConfirmRequest(Version(job), [new(store.VariantId, 2)], "Đã kiểm tra"))) Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(2, task.GetProperty("lines")[0].GetProperty("sent").GetInt32());
    }
}
