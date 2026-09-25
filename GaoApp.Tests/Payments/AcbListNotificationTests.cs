using System.Text.Json;
using System.Text.Json.Nodes;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private static readonly DateTime ReportDay = new(2026, 9, 8);

    [Fact]
    public async Task Retransmission_with_new_transport_trace_time_and_checksum_keeps_original_evidence()
    {
        await using var f = await Fixture.Create();
        var payload = ListNotification("unknown");
        var id = await f.Inbox.AcceptAsync(payload, default);
        var receipt = await f.Db.Set<AcbCallbackReceipt>().SingleAsync();
        receipt.PayloadHash = "legacy-transport-hash";
        await f.Db.SaveChangesAsync();
        var node = JsonNode.Parse(payload.GetRawText())!;
        node["requestTrace"] = "retried-request-trace";
        node["requestDateTime"] = "2026-09-09T12:00:00.000+0700";
        node["requestParameters"]!["masterMeta"]!["checksum"] = "different-retry-checksum";
        Assert.Equal(id, await f.Inbox.AcceptAsync(JsonSerializer.SerializeToElement(node), default));
        Assert.Equal(payload.GetRawText(), receipt.PayloadJson);
        Assert.Single(await f.Db.Set<AcbQrNotificationItem>().ToListAsync());
    }

    [Fact]
    public async Task One_failing_QR_does_not_starve_other_orders_in_the_same_list_page()
    {
        await using var f = await Fixture.Create();
        await f.Service.TryCreateAsync(100, default);
        var second = new Order { StoreId = 1, Id = 101, GrandTotal = 70000, POSShiftId = 10, POSShift = f.Order.POSShift };
        second.Lines.Add(new OrderLine { StoreId = 1, Variant = f.Order.Lines.Single().Variant,
            Quantity = 1, UnitPrice = 70000, LineTotal = 70000 });
        f.Db.Add(second); await f.Db.SaveChangesAsync();
        await f.Service.TryCreateAsync(101, default);
        var ids = f.Bank.Orders.Keys.ToList();
        foreach (var id in ids) f.Bank.Pay(id, 70000);
        f.Bank.FailedRetrieveIds.Add(ids[0]);
        var node = JsonNode.Parse(ListNotification(ids[0]).GetRawText())!;
        var tx = JsonNode.Parse(ListNotification(ids[1]).GetRawText())!["requestParameters"]!["request"]!["requestParams"]!["transactions"]![0]!.DeepClone();
        node["requestParameters"]!["request"]!["requestParams"]!["transactions"]!.AsArray().Add(tx);
        var receiptId = await f.Inbox.AcceptAsync(JsonSerializer.SerializeToElement(node), default);
        await f.Inbox.ProcessAsync(receiptId, default);
        Assert.Equal(AcbSessionStatus.Received, (await f.Db.Set<AcbQrSession>().SingleAsync(x => x.OrderId == 101)).Status);
        Assert.Null((await f.Db.Set<AcbCallbackReceipt>().SingleAsync()).ProcessedAtUtc);
        f.Bank.FailedRetrieveIds.Clear();
        await f.Inbox.RetryAsync(receiptId, default);
        await f.Inbox.ProcessAsync(receiptId, default);
        Assert.NotNull((await f.Db.Set<AcbCallbackReceipt>().SingleAsync()).ProcessedAtUtc);
        Assert.Equal(0, f.FinalizeCount);
    }
    private static JsonElement ListNotification(string orderId, string? requestId = null, int page = 1,
        int pages = 1, string code = "TRANSACTION_HISTORY", string direction = "credit", string status = "COMPLETED", decimal amount = 70000)
    {
        var node = JsonNode.Parse(Notification(orderId, requestId, status, amount, page, pages, code: code).GetRawText())!;
        var tx = node["requestParameters"]!["request"]!["requestParams"]!["transactions"]![0]!;
        tx["transactionChannel"] = "IBFT";
        tx["transactionDate"] = "2026-09-08T10:00:00";
        tx["debitOrCredit"] = direction;
        tx["transactionContent"] = "Thanh toan QR <script>never execute</script>";
        tx["transactionEntityAttribute"]!["virtualAccount"] = "TESTVA";
        return JsonSerializer.SerializeToElement(node);
    }

    [Theory]
    [InlineData("TRANSACTION_UPDATE")]
    [InlineData("TRANSACTION_HISTORY")]
    public async Task List_notification_persists_rows_before_bank_query_and_retries_are_idempotent(string code)
    {
        await using var f = await Fixture.Create();
        var body = ListNotification("unknown", code: code);
        var id = await f.Inbox.AcceptAsync(body, default);
        Assert.Equal(id, await f.Inbox.AcceptAsync(body, default));
        var entry = await f.Db.Set<AcbQrNotificationItem>().SingleAsync();
        Assert.Equal((1, id, code, ReportDay, 70000m), (entry.StoreId, entry.ReceiptId, entry.RequestCode, entry.BusinessDate, entry.Amount));
        Assert.Equal(0, f.Bank.RetrieveCalls);
        Assert.Single(f.Order.Payments);
    }

    [Fact]
    public async Task Daily_and_instant_same_request_identifiers_remain_separate_without_duplicate_money_or_print()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        var bankId = f.Bank.Orders.Single().Key; f.Bank.Pay(bankId, 70000);
        var guid = Guid.NewGuid().ToString();
        var instant = await f.Inbox.AcceptAsync(ListNotification(bankId, guid, code: "TRANSACTION_UPDATE"), default);
        await f.Inbox.ProcessAsync(instant, default);
        await f.Service.CompleteAsync(qr!.Id, default);
        var daily = await f.Inbox.AcceptAsync(ListNotification(bankId, guid), default);
        Assert.NotEqual(instant, daily);
        await f.Inbox.ProcessAsync(daily, default);
        await f.Service.CompleteAsync(qr.Id, default);
        Assert.Equal(1, f.FinalizeCount);
        Assert.Single(f.Order.Payments, x => x.Method == PaymentMethod.BankTransfer);
        var report = await new AcbReconciliationService(f.Db).ReadAsync(ReportDay, ReportDay, null, 1, default);
        Assert.Equal("Matched", Assert.Single(report.Rows).State);
        Assert.Equal(70000m, report.Rows[0].PostedAmount);
        Assert.Single(report.Batches);
        Assert.Equal(2, (await new AcbReconciliationService(f.Db).ReadAsync(ReportDay, ReportDay, "ALL", 1, default)).Total);
    }

    [Fact]
    public async Task Missing_status_callback_is_recovered_by_daily_list_but_only_original_POS_can_finalize()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        var id = f.Bank.Orders.Single().Key; f.Bank.Pay(id, 70000);
        var receiptId = await f.Inbox.AcceptAsync(ListNotification(id), default);
        await f.Inbox.ProcessAsync(receiptId, default);
        Assert.Equal(0, f.FinalizeCount);
        var report = await new AcbReconciliationService(f.Db).ReadAsync(ReportDay, ReportDay, null, 1, default);
        Assert.Equal("Unposted", Assert.Single(report.Rows).State);
        f.Runtime.TerminalId = 4;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CompleteAsync(qr!.Id, default));
        f.Runtime.TerminalId = 3;
        await f.Service.CompleteAsync(qr!.Id, default);
        Assert.Equal(1, f.FinalizeCount);
    }

    [Fact]
    public async Task Out_of_order_pages_show_missing_pages_and_repeated_pages_do_not_add_rows()
    {
        await using var f = await Fixture.Create();
        var guid = Guid.NewGuid().ToString();
        var second = ListNotification("unknown-2", guid, page: 2, pages: 2);
        await f.Inbox.AcceptAsync(second, default);
        await f.Inbox.AcceptAsync(second, default);
        var service = new AcbReconciliationService(f.Db);
        var report = await service.ReadAsync(ReportDay, ReportDay, null, 1, default);
        Assert.Equal((1, 2), (report.Batches[0].ReceivedPages, report.Batches[0].TotalPages));
        Assert.Single(report.Rows);
        await f.Inbox.AcceptAsync(ListNotification("unknown-1", guid, pages: 2), default);
        report = await service.ReadAsync(ReportDay, ReportDay, null, 1, default);
        Assert.Equal((2, 2), (report.Batches[0].ReceivedPages, report.Batches[0].TotalPages));
        Assert.Equal(2, report.Total);
    }

    [Fact]
    public async Task Changed_page_count_and_changed_duplicate_payload_are_rejected_without_overwriting_evidence()
    {
        await using var f = await Fixture.Create();
        var guid = Guid.NewGuid().ToString();
        await f.Inbox.AcceptAsync(ListNotification("unknown", guid, pages: 2), default);
        var error = await Assert.ThrowsAsync<AcbCallbackValidationException>(() => f.Inbox.AcceptAsync(ListNotification("unknown", guid, 2, 3), default));
        Assert.Equal("INCONSISTENT_PAGINATION", error.Code);
        await Assert.ThrowsAsync<AcbCallbackConflictException>(() => f.Inbox.AcceptAsync(ListNotification("different", guid, pages: 2), default));
        Assert.Single(await f.Db.Set<AcbQrNotificationItem>().ToListAsync());
    }

    [Theory]
    [InlineData("debit", "COMPLETED")]
    [InlineData("credit", "ERRORCORRECTED")]
    public async Task Debit_or_corrected_daily_transaction_requires_review_and_does_not_reverse_completed_sale(string direction, string status)
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        var bankId = f.Bank.Orders.Single().Key; f.Bank.Pay(bankId, 70000); await f.Callback();
        await f.Service.CompleteAsync(qr!.Id, default);
        var receiptId = await f.Inbox.AcceptAsync(ListNotification(bankId, direction: direction, status: status), default);
        await f.Inbox.ProcessAsync(receiptId, default);
        Assert.True((await f.Db.Set<AcbCallbackReceipt>().SingleAsync()).NeedsReview);
        Assert.Equal(OrderStatus.Completed, f.Order.Status);
        Assert.Equal(1, f.FinalizeCount);
        Assert.Equal(2, f.Order.Payments.Count);
        var report = await new AcbReconciliationService(f.Db).ReadAsync(ReportDay, ReportDay, null, 1, default);
        Assert.Equal("Review", Assert.Single(report.Rows).State);
    }

    [Theory]
    [InlineData("debitOrCredit")]
    [InlineData("transactionDate")]
    [InlineData("transactionChannel")]
    [InlineData("transactionContent")]
    [InlineData("effectiveDate")]
    public async Task Invalid_daily_required_fields_are_rejected_atomically(string field)
    {
        await using var f = await Fixture.Create();
        var node = JsonNode.Parse(ListNotification("unknown").GetRawText())!;
        node["requestParameters"]!["request"]!["requestParams"]!["transactions"]![0]!.AsObject().Remove(field);
        await Assert.ThrowsAsync<AcbCallbackValidationException>(() => f.Inbox.AcceptAsync(JsonSerializer.SerializeToElement(node), default));
        Assert.Empty(await f.Db.Set<AcbCallbackReceipt>().ToListAsync());
        Assert.Empty(await f.Db.Set<AcbQrNotificationItem>().ToListAsync());
    }

    [Fact]
    public async Task Missing_optional_bill_reference_is_retained_as_unmatched_not_dropped()
    {
        await using var f = await Fixture.Create();
        var id = await f.Inbox.AcceptAsync(ListNotification(""), default);
        await f.Inbox.ProcessAsync(id, default);
        var report = await new AcbReconciliationService(f.Db).ReadAsync(ReportDay, ReportDay, null, 1, default);
        Assert.Equal("Unmatched", Assert.Single(report.Rows).State);
        Assert.True((await f.Db.Set<AcbCallbackReceipt>().SingleAsync()).NeedsReview);
        Assert.Equal(0, f.Bank.RetrieveCalls);
    }

    [Fact]
    public async Task Report_uses_effective_date_not_receipt_date_and_is_store_isolated()
    {
        await using var f = await Fixture.Create();
        var id = await f.Inbox.AcceptAsync(ListNotification("unknown"), default);
        var reports = new AcbReconciliationService(f.Db);
        Assert.Equal(1, (await reports.ReadAsync(ReportDay, ReportDay, null, 1, default)).Total);
        Assert.Equal(0, (await reports.ReadAsync(ReportDay.AddDays(1), ReportDay.AddDays(1), null, 1, default)).Total);
        f.Tenant.SetStore(2, "other");
        Assert.Equal(0, (await reports.ReadAsync(ReportDay, ReportDay, null, 1, default)).Total);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Inbox.RetryAsync(id, default));
    }

    [Fact]
    public async Task Manual_reprocess_unknown_receipt_can_match_a_QR_created_after_notification()
    {
        await using var f = await Fixture.Create();
        var id = await f.Inbox.AcceptAsync(ListNotification("GA0000000001"), default);
        await f.Inbox.ProcessAsync(id, default);
        await f.Service.TryCreateAsync(100, default);
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000);
        await f.Inbox.RetryAsync(id, default);
        await f.Inbox.ProcessAsync(id, default);
        var receipt = await f.Db.Set<AcbCallbackReceipt>().SingleAsync();
        Assert.False(receipt.NeedsReview);
        Assert.Equal(2, receipt.Attempts);
        Assert.Equal("Unposted", (await new AcbReconciliationService(f.Db).ReadAsync(ReportDay, ReportDay, null, 1, default)).Rows[0].State);
    }
}
