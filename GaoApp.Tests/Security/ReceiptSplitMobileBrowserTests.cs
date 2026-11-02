using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class ReceiptSplitMobileBrowserTests
{
    [Fact]
    public async Task Real_phone_split_preserves_source_quantities_permissions_tenant_menus_and_no_posting()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        var manager = await app.AddAccountAsync(store, "*");
        var viewer = await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View);
        using var admin = await app.LoginAsync(manager);
        using var readOnly = await app.LoginAsync(viewer);
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        await using (var setup = app.Database.CreateTenantContext(store.StoreId))
        {
            var receipt = await setup.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            receipt.Status = StockDocumentStatus.PendingApproval;
            receipt.IsMerchandisePaid = false;
            await setup.SaveChangesAsync();
        }
        var protectedBefore = await PosCheckoutMobileBrowserFixture.FingerprintAsync(app, store);
        var apiUrl = $"/admin/api/stock-documents/{seed.ReceiptId}/split";
        async Task<string> SourceSnapshot()
        {
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            return JsonSerializer.Serialize(new {
                header = await db.StockDocuments.AsNoTracking().Where(x => x.Id == seed.ReceiptId).Select(x => new {x.Id,x.Status,x.RowVersion,x.SupplierId,x.WarehouseId}).SingleAsync(),
                lines = await db.StockDocumentLines.AsNoTracking().Where(x => x.StockDocumentId == seed.ReceiptId).OrderBy(x => x.Id).Select(x => new {x.Id,x.RowVersion,x.BaseQuantity,x.Quantity,x.Factor}).ToListAsync()
            });
        }
        var sourceBefore = await SourceSnapshot();
        readOnly.Http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        using (var denied = await readOnly.Http.PostAsJsonAsync(apiUrl, new {})) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await readOnly.Http.GetAsync($"/admin/stock-documents/{seed.ReceiptId}/split")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await foreign.Http.PostAsJsonAsync(apiUrl, new {})) Assert.False(denied.IsSuccessStatusCode);
        var csrf = admin.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        admin.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var denied = await admin.Http.PostAsJsonAsync(apiUrl, new {})) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        admin.Http.DefaultRequestHeaders.Add("RequestVerificationToken", csrf);
        Assert.Equal(sourceBefore, await SourceSnapshot());

        await using var before = app.Database.CreateTenantContext(store.StoreId);
        var original = await before.StockDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        var sourceLines = original.Lines.OrderBy(x => x.LineNo).ToArray();
        var beforeStock = await before.InventoryBalances.AsNoTracking().SumAsync(x => x.OnHandQty);
        var beforeTransactions = await before.InventoryTransactions.CountAsync();
        var beforeReceipts = await before.StockDocuments.CountAsync();
        var root = FullApplicationFixture.SourceRoot();
        var output = Path.Combine(Environment.GetEnvironmentVariable("GAO_UI11_REVIEW_OUTPUT") ?? Path.Combine(root,"TestResults","mobile-ui-11","browser"),"split");
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");
        start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","receipt-split-mobile.browser.cjs"));
        using var browser = Process.Start(start)!;
        var stdout = browser.StandardOutput.ReadToEndAsync(); var stderr = browser.StandardError.ReadToEndAsync();
        await browser.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
            baseUrl = $"http://{store.Host}:{app.Address.Port}",terminalId=store.TerminalId,receiptId=seed.ReceiptId,
            manager = new {user=manager.Name,password=manager.Password}, viewer = new {user=viewer.Name,password=viewer.Password},
            lines=sourceLines.Select(x => new {id=x.Id,baseQuantity=x.BaseQuantity})
        }));
        browser.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try { await browser.WaitForExitAsync(timeout.Token); } catch { if (!browser.HasExited) browser.Kill(true); throw; }
        Assert.True(browser.ExitCode == 0, await stdout + await stderr);
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(beforeReceipts+1, await verify.StockDocuments.CountAsync());
        var results = await verify.StockDocuments.AsNoTracking().Include(x => x.Lines).Where(x => x.Type == StockDocumentType.Receipt).ToListAsync();
        Assert.Equal(2,results.Count);
        Assert.All(results,receipt => {
            Assert.Equal(StockDocumentStatus.PendingApproval,receipt.Status);Assert.Equal(PurchaseReceiptSource.Direct,receipt.ReceiptSource);
            Assert.Equal(original.SupplierId,receipt.SupplierId);Assert.Equal(original.WarehouseId,receipt.WarehouseId);Assert.False(receipt.IsMerchandisePaid);
        });
        foreach (var source in sourceLines)
        {
            var halves=results.SelectMany(x => x.Lines.Where(line => !line.IsDeleted)).Where(x => x.ProductUnitConversionId == source.ProductUnitConversionId).ToArray();
            Assert.Equal(2,halves.Length);Assert.Equal(source.BaseQuantity,halves.Sum(x => x.BaseQuantity));Assert.Equal(source.Quantity,halves.Sum(x => x.Quantity));
            Assert.All(halves,line => {Assert.Equal(source.BaseQuantity/2m,line.BaseQuantity);Assert.Equal(source.Factor,line.Factor);Assert.Equal(source.UnitNameSnapshot,line.UnitNameSnapshot);});
        }
        Assert.Equal(beforeStock,await verify.InventoryBalances.AsNoTracking().SumAsync(x => x.OnHandQty));
        Assert.Equal(beforeTransactions,await verify.InventoryTransactions.CountAsync());Assert.Empty(await verify.PurchasePayables.ToListAsync());
        var audits=await verify.PurchaseReceiptAuditEvents.AsNoTracking().Where(x => x.EventType == PurchaseReceiptAuditEventType.ReceiptSplitSource || x.EventType == PurchaseReceiptAuditEventType.ReceiptSplitChild).ToListAsync();
        Assert.Single(audits,x => x.EventType == PurchaseReceiptAuditEventType.ReceiptSplitSource);Assert.Single(audits,x => x.EventType == PurchaseReceiptAuditEventType.ReceiptSplitChild);
        Assert.Equal(protectedBefore,await PosCheckoutMobileBrowserFixture.FingerprintAsync(app,store));
        await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(new {passed=true,sourceReceiptId=seed.ReceiptId,receiptCount=results.Count,totalBaseQuantity=results.SelectMany(x=>x.Lines).Sum(x=>x.BaseQuantity),exactSourceAndFactorsPreserved=true,inventoryTransactionsUnchanged=true,noPayables=true,sourceAndChildAudits=audits.Count,viewOnlyForeignCsrfRejected=true,storeMenusConfigurationAndOtherStoreUnchanged=true},new JsonSerializerOptions{WriteIndented=true}));
    }
}
