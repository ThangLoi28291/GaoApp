using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06HttpTests(Web01Fixture fixture)
{
    public static TheoryData<string> Commands => new() { "prepare", "pick", "dispatch", "outcome", "return", "incident", "reconcile-incident", "customer-paid", "bank-remittance", "cash-remittance", "withholding", "post-sale" };
    [Theory, MemberData(nameof(Commands))]
    public async Task Actual_admin_mutation_routes_require_real_antiforgery_cookie_and_header(string operation)
    {
        var c = await Web06Case.CreateAsync(fixture); await using var host = await Web06AdminHost.StartAsync(c); using var client = host.Client();
        var before = await c.PhysicalSnapshotAsync(); using var denied = await client.PostAsJsonAsync(Path(c, operation), await c.RequestAsync<StorefrontOperationRequest>());
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode); Assert.Equal(before, await c.PhysicalSnapshotAsync());
    }
    [Theory, MemberData(nameof(Commands))]
    public async Task Actual_admin_action_permission_is_independent_of_ViewOrders_and_cannot_be_obtained_from_request_body(string operation)
    {
        var c = await Web06Case.CreateAsync(fixture); await using var host = await Web06AdminHost.StartAsync(c, codes: [StorefrontPermissions.ViewOrders]); using var client = host.Client();
        await Web06AdminHost.Csrf(client, c.OrderId); var before = await c.PhysicalSnapshotAsync();
        using var denied = await client.PostAsJsonAsync(Path(c, operation), new { clientRequestId = Guid.NewGuid(), expectedRevision = 1, expectedVersion = "client", permissions = new[] { "*" }, isHostAdmin = true, actorUserId = c.Seed.UserId });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal(before, await c.PhysicalSnapshotAsync());
    }
    [Fact]
    public async Task Actual_admin_HTTP_existing_COD_order_can_complete_while_new_sales_closed_with_one_cash_in_sale_invoice_and_readonly_guest()
    {
        var c = await Web06Case.CreateAsync(fixture); await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); await using var host = await Web06AdminHost.StartAsync(c, terminal); using var client = host.Client();
        await using (var db = c.Context()) await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsEnabled, false));
        await Web06AdminHost.Csrf(client, c.OrderId); var initial = await c.RequestAsync<StorefrontOperationRequest>();
        var original = await Post<StorefrontOperationAck>(client, Path(c, "prepare"), initial); Assert.False(original.Replayed);
        Assert.True((await Post<StorefrontOperationAck>(client, Path(c, "prepare"), initial)).Replayed);
        var invalid = await c.PickingAsync(); invalid.Lines[0] = invalid.Lines[0] with { BaseQuantity = "1,5" }; var before = await c.PhysicalSnapshotAsync();
        using (var result = await client.PostAsJsonAsync(Path(c, "pick"), invalid)) Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(before, await c.PhysicalSnapshotAsync());
        await Post<StorefrontOperationAck>(client, Path(c, "pick"), await c.PickingAsync());
        await Post<StorefrontOperationAck>(client, Path(c, "dispatch"), await c.DispatchRequestAsync()); var detail = await c.DetailAsync();
        var outcome = await c.RequestAsync<StorefrontDeliveryOutcomeRequest>(); outcome.FulfillmentSourceId = detail.Children.Single().SourceId; outcome.Delivered = true; outcome.ResultOccurredAt = DateTimeOffset.UtcNow; outcome.EvidenceReference = "Actual controlled HTTP delivered evidence";
        await Post<StorefrontOperationAck>(client, Path(c, "outcome"), outcome);
        var paid = await c.RequestAsync<StorefrontCodCollectionRequest>(); paid.FulfillmentSourceId = outcome.FulfillmentSourceId; paid.Amount = 200_000m; paid.CustomerPaidAt = DateTimeOffset.UtcNow; paid.EvidenceReference = "Actual controlled HTTP customer paid evidence";
        await Post<StorefrontOperationAck>(client, Path(c, "customer-paid"), paid);
        await using (var db = c.Context()) { var summary = await Web06FinanceSupport.Settlement(c, db, terminal).GetAsync(c.OrderId); Assert.Equal(200_000m, summary.CollectorOutstanding); Assert.Equal(0m, summary.ActualStoreReceived); }
        var cashId = await Web06FinanceSupport.ActualCashInAsync(c, terminal, 200_000m); var cash = await c.RequestAsync<StorefrontCodCashReceiptRequest>(); cash.POSShiftCashTransactionId = cashId;
        await Post<StorefrontOperationAck>(client, Path(c, "cash-remittance"), cash); var sale = await c.RequestAsync<StorefrontOperationRequest>();
        await Post<StorefrontOperationAck>(client, Path(c, "post-sale"), sale); Assert.True((await Post<StorefrontOperationAck>(client, Path(c, "post-sale"), sale)).Replayed);
        await using var check = c.Context(); Assert.Equal(StorefrontOrderPhase.Completed, (await check.StorefrontOrders.SingleAsync()).Phase); Assert.Single(await check.Orders.ToListAsync());
        Assert.Single(await check.InvoiceHeads.ToListAsync()); Assert.Single(await check.POSShiftCashTransactions.ToListAsync()); Assert.Equal(200_000m, (await check.POSShifts.SingleAsync()).CashInTotal);
        Assert.Equal(1, await check.InventoryTransactions.CountAsync(x => x.ReferenceType == InventoryReferenceType.Delivery)); Assert.Empty(await check.AdminMenuItems.ToListAsync());
        var token = Web04Support.GuestToken(c.Placed); var guest = await c.Checkout.Service(check).GetGuestOrderAsync(token); Assert.NotNull(guest); Assert.Equal(StorefrontOrderPhase.Completed, guest.Phase);
        using var page = await client.GetAsync("/admin/storefront/fulfillment/" + c.OrderId); Assert.Equal(HttpStatusCode.OK, page.StatusCode); var html = await page.Content.ReadAsStringAsync();
        Assert.DoesNotContain(token, html); Assert.DoesNotContain("protectedEvidence", html, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("BindingSnapshot", html, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Actual_HTTP_foreign_order_lookup_mutation_and_unauthenticated_read_do_not_disclose_other_store_or_write()
    {
        var c = await Web06Case.CreateAsync(fixture); var other = await Web06Case.CreateAsync(fixture);
        await using var host = await Web06AdminHost.StartAsync(c); using var client = host.Client(); using var anonymous = host.Client(false);
        using (var denied = await anonymous.GetAsync("/admin/storefront/fulfillment")) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await Web06AdminHost.Csrf(client, c.OrderId); var before = await other.PhysicalSnapshotAsync();
        using (var read = await client.GetAsync("/admin/storefront/fulfillment/" + other.OrderId))
        { Assert.Equal(HttpStatusCode.NotFound, read.StatusCode); Assert.DoesNotContain(other.Placed.Code, await read.Content.ReadAsStringAsync()); }
        using (var write = await client.PostAsJsonAsync("/admin/storefront/fulfillment/" + other.OrderId + "/prepare", await other.RequestAsync<StorefrontOperationRequest>())) Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        Assert.Equal(before, await other.PhysicalSnapshotAsync()); await using var db = c.Context(); Assert.Empty(await db.DeliveryOrders.ToListAsync());
    }
    [Fact]
    public async Task Extra_client_store_owner_price_or_courier_claims_cannot_redirect_real_source_preparation()
    {
        var c = await Web06Case.CreateAsync(fixture); var other = await Web06Case.CreateAsync(fixture); await using var host = await Web06AdminHost.StartAsync(c); using var client = host.Client();
        await Web06AdminHost.Csrf(client, c.OrderId); var request = await c.RequestAsync<StorefrontOperationRequest>();
        using var result = await client.PostAsJsonAsync(Path(c, "prepare"), new { request.ClientRequestId, request.ExpectedRevision, request.ExpectedVersion, request.Note,
            storeId = other.StoreId, warehouseId = other.Seed.WarehouseId, legalEntityId = other.Seed.LegalEntityId, unitPrice = 1, totalDue = 1, actorUserId = other.Seed.UserId });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode); await using var db = c.Context(); var actual = await db.DeliveryOrders.SingleAsync();
        Assert.Equal(c.StoreId, actual.StoreId); Assert.Equal(c.Seed.WarehouseId, actual.SourceWarehouseId); Assert.Equal(c.Seed.LegalEntityId, actual.SourceLegalEntityId); Assert.Equal(200_000m, actual.QuotedTotal);
        await using var foreign = other.Context(); Assert.Empty(await foreign.DeliveryOrders.ToListAsync()); Assert.Empty(await foreign.Set<StorefrontCommandReceipt>().Where(x => x.Operation == "StartPreparation").ToListAsync());
    }
    [Fact]
    public async Task Actual_admin_HTTP_dispatch_allows_negative_stock_and_retries_do_not_export_twice()
    {
        var c = await Web06Case.CreateAsync(fixture); await c.PrepareAsync(); await c.PickAllAsync();
        await using (var db = c.Context())
        {
            var loss = new InventoryMovementFactory().CreateAdjustmentDecrease(c.Seed.WarehouseId, c.Seed.VariantId, 8m, 10_000m, "WEB06 HTTP actual stock loss after reservation");
            loss.AllowNegativeBalance = true; await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(loss);
        }
        await using var host = await Web06AdminHost.StartAsync(c); using var client = host.Client(); await Web06AdminHost.Csrf(client, c.OrderId);
        var request = await c.DispatchRequestAsync(); using var response = await client.PostAsJsonAsync(Path(c, "dispatch"), request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await c.PhysicalSnapshotAsync();
        using var retry = await client.PostAsJsonAsync(Path(c, "dispatch"), request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode); Assert.Equal(after, await c.PhysicalSnapshotAsync());
        await using var check = c.Context();
        Assert.Equal(-2m, (await check.InventoryBalances.SingleAsync()).OnHandQty);
        Assert.Equal(0m, (await check.InventoryBalances.SingleAsync()).ReservedQty);
        Assert.Single(await check.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Delivery).ToArrayAsync());
        Assert.Contains(await check.DeliveryDispatchCostFragments.ToListAsync(), x => x.IsProvisional);
    }
    [Fact]
    public async Task Actual_admin_HTTP_over_return_rejection_retains_machine_code_and_all_original_dispatch_slices_instead_of_500()
    {
        var c = await Web06Case.CreateAsync(fixture); await c.PrepareAsync(); await c.PickAllAsync(); await c.DispatchAsync(); await c.OutcomeAllAsync(false);
        await using var host = await Web06AdminHost.StartAsync(c); using var client = host.Client(); await Web06AdminHost.Csrf(client, c.OrderId);
        var request = await c.ReturnRequestAsync(); request.Lines[0] = request.Lines[0] with { BaseQuantity = "4.0001" };
        var before = await c.PhysicalSnapshotAsync(); using var response = await client.PostAsJsonAsync(Path(c, "return"), request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("RETURN_BUDGET_EXCEEDED", body.GetProperty("code").GetString());
        Assert.Equal(before, await c.PhysicalSnapshotAsync());
    }
    internal static string Path(Web06Case c, string operation) => "/admin/storefront/fulfillment/" + c.OrderId + "/" + operation;
    internal static async Task<T> Post<T>(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
