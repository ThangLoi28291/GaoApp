using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Banking;
using GaoApp.Infrastructure.Services.Reports;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07PayoutChoiceTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Older_real_eligible_debit_is_not_hidden_by_more_than_one_hundred_newer_negative_internal_transfers_in_the_original_raw_candidate_window()
    {
        var (c, refund) = await ApprovedAsync(); var actual = await Web07RecordedDebit.BankAsync(c, 230_000m);
        await using (var db = c.Context())
        {
            var treasury = new TreasuryService(db);
            for (var i = 0; i < 101; i++) await treasury.CreateAsync(new TreasuryWriteDto
            {
                ClientRequestId = Guid.NewGuid(), Fund = "bank", TargetFund = "cash", Amount = -1m, Date = DateTime.UtcNow.AddHours(7).Date,
                Name = "WEB07 actual internal transfer, never customer payout " + i, Reference = "W07-R01-" + Guid.NewGuid().ToString("N")
            }, default);
            // Exact V1 base candidate predicate: these are all real negative rows
            // inside that range, not CashIn/positive rows excluded before its cap.
            var oldWindow = await db.TreasuryEntries.AsNoTracking().Where(x => x.StoreId == c.StoreId && x.Amount < 0 && -x.Amount <= 230_000m && x.POSShiftCashTransactionId == null)
                .OrderByDescending(x => x.Id).Take(100).Select(x => new { x.Id, x.TargetFund }).ToListAsync();
            Assert.Equal(100, oldWindow.Count); Assert.All(oldWindow, x => Assert.Equal("cash", x.TargetFund)); Assert.DoesNotContain(oldWindow, x => x.Id == actual.Source.Id);
        }
        await using var host = await Web07AdminHost.StartAsync(c); using var client = host.Client();
        using var response = await client.GetAsync($"/admin/storefront/refunds/{c.OrderId}/payout-sources?refundId={refund}"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var choices = (await response.Content.ReadFromJsonAsync<StorefrontRefundPayoutSource[]>())!;
        Assert.Contains(choices, x => x.Kind == actual.Source.Kind && x.Id == actual.Source.Id && x.Amount == 230_000m);
        Assert.DoesNotContain(choices, x => x.Amount == 1m);
        var request = await c.ExecutionRequestAsync(refund, actual.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, actual.ExternalReference, actual.ActualExecutionAt);
        await Web07AdminHost.CsrfAsync(client, c.OrderId); var flow = await Web07RefundExecutionTests.FlowAsync(c);
        using var confirmed = await client.PostAsJsonAsync($"/admin/storefront/refunds/{c.OrderId}/confirm-execution", request); Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(flow, await Web07RefundExecutionTests.FlowAsync(c)); await using var check = c.Context();
        Assert.Equal(230_000m, Assert.Single(await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refund).ToListAsync()).Amount);
        Assert.Equal(102, await check.TreasuryEntries.CountAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Rendered_actual_payer_account_version_prevents_silent_physical_rebinding_on_new_claim_while_successful_replay_preserves_original_evidence(bool alreadyExecuted)
    {
        var (c, refund) = await ApprovedAsync(); var debit = await Web07RecordedDebit.BankAsync(c, 230_000m);
        int payerId; string originalIdentity;
        await using (var db = c.Context())
        {
            var bank = new StoreBankAccount { StoreId = c.StoreId, BankCode = "ACB", BankName = "WEB07 independent actual payer", AccountName = "Synthetic payer",
                AccountNumber = "000W07PAYER" + c.StoreId, IsActive = true };
            db.Add(bank); await db.SaveChangesAsync(); payerId = bank.Id; originalIdentity = GlobalBankReceiptLedger.AccountIdentity(bank.BankCode, bank.AccountNumber);
        }
        await using var host = await Web07AdminHost.StartAsync(c); using var client = host.Client(); await Web07AdminHost.CsrfAsync(client, c.OrderId);
        var html = await client.GetStringAsync($"/admin/storefront/refunds/{c.OrderId}");
        var selected = Regex.Match(html, "<option[^>]*value=\"" + payerId + "\"[^>]*data-version=\"([^\"]+)\"[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(selected.Success, "Actual masked payer option must carry its selected canonical source version.");
        var renderedVersion = WebUtility.HtmlDecode(selected.Groups[1].Value); Assert.Equal(8, Convert.FromBase64String(renderedVersion).Length);
        Assert.DoesNotContain("000W07PAYER" + c.StoreId, html);
        var request = await c.ExecutionRequestAsync(refund, debit.Source, c.Seed.LegalEntityId, payerId, debit.ExternalReference, debit.ActualExecutionAt);
        request.PayerBankAccountVersion = renderedVersion; StorefrontOperationAck? original = null;
        if (alreadyExecuted)
        {
            using var execution = await client.PostAsJsonAsync($"/admin/storefront/refunds/{c.OrderId}/confirm-execution", request);
            Assert.Equal(HttpStatusCode.OK, execution.StatusCode); original = await execution.Content.ReadFromJsonAsync<StorefrontOperationAck>();
        }
        await using (var db = c.Context())
        {
            var bank = await db.StoreBankAccounts.SingleAsync(x => x.Id == payerId); bank.AccountNumber += "CHANGED"; await db.SaveChangesAsync();
            Assert.NotEqual(renderedVersion, Convert.ToBase64String(bank.RowVersion)); Assert.NotEqual(originalIdentity, GlobalBankReceiptLedger.AccountIdentity(bank.BankCode, bank.AccountNumber));
        }
        var before = await c.SnapshotAsync(); var flow = await Web07RefundExecutionTests.FlowAsync(c);
        using (var result = await client.PostAsJsonAsync($"/admin/storefront/refunds/{c.OrderId}/confirm-execution", request))
        {
            Assert.Equal(alreadyExecuted ? HttpStatusCode.OK : HttpStatusCode.Conflict, result.StatusCode);
            if (alreadyExecuted) Assert.Equal(original! with { Replayed = true }, await result.Content.ReadFromJsonAsync<StorefrontOperationAck>());
        }
        Assert.Equal(before, await c.SnapshotAsync()); Assert.Equal(flow, await Web07RefundExecutionTests.FlowAsync(c));
        if (alreadyExecuted)
        {
            await Web07AdminHost.RevokeAsync(c, PermissionCodes.Report.Treasury.Manage);
            using (var forbidden = await client.PostAsJsonAsync($"/admin/storefront/refunds/{c.OrderId}/confirm-execution", request)) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.Equal(before, await c.SnapshotAsync()); await Web07AdminHost.GrantAsync(c, [PermissionCodes.Report.Treasury.Manage]);
            using var replay = await client.PostAsJsonAsync($"/admin/storefront/refunds/{c.OrderId}/confirm-execution", request); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        }
        await using var check = c.Context(); var rows = await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refund).ToListAsync();
        if (!alreadyExecuted) Assert.Empty(rows);
        else
        {
            var execution = Assert.Single(rows); using var snapshot = JsonDocument.Parse(execution.SourceSnapshotJson!);
            Assert.Equal(originalIdentity, snapshot.RootElement.GetProperty("payerAccountIdentity").GetString());
            Assert.Equal(renderedVersion, snapshot.RootElement.GetProperty("payerAccountVersion").GetString());
            Assert.Equal(230_000m, execution.Amount);
        }
    }
    private async Task<(Web07Case Case, int Refund)> ApprovedAsync()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        int id; await using (var db = c.Context()) id = (await db.Set<StorefrontRefund>().SingleAsync()).Id;
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(id, "WEB07 nguồn nhận nguyên thủy hợp lệ"));
        return (c, id);
    }
}
