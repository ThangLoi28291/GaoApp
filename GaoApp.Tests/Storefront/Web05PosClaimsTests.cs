using System.Data.Common;
using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Banking;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05PosClaimsTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task API_POS_and_web_cannot_consume_one_physical_external_transfer_twice_in_either_order(bool webFirst)
    {
        var web = await Web05Prepared.CreateAsync(fixture); await using var pos = await Web05PosCase.CreateAsync(web.Case); var external = "W05-API-" + Guid.NewGuid().ToString("N");
        if (webFirst)
        {
            web.Bank.Pay(web.Reference, 45_000m, external); await web.Case.ProcessAsync(web.IntentId, web.Bank);
            await pos.RetrievePaid(external); await Assert.ThrowsAsync<InvalidOperationException>(() => pos.Payments.CompleteAsync(pos.QrId, default));
        }
        else
        {
            await pos.RetrievePaid(external); await pos.Payments.CompleteAsync(pos.QrId, default);
            web.Bank.Pay(web.Reference, 45_000m, external); await web.Case.ProcessAsync(web.IntentId, web.Bank);
        }
        await using var check = web.Case.Context(); var receipt = Assert.Single(await check.Set<BankVerifiedReceipt>().Where(x => x.ExternalTransactionId == external).ToListAsync()); Assert.True(receipt.NeedsReview); Assert.Equal(45_000m, receipt.Amount);
        Assert.Single(await check.Set<BankReceiptConsumption>().Where(x => x.BankVerifiedReceiptId == receipt.Id).ToListAsync());
        Assert.Equal(webFirst ? 1 : 0, await check.Set<StorefrontPaymentAllocation>().CountAsync()); Assert.Equal(webFirst ? 0 : 1, await check.OrderPayments.CountAsync());
        if (!webFirst) Assert.Equal(StorefrontPaymentState.Review, (await check.StorefrontOrders.SingleAsync()).PaymentState);
        Assert.Equal(2, await check.Set<BankPaymentReference>().CountAsync(x => x.OriginStoreId == web.Case.StoreId));
        Assert.NotEqual(pos.Reference, web.Reference); Assert.StartsWith("GA", pos.Reference); Assert.StartsWith("W", web.Reference);
    }

    [Theory]
    [InlineData(false, true, true)][InlineData(true, true, true)]
    [InlineData(false, false, true)][InlineData(true, false, true)]
    [InlineData(false, false, false)][InlineData(true, false, false)]
    public async Task Trusted_phone_and_API_unproven_identifier_mapping_fails_closed_in_both_orders_without_requiring_active_intent(bool webFirst, bool sameLiteral, bool active)
    {
        var c = await fixture.Web05Async(); var baseline = await c.QuoteAsync(); Assert.Equal(StorefrontPaymentMethod.Qr, baseline.PaymentMethod); Assert.Equal(45_000m, baseline.TotalDue);
        await using var phone = await Web05PosCase.CreateAsync(c, manual: true);
        var phoneExternal = "FT" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant(); var external = sameLiteral ? phoneExternal : "987" + Guid.NewGuid().ToString("N")[..20]; Web05Prepared? web = null; var enrollmentBlocked = false;
        if (webFirst)
        {
            web = await Web05Prepared.CreateAsync(c);
            web.Bank.Pay(web.Reference, 45_000m, external); await web.Case.ProcessAsync(web.IntentId, web.Bank);
            if (!active) { await using var db = c.Context(); await db.Set<StorefrontPaymentIntent>().ExecuteUpdateAsync(x => x.SetProperty(i => i.IsActive, false)); }
            try
            {
                var evidence = await phone.ReceivePhone(phoneExternal);
                await Assert.ThrowsAsync<InvalidOperationException>(() => evidence.Bridge.ConfirmAsync(evidence.Receipt, evidence.Wait, false, default));
            }
            catch (DbUpdateException error) when (error.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 51173 }) { enrollmentBlocked = true; }
        }
        else
        {
            var evidence = await phone.ReceivePhone(phoneExternal); await evidence.Bridge.ConfirmAsync(evidence.Receipt, evidence.Wait, false, default);
            try { web = await Web05Prepared.CreateAsync(c); }
            catch (GaoApp.Application.Common.Exceptions.ValidationAppException error)
            { Assert.StartsWith("Tài khoản này chưa có xác minh định danh dùng chung với kênh thông báo ngân hàng.", error.Message); }
            if (web is not null)
            {
                if (!active) { await using var db = c.Context(); await db.Set<StorefrontPaymentIntent>().ExecuteUpdateAsync(x => x.SetProperty(i => i.IsActive, false)); }
                web.Bank.Pay(web.Reference, 45_000m, external); await web.Case.ProcessAsync(web.IntentId, web.Bank);
            }
            else
            {
                // Default mapping gate may reject the new QR before any bank
                // request. A separately authenticated API observation still has
                // to survive as ambiguity, without claiming it is the phone ID.
                await using var observed = c.Context(); var bank = await observed.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == c.Seed.BankAccountId);
                await new GlobalBankReceiptLedger(observed, Web05Support.AdminProtocol(c.AdminKeys)).RecordVerifiedAsync(new(GlobalBankReceiptLedger.AccountSnapshot(bank), "UNMAPPED-API-REFERENCE", BankPaymentChannel.Unknown,
                    external, 45_000m, "COMPLETED", DateTime.UtcNow.ToString("O"), "Synthetic separately authenticated API evidence with unproven phone mapping", BankExternalIdentityKind.AcbRetrieve, false, true));
            }
        }
        await using var check = c.Context(); Assert.Equal(webFirst ? 0 : 1, await check.OrderPayments.CountAsync()); Assert.Equal(webFirst ? 1 : 0, await check.Set<StorefrontPaymentAllocation>().CountAsync());
        Assert.Equal(enrollmentBlocked ? 0 : 1, await check.Set<BankNotificationReceipt>().CountAsync());
        if (!webFirst && web is not null) Assert.Equal(StorefrontPaymentState.Review, (await check.StorefrontOrders.SingleAsync()).PaymentState);
        if (web is null) { Assert.Empty(await check.StorefrontOrders.ToListAsync()); Assert.Empty(await check.Set<StorefrontPaymentIntent>().ToListAsync()); Assert.Empty(await check.InventoryReservations.ToListAsync()); }
        var apiReceipts = await check.Set<BankVerifiedReceipt>().Where(x => x.ExternalTransactionId == external).ToListAsync(); Assert.InRange(apiReceipts.Count, 0, 1);
        var posMoney = await check.OrderPayments.SumAsync(x => (decimal?)x.Amount) ?? 0m;
        var webMoney = await check.Set<StorefrontPaymentAllocation>().SumAsync(x => (decimal?)x.Amount) ?? 0m; Assert.Equal(45_000m, posMoney + webMoney);
        await Web05CashFlowTests.SnapshotIsolation(c.Seed); var report = await Web05CashFlowTests.Flow(c.Seed, DateTime.UtcNow.AddHours(7).Date);
        Assert.Equal(45_000m, report.Summary.Single(x => x.Fund == "bank").Inflow);
        if (!enrollmentBlocked) { Assert.NotNull(report.BankReceiptReviews); Assert.Contains(report.BankReceiptReviews, x => x.Amount == 45_000m); }
        Assert.DoesNotContain(report.Items, x => x.Amount == 90_000m);
    }

    [Fact]
    public async Task Primitive_authenticated_second_protocol_observation_keeps_full_ambiguous_money_without_certifying_a_second_asset_or_consumption()
    {
        var p = await Web05Prepared.CreateAsync(fixture); p.Bank.Pay(p.Reference, 45_000m, "987" + Guid.NewGuid().ToString("N")); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        await using (var db = p.Case.Context())
        {
            var bank = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == p.Case.Seed.BankAccountId);
            await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(p.Case.AdminKeys)).RecordVerifiedAsync(new(GlobalBankReceiptLedger.AccountSnapshot(bank), "PHONE-UNMAPPED", BankPaymentChannel.Unknown,
                "PHONE:FT" + Guid.NewGuid().ToString("N"), 45_000m, "COMPLETED", DateTime.UtcNow.ToString("O"), "Synthetic authenticated second-protocol observation; not proof of identity mapping", BankExternalIdentityKind.BankNotification, false, true));
        }
        await using var check = p.Case.Context(); var evidence = await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync(); Assert.Equal(2, evidence.Count); Assert.All(evidence, x => Assert.Equal(45_000m, x.Amount));
        Assert.Single(await check.Set<BankReceiptConsumption>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync()); Assert.Single(await check.Set<StorefrontPaymentAllocation>().ToListAsync());
        await Web05CashFlowTests.SnapshotIsolation(p.Case.Seed); var report = await Web05CashFlowTests.Flow(p.Case.Seed, DateTime.UtcNow.AddHours(7).Date);
        Assert.Equal(45_000m, report.Summary.Single(x => x.Fund == "bank").Inflow); Assert.NotNull(report.BankReceiptReviews); Assert.Contains(report.BankReceiptReviews, x => x.Amount == 45_000m);
    }

    [Theory]
    [InlineData("amount")][InlineData("bank-time")][InlineData("status")]
    public async Task Conflicting_replay_of_same_identity_ambiguous_secondary_evidence_cannot_promote_it_into_actual_bank_inflow(string changedFact)
    {
        var p = await Web05Prepared.CreateAsync(fixture); var day = p.Case.Web.Clock.Utc.AddHours(7).Date; await Web05CashFlowTests.SnapshotIsolation(p.Case.Seed);
        await using (var db = p.Case.Context()) { db.Add(new TreasuryOpeningBalance { StoreId = p.Case.StoreId, Fund = "bank", AsOfDate = day, Amount = 1_000m, Note = "Synthetic verified baseline before ambiguous evidence" }); await db.SaveChangesAsync(); }
        p.Bank.Pay(p.Reference, 45_000m, "987" + Guid.NewGuid().ToString("N")); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        BankVerifiedInput secondary; int secondaryId;
        await using (var db = p.Case.Context())
        {
            var bank = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == p.Case.Seed.BankAccountId);
            secondary = new(GlobalBankReceiptLedger.AccountSnapshot(bank), "PHONE-UNMAPPED", BankPaymentChannel.Unknown, "PHONE:FT" + Guid.NewGuid().ToString("N"),
                45_000m, "COMPLETED", p.Case.Web.Clock.Utc.ToString("O"), "Synthetic authenticated ambiguous45k secondary; no mapping proof", BankExternalIdentityKind.BankNotification, false, true);
            secondaryId = (await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(p.Case.AdminKeys), p.Case.Web.Clock).RecordVerifiedAsync(secondary)).Id;
        }
        var before = await Web05CashFlowTests.Flow(p.Case.Seed, day); Assert.Equal(45_000m, before.Summary.Single(x => x.Fund == "bank").Inflow);
        Assert.NotNull(before.BankReceiptReviews); Assert.Contains(before.BankReceiptReviews, x => x.Amount == 45_000m);
        var replay = changedFact switch { "amount" => secondary with { Amount = 45_001m }, "bank-time" => secondary with { RawBankTime = p.Case.Web.Clock.Utc.AddSeconds(1).ToString("O") }, _ => secondary with { Status = "REVERSED" } };
        replay = replay with { RawEvidence = JsonSerializer.Serialize(new { replay.Amount, replay.RawBankTime, replay.Status, Original = "ambiguous second observation remains unproved" }) };
        for (var i = 0; i < 2; i++) { await using var db = p.Case.Context(); var result = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(p.Case.AdminKeys), p.Case.Web.Clock).RecordVerifiedAsync(replay); Assert.Equal(secondaryId, result.Id); Assert.True(result.Conflict); Assert.True(result.Duplicate); }
        var after = await Web05CashFlowTests.Flow(p.Case.Seed, day); var summary = after.Summary.Single(x => x.Fund == "bank");
        await File.WriteAllTextAsync(Web05Support.Artifact("repros/R19-" + Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(new
        {
            ChangedFact = changedFact, Before = before.Summary.Single(x => x.Fund == "bank"), BeforeReviews = before.BankReceiptReviews,
            After = summary, AfterReviews = after.BankReceiptReviews, secondaryId
        }));
        Assert.Equal(45_000m, summary.Inflow); Assert.Null(summary.Closing);
        Assert.NotNull(after.BankReceiptReviews); Assert.Contains(after.BankReceiptReviews, x => x.Amount == 45_000m);
        await using var check = p.Case.Context(); var receipt = await check.Set<BankVerifiedReceipt>().SingleAsync(x => x.Id == secondaryId); Assert.Equal(45_000m, receipt.Amount); Assert.True(receipt.NeedsReview);
        Assert.Equal(2, await check.Set<BankVerifiedReceipt>().CountAsync(x => x.OriginStoreId == p.Case.StoreId)); Assert.Single(await check.Set<BankReceiptConsumption>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync());
        var conflict = Assert.Single(await check.Set<BankReceiptConflict>().Where(x => x.BankVerifiedReceiptId == secondaryId).ToListAsync()); Assert.Equal(replay.RawEvidence, Web05Support.VerifiedRaw(Web05Support.AdminProtocol(p.Case.AdminKeys), p.Case.StoreId, conflict.ProtectedEvidence));
    }

    [Fact]
    public async Task Equal_numeric_POS_and_web_order_ids_use_distinct_registered_namespaces_and_distinct_authoritative_transfers_are_not_merged()
    {
        var isolated = new Web01Fixture(); await isolated.InitializeAsync();
        try
        {
            var web = await Web05Prepared.CreateAsync(isolated); await using var pos = await Web05PosCase.CreateAsync(web.Case); Assert.Equal(web.OrderId, pos.Order.Id);
            web.Bank.Pay(web.Reference, 45_000m, "WEB-AUTHORITATIVE-1"); await web.Case.ProcessAsync(web.IntentId, web.Bank);
            await pos.RetrievePaid("POS-AUTHORITATIVE-2"); await pos.Payments.CompleteAsync(pos.QrId, default);
            await using var db = web.Case.Context(); Assert.Single(await db.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Single(await db.OrderPayments.ToListAsync());
            Assert.Equal(2, await db.Set<BankVerifiedReceipt>().CountAsync()); Assert.Equal(2, await db.Set<BankReceiptConsumption>().CountAsync()); Assert.Equal(90_000m, await db.Set<BankVerifiedReceipt>().SumAsync(x => x.Amount));
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Fact]
    public async Task Complete_rechecks_global_review_after_receipt_lock_when_conflicting_verified_evidence_commits_after_initial_read()
    {
        var c = await fixture.Web05Async(); var read = new ObserveReceiptRead(); await using var pos = await Web05PosCase.CreateAsync(c, interceptor: read);
        var external = "W05-R8-" + Guid.NewGuid().ToString("N"); await pos.RetrievePaid(external); BankVerifiedInput changed;
        await using (var db = c.Context())
        {
            var receipt = await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.ExternalTransactionId == external);
            var bank = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == c.Seed.BankAccountId);
            changed = new(GlobalBankReceiptLedger.AccountSnapshot(bank), pos.Reference, BankPaymentChannel.Pos, external, 45_001m, "COMPLETED", receipt.RawBankTime, "Synthetic conflicting trusted evidence");
        }
        var block = new HoldConflictingSave(); await using var conflicting = c.Seed.Database.CreateTenantContext(c.StoreId, block);
        var writing = new GlobalBankReceiptLedger(conflicting, Web05Support.AdminProtocol(c.AdminKeys)).RecordVerifiedAsync(changed);
        await block.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15)); read.Enabled = true;
        var completing = pos.Payments.CompleteAsync(pos.QrId, default);
        try
        {
            await read.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15)); await Task.Delay(200);
            Assert.False(completing.IsCompleted, "Complete must wait for the competing physical receipt lock after reading initial receipt facts.");
        }
        finally { block.Release.TrySetResult(); }
        await writing.WaitAsync(TimeSpan.FromSeconds(20)); await Assert.ThrowsAsync<InvalidOperationException>(async () => await completing.WaitAsync(TimeSpan.FromSeconds(20)));
        await using var check = c.Context(); Assert.True((await check.Set<BankVerifiedReceipt>().SingleAsync(x => x.ExternalTransactionId == external)).NeedsReview);
        Assert.Empty(await check.OrderPayments.ToListAsync()); Assert.Empty(await check.Set<BankReceiptConsumption>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync());
        Assert.Equal(OrderStatus.Draft, (await check.Orders.SingleAsync()).Status); Assert.Equal(0, (await check.Orders.SingleAsync()).PaidTotal); Assert.Empty(await check.InventoryTransactions.ToListAsync());
    }

    [Fact]
    public async Task Existing_POS_partial_payment_and_pure_finalize_guard_keep_their_shift_fingerprint_and_no_network_in_guard_behavior()
    {
        var c = await fixture.Web05Async(); await using var pos = await Web05PosCase.CreateAsync(c); var external = "W05-POS-" + Guid.NewGuid().ToString("N");
        await pos.RetrievePaid(external); await pos.Payments.CompleteAsync(pos.QrId, default); await pos.Payments.CompleteAsync(pos.QrId, default);
        var requests = pos.Bank.Requests.Count; var order = await pos.Db.Orders.Include(x => x.Payments).SingleAsync(); Assert.Equal(45_000m, order.PaidTotal); Assert.Equal(55_000m, order.BalanceDue); Assert.Equal(OrderStatus.Draft, order.Status);
        await using (var tx = await pos.Db.Database.BeginTransactionAsync()) { await new AcbFinalizeGuard(pos.Db).ValidateAsync(order, default); await tx.RollbackAsync(); }
        Assert.Equal(requests, pos.Bank.Requests.Count); Assert.Single(await pos.Db.OrderPayments.ToListAsync()); Assert.Single(await pos.Db.Set<BankReceiptConsumption>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync());
        pos.Runtime.TerminalId = int.MaxValue; await Assert.ThrowsAsync<InvalidOperationException>(() => pos.Payments.CompleteAsync(pos.QrId, default));
        Assert.Single(await pos.Db.OrderPayments.ToListAsync()); Assert.Empty(await pos.Db.InventoryTransactions.ToListAsync());
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Existing_trusted_phone_POS_confirmation_keeps_legacy_acceptance_with_or_without_bank_timestamp(bool timestamp)
    {
        var c = await fixture.Web05Async(); await using var phone = await Web05PosCase.CreateAsync(c, manual: true); var external = "FT" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        var received = await phone.ReceivePhone(external, timestamp); await received.Bridge.ConfirmAsync(received.Receipt, received.Wait, false, default);
        await using var check = c.Context(); var payment = Assert.Single(await check.OrderPayments.ToListAsync()); Assert.Equal(45_000m, payment.Amount);
        Assert.Equal(OrderStatus.Draft, (await check.Orders.SingleAsync()).Status); Assert.Equal(55_000m, (await check.Orders.SingleAsync()).BalanceDue);
        var raw = await check.Set<BankNotificationReceipt>().SingleAsync(); Assert.Equal(payment.Id, raw.PaymentId); Assert.Equal(received.Wait, raw.AppliedWaitId);
        Assert.Empty(await check.InventoryTransactions.ToListAsync()); Assert.Empty(await check.Set<StorefrontPaymentAllocation>().ToListAsync());
    }

    private sealed class HoldConflictingSave : SaveChangesInterceptor
    {
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<BankReceiptConflict>().Any(x => x.State == EntityState.Added))
            { Reached.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); }
            return result;
        }
    }
    private sealed class ObserveReceiptRead : DbCommandInterceptor
    {
        internal bool Enabled;
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        { if (Enabled && command.CommandText.Contains("[BankVerifiedReceipts]", StringComparison.Ordinal)) Reached.TrySetResult(); return ValueTask.FromResult(result); }
    }
}
