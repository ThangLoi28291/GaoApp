using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class InputInvoiceCommercialAccessSqlServerTests(InputInvoiceCommercialFixture f)
    : IClassFixture<InputInvoiceCommercialFixture>
{
    private const string Url = "/admin/input-invoices";
    public static IEnumerable<object[]> Matrix =>
        from role in new[] { "viewer", "cost", "direct", "purchase", "both", "importer" }
        from kind in new[] { "unlinked", "direct", "purchase", "mixed", "foreign-key" }
        select new object[] { role, kind };

    private static bool Approves(string role, string kind) => role == "both" ||
        (role == "direct" && kind is "unlinked" or "direct") ||
        (role == "purchase" && kind is "purchase" or "foreign-key");

    [Theory, MemberData(nameof(Matrix))]
    public async Task Real_HTTP_list_detail_preview_and_original_files_enforce_the_same_commercial_policy(string role, string kind)
    {
        var client = f.Clients[role]; var id = f.Ids[kind];
        var allowed = role == "cost" || Approves(role, kind);
        var list = await client.JsonAsync(HttpMethod.Get, Url + "/list?search=" + f.Numbers[kind]);
        var row = Assert.Single(list.GetProperty("rows").EnumerateArray());
        Assert.Equal(allowed, row.GetProperty("canViewCost").GetBoolean());
        AssertMoney(row, "total", allowed, 108000); AssertMoney(row, "tax", allowed, 8000);
        var group = Assert.Single(list.GetProperty("summary").EnumerateArray());
        Assert.Equal(allowed, group.GetProperty("canViewCost").GetBoolean());
        AssertMoney(group, "total", allowed, 108000);
        var detail = await client.JsonAsync(HttpMethod.Get, $"{Url}/{id}");
        Assert.Equal(allowed, detail.GetProperty("canViewCost").GetBoolean());
        Assert.Equal(Approves(role, kind), detail.GetProperty("canReview").GetBoolean());
        AssertMoney(detail, "beforeTax", allowed, 100000); AssertMoney(detail, "total", allowed, 108000);
        AssertMoney(detail, "tax", allowed, 8000);
        var text = detail.GetRawText();
        Assert.Equal(allowed, text.Contains(InputInvoiceCommercialFixture.PrivateNote));
        if (!allowed)
        {
            foreach (var receipt in detail.GetProperty("receipts").EnumerateArray())
                Assert.Equal(JsonValueKind.Null, receipt.GetProperty("documentTitle").ValueKind);
            Assert.DoesNotContain("100000", text); Assert.DoesNotContain("108000", text);
        }
        foreach (var suffix in new[] { "preview", "xml", "pdf" })
        {
            using var response = await client.Http.GetAsync($"{Url}/{id}/{suffix}");
            Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
            if (!allowed) Assert.DoesNotContain("100000", await response.Content.ReadAsStringAsync());
            else if (suffix == "xml") Assert.Equal(f.XmlBytes[kind], await response.Content.ReadAsByteArrayAsync());
            else if (suffix == "pdf") Assert.Equal(InputInvoiceCommercialFixture.Pdf, await response.Content.ReadAsByteArrayAsync());
            else Assert.Contains("Chi phí cửa hàng", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }
    }

    [Theory, MemberData(nameof(Matrix))]
    public async Task Review_requires_the_matching_approval_permission_and_cost_access_alone_does_not_grant_it(string role, string kind)
    {
        var client = f.Clients[role]; var id = f.Ids[kind];
        var detail = await client.JsonAsync(HttpMethod.Get, $"{Url}/{id}");
        var before = detail.GetProperty("history").GetArrayLength();
        using var result = await client.Http.PostAsJsonAsync($"{Url}/{id}/review", new
        {
            status = 1, note = InputInvoiceCommercialFixture.PrivateNote,
            rowVersion = detail.GetProperty("rowVersion").GetString(), canReview = true, canViewCost = true
        });
        var allowed = Approves(role, kind);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, result.StatusCode);
        var after = await client.JsonAsync(HttpMethod.Get, $"{Url}/{id}");
        Assert.Equal(before + (allowed ? 1 : 0), after.GetProperty("history").GetArrayLength());
    }

    [Theory]
    [InlineData("viewer", false, false)]
    [InlineData("importer", false, false)]
    [InlineData("direct", false, false)]
    [InlineData("purchase", false, true)]
    [InlineData("both", true, true)]
    [InlineData("cost", true, true)]
    public async Task Mixed_groups_never_return_restricted_or_partial_amounts(string role, bool original, bool replacement)
    {
        var list = await f.Clients[role].JsonAsync(HttpMethod.Get, Url + "/list");
        Assert.Equal(6, list.GetProperty("total").GetInt32());
        foreach (var group in list.GetProperty("summary").EnumerateArray())
        {
            var allowed = group.GetProperty("kind").GetInt32() == 1 ? replacement : original;
            Assert.Equal(allowed, group.GetProperty("canViewCost").GetBoolean());
            AssertMoney(group, "total", allowed, group.GetProperty("kind").GetInt32() == 1 ? 108000 : 432000);
        }
    }

    [Fact]
    public async Task A_real_zero_amount_is_visible_as_zero_and_query_flags_cannot_override_permissions()
    {
        var id = f.Ids["zero"];
        var allowed = await f.Clients["cost"].JsonAsync(HttpMethod.Get, $"{Url}/{id}");
        Assert.True(allowed.GetProperty("canViewCost").GetBoolean()); AssertMoney(allowed, "total", true, 0);
        var denied = await f.Clients["viewer"].JsonAsync(HttpMethod.Get,
            $"{Url}/{id}?canViewCost=true&viewCost=true&approveDirectReceipt=true&approvePurchaseReceipt=true");
        Assert.False(denied.GetProperty("canViewCost").GetBoolean()); AssertMoney(denied, "total", false, 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/preview")]
    [InlineData("/xml")]
    [InlineData("/pdf")]
    public async Task Foreign_store_remains_not_found_even_with_all_permissions(string suffix)
    {
        using var denied = await f.Foreign.Http.GetAsync($"{Url}/{f.Ids["unlinked"]}{suffix}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var list = await f.Foreign.JsonAsync(HttpMethod.Get, Url + "/list");
        Assert.Empty(list.GetProperty("rows").EnumerateArray()); Assert.Empty(list.GetProperty("summary").EnumerateArray());
    }

    [Fact]
    public async Task File_access_is_rechecked_after_invoice_gets_a_purchase_order_receipt()
    {
        var number = "B2-RELINK-" + Guid.NewGuid().ToString("N")[..8];
        var bytes = Encoding.UTF8.GetBytes(InputInvoiceCatalogSqlServerTests.Xml(number));
        using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "xml", "invoice.xml");
        using var imported = await f.Admin.Http.PostAsync(Url + "/upload", form);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var list = await f.Admin.JsonAsync(HttpMethod.Get, Url + "/list?search=" + number);
        var id = list.GetProperty("rows")[0].GetProperty("id").GetInt32();
        using (var before = await f.Clients["direct"].Http.GetAsync($"{Url}/{id}/xml")) Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        await f.LinkAsync(id, PurchaseReceiptSource.PurchaseOrder);
        using (var after = await f.Clients["direct"].Http.GetAsync($"{Url}/{id}/xml")) Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
        using (var permitted = await f.Clients["purchase"].Http.GetAsync($"{Url}/{id}/xml")) Assert.Equal(HttpStatusCode.OK, permitted.StatusCode);
        // Remove only this test's map/entry so the shared catalog stays stable.
        await using var db = f.App.Database.CreateTenantContext(f.Store.StoreId);
        var head = await db.InputInvoiceHeads.SingleAsync(x => x.NormalizedInvoiceNumber == number);
        db.RemoveRange(await db.StockDocumentInputInvoiceMaps.Where(x => x.InputInvoiceHeadId == head.Id).ToListAsync());
        db.Remove(await db.InputInvoiceLibraryEntries.SingleAsync(x => x.Id == id)); await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Existing_view_permission_and_antiforgery_are_still_required()
    {
        using var noView = await f.App.LoginAsync(await f.App.AddAccountAsync(f.Store, PermissionCodes.Purchase.Order.ViewCost));
        using (var denied = await noView.Http.GetAsync(Url + "/list")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var noCsrf = new HttpClient(new HttpClientHandler { CookieContainer = f.Admin.Cookies }) { BaseAddress = f.App.Address };
        noCsrf.DefaultRequestHeaders.Host = f.Store.Host;
        var detail = await f.Admin.JsonAsync(HttpMethod.Get, $"{Url}/{f.Ids["unlinked"]}");
        using var deniedCsrf = await noCsrf.PostAsJsonAsync($"{Url}/{f.Ids["unlinked"]}/review", new
            { status = 1, rowVersion = detail.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, deniedCsrf.StatusCode);
    }

    [Theory]
    [InlineData("input-invoices")]
    [InlineData("input-invoices/line-maps")]
    [InlineData("input-invoices/picker/candidates?year=2026&month=10")]
    [InlineData("input-invoices/picker/xml?documentKey=forged")]
    [InlineData("input-invoices/picker/pdf?documentKey=forged")]
    [InlineData("input-invoices/{head}/preview/xml")]
    [InlineData("input-invoices/{head}/preview/pdf")]
    [InlineData("input-invoices/reconciliation")]
    public async Task View_only_reader_cannot_bypass_library_policy_through_receipt_XML_routes(string suffix)
    {
        await using var db = f.App.Database.CreateTenantContext(f.Store.StoreId);
        var entry = await db.InputInvoiceLibraryEntries.SingleAsync(x => x.Id == f.Ids["direct"]);
        var map = await db.StockDocumentInputInvoiceMaps.SingleAsync(x => x.InputInvoiceHead.NormalizedInvoiceNumber == entry.Number);
        suffix = suffix.Replace("{head}", map.InputInvoiceHeadId.ToString());
        using var denied = await f.Clients["viewer"].Http.GetAsync($"/admin/api/stock-documents/{map.StockDocumentId}/{suffix}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.DoesNotContain("100000", await denied.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("pdf")]
    public async Task Original_library_files_are_not_accessible_through_public_upload_URLs(string extension)
    {
        using var denied = await f.Clients["viewer"].Http.GetAsync($"/uploads/XML/2026/0312770607/10/invoice-1.{extension}");
        Assert.Contains(denied.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden });
        Assert.DoesNotContain("100000", await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Chrome_displays_restricted_metadata_without_loading_files_and_switches_permissions_per_invoice()
    {
        var root = FullApplicationFixture.SourceRoot(); var output = Path.Combine(root, ".artifacts/b2-invoice-cost-access-20261023/browser");
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs/pos-offline-browser-deps/node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser/input-invoice-commercial-access.browser.cjs"));
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            baseUrl = $"http://{f.Store.Host}:{f.App.Address.Port}", terminalId = f.Store.TerminalId, output, ids = f.Ids,
            accounts = f.Accounts.Where(x => x.Key is "viewer" or "cost" or "direct" or "purchase").ToDictionary(x => x.Key,
                x => new { user = x.Value.Name, password = x.Value.Password })
        }));
        process.StandardInput.Close(); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout + "\n" + await stderr; await File.WriteAllTextAsync(Path.Combine(output, "chrome.log"), log);
        Assert.True(process.ExitCode == 0, log);
    }

    private static void AssertMoney(JsonElement data, string property, bool allowed, decimal amount)
    {
        if (allowed) Assert.Equal(amount, data.GetProperty(property).GetDecimal());
        else Assert.Equal(JsonValueKind.Null, data.GetProperty(property).ValueKind);
    }
}

public sealed class InputInvoiceCommercialFixture : IAsyncLifetime
{
    internal const string PrivateNote = "B2_PRIVATE_PRICE_NOTE_100000";
    internal static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% B2 original transport fixture 100000\n%%EOF");
    internal FullApplicationFixture App { get; private set; } = null!;
    internal FullApplicationFixture.StoreSeed Store => App.Stores[0];
    internal FullApplicationFixture.Client Admin { get; private set; } = null!;
    internal FullApplicationFixture.Client Foreign { get; private set; } = null!;
    internal Dictionary<string, FullApplicationFixture.Client> Clients { get; } = [];
    internal Dictionary<string, FullApplicationFixture.Account> Accounts { get; } = [];
    internal Dictionary<string, int> Ids { get; } = [];
    internal Dictionary<string, string> Numbers { get; } = [];
    internal Dictionary<string, byte[]> XmlBytes { get; } = [];

    public async Task InitializeAsync()
    {
        App = await FullApplicationFixture.StartWithInvoiceLibraryAsync();
        try
        {
            await InputInvoiceCatalogSqlServerTests.SeedAsync(App, Store, 5);
            var folder = Path.Combine(App.InvoiceLibraryRoot, "2026/0312770607/10");
            foreach (var path in Directory.GetFiles(folder, "invoice-*.xml"))
                await File.WriteAllBytesAsync(Path.ChangeExtension(path, ".pdf"), Pdf);
            var zero = Encoding.UTF8.GetBytes(InputInvoiceCatalogSqlServerTests.Xml("B2-ZERO").Replace("100000", "0").Replace("108000", "0").Replace("8000", "0"));
            await File.WriteAllBytesAsync(Path.Combine(folder, "zero.xml"), zero);
            await File.WriteAllBytesAsync(Path.Combine(folder, "zero.pdf"), Pdf);
            Admin = await App.LoginAsync(await App.AddAccountAsync(Store, "*"));
            Foreign = await App.LoginAsync(await App.AddAccountAsync(App.Stores[1], "*"));
            var permissions = new Dictionary<string, string[]>
            {
                ["viewer"] = [], ["cost"] = [PermissionCodes.Purchase.Order.ViewCost],
                ["direct"] = [PermissionCodes.Inventory.StockDocument.Approve],
                ["purchase"] = [PermissionCodes.Purchase.Receipt.Approve],
                ["both"] = [PermissionCodes.Inventory.StockDocument.Approve, PermissionCodes.Purchase.Receipt.Approve],
                ["importer"] = [PermissionCodes.Inventory.StockDocument.Update]
            };
            foreach (var (role, extra) in permissions)
            {
                var account = await App.AddAccountAsync(Store, [PermissionCodes.Inventory.StockDocument.View, .. extra]);
                Accounts.Add(role, account); Clients.Add(role, await App.LoginAsync(account));
            }
            await Admin.JsonAsync(HttpMethod.Post, "/admin/input-invoices/sync?year=2026&month=10");
            string[] kinds = ["unlinked", "foreign-key", "direct", "purchase", "mixed"];
            await using (var db = App.Database.CreateTenantContext(Store.StoreId))
            {
                for (var i = 0; i < kinds.Length; i++)
                {
                    var number = (i + 1).ToString("00000000");
                    Ids.Add(kinds[i], (await db.InputInvoiceLibraryEntries.SingleAsync(x => x.Number == number)).Id);
                    Numbers.Add(kinds[i], number); XmlBytes.Add(kinds[i], await File.ReadAllBytesAsync(Path.Combine(folder, $"invoice-{i + 1}.xml")));
                }
                Ids.Add("zero", (await db.InputInvoiceLibraryEntries.SingleAsync(x => x.Number == "B2-ZERO")).Id);
            }
            await LinkAsync(Ids["direct"], PurchaseReceiptSource.Direct);
            await LinkAsync(Ids["purchase"], PurchaseReceiptSource.PurchaseOrder);
            await LinkAsync(Ids["mixed"], PurchaseReceiptSource.Direct);
            await LinkAsync(Ids["mixed"], PurchaseReceiptSource.PurchaseOrder);
            await LinkAsync(Ids["foreign-key"], PurchaseReceiptSource.Direct, withPurchaseOrder: true);
            foreach (var kind in kinds)
            {
                var detail = await Admin.JsonAsync(HttpMethod.Get, $"/admin/input-invoices/{Ids[kind]}");
                await Admin.JsonAsync(HttpMethod.Post, $"/admin/input-invoices/{Ids[kind]}/review", new
                    { status = 1, note = PrivateNote, rowVersion = detail.GetProperty("rowVersion").GetString() });
            }
        }
        catch { await DisposeAsync(); throw; }
    }

    internal async Task LinkAsync(int id, PurchaseReceiptSource source, bool withPurchaseOrder = false)
    {
        await using var db = App.Database.CreateTenantContext(Store.StoreId);
        var entry = await db.InputInvoiceLibraryEntries.SingleAsync(x => x.Id == id);
        var head = await db.InputInvoiceHeads.SingleOrDefaultAsync(x => x.NormalizedInvoiceNumber == entry.Number && x.NormalizedInvoiceSeries == entry.Series);
        if (head is null)
        {
            head = new InputInvoiceHead { StoreId = Store.StoreId, SellerTaxCode = entry.SellerTaxCode, BuyerTaxCode = entry.BuyerTaxCode,
                InvoiceSeries = entry.Series, InvoiceNumber = entry.Number, InvoiceDate = entry.InvoiceDate };
            InputInvoiceIdentityPolicy.ApplyRequiredIdentity(head); db.Add(head);
        }
        var receipt = new StockDocument { StoreId = Store.StoreId, DocumentNo = "B2-" + Guid.NewGuid().ToString("N"),
            DocumentTitle = PrivateNote, WarehouseId = Store.WarehouseId, ReceiptSource = source };
        if (withPurchaseOrder)
        {
            var legalEntity = await db.LegalEntities.FirstAsync(x => x.Code == "XMLBUYER");
            receipt.PurchaseOrder = new PurchaseOrder { StoreId = Store.StoreId, OrderNumber = "B2-PO-" + Guid.NewGuid().ToString("N"),
                ExpectedWarehouseId = Store.WarehouseId, LegalEntityId = legalEntity.Id };
        }
        db.Add(new StockDocumentInputInvoiceMap { StoreId = Store.StoreId, StockDocument = receipt, InputInvoiceHead = head });
        await db.SaveChangesAsync();
    }
    public async Task DisposeAsync()
    {
        foreach (var client in Clients.Values) client.Dispose();
        Admin?.Dispose(); Foreign?.Dispose(); if (App is not null) await App.DisposeAsync();
    }
}
