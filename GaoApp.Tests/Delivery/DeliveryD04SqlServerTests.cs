using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD04"), Trait("Category", "DeliveryD04")]
public sealed class DeliveryD04SqlServerTests(DeliveryD02Fixture fixture, ITestOutputHelper? output = null)
{
    [Fact]
    public async Task S01_Concurrent_claims_by_two_actors_have_one_SQL_winner_and_one_work_row()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var other = await c.ActorAsync(PermissionCodes.Delivery.View, PermissionCodes.Delivery.Pick);
        using var client = other.Client;
        var initial = await c.GetAsync();
        var responses = await UnderSqlBarrierAsync<HttpResponseMessage>(c,
            "order:" + c.Account.Store.StoreId + ":" + c.Id, () =>
            [c.Client.Http.PostAsJsonAsync(c.Path("claim"), new DeliveryPickingEnvelope(Guid.NewGuid(), initial.Delivery.Version)),
             client.Http.PostAsJsonAsync(c.Path("claim"), new DeliveryPickingEnvelope(Guid.NewGuid(), initial.Delivery.Version))]);
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        await using var db = c.Source.Context();
        var work = Assert.Single(await db.DeliveryPickingWorks.Where(x => x.DeliveryOrderId == c.Id).ToListAsync());
        Assert.Contains(work.PickerUserId, new[] { c.Account.UserId, other.Account.UserId });
        Assert.Equal(2, await db.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == c.Id));
        Assert.Equal(2, await db.DeliveryCommandReceipts.CountAsync(x => x.DeliveryOrderId == c.Id));
        Assert.Single(await db.DeliveryPickingLines.Where(x => x.DeliveryOrderId == c.Id).ToListAsync());
    }

    [Fact]
    public async Task S02_Concurrent_same_intent_records_one_effect_and_changed_body_conflicts()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var claimed = await c.ClaimAsync();
        var request = new DeliveryPickingReportRequest(Guid.NewGuid(), claimed.Delivery.Version,
            [new(claimed.Lines[0].LineId, "2", null)]);
        var results = await UnderSqlBarrierAsync<DeliveryPickingCommandAck>(c,
            "command:" + c.Account.Store.StoreId + ":" + request.ClientRequestId,
            () => [c.CommandAsync("report", request), c.CommandAsync("report", request)]);
        Assert.Equal(results[0].AppliedRevision, results[1].AppliedRevision);
        Assert.Equal(results[0].AppliedVersion, results[1].AppliedVersion);
        Assert.Single(results, x => !x.Replayed); Assert.Single(results, x => x.Replayed);
        await c.ErrorAsync("report", request with { Lines = [new(claimed.Lines[0].LineId, "1", "Thiếu")] },
            HttpStatusCode.Conflict, "REQUEST_KEY_CONFLICT");
        await using var db = c.Source.Context();
        Assert.Equal(1, await db.DeliveryCommandReceipts.CountAsync(x => x.ClientRequestId == request.ClientRequestId));
        Assert.Equal(3, await db.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == c.Id));
        Assert.Equal(3, await db.DeliveryOutboxMessages.CountAsync(x => x.DeliveryOrderId == c.Id));
        Assert.Equal("2", (await c.GetAsync()).Lines[0].ReportedQuantityText);
    }

    [Fact]
    public async Task S03_Committed_report_and_submit_replay_after_process_restart_with_exact_history_and_sanitized_events()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var claimed = await c.ClaimAsync();
        var report = new DeliveryPickingReportRequest(Guid.NewGuid(), claimed.Delivery.Version, [new(claimed.Lines[0].LineId, "2", null)]);
        var reportAck = await c.CommandAsync("report", report);
        var current = await c.GetAsync();
        var submit = new DeliveryPickingEnvelope(Guid.NewGuid(), current.Delivery.Version);
        var submitAck = await c.CommandAsync("submit", submit);
        await fixture.Web.RestartAsync(enableDeliveryOutbox: false);
        Assert.Equal(reportAck with { Replayed = true }, await c.CommandAsync("report", report));
        Assert.Equal(submitAck with { Replayed = true }, await c.CommandAsync("submit", submit));
        var latest = await c.GetAsync();
        Assert.Equal("ReadyForHandover", latest.Delivery.State);
        Assert.Equal("2", latest.Lines[0].ApprovedQuantityText);
        Assert.Equal("40", latest.ApprovedTotalText);
        await using var db = c.Source.Context();
        Assert.Equal(4, await db.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == c.Id));
        Assert.Equal(4, await db.DeliveryCommandReceipts.CountAsync(x => x.DeliveryOrderId == c.Id));
        var events = await db.DeliveryOutboxMessages.Where(x => x.DeliveryOrderId == c.Id).OrderBy(x => x.Revision).ToArrayAsync();
        Assert.Equal(4, events.Length);
        foreach (var message in events)
        {
            using var document = JsonDocument.Parse(message.PayloadJson);
            var payload = document.RootElement;
            Assert.Equal(message.Revision, payload.GetProperty("revision").GetInt32());
            Assert.Equal(message.EventId, payload.GetProperty("eventId").GetGuid());
            Assert.False(payload.TryGetProperty("recipientPhone", out _));
            Assert.False(payload.TryGetProperty("recipientAddress", out _));
            Assert.False(payload.TryGetProperty("lookupToken", out _));
            Assert.DoesNotContain(latest.Delivery.LookupToken, message.PayloadJson);
        }
        var receipts = await db.DeliveryCommandReceipts.Where(x => x.DeliveryOrderId == c.Id && x.Operation.StartsWith("picking-")).ToArrayAsync();
        Assert.All(receipts, receipt =>
        {
            using var ack = JsonDocument.Parse(receipt.OutcomeJson);
            Assert.False(ack.RootElement.TryGetProperty("capabilities", out _));
            Assert.False(ack.RootElement.TryGetProperty("lines", out _));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S04_Failure_after_aggregate_save_rolls_back_all_picking_facts_and_same_intent_can_retry(bool report)
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var detail = report ? await c.ClaimAsync() : await c.GetAsync();
        var key = Guid.NewGuid();
        await using (var db = c.Source.Context(new DeliveryHistoryFailure()))
        {
            var service = c.Service(db);
            if (report)
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReportAsync(c.Id,
                    new(key, detail.Delivery.Version, [new(detail.Lines[0].LineId, "2", null)])));
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClaimAsync(c.Id, new(key, detail.Delivery.Version)));
        }
        var unchanged = await c.GetAsync();
        Assert.Equal(detail.Delivery.Version, unchanged.Delivery.Version);
        Assert.Equal(detail.Delivery.Revision, unchanged.Delivery.Revision);
        Assert.Null(unchanged.Lines[0].ReportedQuantityText);
        await using (var db = c.Source.Context())
        {
            Assert.False(await db.DeliveryCommandReceipts.AnyAsync(x => x.ClientRequestId == key));
            Assert.Equal(detail.Delivery.Revision, await db.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == c.Id));
            Assert.Equal(detail.Delivery.Revision, await db.DeliveryOutboxMessages.CountAsync(x => x.DeliveryOrderId == c.Id));
            if (!report)
            {
                Assert.False(await db.DeliveryPickingWorks.AnyAsync(x => x.DeliveryOrderId == c.Id));
                Assert.False(await db.DeliveryPickingLines.AnyAsync(x => x.DeliveryOrderId == c.Id));
            }
        }
        if (report)
            await c.CommandAsync("report", new DeliveryPickingReportRequest(key, detail.Delivery.Version, [new(detail.Lines[0].LineId, "2", null)]));
        else await c.CommandAsync("claim", new DeliveryPickingEnvelope(key, detail.Delivery.Version));
    }

    [Fact]
    public async Task S05_P08_Reduced_plan_resets_invalid_report_and_new_replacement_needs_physical_picker_report_before_approval()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture, quantity: 10, unitPrice: 100, lineDiscount: 200);
        var replacement = await c.ReplacementAsync();
        var claimed = await c.ClaimAsync();
        await c.ReportAsync("10");
        var current = await c.GetAsync();
        await c.CommandAsync("plan", Plan(current, "8", replacement.VariantId, replacement.BottleId, "2", "2"));
        var planned = await c.GetAsync();
        Assert.All(planned.Lines.Where(x => x.IsActive), line => Assert.Null(line.ReportedQuantityText));
        Assert.Equal("880", planned.DraftTotalText);
        Assert.Equal("800", planned.QuotedTotalText);
        await c.ErrorAsync("submit", new DeliveryPickingEnvelope(Guid.NewGuid(), planned.Delivery.Version), HttpStatusCode.BadRequest, "PICKING_INCOMPLETE");
        await c.CommandAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), planned.Delivery.Version,
            planned.Lines.Where(x => x.IsActive).Select(x => new DeliveryPickingReportLine(x.LineId, x.PlannedQuantityText, null)).ToArray()));
        var submitted = await c.SubmitAsync();
        Assert.Equal("AwaitingApproval", submitted.Delivery.State);
        Assert.Null(submitted.ApprovedTotalText);
        var approved = await c.ApproveAsync();
        Assert.Equal("ReadyForHandover", approved.Delivery.State);
        Assert.Equal("880", approved.ApprovedTotalText);
        Assert.Equal("0", approved.RootCoverage.Single().ApprovedMissingQuantityText);
        Assert.All(approved.Lines.Where(x => x.IsActive), line => Assert.Equal(c.Account.UserId, line.ReporterUserId));
    }

    [Fact]
    public async Task S05_P07_Partial_and_explicit_zero_lines_remain_distinct_and_require_manager_approval()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture, secondLine: true);
        var detail = await c.ClaimAsync();
        var first = detail.Lines[0]; var second = detail.Lines[1];
        await c.CommandAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), detail.Delivery.Version, [new(first.LineId, "1", "Thiếu một") ]));
        var partial = await c.GetAsync();
        Assert.Null(partial.Lines.Single(x => x.LineId == second.LineId).ReportedQuantityText);
        await c.ErrorAsync("submit", new DeliveryPickingEnvelope(Guid.NewGuid(), partial.Delivery.Version), HttpStatusCode.BadRequest, "PICKING_INCOMPLETE");
        await c.CommandAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), partial.Delivery.Version, [new(second.LineId, "0", "Hết hàng")]));
        var submitted = await c.SubmitAsync();
        Assert.Equal("AwaitingApproval", submitted.Delivery.State);
        var approved = await c.ApproveAsync();
        Assert.Equal("20", approved.ApprovedTotalText);
        Assert.Equal("0", approved.Lines.Single(x => x.LineId == second.LineId).ApprovedQuantityText);
        Assert.Equal("Hết hàng", approved.Lines.Single(x => x.LineId == second.LineId).ShortageReason);
    }

    [Fact]
    public async Task S06_Complete_plan_and_quote_history_preserve_original_source_and_reject_omitted_or_foreign_lines()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture, secondLine: true);
        var detail = await c.ClaimAsync();
        var sourceBefore = await SourceSnapshot(c);
        await c.ErrorAsync("plan", new DeliveryPickingPlanRequest(Guid.NewGuid(), detail.Delivery.Version,
            [new(detail.Lines[0].LineId, "1", null, "Khách giảm")], [], "Đổi kế hoạch", "Khách đồng ý"), HttpStatusCode.BadRequest, "PLAN_INCOMPLETE");
        using var other = await DeliveryD04Case.CreateAsync(fixture);
        var foreignLine = other.Source.Detail.Lines.Single().Id;
        await c.ErrorAsync("plan", new DeliveryPickingPlanRequest(Guid.NewGuid(), detail.Delivery.Version,
            [new(detail.Lines[0].LineId, "1", null, "Giảm"), new(foreignLine, "1", null, null)], [], "Đổi kế hoạch", "Khách đồng ý"), HttpStatusCode.BadRequest);
        await c.CommandAsync("plan", new DeliveryPickingPlanRequest(Guid.NewGuid(), detail.Delivery.Version,
            detail.Lines.Select(x => new DeliveryPickingPlanLine(x.LineId, "0", null, "Khách bỏ dòng")).ToArray(), [], "Bỏ hàng", "Khách đồng ý"));
        var changed = await c.GetAsync();
        Assert.All(changed.Lines, line =>
        {
            Assert.Equal("0", line.ReportedQuantityText);
            Assert.Equal("plan-removal", line.ReportFactKind);
            Assert.Null(line.ApprovedQuantityText);
        });
        Assert.Equal(sourceBefore, await SourceSnapshot(c));
        Assert.Equal(detail.QuotedTotalText, changed.QuotedTotalText);
        var submitted = await c.SubmitAsync();
        Assert.Equal("AwaitingApproval", submitted.Delivery.State);
        await c.ErrorAsync("approve", new DeliveryPickingApproveRequest(Guid.NewGuid(), submitted.Delivery.Version,
            submitted.Lines.Select(x => new DeliveryPickingApprovalLine(x.LineId, "0", null)).ToArray(), "Không có hàng", "Khách bỏ hàng"),
            HttpStatusCode.Conflict, "NO_READY_GOODS");
    }

    [Fact]
    public async Task S07_Several_replacement_SKUs_share_explicit_root_budget_and_cannot_chain_or_cross_order()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture, quantity: 10);
        var a = await c.ReplacementAsync(); var b = await c.ReplacementAsync(bottlePrice: 50);
        var detail = await c.ClaimAsync();
        var root = detail.Lines.Single().LineId;
        await c.ErrorAsync("plan", new DeliveryPickingPlanRequest(Guid.NewGuid(), detail.Delivery.Version,
            [new(root, "0", null, "Thay toàn bộ")], [new(root, a.VariantId, a.BottleId, "3", "7"), new(root, b.VariantId, b.BottleId, "1", "4")],
            "Thay hai hàng", "Khách đồng ý"), HttpStatusCode.BadRequest, "ROOT_COVERAGE_EXCEEDED");
        await c.CommandAsync("plan", new DeliveryPickingPlanRequest(Guid.NewGuid(), detail.Delivery.Version,
            [new(root, "0", null, "Thay toàn bộ")], [new(root, a.VariantId, a.BottleId, "3", "7"), new(root, b.VariantId, b.BottleId, "1", "3")],
            "Thay hai hàng", "Khách đồng ý"));
        var planned = await c.GetAsync();
        Assert.Equal("10", planned.RootCoverage.Single().DraftReplacementCoverageText);
        Assert.Equal("0", planned.RootCoverage.Single().DraftMissingQuantityText);
        Assert.Equal(2, planned.Lines.Count(x => x.IsReplacement));
        var complete = planned.Lines.Where(x => x.IsActive || !x.IsReplacement)
            .Select(x => new DeliveryPickingPlanLine(x.LineId, x.PlannedQuantityText, x.IsReplacement ? x.DraftOriginalCoverageText : null,
                x.PlannedQuantityText == "0" ? "Bỏ hàng" : null)).ToArray();
        await c.ErrorAsync("plan", new DeliveryPickingPlanRequest(Guid.NewGuid(), planned.Delivery.Version, complete,
            [new(planned.Lines.First(x => x.IsReplacement).LineId, a.VariantId, a.BottleId, "1", "1")], "Không nối chuỗi", "Khách đồng ý"), HttpStatusCode.BadRequest, "ROOT_INVALID");
        using var other = await DeliveryD04Case.CreateAsync(fixture);
        await c.ErrorAsync("plan", new DeliveryPickingPlanRequest(Guid.NewGuid(), planned.Delivery.Version, complete,
            [new(other.Source.Detail.Lines[0].Id, a.VariantId, a.BottleId, "1", "1")], "Không nối đơn", "Khách đồng ý"), HttpStatusCode.BadRequest, "ROOT_INVALID");
    }

    [Fact]
    public async Task S08_P10_POST_quotes_fresh_customer_tier_and_replay_preserves_frozen_price_after_catalog_changes()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture, quantity: 10, customerTier: "RETAIL");
        var replacement = await c.ReplacementAsync(bottlePrice: 5000, packPrice: 27000, wholesalePrice: 4500);
        var detail = await c.ClaimAsync();
        var options = await c.Client.Http.GetFromJsonAsync<List<DeliveryPickingReplacementOptionDto>>(c.Path("replacement-options"));
        Assert.Contains(options!, x => x.VariantId == replacement.VariantId && x.ProductUnitConversionId == replacement.BottleId && x.UnitPriceText == "5000.00");
        await using (var db = c.Source.Context())
        {
            var customer = await db.Customers.SingleAsync(x => x.Id == detail.Delivery.CustomerId);
            customer.PriceTier = "WHOLESALE"; await db.SaveChangesAsync();
        }
        var request = Plan(detail, "4", replacement.VariantId, replacement.BottleId, "6", "6");
        var ack = await c.CommandAsync("plan", request);
        var quoted = await c.GetAsync();
        var line = Assert.Single(quoted.Lines, x => x.IsReplacement);
        Assert.Equal("4500.00", line.UnitPriceText); Assert.Equal("27000", line.QuotedNetText);
        await using (var db = c.Source.Context())
        {
            (await db.ProductUnitConversions.SingleAsync(x => x.Id == replacement.BottleId)).WholesalePrice = 7000;
            await db.SaveChangesAsync();
        }
        Assert.Equal(ack with { Replayed = true }, await c.CommandAsync("plan", request));
        Assert.Equal(line.UnitPriceText, (await c.GetAsync()).Lines.Single(x => x.LineId == line.LineId).UnitPriceText);
    }

    [Theory]
    [InlineData(false, "30000", "1")]
    [InlineData(true, "27000", "6")]
    public async Task P10_Replacement_quote_uses_listed_unit_without_automatic_pack_regrouping(bool pack, string expectedNet, string expectedMultiplier)
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture, quantity: 10);
        var replacement = await c.ReplacementAsync(bottlePrice: 5000, packPrice: 27000);
        var detail = await c.ClaimAsync();
        await c.CommandAsync("plan", Plan(detail, "4", replacement.VariantId, pack ? replacement.PackId : replacement.BottleId, pack ? "1" : "6", "6"));
        var line = Assert.Single((await c.GetAsync()).Lines, x => x.IsReplacement);
        Assert.Equal(expectedNet, line.QuotedNetText); Assert.Equal(expectedMultiplier, line.BaseMultiplierText);
        Assert.Equal("6", line.DraftOriginalCoverageText);
    }

    [Fact]
    public async Task P05_Actual_partial_replacement_approval_prorates_its_frozen_one_dong_quote()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var replacement = await c.ReplacementAsync(bottlePrice: 0.26m);
        var detail = await c.ClaimAsync();
        await c.CommandAsync("plan", Plan(detail, "0", replacement.VariantId, replacement.BottleId, "2", "2"));
        var planned = await c.GetAsync(); var line = planned.Lines.Single(x => x.IsReplacement);
        Assert.Equal("1", line.QuotedNetText);
        await c.CommandAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), planned.Delivery.Version,
            [new(line.LineId, "1", "Chỉ soạn được một")]));
        var submitted = await c.SubmitAsync();
        await c.CommandAsync("approve", new DeliveryPickingApproveRequest(Guid.NewGuid(), submitted.Delivery.Version,
            [new(submitted.Lines.Single(x => !x.IsReplacement).LineId, "0", null), new(line.LineId, "1", "1")],
            "Duyệt lượng thực có", "Khách đồng ý"));
        var approved = await c.GetAsync();
        Assert.Equal("1", approved.ApprovedTotalText);
        Assert.Equal("1", approved.Lines.Single(x => x.IsReplacement).ApprovedNetText);
        Assert.Equal("1", approved.RootCoverage.Single().ApprovedMissingQuantityText);
    }

    [Fact]
    public async Task P06_Partial_replacement_approval_requires_explicit_original_coverage_without_inferred_ratio()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture, quantity: 10);
        var replacement = await c.ReplacementAsync(); var detail = await c.ClaimAsync();
        await c.CommandAsync("plan", Plan(detail, "0", replacement.VariantId, replacement.BottleId, "3", "10"));
        var planned = await c.GetAsync(); var line = planned.Lines.Single(x => x.IsReplacement);
        await c.CommandAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), planned.Delivery.Version,
            [new(line.LineId, "1", "Thiếu hai hàng thay")]));
        var submitted = await c.SubmitAsync(); var root = submitted.Lines.Single(x => !x.IsReplacement).LineId;
        await c.ErrorAsync("approve", new DeliveryPickingApproveRequest(Guid.NewGuid(), submitted.Delivery.Version,
            [new(root, "0", null), new(line.LineId, "0", "4.5")], "Duyệt", "Khách đồng ý"), HttpStatusCode.BadRequest, "COVERAGE_INVALID");
        await c.ErrorAsync("approve", new DeliveryPickingApproveRequest(Guid.NewGuid(), submitted.Delivery.Version,
            [new(root, "0", null), new(line.LineId, "2", "4.5")], "Duyệt", "Khách đồng ý"), HttpStatusCode.BadRequest, "QUANTITY_EXCEEDS_SOURCE");
        await c.CommandAsync("approve", new DeliveryPickingApproveRequest(Guid.NewGuid(), submitted.Delivery.Version,
            [new(root, "0", null), new(line.LineId, "1", "4.5")], "Duyệt lượng và bao phủ", "Khách đồng ý"));
        var approved = await c.GetAsync();
        Assert.Equal("4.5", approved.RootCoverage.Single().ApprovedReplacementCoverageText);
        Assert.Equal("5.5", approved.RootCoverage.Single().ApprovedMissingQuantityText);
        Assert.Equal("120", approved.ApprovedTotalText);
    }

    [Theory]
    [InlineData(0, "75.00")]
    [InlineData(1, "20.00")]
    [InlineData(2, "60.00")]
    public async Task P10_Server_price_precedence_and_no_active_conversion_fallback_are_persisted_exactly(int mode, string expected)
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture, customerTier: mode == 2 ? "WHOLESALE" : null);
        var replacement = await c.ReplacementAsync();
        await using (var db = c.Source.Context())
        {
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == replacement.VariantId);
            variant.Price = mode == 1 ? 0 : 75; variant.WholesalePrice = 60;
            foreach (var conversion in await db.ProductUnitConversions.Where(x => x.ProductVariantId == replacement.VariantId).ToArrayAsync())
            {
                conversion.Price = 0; conversion.WholesalePrice = null;
                if (mode == 2) conversion.IsActive = false;
            }
            await db.SaveChangesAsync();
        }
        var detail = await c.ClaimAsync();
        await c.CommandAsync("plan", Plan(detail, "1", replacement.VariantId, mode == 2 ? null : replacement.BottleId, "1", "1"));
        var quote = (await c.GetAsync()).Lines.Single(x => x.IsReplacement);
        Assert.Equal(expected, quote.UnitPriceText); Assert.Equal("1", quote.BaseMultiplierText);
        if (mode == 2) Assert.Null(quote.ProductUnitConversionId);
        else Assert.Equal(replacement.BottleId, quote.ProductUnitConversionId);
    }

    [Fact]
    public async Task S09_P09_Reopen_preserves_valid_reports_but_requires_new_approval_even_when_quantities_return_to_original()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        await c.ClaimAsync(); await c.ReportAsync("2"); var ready = await c.SubmitAsync();
        Assert.Equal("ReadyForHandover", ready.Delivery.State);
        await c.CommandAsync("reopen", new DeliveryPickingReopenRequest(Guid.NewGuid(), ready.Delivery.Version, "Kiểm tra lại"));
        var reopened = await c.GetAsync();
        Assert.Equal("2", reopened.Lines[0].ReportedQuantityText);
        Assert.Null(reopened.ApprovedRevision); Assert.Null(reopened.ApprovedTotalText);
        var submitted = await c.SubmitAsync();
        Assert.Equal("AwaitingApproval", submitted.Delivery.State);
        Assert.True(submitted.ApprovalRequired);
        Assert.Equal("ReadyForHandover", (await c.ApproveAsync()).Delivery.State);
    }

    [Fact]
    public async Task P02_Real_plan_rejects_sub_base_four_digit_residual_without_quote_work_or_history_mutation()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var replacement = await c.ReplacementAsync();
        await using (var db = c.Source.Context())
        {
            var conversion = await db.ProductUnitConversions.SingleAsync(x => x.Id == replacement.BottleId);
            conversion.Factor = 0.5m; conversion.IsBaseUnit = false;
            await db.SaveChangesAsync();
        }
        var detail = await c.ClaimAsync();
        var effects = await c.NonDeliveryEffectsAsync();
        await c.ErrorAsync("plan", Plan(detail, "1", replacement.VariantId, replacement.BottleId, "0.0001", "1"),
            HttpStatusCode.BadRequest, "BASE_QUANTITY_INVALID");
        var unchanged = await c.GetAsync();
        Assert.Equal(detail.Delivery.Version, unchanged.Delivery.Version);
        Assert.Single(unchanged.Lines); Assert.Null(unchanged.Lines[0].ReportedQuantityText);
        Assert.Equal(effects, await c.NonDeliveryEffectsAsync());
        await using var verify = c.Source.Context();
        Assert.Single(await verify.DeliveryOrderLines.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
        Assert.Equal(2, await verify.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == c.Id));
        Assert.Equal(2, await verify.DeliveryCommandReceipts.CountAsync(x => x.DeliveryOrderId == c.Id));
        Assert.Equal(2, await verify.DeliveryOutboxMessages.CountAsync(x => x.DeliveryOrderId == c.Id));
    }

    [Fact]
    public async Task P02_SQL_bounds_trigger_rejects_sub_base_precision_on_an_existing_valid_replacement()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var replacement = await c.ReplacementAsync();
        await using (var db = c.Source.Context())
        {
            var conversion = await db.ProductUnitConversions.SingleAsync(x => x.Id == replacement.BottleId);
            conversion.Factor = 0.5m; conversion.IsBaseUnit = false;
            await db.SaveChangesAsync();
        }
        var detail = await c.ClaimAsync();
        await c.CommandAsync("plan", Plan(detail, "1", replacement.VariantId, replacement.BottleId, "1", "1"));
        var valid = await c.GetAsync(); var line = valid.Lines.Single(x => x.IsReplacement);
        Assert.Equal("0.5", line.PlannedBaseQuantityText);
        await using var verify = c.Source.Context();
        var row = await verify.DeliveryPickingLines.AsNoTracking().SingleAsync(x => x.DeliveryOrderLineId == line.LineId);
        var error = await Assert.ThrowsAsync<SqlException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE DeliveryPickingLines SET PlannedQuantity=0.0001 WHERE DeliveryOrderLineId={line.LineId}"));
        Assert.Equal(51008, error.Number);
        var after = await verify.DeliveryPickingLines.AsNoTracking().SingleAsync(x => x.Id == row.Id);
        Assert.Equal(row.RowVersion, after.RowVersion); Assert.Equal(1, after.PlannedQuantity);
        Assert.Equal(valid.Delivery.Version, (await c.GetAsync()).Delivery.Version);
    }

    [Fact]
    public async Task P08_Plan_increase_resets_old_full_report_that_would_be_reasonless_partial_under_new_plan()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var claimed = await c.ClaimAsync();
        var root = claimed.Lines[0].LineId;
        await c.CommandAsync("plan", new DeliveryPickingPlanRequest(Guid.NewGuid(), claimed.Delivery.Version,
            [new(root, "1", null, null)], [], "Khách giảm lượng", "Khách đồng ý"));
        await c.ReportAsync("1");
        var reported = await c.GetAsync();
        Assert.Null(reported.Lines[0].ShortageReason);
        var reporter = reported.Lines[0].ReporterUserId;
        await c.CommandAsync("plan", new DeliveryPickingPlanRequest(Guid.NewGuid(), reported.Delivery.Version,
            [new(root, "2", null, null)], [], "Khách lấy lại đủ", "Khách đồng ý"));
        var increased = await c.GetAsync();
        Assert.Equal("2", increased.Lines[0].PlannedQuantityText);
        Assert.Null(increased.Lines[0].ReportedQuantityText);
        Assert.Null(increased.Lines[0].ReporterUserId);
        Assert.Null(increased.Lines[0].ShortageReason);
        Assert.Equal("unreported", increased.Lines[0].ReportFactKind);
        await c.ErrorAsync("submit", new DeliveryPickingEnvelope(Guid.NewGuid(), increased.Delivery.Version), HttpStatusCode.BadRequest, "PICKING_INCOMPLETE");
        await c.ReportAsync("2");
        Assert.Equal(reporter, (await c.GetAsync()).Lines[0].ReporterUserId);
        Assert.Equal("AwaitingApproval", (await c.SubmitAsync()).Delivery.State);
    }

    [Fact]
    public async Task S10_Ready_reassignment_keeps_approval_and_reporter_identity_without_allowing_old_picker_new_report()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        await c.ClaimAsync(); await c.ReportAsync("2"); var ready = await c.SubmitAsync();
        var target = await c.ActorAsync(PermissionCodes.Delivery.View, PermissionCodes.Delivery.Pick);
        using var client = target.Client;
        await c.CommandAsync("reassign", new DeliveryPickingReassignRequest(Guid.NewGuid(), ready.Delivery.Version, target.Account.UserId, "Nhận bàn giao soạn"));
        var latest = await c.GetAsync();
        Assert.Equal(ready.ApprovedRevision, latest.ApprovedRevision);
        Assert.Equal(ready.ApprovedTotalText, latest.ApprovedTotalText);
        Assert.Equal("ReadyForHandover", latest.Delivery.State);
        Assert.Equal(c.Account.UserId, latest.Lines[0].ReporterUserId);
        Assert.Equal(target.Account.UserId, latest.PickerUserId);
        await c.ErrorAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), latest.Delivery.Version,
            [new(latest.Lines[0].LineId, "2", null)]), HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task S11_Inactive_source_warehouse_or_owner_blocks_even_exact_replay(bool warehouse)
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var initial = await c.GetAsync();
        var request = new DeliveryPickingEnvelope(Guid.NewGuid(), initial.Delivery.Version);
        await c.CommandAsync("claim", request);
        await using var db = c.Source.Context();
        var id = warehouse ? initial.Delivery.SourceWarehouseId : initial.Delivery.SourceLegalEntityId;
        try
        {
            await db.Database.ExecuteSqlRawAsync(warehouse ? "UPDATE [Warehouses] SET IsActive=0 WHERE Id={0}" : "UPDATE [LegalEntities] SET IsActive=0 WHERE Id={0}", id);
            await c.ErrorAsync("claim", request, HttpStatusCode.Forbidden, "SOURCE_SCOPE_INVALID");
        }
        finally { await db.Database.ExecuteSqlRawAsync(warehouse ? "UPDATE [Warehouses] SET IsActive=1 WHERE Id={0}" : "UPDATE [LegalEntities] SET IsActive=1 WHERE Id={0}", id); }
        Assert.True((await c.CommandAsync("claim", request)).Replayed);
    }

    [Fact]
    public async Task S12_All_seven_commands_preserve_stock_sale_money_debt_rewards_and_invoice_evidence()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture, quantity: 10, unitPrice: 100, lineDiscount: 200);
        var replacement = await c.ReplacementAsync();
        var effects = await c.NonDeliveryEffectsAsync();
        async Task NoEffects() => Assert.Equal(effects, await c.NonDeliveryEffectsAsync());
        await c.ClaimAsync(); await NoEffects();
        await c.ReportAsync("10"); await NoEffects();
        var current = await c.GetAsync();
        await c.CommandAsync("plan", Plan(current, "8", replacement.VariantId, replacement.BottleId, "2", "2")); await NoEffects();
        var plan = await c.GetAsync();
        await c.CommandAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), plan.Delivery.Version,
            plan.Lines.Where(x => x.IsActive).Select(x => new DeliveryPickingReportLine(x.LineId, x.PlannedQuantityText, null)).ToArray())); await NoEffects();
        await c.SubmitAsync(); await NoEffects();
        var ready = await c.ApproveAsync(); await NoEffects();
        await c.CommandAsync("reopen", new DeliveryPickingReopenRequest(Guid.NewGuid(), ready.Delivery.Version, "Đếm lại")); await NoEffects();
        await c.SubmitAsync(); await NoEffects();
        ready = await c.ApproveAsync(); await NoEffects();
        var target = await c.ActorAsync(PermissionCodes.Delivery.View, PermissionCodes.Delivery.Pick);
        using var client = target.Client;
        await c.CommandAsync("reassign", new DeliveryPickingReassignRequest(Guid.NewGuid(), ready.Delivery.Version, target.Account.UserId, "Đổi người")); await NoEffects();
        var latest = await c.GetAsync();
        Assert.Equal("880", latest.ApprovedTotalText);
        await using var db = c.Source.Context();
        Assert.Equal(latest.Delivery.Revision, await db.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == c.Id));
        Assert.Equal(latest.Delivery.Revision, await db.DeliveryCommandReceipts.CountAsync(x => x.DeliveryOrderId == c.Id));
        Assert.Equal(latest.Delivery.Revision, await db.DeliveryOutboxMessages.CountAsync(x => x.DeliveryOrderId == c.Id));
    }

    private static DeliveryPickingPlanRequest Plan(DeliveryPickingDetailDto detail, string retained, int variantId,
        int? conversionId, string replacementQuantity, string coverage)
        => new(Guid.NewGuid(), detail.Delivery.Version,
            [new(detail.Lines.Single(x => !x.IsReplacement).LineId, retained, null, retained == "0" ? "Thay hàng" : null)],
            [new(detail.Lines.Single(x => !x.IsReplacement).LineId, variantId, conversionId, replacementQuantity, coverage)],
            "Điều chỉnh hàng theo yêu cầu", "Khách đã đồng ý hàng thay");

    private async Task<T[]> UnderSqlBarrierAsync<T>(DeliveryD04Case c, string resource, Func<Task<T>[]> start)
    {
        await using var gate = LocalDbSqlConnectionFactory.Create(c.Web.Database.ConnectionString);
        await gate.OpenAsync();
        await using var tx = (SqlTransaction)await gate.BeginTransactionAsync();
        await using (var command = gate.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=1000; SELECT @r;";
            command.Parameters.AddWithValue("@resource", "GaoApp:Delivery:" + resource);
            Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync()) >= 0);
        }
        string gateResource;
        await using (var command = gate.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = "SELECT resource_description FROM sys.dm_tran_locks WHERE resource_database_id=DB_ID() AND resource_type='APPLICATION' AND request_session_id=@@SPID AND request_mode='X' AND request_status='GRANT'";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            gateResource = reader.GetString(0);
            Assert.False(await reader.ReadAsync());
        }
        var pending = start();
        var blocked = 0;
        var gateHeldBeforeRelease = false;
        var bothPendingBeforeRelease = false;
        var workerSessions = Array.Empty<int>();
        var gatePaths = Array.Empty<int[]>();
        try
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(4))
            {
                await using var command = gate.CreateCommand();
                command.Transaction = tx;
                command.CommandText = """
                    SELECT session_id,blocking_session_id,wait_type FROM sys.dm_exec_requests
                    WHERE database_id=DB_ID() AND session_id<>@@SPID;
                    SELECT request_session_id,request_mode,request_status FROM sys.dm_tran_locks
                    WHERE resource_database_id=DB_ID() AND resource_type='APPLICATION' AND resource_description=@resource;
                    """;
                command.Parameters.AddWithValue("@resource", gateResource);
                var requests = new List<(int SessionId, int BlockingSessionId, string? WaitType)>();
                var locks = new List<(int SessionId, string Mode, string Status)>();
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync()) requests.Add((reader.GetInt16(0), reader.GetInt16(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
                    await reader.NextResultAsync();
                    while (await reader.ReadAsync()) locks.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
                }
                workerSessions = locks.Where(x => x.SessionId != gate.ServerProcessId && x.Mode == "X" && x.Status is "WAIT" or "CONVERT")
                    .Select(x => x.SessionId).Distinct().Order().ToArray();
                int[] PathToHeldGate(int worker)
                {
                    var visited = new HashSet<int>();
                    var path = new List<int>();
                    var current = worker;
                    for (var depth = 0; depth < 32 && visited.Add(current); depth++)
                    {
                        path.Add(current);
                        if (current == gate.ServerProcessId) return path.ToArray();
                        var request = requests.FirstOrDefault(x => x.SessionId == current);
                        if (request.BlockingSessionId <= 0) break;
                        current = request.BlockingSessionId;
                    }
                    return [];
                }
                gatePaths = workerSessions.Where(worker => requests.Any(x => x.SessionId == worker && x.WaitType == "LCK_M_X"))
                    .Select(PathToHeldGate).Where(path => path.Length > 1).ToArray();
                blocked = gatePaths.Length;
                gateHeldBeforeRelease = locks.Count(x => x.SessionId == gate.ServerProcessId && x.Mode == "X" && x.Status == "GRANT") == 1;
                bothPendingBeforeRelease = pending.Length == 2 && pending.All(task => !task.IsCompleted);
                if (workerSessions.Length == 2 && blocked == 2 && gateHeldBeforeRelease && bothPendingBeforeRelease) break;
                await Task.Delay(25);
            }
            output?.WriteLine("D04_SQL_BARRIER_PROOF " + JsonSerializer.Serialize(new
            {
                OwnedDatabase = gate.Database, GateSessionId = gate.ServerProcessId, ExactGateApplicationResource = gateResource,
                DistinctWorkerSessionIds = workerSessions, AcyclicGatePaths = gatePaths,
                GateHeldBeforeRelease = gateHeldBeforeRelease, BothTasksPendingBeforeRelease = bothPendingBeforeRelease,
                BlockedActualWorkerSessions = blocked
            }));
        }
        finally { await tx.RollbackAsync(); }
        var results = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(35));
        Assert.Equal(2, pending.Length);
        Assert.Equal(2, workerSessions.Length);
        Assert.True(gateHeldBeforeRelease);
        Assert.True(bothPendingBeforeRelease);
        Assert.Equal(2, blocked); // Both real SQL commands were waiting before either effect could commit.
        return results;
    }

    private static async Task<string> SourceSnapshot(DeliveryD04Case c)
    {
        await using var db = c.Source.Context();
        return JsonSerializer.Serialize(new
        {
            Order = await db.Orders.AsNoTracking().Where(x => x.Id == c.Source.CartId)
                .Select(x => new { x.Id, x.StoreId, x.POSShiftId, x.Status, x.CustomerId, x.Subtotal, x.GrandTotal, x.PaidTotal, x.Note, x.RowVersion }).SingleAsync(),
            Lines = await db.OrderLines.AsNoTracking().Where(x => x.OrderId == c.Source.CartId).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.VariantId, x.Quantity, x.BaseQuantity, x.UnitPrice, x.LineDiscount, x.LineTotal, x.ItemName, x.RowVersion }).ToArrayAsync()
        });
    }
}
