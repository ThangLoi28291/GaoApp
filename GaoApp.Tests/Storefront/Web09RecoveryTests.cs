using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Tests.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GaoApp.Application.Common;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Services.Acb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb09"), Trait("Category", "StorefrontWeb09")]
public sealed class Web09RecoveryTests
{
    [Fact]
    public async Task Actual_bank_asset_partial_refund_backup_restore_preserves_external_uniqueness_and_worker_redelivery_remaining_budget()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WEB09 protected bank recovery currently targets Windows only.");
        var source = new Web01Fixture(); Web09RecoverySupport.Plan(source.Database, "WEB09 isolated historical bank obligation recovery, no live bank or new operator intake claim");
        await using var target = new InventoryPostingLocalDb(); Web09RecoverySupport.Plan(target, "WEB09 distinct bank recovery target");
        var output = Web09Support.Runtime("WEB09 actual controlled bank asset/payout liability backup restore and redelivery; historical fixture is not new activation proof");
        try
        {
            await source.InitializeAsync(); var bank = await source.Web05Async();
            Assert.Empty(Directory.EnumerateFiles(bank.Web.KeysDirectory, "*.xml"));
            var initialProtection = DataProtectionProvider.Create(new DirectoryInfo(bank.Web.KeysDirectory), builder =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("New owned DPAPI ring requires Windows.");
                builder.SetApplicationName("GaoApp.Storefront").ProtectKeysWithDpapi();
            });
            _ = initialProtection.CreateProtector("WEB09 new historical-fixture ring, no prior order").Protect("synthetic non-secret bootstrap input");
            var pending = await Web05Prepared.CreateAsync(bank);
            var control = new Web09Case(bank.Seed, bank.Web); await control.GrantAsync(StorefrontPermissions.ViewSettings, StorefrontPermissions.ManageSettings);
            await control.StopAsync(false); // Actual narrow operator stop; the old builder is not a release/activation positive.
            var originalExternal = "WEB09-ORIGINAL-" + Guid.NewGuid().ToString("N"); bank.Web.Clock.Utc = DateTime.UtcNow;
            pending.Bank.Pay(pending.Reference, 45000m, originalExternal); await bank.ProcessAsync(pending.IntentId, pending.Bank);
            var c = new Web07Case(bank.Seed, bank.Web, pending.Placed, pending.OrderId, output, null, bank);
            await using (var db = c.Context()) await Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(await c.CancelRequestAsync("WEB09 actual funded historical cancellation before backup"));
            StorefrontRefundRead refund; await using (var db = c.Context()) refund = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
            Assert.Equal(45000m, refund.Amount); Assert.Equal(0m, refund.ExecutedAmount);
            await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund.Id, "WEB09 original actual asset verified before recovery"));
            var debit = await Web07RecordedDebit.BankAsync(c, 10000m, "WEB09-RECORDED-10K-" + Guid.NewGuid().ToString("N"));
            var execution = await c.ExecutionRequestAsync(refund.Id, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt);
            await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, execution);
            await Web05CashFlowTests.SnapshotIsolation(c.Seed); await AssertBankBudget(c, 1, 45000m, 35000m);
            StorefrontPlaceOrderRequest placement;
            await using (var db = c.Context())
            {
                var order = await db.StorefrontOrders.SingleAsync(); var quote = await db.StorefrontQuotes.SingleAsync(x => x.Id == order.QuoteId);
                placement = new() { ClientRequestId = await db.Set<StorefrontCommandReceipt>().Where(x => x.Operation == "place-cod-v1").Select(x => x.ClientRequestId).SingleAsync(),
                    QuoteToken = quote.Token, Fingerprint = quote.Fingerprint };
            }
            // Source public requests settle and close before the consistent backup.
            await using (var host = await Web09PublicProcess.StartAsync(control))
            {
                var csrf = await host.CsrfAsync(); using var replay = await Web09ReleaseHttpTests.Post(host, "/shop/checkout/place", placement, csrf);
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode); var actual = await replay.Content.ReadFromJsonAsync<StorefrontPlacementResult>();
                Assert.Equal(pending.Placed, actual?.Order); Assert.Equal(StorefrontPlacementStatus.Placed, actual!.Status); Assert.Null(actual.ReplacementQuote);
            }
            string physical, financial;
            await using (var db = c.Context()) { physical = await Web09RecoverySupport.DigestAsync(db); financial = await Web09RecoverySupport.BankDigestAsync(db); }
            await target.MigrateAsync(); await Web09RecoverySupport.BackupRestoreAsync(source.Database, target, output);
            // Genuine external post-backup credit exists at the controlled bank,
            // and source processing proves it is absent from the backup point.
            var lateExternal = "WEB09-LATE-1K-" + Guid.NewGuid().ToString("N");
            pending.Bank.Pay(pending.Reference, 1000m, lateExternal); await bank.ProcessAsync(pending.IntentId, pending.Bank);
            await AssertBankBudget(c, 2, 46000m, 36000m);
            var seed = c.Seed with { Database = target }; var checkout = c.Checkout with { Seed = seed }; var restoredBank = bank with { Web = checkout };
            var restored = c with { Seed = seed, Checkout = checkout, Banking = restoredBank };
            await using (var db = restored.Context())
            { Assert.Equal(physical, await Web09RecoverySupport.DigestAsync(db)); Assert.Equal(financial, await Web09RecoverySupport.BankDigestAsync(db)); }
            await AssertBankBudget(restored, 1, 45000m, 35000m);
            await using (var db = restored.Context()) Assert.True((await Web07Support.Refunds(restored, db).ConfirmExecutionAsync(restored.OrderId, execution)).Replayed);
            await using (var db = restored.Context()) Assert.Equal(financial, await Web09RecoverySupport.BankDigestAsync(db));
            var restoredControl = new Web09Case(seed, checkout);
            await using (var host = await Web09PublicProcess.StartAsync(restoredControl))
            {
                var csrf = await host.CsrfAsync(); using var replay = await Web09ReleaseHttpTests.Post(host, "/shop/checkout/place", placement, csrf);
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode); var actual = await replay.Content.ReadFromJsonAsync<StorefrontPlacementResult>();
                Assert.Equal(pending.Placed, actual?.Order); Assert.Equal("Đã nhận lại đúng đơn được tạo trước đó.", actual!.Message);
            }
            await RunRestoredBankWorker(restored, pending.IntentId, pending.Bank);
            // Repeated real authenticated retrieval must not multiply assets,
            // old execution, funding budgets, source cashflow or customer order.
            await restoredBank.ProcessAsync(pending.IntentId, pending.Bank); await AssertBankBudget(restored, 2, 46000m, 36000m);
            await using (var db = restored.Context())
            {
                var actual = await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == restored.StoreId).OrderBy(x => x.ExternalTransactionId).Select(x => x.ExternalTransactionId).ToListAsync();
                Assert.Equal(new[] { originalExternal, lateExternal }.Order().ToArray(), actual);
                Assert.Equal(1, await db.Set<BankReceiptConsumption>().Where(x => x.OriginStoreId == restored.StoreId).CountAsync());
                Assert.Equal(StorefrontRefundState.Requested, (await db.StorefrontOrders.SingleAsync()).RefundState);
            }
            await File.WriteAllTextAsync(Path.Combine(output, "actual-bank-recovery-result.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow,
                Owner = Web09Support.Owner(), HistoricalBuilderNotActivationProof = true, ActualBackupRestore = true,
                OriginalAsset = 45000m, OriginalExecutedPriorDebit = 10000m, RestoredInitialRemaining = 35000m,
                PostBackupLateAsset = 1000m, FinalAssets = 46000m, FinalRemaining = 36000m, OriginalAndLateExternalIdentity = new[] { originalExternal, lateExternal },
                ActualOwnedWorkerRedelivery = true, ZeroLossClaim = false, NewPayoutTransferSent = false, NoPrivateRingCopy = true, RetainBackupsAndKeys = true }));
        }
        finally { await source.DisposeAsync(); }
    }

    private static async Task AssertBankBudget(Web07Case c, int assets, decimal total, decimal remaining)
    {
        await using var db = c.Context(); var order = await db.StorefrontOrders.SingleAsync(); Assert.Equal(StorefrontOrderPhase.Cancelled, order.Phase);
        Assert.Equal(45000m, order.TotalDue); Assert.False((await db.StorefrontSettings.SingleAsync()).IsEnabled);
        Assert.Equal(assets, await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).CountAsync());
        Assert.Equal(total, await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).SumAsync(x => x.Amount));
        Assert.Equal(total, await db.Set<StorefrontRefundFunding>().SumAsync(x => x.Amount));
        Assert.Equal(10000m, await db.Set<StorefrontRefundEvidence>().SumAsync(x => x.Amount)); Assert.Single(await db.Set<StorefrontRefundEvidence>().ToListAsync());
        var reads = await Web07Support.Refunds(c, db).GetAsync(c.OrderId); Assert.Equal(remaining, reads.Sum(x => x.Amount - x.ExecutedAmount));
        Assert.Equal(1, await db.TreasuryEntries.CountAsync()); Assert.Equal(-10000m, await db.TreasuryEntries.SumAsync(x => x.Amount));
        Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync()); Assert.Empty(await db.InventoryTransactions.ToListAsync());
        var flow = await Web05CashFlowTests.Flow(c.Seed, DateTime.UtcNow.AddHours(7).Date); var bankFlow = flow.Summary.Single(x => x.Fund == "bank");
        Assert.Equal(total, bankFlow.Inflow); Assert.Equal(10000m, bankFlow.Outflow);
    }

    private static async Task RunRestoredBankWorker(Web07Case c, int intent, HttpMessageHandler transport)
    {
        await using (var db = c.Context())
        {
            var due = DateTime.SpecifyKind((await db.Set<StorefrontQrProcessing>().SingleAsync(x => x.PaymentIntentId == intent)).NextAttemptAtUtc, DateTimeKind.Utc).AddSeconds(1);
            if (due > c.Checkout.Clock.Utc) c.Checkout.Clock.Utc = due;
        }
        var services = new ServiceCollection(); services.AddLogging(x => x.ClearProviders()); services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(x => x.GetRequiredService<TenantContext>()); services.AddScoped<ITenantContextWriter>(x => x.GetRequiredService<TenantContext>());
        services.AddScoped<AppDbContext>(x => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(c.Seed.Database.ConnectionString).Options,
            x.GetRequiredService<ITenantContext>(), new Web01Actor(null, false)));
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        services.AddScoped<IStorefrontBankProcessor>(x => new ObservedActualBankProcessor(new StorefrontBankProcessor(x.GetRequiredService<AppDbContext>(), Web05Support.AdminProtocol(c.Banking!.AdminKeys, transport), c.Clock), completed));
        await using var provider = services.BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["StorefrontBanking:WorkerEnabled"] = "true", ["StorefrontHost:ReleaseEnabled"] = "false" }).Build();
        using var worker = new StorefrontBankWorker(provider.GetRequiredService<IServiceScopeFactory>(), configuration, NullLogger<StorefrontBankWorker>.Instance, c.Clock);
        await worker.StartAsync(default);
        try
        {
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(25));
            await using var db = c.Context(); Assert.Equal(46000m, await db.Set<StorefrontRefundFunding>().SumAsync(x => x.Amount));
            await File.WriteAllTextAsync(Path.Combine(c.Directory, "restored-bank-worker-completed.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow,
                Owner = Web09Support.Owner(), ActualProcessorReturnedBeforeWitness = true, FundingAmount = 46000m, NoBusinessResultSubstituted = true }));
        }
        finally { await worker.StopAsync(default); }
    }

    private sealed class ObservedActualBankProcessor(IStorefrontBankProcessor actual, TaskCompletionSource completed) : IStorefrontBankProcessor
    {
        public async Task ProcessAsync(int intent, CancellationToken ct = default)
        { await actual.ProcessAsync(intent, ct); completed.TrySetResult(); }
        public Task ProcessCallbackAsync(JsonElement authenticatedPayload, CancellationToken ct = default)
            => actual.ProcessCallbackAsync(authenticatedPayload, ct);
    }

    [Fact]
    public async Task Actual_copy_only_backup_restore_replays_old_protected_order_with_same_ring_and_explicit_post_backup_redelivery()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WEB09 protected recovery rehearsal currently targets Windows only.");
        var source = new Web01Fixture(); Web09RecoverySupport.Plan(source.Database, "Fresh WEB09 recovery source, never existing dataset");
        await using var target = new InventoryPostingLocalDb(); Web09RecoverySupport.Plan(target, "Fresh separate WEB09 restore target, no source overwrite");
        try
        {
            await source.InitializeAsync(); var c = await Web09Case.CreateAsync(source); await c.ActivateAsync();
            StorefrontPlaceOrderRequest command; StorefrontPlacementResult original;
            await using (var host = await Web09PublicProcess.StartAsync(c))
            {
                var csrf = await host.CsrfAsync("/shop/checkout");
                using var quoted = await Web09ReleaseHttpTests.Post(host, "/shop/checkout/quote", c.Checkout.Request(), csrf);
                Assert.Equal(HttpStatusCode.OK, quoted.StatusCode); var quote = await quoted.Content.ReadFromJsonAsync<StorefrontQuoteDto>(); Assert.NotNull(quote);
                command = Web04Case.PlaceRequest(quote);
                using var placed = await Web09ReleaseHttpTests.Post(host, "/shop/checkout/place", command, csrf); Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
                original = (await placed.Content.ReadFromJsonAsync<StorefrontPlacementResult>())!; Assert.NotNull(original.Order);
                await c.StopAsync();
            }
            // All requests/processes are closed before the backup point. Rotate
            // normally, so successful restored replay must load an older key.
            using (var keys = new ServiceCollection().AddDataProtection().SetApplicationName("GaoApp.Storefront")
                .PersistKeysToFileSystem(new DirectoryInfo(c.Checkout.KeysDirectory)).ProtectKeysWithDpapi().Services.BuildServiceProvider())
            {
                var manager = keys.GetRequiredService<IKeyManager>(); var oldKey = Assert.Single(manager.GetAllKeys());
                var next = manager.CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90)); Assert.NotEqual(oldKey.KeyId, next.KeyId);
                Assert.Equal(2, manager.GetAllKeys().Count);
                await File.WriteAllTextAsync(Path.Combine(c.Directory, "normal-rotation-metadata.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow,
                    OriginalKeyId = oldKey.KeyId, AdditionalKeyId = next.KeyId, FrameworkOnly = true, PrivateXmlReadCopiedOrExported = false }));
            }
            string before;
            await using (var db = c.Context()) before = await Web09RecoverySupport.DigestAsync(db);
            await target.MigrateAsync(); await Web09RecoverySupport.BackupRestoreAsync(source.Database, target, c.Directory);
            var targetSeed = c.Seed with { Database = target }; var restored = new Web09Case(targetSeed, c.Checkout with { Seed = targetSeed });
            await using (var db = restored.Context()) Assert.Equal(before, await Web09RecoverySupport.DigestAsync(db));
            // Real post-backup command is deliberately absent from the backup.
            // Do not label the restore zero-loss; explicitly redeliver it later.
            StorefrontGuestCancellationRequest cancellation;
            await using (var db = c.Context())
            {
                var order = await db.StorefrontOrders.AsNoTracking().SingleAsync();
                cancellation = new() { Token = Web04Support.GuestToken(original.Order!), Reason = "WEB09 actual post-backup customer command",
                    ClientRequestId = Guid.NewGuid(), ExpectedRevision = order.Revision, ExpectedVersion = Convert.ToBase64String(order.RowVersion) };
                await new StorefrontCancellationService(db, new Web01Actor(null, false), new InventoryBalanceRepository(db), c.Checkout.Clock).CancelGuestAsync(cancellation);
            }
            await using (var db = restored.Context())
            {
                Assert.Equal(before, await Web09RecoverySupport.DigestAsync(db)); Assert.NotEqual(StorefrontOrderPhase.Cancelled, (await db.StorefrontOrders.SingleAsync()).Phase);
                await Assert.ThrowsAsync<ConflictAppException>(() => restored.Checkout.Service(db, new EphemeralDataProtectionProvider()).PlaceAsync(command));
            }
            await using (var host = await Web09PublicProcess.StartAsync(restored))
            {
                var csrf = await host.CsrfAsync();
                using var replay = await Web09ReleaseHttpTests.Post(host, "/shop/checkout/place", command, csrf); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                var replayed = await replay.Content.ReadFromJsonAsync<StorefrontPlacementResult>(); Assert.NotNull(replayed);
                Assert.Equal(StorefrontPlacementStatus.Placed, replayed.Status); Assert.Equal(original.Order, replayed.Order); Assert.Null(replayed.ReplacementQuote);
                Assert.Equal("Đã nhận lại đúng đơn được tạo trước đó.", replayed.Message);
                Assert.Equal("Cửa hàng đã nhận đơn và đang chờ chuẩn bị. Thanh toán COD khi nhận hàng.", original.Message);
                await using (var db = restored.Context()) Assert.Equal(before, await Web09RecoverySupport.DigestAsync(db));
                using var changed = await Web09ReleaseHttpTests.Post(host, "/shop/checkout/place", new StorefrontPlaceOrderRequest { ClientRequestId = command.ClientRequestId,
                    QuoteToken = command.QuoteToken, Fingerprint = new string('0', 64) }, csrf); Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
                using var redelivery = await Web09ReleaseHttpTests.Post(host, "/shop/orders/cancellation", cancellation, csrf); Assert.Equal(HttpStatusCode.OK, redelivery.StatusCode);
                var afterFirst = await restored.Checkout.CountsAsync();
                using var duplicate = await Web09ReleaseHttpTests.Post(host, "/shop/orders/cancellation", cancellation, csrf); Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
                Assert.Equal(afterFirst, await restored.Checkout.CountsAsync());
            }
            await using (var db = restored.Context())
            {
                Assert.Equal(StorefrontOrderPhase.Cancelled, (await db.StorefrontOrders.SingleAsync()).Phase);
                Assert.Equal(1, await db.StorefrontOrders.CountAsync()); Assert.Equal(1, await db.Set<StorefrontCommandReceipt>().CountAsync(x => x.ClientRequestId == command.ClientRequestId));
                Assert.Equal(1, await db.Set<StorefrontCommandReceipt>().CountAsync(x => x.ClientRequestId == cancellation.ClientRequestId));
                Assert.Empty(await db.OrderPayments.ToListAsync()); Assert.Empty(await db.InventoryTransactions.ToListAsync()); Assert.False((await db.StorefrontSettings.SingleAsync()).IsEnabled);
            }
            await File.WriteAllTextAsync(Path.Combine(c.Directory, "recovery-rehearsal-result.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow,
                SameRetainedRingPath = c.Checkout.KeysDirectory, ActualBackupRestore = true, OldProtectedReceiptReplayed = true,
                PostBackupCommandWasAbsentUntilActualRedelivery = true, ZeroLossClaim = false, ProductionRpoRtoClaim = false, PrivateKeysCopied = false,
                RestoredSnapshotDigest = before, RetainBackupAndNewKeys = true }));
        }
        finally { await source.DisposeAsync(); }
    }
}
