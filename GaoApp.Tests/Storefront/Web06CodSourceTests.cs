using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Banking;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06CodSourceTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Server_mapping_configuration_changed_after_original_native_evidence_cannot_commit_a_second_bank_asset_before_replay_rejection(bool interruptedBeforeCertification)
    {
        var c = await Web06Case.CreateAsync(fixture, quantity: .9m); await Web05CashFlowTests.SnapshotIsolation(c.Seed); var native = await NativeAsync(c);
        await using var host = await Web06NotificationHost.StartAsync(c); var notification = await Accepted(host, native, Upload(45_000m));
        var originalScope = await Web06BankInspection.CaptureAsync(c); var fault = new CertificationWriteFailure();
        await using (var first = c.Context(interruptedBeforeCertification ? fault : null))
        {
            if (interruptedBeforeCertification)
            {
                await Assert.ThrowsAsync<CertificationFixtureFault>(() => Importer(c, first).ImportAuthenticatedNotificationAsync(notification)); Assert.Equal(1, fault.Hits);
            }
            else await Importer(c, first).ImportAuthenticatedNotificationAsync(notification);
        }
        int globalId; string originalGlobal; string? originalCertification;
        await using (var db = c.Context())
        {
            var original = Assert.Single(await originalScope.Receipts(db).AsNoTracking().ToListAsync()); globalId = original.Id;
            Assert.False(original.CrossProtocolMappingVerified); Assert.StartsWith("PHONE:", original.ExternalTransactionId);
            Assert.Equal(45_000m, original.Amount); Assert.Equal(BankReceiptAssetConfidence.CertifiedDistinct, original.AssetConfidence); Assert.Equal(BankReceiptRoutingProvenance.UnmatchedOnly, original.RoutingProvenance);
            originalGlobal = JsonSerializer.Serialize(original);
            var certificates = await db.Set<StorefrontNotificationCertification>().AsNoTracking().ToListAsync();
            if (interruptedBeforeCertification) { Assert.Empty(certificates); originalCertification = null; }
            else originalCertification = JsonSerializer.Serialize(Assert.Single(certificates));
            await originalScope.AssertDeltaAsync(db, receipts: 1, amount: 45_000m);
        }
        var globalBudget = await Web06BankInspection.CaptureAsync(c); var day = DateTime.UtcNow.AddHours(7).Date;
        var beforeFlow = await Web05CashFlowTests.Flow(c.Seed, day); Assert.Equal(45_000m, beforeFlow.Summary.Single(x => x.Fund == "bank").Inflow);
        await using (var db = c.Context())
        {
            var account = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == c.Seed.BankAccountId);
            // Controlled trusted SERVER configuration only. It is not a client
            // verification flag or evidence that an actual bank mapped protocols.
            var changed = new GaoApp.Application.Storefront.StorefrontBankRegistration(true, "SYNTHETIC server configuration change, no live bank mapping",
                "SYNTHETIC", "SYNTHETIC", "SYNTHETIC", account.Id, c.Seed.LegalEntityId,
                GlobalBankReceiptLedger.AccountIdentity(account.BankCode, account.AccountNumber), true, "Synthetic operator fixture");
            var service = new StorefrontCodBankSourceService(db, new Web01Actor(c.Seed.UserId), new Web01Permissions(c.StoreId, c.Seed.UserId, [StorefrontPermissions.Reconcile]),
                Web05Support.AdminProtocol(Path.Combine(c.Checkout.Directory, "admin-keys")), TimeProvider.System, new Web05Registration(c.StoreId, changed));
            await Assert.ThrowsAsync<ConflictAppException>(() => service.ImportAuthenticatedNotificationAsync(notification));
            Assert.Equal(originalGlobal, JsonSerializer.Serialize(await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.Id == globalId)));
            var certificates = await db.Set<StorefrontNotificationCertification>().AsNoTracking().ToListAsync();
            if (interruptedBeforeCertification) Assert.Empty(certificates); else Assert.Equal(originalCertification, JsonSerializer.Serialize(Assert.Single(certificates)));
            await globalBudget.AssertDeltaAsync(db); Assert.Single(await globalBudget.Receipts(db).ToListAsync());
            Assert.Empty(await globalBudget.Consumptions(db).ToListAsync()); Assert.Empty(await db.Set<StorefrontCodBankRouting>().ToListAsync());
            Assert.Empty(await db.Set<StorefrontCodReceipt>().ToListAsync()); Assert.Empty(await db.Set<StorefrontServiceFunding>().ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
        }
        var afterFlow = await Web05CashFlowTests.Flow(c.Seed, day); Assert.Equal(beforeFlow.Summary.Single(x => x.Fund == "bank").Inflow, afterFlow.Summary.Single(x => x.Fund == "bank").Inflow);
        await using (var recovered = c.Context())
        {
            Assert.Equal(globalId, await Importer(c, recovered).ImportAuthenticatedNotificationAsync(notification));
            Assert.Equal(globalId, (await recovered.Set<StorefrontNotificationCertification>().SingleAsync()).GlobalBankReceiptId);
            Assert.Equal(originalGlobal, JsonSerializer.Serialize(await recovered.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.Id == globalId)));
            await globalBudget.AssertDeltaAsync(recovered); Assert.Empty(await globalBudget.Consumptions(recovered).ToListAsync());
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_authenticated_notification_205k_and_evidenced_25k_withholding_settle_230k_without_second_fund_movement(bool evidence)
    {
        var c = await Web06Case.CreateAsync(fixture, shipping: 30_000m, national: true, multipleSources: true);
        await Web05CashFlowTests.SnapshotIsolation(c.Seed);
        var feeOwner = await Web06FinanceSupport.ServiceSettingAsync(c, 0, separateOwner: true); await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
        var native = await NativeAsync(c); var bankInspection = await Web06BankInspection.CaptureAsync(c); await using var host = await Web06NotificationHost.StartAsync(c);
        var upload = Upload(205_000m); var receiptId = await Accepted(host, native, upload); int globalId;
        await using (var db = c.Context()) globalId = await Importer(c, db).ImportAuthenticatedNotificationAsync(receiptId);
        var remit = await c.RequestAsync<StorefrontCodBankReceiptRequest>(); remit.GlobalBankReceiptId = globalId; remit.ReceivingLegalEntityId = c.Seed.LegalEntityId; remit.EvidenceReference = "Synthetic national carrier paid net 205000";
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db).ReceiveBankRemittanceAsync(c.OrderId, remit);
        await using (var db = c.Context())
        {
            var receipt = await bankInspection.Receipts(db).SingleAsync(); Assert.Equal(205_000m, receipt.Amount); Assert.Equal(BankReceiptAssetConfidence.CertifiedDistinct, receipt.AssetConfidence);
            Assert.Equal(BankReceiptRoutingProvenance.UnmatchedOnly, receipt.RoutingProvenance); Assert.Equal(BankPaymentChannel.Unknown, receipt.Channel); Assert.True(receipt.NeedsReview);
            Assert.Equal("UNMATCHED_CHANNEL_BINDING", receipt.ReviewCode); Assert.NotNull(receipt.NativeCollectionAuthorityProofJson); Assert.NotNull(receipt.OccurredAtUtc);
            Assert.Single(await db.Set<StorefrontNotificationAttestation>().ToListAsync()); Assert.Single(await db.Set<StorefrontNotificationCertification>().ToListAsync());
            Assert.Single(await db.Set<StorefrontCodBankRouting>().ToListAsync()); Assert.Empty(await bankInspection.Consumptions(db).ToListAsync()); await bankInspection.AssertDeltaAsync(db, receipts: 1, amount: 205_000m);
            var summary = await Web06FinanceSupport.Settlement(c, db).GetAsync(c.OrderId); Assert.Equal(230_000m, summary.CustomerPaid); Assert.Equal(205_000m, summary.ActualStoreReceived); Assert.Equal(25_000m, summary.CollectorOutstanding); Assert.False(summary.CanPostSale);
        }
        if (!evidence) return;
        int expense;
        await using (var db = c.Context())
        {
            var item = new OperatingExpense { StoreId = c.StoreId, ClientRequestId = Guid.NewGuid(), Name = "WEB06 evidenced actual carrier deduction", Category = "shipping", Amount = 25_000m,
                RecognitionFrom = DateTime.UtcNow.Date, RecognitionTo = DateTime.UtcNow.Date, Status = "confirmed", ConfirmedAtUtc = DateTime.UtcNow, ConfirmedBy = c.Seed.UserId, IsPaid = false };
            db.Add(item); await db.SaveChangesAsync(); expense = item.Id;
        }
        var withheld = await c.RequestAsync<StorefrontCarrierWithholdingRequest>(); withheld.OperatingExpenseId = expense; withheld.LegalEntityId = feeOwner;
        withheld.CarrierEvidenceReference = "WEB06 real persisted unpaid carrier bill 25000";
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db).RecordCarrierWithholdingAsync(c.OrderId, withheld);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c);
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, await c.RequestAsync<StorefrontOperationRequest>());
        await using var check = c.Context(); Assert.Equal(205_000m, await bankInspection.Receipts(check).SumAsync(x => x.Amount)); Assert.Single(await bankInspection.Receipts(check).ToListAsync()); await bankInspection.AssertDeltaAsync(check, receipts: 1, amount: 205_000m);
        Assert.Equal(200_000m, (await check.Orders.SingleAsync()).GrandTotal); Assert.Equal(30_000m, (await check.Set<StorefrontServiceRevenue>().SingleAsync()).GrossAmount);
        Assert.Equal(230_000m, await check.OrderPayments.SumAsync(x => x.Amount) + await check.Set<StorefrontServiceFunding>().SumAsync(x => x.Amount));
        Assert.Equal(25_000m, (await check.Set<StorefrontCarrierWithholding>().SingleAsync()).Amount); Assert.True((await check.OperatingExpenses.SingleAsync()).IsPaid);
        Assert.Empty(await check.TreasuryEntries.ToListAsync()); Assert.Empty(await check.POSShiftCashTransactions.ToListAsync());
        var shift = await check.POSShifts.SingleAsync(); Assert.Equal(0m, shift.CashInTotal); Assert.Equal(0m, shift.CashSalesTotal); Assert.Equal(0m, shift.NonCashSalesTotal); Assert.Equal(0m, shift.CashOutTotal);
        Assert.Equal(2, await check.InventoryTransactions.CountAsync(x => x.ReferenceType == InventoryReferenceType.Delivery));
        var flow = await Web05CashFlowTests.Flow(c.Seed, DateTime.UtcNow.AddHours(7).Date);
        Assert.Equal(205_000m, flow.Summary.Single(x => x.Fund == "bank").Inflow); Assert.Equal(0m, flow.Summary.Single(x => x.Fund == "bank").Outflow);
        Assert.Equal(0m, flow.Summary.Single(x => x.Fund == "cash").Inflow); Assert.Equal(0m, flow.Summary.Single(x => x.Fund == "cash").Outflow);
    }

    [Fact]
    public async Task Direct_legacy_accept_and_richer_authenticated_retry_never_gain_original_authenticated_attestation()
    {
        var c = await Web06Case.CreateAsync(fixture); var native = await NativeAsync(c); var upload = Upload(200_000m); int id;
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        await using (var db = c.Context())
        {
            var device = await db.Set<BankNotificationDevice>().SingleAsync();
            id = (await Web06NotificationHost.Bridge(c.Seed, db, Path.Combine(c.Checkout.Directory, "admin-keys")).AcceptAsync(device, upload, default))!.Value;
            Assert.Empty(await db.Set<StorefrontNotificationAttestation>().ToListAsync());
        }
        await using var host = await Web06NotificationHost.StartAsync(c); Assert.Equal(id, await Accepted(host, native, upload));
        await using var check = c.Context(); Assert.Empty(await check.Set<StorefrontNotificationAttestation>().ToListAsync());
        await Assert.ThrowsAsync<ConflictAppException>(() => Importer(c, check).ImportAuthenticatedNotificationAsync(id)); Assert.Empty(await bankInspection.Receipts(check).ToListAsync()); await bankInspection.AssertDeltaAsync(check);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_HTTPS_token_guard_and_unknown_bank_time_never_certify_POSTED_time_as_bank_money(bool omitBankTime)
    {
        var c = await Web06Case.CreateAsync(fixture); var native = await NativeAsync(c); await using var host = await Web06NotificationHost.StartAsync(c);
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        var upload = Upload(200_000m, includeTime: !omitBankTime);
        using (var unauthenticated = await host.UploadAsync(native.Id, native.Token, upload, authenticate: false)) Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using (var wrong = await host.UploadAsync(native.Id, new string('0', 64), upload)) Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        await using (var db = c.Context()) { Assert.Empty(await db.Set<BankNotificationReceipt>().ToListAsync()); Assert.Empty(await db.Set<StorefrontNotificationAttestation>().ToListAsync()); }
        var id = await Accepted(host, native, upload); await using var check = c.Context();
        Assert.Equal(id, await Accepted(host, native, upload)); Assert.Single(await check.Set<BankNotificationReceipt>().ToListAsync()); Assert.Single(await check.Set<StorefrontNotificationAttestation>().ToListAsync());
        var global = await Importer(c, check).ImportAuthenticatedNotificationAsync(id); var receipt = await bankInspection.Receipts(check).SingleAsync(x => x.Id == global); await bankInspection.AssertDeltaAsync(check, receipts: 1, amount: 200_000m);
        if (omitBankTime) { Assert.True(receipt.NeedsReview); Assert.Equal(BankReceiptRoutingProvenance.Unproven, receipt.RoutingProvenance); Assert.Equal(BankTimePrecision.Unknown, receipt.TimePrecision); Assert.Null(receipt.OccurredAtUtc); Assert.Empty(await Importer(c, check).ListAsync()); Assert.Empty(await check.Set<StorefrontCodBankRouting>().ToListAsync()); }
        else { Assert.Equal(BankTimePrecision.Timestamp, receipt.TimePrecision); Assert.NotNull(receipt.OccurredAtUtc); Assert.Equal(BankReceiptAssetConfidence.CertifiedDistinct, receipt.AssetConfidence); }
    }

    [Fact]
    public async Task Grant_added_after_real_intake_cannot_authorize_fee_owner_for_that_original_receipt()
    {
        var c = await Web06Case.CreateAsync(fixture, shipping: 30_000m); var feeOwner = await Web06FinanceSupport.ServiceSettingAsync(c, separateOwner: true);
        await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c); var native = await NativeAsync(c, excludeOwner: feeOwner);
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        await using var host = await Web06NotificationHost.StartAsync(c); var id = await Accepted(host, native, Upload(230_000m)); int global;
        await using (var db = c.Context()) global = await Importer(c, db).ImportAuthenticatedNotificationAsync(id);
        await using (var db = c.Context()) { db.Add(new StorefrontBankCollectionAuthority { StoreId = c.StoreId, BankAccountId = c.Seed.BankAccountId, ReceivingLegalEntityId = c.Seed.LegalEntityId, SourceLegalEntityId = feeOwner, IsActive = true }); await db.SaveChangesAsync(); }
        var request = await c.RequestAsync<StorefrontCodBankReceiptRequest>(); request.GlobalBankReceiptId = global; request.ReceivingLegalEntityId = c.Seed.LegalEntityId; request.EvidenceReference = "Unacceptable late authority proof";
        await using var check = c.Context(); await Assert.ThrowsAsync<ConflictAppException>(() => Web06FinanceSupport.Settlement(c, check).ReceiveBankRemittanceAsync(c.OrderId, request));
        Assert.Empty(await check.Set<StorefrontCodBankRouting>().ToListAsync()); Assert.Empty(await check.Set<StorefrontCodReceipt>().ToListAsync()); Assert.Empty(await check.Orders.ToListAsync()); Assert.Equal(230_000m, (await bankInspection.Receipts(check).SingleAsync()).Amount); await bankInspection.AssertDeltaAsync(check, receipts: 1, amount: 230_000m);
    }
    internal static StorefrontCodBankSourceService Importer(Web06Case c, AppDbContext db)
        => new(db, new Web01Actor(c.Seed.UserId), new Web01Permissions(c.StoreId, c.Seed.UserId, [StorefrontPermissions.Reconcile]),
            Web05Support.AdminProtocol(Path.Combine(c.Checkout.Directory, "admin-keys")), TimeProvider.System);
    internal static async Task<(int Id, string Token)> NativeAsync(Web06Case c, int? excludeOwner = null)
    {
        await using var db = c.Context(); var bank = await db.StoreBankAccounts.SingleAsync(x => x.Id == c.Seed.BankAccountId);
        bank.AccountNumber = "000W06" + c.StoreId; bank.ConfirmMode = BankQrConfirmMode.Manual; bank.QrRenderMode = BankQrRenderMode.LocalEmvQr;
        await db.SaveChangesAsync();
        var owners = await db.Set<StorefrontFulfillmentSource>().Select(x => x.SourceLegalEntityId).Distinct().ToListAsync();
        var fee = await db.Set<StorefrontServiceRevenueSetting>().SingleOrDefaultAsync(); if (fee is not null && !owners.Contains(fee.LegalEntityId)) owners.Add(fee.LegalEntityId);
        foreach (var owner in owners.Where(x => x != excludeOwner)) db.Add(new StorefrontBankCollectionAuthority { StoreId = c.StoreId, BankAccountId = bank.Id, ReceivingLegalEntityId = c.Seed.LegalEntityId, SourceLegalEntityId = owner, IsActive = true });
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var device = new BankNotificationDevice { StoreId = c.StoreId, BankAccountId = bank.Id, BankAccountFingerprint = BankNotificationParser.AccountFingerprint(bank), Name = "WEB06 authenticated synthetic phone", Enabled = true, TokenHash = BankNotificationParser.Hash(token) };
        db.Add(device); await db.SaveChangesAsync(); return (device.Id, token);
    }
    internal static BankNotificationUpload Upload(decimal amount, string? external = null, bool includeTime = true)
    {
        var bankTime = includeTime ? DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).ToString("ddMMyy-HH:mm:ss", CultureInfo.InvariantCulture) : "";
        return new("mobile.acb.com.vn", $"Nhận thành công +{amount.ToString("0", CultureInfo.InvariantCulture)} VND từ KHACH THU tại ACB {external ?? "FTW06" + Guid.NewGuid().ToString("N").ToUpperInvariant()} {bankTime}", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
    internal static async Task<int> Accepted(Web06NotificationHost host, (int Id, string Token) native, BankNotificationUpload upload)
    {
        using var result = await host.UploadAsync(native.Id, native.Token, upload); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var data = await result.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(data.GetProperty("accepted").GetBoolean()); return data.GetProperty("receiptId").GetInt32();
    }
    private sealed class CertificationFixtureFault : Exception { }
    private sealed class CertificationWriteFailure : SaveChangesInterceptor
    {
        internal int Hits;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Hits == 0 && data.Context!.ChangeTracker.Entries<StorefrontNotificationCertification>().Any(x => x.State == EntityState.Added)) { Hits++; throw new CertificationFixtureFault(); }
            return ValueTask.FromResult(result);
        }
    }
}
