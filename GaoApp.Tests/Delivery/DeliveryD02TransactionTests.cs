using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD02"), Trait("Category", "DeliveryD02")]
public sealed class DeliveryD02TransactionTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task Create_is_atomic_replayable_and_does_not_post_sale_stock_or_money()
    {
        using var c = await fixture.CaseAsync(false); var key = Guid.NewGuid();
        int stockCount, orderCount, cashCount;
        await using (var before = c.Context())
        {
            stockCount = await before.InventoryTransactions.CountAsync();
            orderCount = await before.Orders.CountAsync();
            cashCount = await before.POSShiftCashTransactions.CountAsync();
        }
        var result = await c.CreateAsync(key); var retry = await c.CreateAsync(key);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(retry));
        Assert.Equal("Created", result.State); Assert.Equal(64, result.LookupToken.Length); Assert.Equal(40, result.QuotedTotal);
        await using var db = c.Context();
        Assert.Equal(stockCount, await db.InventoryTransactions.CountAsync());
        Assert.Equal(orderCount, await db.Orders.CountAsync());
        Assert.Equal(cashCount, await db.POSShiftCashTransactions.CountAsync());
        Assert.Equal(1, await db.DeliveryOrders.CountAsync(x => x.SourceCartId == c.CartId));
        Assert.Equal(1, await db.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == result.Id));
        Assert.Equal(1, await db.DeliveryOutboxMessages.CountAsync(x => x.DeliveryOrderId == result.Id));
        Assert.Equal(0, await db.DeliveryJournalEntries.CountAsync(x => x.DeliveryOrderId == result.Id));
        Assert.Equal(0, await db.DeliveryDispatchCostFragments.CountAsync(x => x.DeliveryOrderId == result.Id));
        var cart = await db.Orders.SingleAsync(x => x.Id == c.CartId);
        Assert.Equal(GaoApp.Domain.Enums.OrderStatus.Draft, cart.Status); Assert.Equal(0, cart.PaidTotal);
        Assert.Empty(await db.OrderPayments.Where(x => x.OrderId == cart.Id).ToListAsync());
        Assert.Equal(2, Assert.Single(result.Lines).OrderedQuantity);
        var duplicate = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.CreateAsync(Guid.NewGuid()));
        Assert.Equal("SOURCE_ALREADY_USED", duplicate.Code);
    }
    [Fact]
    public async Task Creation_requires_outer_transaction()
    {
        using var c = await fixture.CaseAsync(false); await using var db = c.Context();
        var ex = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.Service(db).CreateInTransactionAsync(new(Guid.NewGuid(), c.CartId, "A", "0901", "Địa chỉ", null)));
        Assert.Equal("TRANSACTION_REQUIRED", ex.Code);
    }
    [Fact]
    public async Task Commit_response_lost_retries_exact_outcome_even_after_newer_revision()
    {
        using var c = await fixture.CaseAsync(); var request = c.Change();
        var first = await c.PostAsync(request);
        await c.PostAsync(c.Change(version: first.Version, name: "Sửa tiếp"));
        var retry = await c.PostAsync(request);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(retry));
        await using var db = c.Context();
        Assert.Equal(3, await db.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == first.Id));
        Assert.Equal(3, await db.DeliveryCommandReceipts.CountAsync(x => x.DeliveryOrderId == first.Id));
    }
    [Fact]
    public async Task Same_key_changed_payload_conflicts_without_second_effect()
    {
        using var c = await fixture.CaseAsync(); var request = c.Change(); await c.PostAsync(request);
        using var response = await c.Client.Http.PostAsJsonAsync("/admin/api/deliveries/" + c.Detail.Id + "/recipient", request with { RecipientName = "Khác" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var db = c.Context(); Assert.Equal(2, await db.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == c.Detail.Id));
    }
    [Fact]
    public async Task Concurrent_same_key_returns_one_stored_response()
    {
        using var c = await fixture.CaseAsync(); var request = c.Change();
        var results = await Task.WhenAll(c.PostAsync(request), c.PostAsync(request));
        Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(results[1]));
        await using var db = c.Context(); Assert.Equal(2, await db.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == c.Detail.Id));
    }
    [Fact]
    public async Task Concurrent_different_commands_same_version_only_one_wins()
    {
        using var c = await fixture.CaseAsync(); var path = "/admin/api/deliveries/" + c.Detail.Id + "/recipient";
        var results = await Task.WhenAll(c.Client.Http.PostAsJsonAsync(path, c.Change(name: "A")), c.Client.Http.PostAsJsonAsync(path, c.Change(name: "B")));
        try { Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var response in results) response.Dispose(); }
        await using var db = c.Context(); Assert.Equal(2, await db.DeliveryOutboxMessages.CountAsync(x => x.DeliveryOrderId == c.Detail.Id));
    }
    [Fact]
    public async Task Failure_after_aggregate_insert_rolls_back_all_and_retry_can_create()
    {
        using var c = await fixture.CaseAsync(false); var key = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.CreateAsync(key, new DeliveryHistoryFailure()));
        await using (var db = c.Context())
        {
            Assert.Empty(await db.DeliveryOrders.Where(x => x.SourceCartId == c.CartId).ToListAsync());
            Assert.Empty(await db.DeliveryCommandReceipts.Where(x => x.ClientRequestId == key).ToListAsync());
            Assert.Equal(GaoApp.Domain.Enums.OrderStatus.Draft, (await db.Orders.SingleAsync(x => x.Id == c.CartId)).Status);
        }
        var success = await c.CreateAsync(key);
        await using var check = c.Context(); Assert.Equal(1, await check.DeliveryOutboxMessages.CountAsync(x => x.DeliveryOrderId == success.Id));
    }
    [Fact]
    public async Task Failure_after_recipient_update_rolls_back_version_history_receipt_and_event()
    {
        using var c = await fixture.CaseAsync(); var request = c.Change();
        await using (var db = c.Context(new DeliveryHistoryFailure()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => c.Service(db).UpdateRecipientAsync(c.Detail.Id, request));
        await using (var db = c.Context())
        {
            var actual = await c.Service(db).GetAsync(c.Detail.Id);
            Assert.Equal(c.Detail.Version, actual.Version); Assert.Equal(c.Detail.RecipientName, actual.RecipientName);
            Assert.Equal(1, await db.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == c.Detail.Id));
            Assert.Equal(1, await db.DeliveryOutboxMessages.CountAsync(x => x.DeliveryOrderId == c.Detail.Id));
            Assert.False(await db.DeliveryCommandReceipts.AnyAsync(x => x.ClientRequestId == request.ClientRequestId));
        }
        await c.PostAsync(request);
    }
    [Fact]
    public async Task SQL_and_EF_reject_mutating_history_or_origin()
    {
        using var c = await fixture.CaseAsync(); await using var db = c.Context();
        var history = await db.DeliveryRevisions.SingleAsync(x => x.DeliveryOrderId == c.Detail.Id);
        history.Action = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE DeliveryRevisions SET Action='tampered' WHERE DeliveryOrderId={c.Detail.Id}"));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM DeliveryOutboxMessages WHERE DeliveryOrderId={c.Detail.Id}"));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE DeliveryOrders SET LookupToken='tampered' WHERE Id={c.Detail.Id}"));
        Assert.Equal(c.Detail.LookupToken, (await c.Service(db).GetAsync(c.Detail.Id)).LookupToken);
    }
}
