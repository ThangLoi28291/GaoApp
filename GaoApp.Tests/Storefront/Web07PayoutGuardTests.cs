using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Services.Reports;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07PayoutGuardTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("unknown-kind")] [InlineData("missing-source")] [InlineData("bad-version")] [InlineData("stale-version")]
    [InlineData("missing-owner")] [InlineData("foreign-owner")] [InlineData("foreign-bank")]
    [InlineData("missing-reference")] [InlineData("missing-time")] [InlineData("before-receipt-time")] [InlineData("future-time")]
    [InlineData("missing-payer-version")] [InlineData("malformed-payer-version")] [InlineData("short-payer-version")]
    public async Task Actual_HTTP_execution_rejects_invalid_source_version_owner_account_reference_or_time_with_no_financial_mutation(string invalid)
    {
        var (c, id) = await ApprovedAsync(); var foreign = invalid.StartsWith("foreign-", StringComparison.Ordinal) ? await Web07Case.CreatePhysicalAsync(fixture) : null;
        var debit = await Web07RecordedDebit.BankAsync(c, 230_000m);
        var request = await c.ExecutionRequestAsync(id, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt);
        switch (invalid)
        {
            case "unknown-kind": request.SourceKind = (StorefrontRefundPayoutSourceKind)99; break;
            case "missing-source": request.SourceId = int.MaxValue; break;
            case "bad-version": request.SourceVersion = "not-base64"; break;
            case "stale-version": request.SourceVersion = Convert.ToBase64String(new byte[8]); break;
            case "missing-owner": request.PayerLegalEntityId = 0; break;
            case "foreign-owner": request.PayerLegalEntityId = foreign!.Seed.LegalEntityId; break;
            case "foreign-bank": request.PayerBankAccountId = foreign!.Seed.BankAccountId; break;
            case "missing-reference": request.ExternalTransactionId = ""; break;
            case "missing-time": request.ExecutedAt = null; break;
            case "before-receipt-time": request.ExecutedAt = new DateTimeOffset(DateTime.UtcNow.AddDays(-1)); break;
            case "future-time": request.ExecutedAt = new DateTimeOffset(DateTime.UtcNow.AddDays(1)); break;
            case "missing-payer-version": request.PayerBankAccountVersion = null; break;
            case "malformed-payer-version": request.PayerBankAccountVersion = "not-base64"; break;
            case "short-payer-version": request.PayerBankAccountVersion = Convert.ToBase64String(new byte[7]); break;
        }
        await using var host = await Web07AdminHost.StartAsync(c); using var client = host.Client(); await Web07AdminHost.CsrfAsync(client, c.OrderId);
        var before = await c.SnapshotAsync(); var flow = await Web07RefundExecutionTests.FlowAsync(c);
        using var response = await client.PostAsJsonAsync($"/admin/storefront/refunds/{c.OrderId}/confirm-execution", request);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict, HttpStatusCode.NotFound, HttpStatusCode.Forbidden });
        Assert.False(string.IsNullOrWhiteSpace(await response.Content.ReadAsStringAsync())); Assert.Equal(before, await c.SnapshotAsync());
        Assert.Equal(flow, await Web07RefundExecutionTests.FlowAsync(c));
    }

    [Theory]
    [InlineData("cash-in")] [InlineData("bank-in")] [InlineData("internal-transfer")] [InlineData("reversed")]
    [InlineData("protected-deposit-reason")] [InlineData("carrier-expense")]
    public async Task Actual_nonpayout_or_reversed_bookkeeping_sources_cannot_be_relabelled_as_a_customer_refund(string invalid)
    {
        var (c, id) = await ApprovedAsync(); Web07RecordedDebit debit;
        if (invalid == "protected-deposit-reason")
        {
            // Actual ordinary CashOut API with the existing protected legacy
            // label, not a claim that the deposit-refund workflow was exercised.
            debit = await Web07RecordedDebit.CashAsync(c, 230_000m, recordedReason: "Hoàn cọc khách hàng");
        }
        else if (invalid == "carrier-expense")
        {
            int expenseId; string expenseVersion;
            await using (var db = c.Context())
            {
                var expense = new OperatingExpense { StoreId = c.StoreId, ClientRequestId = Guid.NewGuid(), Name = "WEB07 separate actual carrier bill",
                    Category = "shipping", Amount = 25_000m, RecognitionFrom = DateTime.UtcNow.Date, RecognitionTo = DateTime.UtcNow.Date,
                    Status = "confirmed", ConfirmedAtUtc = DateTime.UtcNow, ConfirmedBy = c.Seed.UserId, IsPaid = false };
                db.Add(expense); await db.SaveChangesAsync(); expenseId = expense.Id; expenseVersion = Convert.ToBase64String(expense.RowVersion);
            }
            int sourceId; await using (var db = c.Context()) sourceId = await new TreasuryService(db).CreateAsync(new TreasuryWriteDto
            { ClientRequestId = Guid.NewGuid(), Fund = "bank", Amount = -25_000m, Date = DateTime.UtcNow.AddHours(7).Date,
                Name = "WEB07 pay carrier expense once", OperatingExpenseId = expenseId, SourceRowVersion = expenseVersion,
                Reference = "W07-CARRIER-" + Guid.NewGuid().ToString("N") }, default);
            await using var read = c.Context(); var source = await read.TreasuryEntries.SingleAsync(x => x.Id == sourceId);
            Assert.True((await read.OperatingExpenses.SingleAsync(x => x.Id == expenseId)).IsPaid);
            debit = new(new(StorefrontRefundPayoutSourceKind.TreasuryDebit, source.Id, "Actual carrier expense debit", "bank", 25_000m,
                Convert.ToBase64String(source.RowVersion), source.OccurredAtUtc), null, source.Reference!, DateTimeOffset.UtcNow);
        }
        else if (invalid == "cash-in")
        {
            var cash = await Web07RecordedDebit.CashAsync(c, 230_000m); await using var db = c.Context();
            var receipt = await db.POSShiftCashTransactions.SingleAsync(x => x.Type == POSShiftCashTransactionType.CashIn);
            debit = cash with { Source = cash.Source with { Id = receipt.Id, Version = Convert.ToBase64String(receipt.RowVersion) } };
        }
        else if (invalid == "reversed")
        {
            debit = await Web07RecordedDebit.BankAsync(c, 230_000m);
            await using (var db = c.Context()) await new TreasuryService(db).ReverseAsync(debit.Source.Id, new TreasuryReverseDto
            { ClientRequestId = Guid.NewGuid(), Date = DateTime.UtcNow.AddHours(7).Date, Reason = "WEB07 actual prior reversal, never a payout", RowVersion = debit.Source.Version }, default);
            await using var read = c.Context(); var source = await read.TreasuryEntries.SingleAsync(x => x.Id == debit.Source.Id);
            debit = debit with { Source = debit.Source with { Version = Convert.ToBase64String(source.RowVersion) } };
        }
        else
        {
            int sourceId; await using (var db = c.Context()) sourceId = await new TreasuryService(db).CreateAsync(new TreasuryWriteDto
            { ClientRequestId = Guid.NewGuid(), Fund = "bank", Amount = invalid == "bank-in" ? 230_000m : -230_000m, TargetFund = invalid == "internal-transfer" ? "cash" : null,
                Date = DateTime.UtcNow.AddHours(7).Date, Name = "WEB07 actual non-refund movement", Reference = "W07-NONPAYOUT-" + Guid.NewGuid().ToString("N") }, default);
            await using var read = c.Context(); var source = await read.TreasuryEntries.SingleAsync(x => x.Id == sourceId);
            debit = new(new(StorefrontRefundPayoutSourceKind.TreasuryDebit, source.Id, "Actual nonpayout", source.Fund, 230_000m, Convert.ToBase64String(source.RowVersion), source.OccurredAtUtc),
                null, source.Reference!, DateTimeOffset.UtcNow);
        }
        var request = await c.ExecutionRequestAsync(id, debit.Source, c.Seed.LegalEntityId, debit.Source.Fund == "bank" ? c.Seed.BankAccountId : null, debit.ExternalReference, debit.ActualExecutionAt);
        await using var host = await Web07AdminHost.StartAsync(c); using var client = host.Client(); await Web07AdminHost.CsrfAsync(client, c.OrderId);
        var before = await c.SnapshotAsync(); using var response = await client.PostAsJsonAsync($"/admin/storefront/refunds/{c.OrderId}/confirm-execution", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
        await using var check = c.Context(); Assert.Empty(await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == id).ToListAsync());
        Assert.Equal(230_000m, (await check.Set<StorefrontRefund>().SingleAsync()).Amount);
        Assert.Equal(230_000m, await check.Set<StorefrontRefundFunding>().Where(x => x.RefundId == id).SumAsync(x => x.Amount));
    }

    private async Task<(Web07Case Case, int Refund)> ApprovedAsync()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        int id; await using (var db = c.Context()) id = (await db.Set<StorefrontRefund>().SingleAsync()).Id;
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(id, "WEB07 original source independently verified"));
        return (c, id);
    }
}
