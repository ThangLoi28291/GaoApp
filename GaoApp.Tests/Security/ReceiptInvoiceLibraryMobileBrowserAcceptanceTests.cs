using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class ReceiptInvoiceLibraryMobileBrowserAcceptanceTests(InputInvoiceCommercialFixture f)
    : IClassFixture<InputInvoiceCommercialFixture>
{
    [Fact]
    public async Task Phone_library_keeps_native_readonly_and_source_permissions_across_resizing_without_writes()
    {
        // This existing fixture owns a GUID LocalDB and a private XML directory. Its setup uses
        // FullApplicationFixture.StartWithInvoiceLibraryAsync and the real catalog SeedAsync.
        var foreignAccount = await f.App.AddAccountAsync(f.App.Stores[1], "*");
        var before = await FingerprintAsync();
        const string url = "/admin/input-invoices";
        var detail = await f.Clients["viewer"].JsonAsync(HttpMethod.Get, $"{url}/{f.Ids["unlinked"]}");
        using (var denied = await f.Clients["viewer"].Http.PostAsJsonAsync($"{url}/{f.Ids["unlinked"]}/review", new
        {
            status = 1, note = "UI-11 forbidden command must not be stored",
            rowVersion = detail.GetProperty("rowVersion").GetString(), canReview = true, canViewCost = true
        })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        // An XML associated with a PO remains purchase-owned even if its legacy receipt source says direct.
        Assert.False((await f.Clients["direct"].JsonAsync(HttpMethod.Get, $"{url}/{f.Ids["foreign-key"]}"))
            .GetProperty("canViewCost").GetBoolean());
        Assert.True((await f.Clients["purchase"].JsonAsync(HttpMethod.Get, $"{url}/{f.Ids["foreign-key"]}"))
            .GetProperty("canViewCost").GetBoolean());
        Assert.Equal(0, (await f.Admin.JsonAsync(HttpMethod.Get, url + "/list?search=FOREIGN"))
            .GetProperty("total").GetInt32());
        foreach (var suffix in new[] { "", "/preview", "/xml", "/pdf" })
        {
            using var denied = await f.Foreign.Http.GetAsync($"{url}/{f.Ids["unlinked"]}{suffix}");
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }

        var root = FullApplicationFixture.SourceRoot();
        var output = MobileAcceptanceEvidence.PathFor(root, "TestResults", "mobile-ui-11-20261026", "library-permissions");
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "receipt-invoice-library-mobile.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            baseUrl = $"http://{f.Store.Host}:{f.App.Address.Port}", terminalId = f.Store.TerminalId, output, ids = f.Ids,
            foreignBaseUrl = $"http://{foreignAccount.Store.Host}:{f.App.Address.Port}",
            foreignTerminalId = foreignAccount.Store.TerminalId,
            foreignAccount = new { user = foreignAccount.Name, password = foreignAccount.Password },
            accounts = f.Accounts.Where(x => x.Key is "viewer" or "cost" or "direct" or "purchase" or "both")
                .ToDictionary(x => x.Key, x => new { user = x.Value.Name, password = x.Value.Password })
        }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout + "\n" + await stderr;
        await File.WriteAllTextAsync(Path.Combine(output, "chrome.log"), log);
        Assert.True(process.ExitCode == 0, log);
        Assert.Equal(before, await FingerprintAsync());
        await File.WriteAllTextAsync(Path.Combine(output, "sql-review.json"), JsonSerializer.Serialize(new
        {
            deniedReviewStatus = 403, foreignStoreStatus = 404, foreignBuyerExcluded = true,
            purchaseSourceWithDirectLegacyFlagUsesPurchasePolicy = true,
            invoiceRecordsReviewsAndStockStoreConfigurationUnchanged = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task<string> FingerprintAsync()
    {
        var store = await PosCheckoutMobileBrowserFixture.FingerprintAsync(f.App, f.Store);
        await using var db = f.App.Database.CreateTenantContext(f.Store.StoreId);
        var entries = await db.InputInvoiceLibraryEntries.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.XmlHash, x.IdentityHash, x.DocumentKey, x.BeforeTax, x.Tax, x.Total,
                x.ReviewStatus, x.ReviewNote, x.ReviewedAtUtc, x.ReviewedBy, x.RowVersion }).ToListAsync();
        var reviews = await db.InputInvoiceLibraryReviews.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.InputInvoiceLibraryEntryId, x.Status, x.PreviousStatus, x.Note, x.ActorId, x.OccurredAtUtc }).ToListAsync();
        var balances = await db.InventoryBalances.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.OnHandQty, x.RowVersion }).ToListAsync();
        var documents = await db.StockDocuments.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Status, x.TotalAmount, x.RowVersion }).ToListAsync();
        var payables = await db.PurchasePayables.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.RowVersion }).ToListAsync();
        var transactions = await db.InventoryTransactions.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.RowVersion }).ToListAsync();
        return JsonSerializer.Serialize(new { store, entries, reviews, balances, documents, payables, transactions });
    }
}
