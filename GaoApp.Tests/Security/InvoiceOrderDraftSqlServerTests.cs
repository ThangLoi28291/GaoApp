using System.Diagnostics;
using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

public sealed class InvoiceOrderDraftSqlServerTests
{
    [Fact]
    public async Task Empty_draft_is_duplicate_safe_tenant_scoped_and_accepts_existing_manual_lines()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        var otherAccount = await app.AddAccountAsync(app.Stores[1], "*");
        var limitedAccount = await app.AddAccountAsync(store, PermissionCodes.System.Integration.Manage);
        var forbiddenAccount = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var orderId = await SeedOrder(app, account, "POS-DRAFT-RECOVERY-01", InvoiceIssuanceRoute.Automatic);
        var browserOrderId = await SeedOrder(app, account, "POS-DRAFT-BROWSER-02", InvoiceIssuanceRoute.Manual);
        var draftOrder = await SeedOrder(app, account, "POS-DRAFT-NOT-COMPLETED", InvoiceIssuanceRoute.Unselected, OrderStatus.Draft);
        // Integration.Manage is the existing alias for Invoice.Route; preserve that permission contract.
        using var client = await app.LoginAsync(limitedAccount);
        using var other = await app.LoginAsync(otherAccount);
        using var forbidden = await app.LoginAsync(forbiddenAccount);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var orderCount = await db.Orders.CountAsync();
        var stockCount = await db.InventoryTransactions.CountAsync();
        var paymentCount = await db.OrderPayments.CountAsync();

        var search = await client.JsonAsync(HttpMethod.Get, $"/Admin/Invoice/SearchOrdersForDraft?term={orderId}");
        Assert.Single(search.EnumerateArray());
        Assert.False(search[0].GetProperty("hasInvoice").GetBoolean());
        var preview = await client.JsonAsync(HttpMethod.Get, $"/Admin/Invoice/PreviewOrderDraft?orderId={orderId}");
        Assert.Empty(preview.GetProperty("invoices").EnumerateArray());
        Assert.False(await db.InvoiceHeads.AnyAsync());

        using (var crossStore = await PostCreate(other, orderId)) Assert.Equal(HttpStatusCode.BadRequest, crossStore.StatusCode);
        using (var noPermission = await PostCreate(forbidden, orderId)) Assert.Equal(HttpStatusCode.Forbidden, noPermission.StatusCode);
        using (var notCompleted = await PostCreate(client, draftOrder)) Assert.Equal(HttpStatusCode.BadRequest, notCompleted.StatusCode);
        var token = client.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var noToken = await PostCreate(client, orderId)) Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        client.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        Assert.False(await db.InvoiceHeads.AnyAsync());

        var responses = await Task.WhenAll(PostCreate(client, orderId), PostCreate(client, orderId));
        var outcomes = new List<JsonElement>();
        foreach (var response in responses)
        {
            using (response)
            {
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                outcomes.Add(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
            }
        }
        Assert.Single(outcomes, x => x.GetProperty("created").GetBoolean());
        Assert.Single(outcomes, x => !x.GetProperty("created").GetBoolean());
        var head = await db.InvoiceHeads.AsNoTracking().Include(x => x.Details).SingleAsync(x => x.OrderId == orderId);
        Assert.Empty(head.Details); Assert.Equal(0, head.GrandTotal); Assert.Equal(0, head.TotalQuantity);
        Assert.False(head.IsLocked); Assert.Equal(InvoiceProviderStatus.LocalDraft, head.ProviderStatus);
        Assert.Null(head.ProviderInvoiceNo); Assert.NotNull(head.TransactionUuid);
        Assert.Equal("C26DRF", head.InvoiceSeries);
        Assert.Equal(InvoiceIssuanceRoute.Manual, (await db.Orders.AsNoTracking().SingleAsync(x => x.Id == orderId)).InvoiceIssuanceRoute);
        Assert.All(outcomes, x => Assert.Equal(head.Id, x.GetProperty("invoices")[0].GetProperty("id").GetInt32()));
        using (var manual = await client.Http.PostAsync("/Admin/Invoice/CreateManualDetail", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["InvoiceHeadId"] = head.Id.ToString(), ["ProductVariantId"] = store.VariantId.ToString(),
            ["Quantity"] = "3", ["UnitPrice"] = "7000", ["VatRate"] = "0"
        }))) Assert.True(manual.IsSuccessStatusCode, await manual.Content.ReadAsStringAsync());
        var after = await db.InvoiceHeads.AsNoTracking().Include(x => x.Details).SingleAsync(x => x.Id == head.Id);
        Assert.Equal(21000m, after.GrandTotal); Assert.Single(after.Details);
        Assert.Null(after.Details.Single().OrderLineId);
        Assert.Equal(InvoiceDetailSourceType.Manual, after.Details.Single().SourceType);
        var duplicate = await client.JsonAsync(HttpMethod.Get, $"/Admin/Invoice/PreviewOrderDraft?orderId={orderId}");
        Assert.Equal(head.Id, duplicate.GetProperty("invoices")[0].GetProperty("id").GetInt32());
        await RunBrowser(app, account, browserOrderId, orderId);
        Assert.Equal(orderCount, await db.Orders.CountAsync());
        Assert.Equal(stockCount, await db.InventoryTransactions.CountAsync());
        Assert.Equal(paymentCount, await db.OrderPayments.CountAsync());
        Assert.Empty(await db.AutoInvoiceOperations.ToListAsync());
    }

    [Fact]
    public async Task Allocated_order_creates_empty_heads_for_each_HKD_and_invalid_setting_is_atomic()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var account = await app.AddAccountAsync(app.Stores[0], "*");
        var orderId = await SeedOrder(app, account, "POS-MULTI-DRAFT", InvoiceIssuanceRoute.Manual);
        await using var db = app.Database.CreateTenantContext(account.Store.StoreId);
        var order = await db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == orderId);
        var firstSetting = await db.InvoiceProviderSettings.SingleAsync();
        var secondSetting = Setting(account.Store.StoreId, "0100000002", "C26DRE");
        db.InvoiceProviderSettings.Add(secondSetting); await db.SaveChangesAsync();
        var first = await db.LegalEntities.SingleAsync();
        first.TaxCode = firstSetting.SupplierTaxCode; first.InvoiceProviderSettingId = firstSetting.Id;
        var second = new LegalEntity { StoreId = account.Store.StoreId, Code = "DRAFT-B", Name = "HKD B", LegalName = "HKD B",
            TaxCode = secondSetting.SupplierTaxCode, SalePriority = 2, InvoiceProviderSettingId = secondSetting.Id, IsActive = true };
        db.LegalEntities.Add(second); await db.SaveChangesAsync();
        foreach (var entity in new[] { first, second }) db.OrderLegalEntityAllocations.Add(new()
        {
            StoreId = account.Store.StoreId, OrderId = orderId, OrderLineId = order.Lines.Single().Id,
            ProductVariantId = account.Store.VariantId, LegalEntityId = entity.Id, WarehouseId = account.Store.WarehouseId,
            SalePriority = entity.SalePriority, Quantity = 1, BaseQuantity = 1, UnitPrice = 5000, LineTotal = 5000, NetAmount = 5000
        });
        order.HasMultipleLegalEntities = true; order.LegalEntityCount = 2;
        secondSetting.TemplateCode = "";
        await db.SaveChangesAsync();
        using var client = await app.LoginAsync(account);
        using (var invalid = await PostCreate(client, orderId)) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.False(await db.InvoiceHeads.AnyAsync());
        secondSetting.TemplateCode = "1/001"; await db.SaveChangesAsync();
        using (var valid = await PostCreate(client, orderId)) Assert.True(valid.IsSuccessStatusCode, await valid.Content.ReadAsStringAsync());
        var heads = await db.InvoiceHeads.AsNoTracking().Include(x => x.Details).OrderBy(x => x.LegalEntityId).ToListAsync();
        Assert.Equal(2, heads.Count);
        Assert.Equal(new int?[] { first.Id, second.Id }, heads.Select(x => x.LegalEntityId));
        Assert.Equal(new int?[] { firstSetting.Id, secondSetting.Id }, heads.Select(x => x.InvoiceProviderSettingId));
        Assert.All(heads, x => { Assert.Empty(x.Details); Assert.Equal(0, x.GrandTotal); Assert.False(x.IsLocked); });
        using (var retry = await PostCreate(client, orderId))
        {
            var data = JsonDocument.Parse(await retry.Content.ReadAsStringAsync()).RootElement;
            Assert.False(data.GetProperty("created").GetBoolean());
            Assert.Equal(2, data.GetProperty("invoices").GetArrayLength());
        }
        Assert.Equal(2, await db.InvoiceHeads.CountAsync());
    }

    private static Task<HttpResponseMessage> PostCreate(FullApplicationFixture.Client client, int orderId)
        => client.Http.PostAsync("/Admin/Invoice/CreateOrderDraft", new FormUrlEncodedContent(new Dictionary<string, string> { ["orderId"] = orderId.ToString() }));

    private static InvoiceProviderSetting Setting(int storeId, string taxCode = "0100000001", string series = "C26DRF")
        => new() { StoreId = storeId, ProviderCode = "VIETTEL", BaseUrl = "https://example.test", Username = "test", Password = "test",
            SupplierTaxCode = taxCode, InvoiceType = "1", TemplateCode = "1/001", InvoiceSeries = series, CurrencyCode = "VND", IsActive = true };

    internal static async Task<int> SeedOrder(FullApplicationFixture app, FullApplicationFixture.Account account, string number,
        InvoiceIssuanceRoute route, OrderStatus status = OrderStatus.Completed)
    {
        var store = account.Store;
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        if (!await db.InvoiceProviderSettings.AnyAsync()) db.InvoiceProviderSettings.Add(Setting(store.StoreId));
        var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
        variant.HasInputInvoice = true;
        var shift = await db.POSShifts.SingleOrDefaultAsync(x => x.TerminalId == store.TerminalId && x.Status == POSShiftStatus.Open)
            ?? new POSShift { StoreId = store.StoreId, TerminalId = store.TerminalId, WarehouseId = store.WarehouseId, OpenedByUserId = account.UserId };
        var order = new Order { StoreId = store.StoreId, POSShift = shift, OrderNumber = number, Status = status,
            PaymentStatus = PaymentStatus.Paid, Subtotal = 10000, GrandTotal = 10000, PaidTotal = 10000,
            CompletedAtUtc = status == OrderStatus.Completed ? DateTime.UtcNow : null, InvoiceIssuanceRoute = route };
        order.Lines.Add(new() { StoreId = store.StoreId, ProductId = variant.ProductId, VariantId = variant.Id,
            SellingUnitId = variant.Product.BaseUnitId, BaseUnitId = variant.Product.BaseUnitId,
            Quantity = 2, BaseQuantity = 2, Multiplier = 1, UnitPrice = 5000, LineTotal = 10000, ItemName = "Draft recovery product", Sku = variant.Sku });
        db.Orders.Add(order); await db.SaveChangesAsync();
        return order.Id;
    }

    private static async Task RunBrowser(FullApplicationFixture app, FullApplicationFixture.Account account, int orderId, int duplicateOrderId)
    {
        var root = FullApplicationFixture.SourceRoot();
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs/pos-offline-browser-deps/node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser/invoice-order-draft.browser.cjs"));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            baseUrl = $"http://{account.Store.Host}:{app.Address.Port}", user = account.Name, password = account.Password,
            terminalId = account.Store.TerminalId, orderId, duplicateOrderId,
            evidenceRoot = Path.Combine(root, "Logs/invoice-order-draft-20261009")
        }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}
