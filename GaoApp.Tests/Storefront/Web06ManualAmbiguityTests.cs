using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.POS;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Services.POSPaymentQrs;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.POSPaymentQrs;
using GaoApp.Infrastructure.Repositories.StoreBankAccounts;
using GaoApp.Infrastructure.Services.Banking;
using GaoApp.Infrastructure.Services.POSPaymentQrs;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06ManualAmbiguityTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_manual_paid_first_then_plain_authenticated_phone_retains_ambiguous_secondary_without_certified_extra_asset_or_COD_route(bool crossStore)
    {
        var c = await Web06Case.CreateAsync(fixture, quantity: .9m); await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
        var native = await Web06CodSourceTests.NativeAsync(c); var manualStore = crossStore ? await Web06Case.CreateAsync(fixture, quantity: .9m) : c;
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        var identity = await PhysicalAccount(c); var manual = await ManualAsync(manualStore, identity);
        await using (var db = manualStore.Context()) await Payments(manualStore, db, manual.Terminal).ConfirmManualQrAsync(manual.Qr, default);
        await using (var db = manualStore.Context())
        {
            var original = await db.OrderPayments.SingleAsync(); Assert.Equal(45_000m, original.Amount); Assert.Null(original.BankVerifiedReceiptId);
            var order = await db.Orders.SingleAsync(); Assert.Equal(OrderStatus.Draft, order.Status); Assert.Equal(90_000m, order.GrandTotal);
            Assert.Equal(45_000m, order.PaidTotal); Assert.Equal(45_000m, order.BalanceDue); Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);
        }
        await using var host = await Web06NotificationHost.StartAsync(c);
        var id = await Web06CodSourceTests.Accepted(host, native, Web06CodSourceTests.Upload(45_000m)); int global;
        await using (var db = c.Context())
        {
            Assert.True((await db.Set<StorefrontNotificationAttestation>().SingleAsync()).HadManualAmbiguity);
            global = await Web06CodSourceTests.Importer(c, db).ImportAuthenticatedNotificationAsync(id);
            var receipt = await db.Set<BankVerifiedReceipt>().SingleAsync(x => x.Id == global); Assert.Equal(45_000m, receipt.Amount);
            Assert.Equal(BankReceiptAssetConfidence.TransactionIdentityAmbiguous, receipt.AssetConfidence); Assert.Equal(BankReceiptRoutingProvenance.Unproven, receipt.RoutingProvenance); Assert.True(receipt.NeedsReview); Assert.NotEmpty(receipt.ProtectedEvidence);
            // Operational labels are mutable; original ambiguity and immutable asset classification are authority.
            await db.Set<BankNotificationReceipt>().Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(r => r.ConfirmationSource, (string?)null));
            await db.Set<BankVerifiedReceipt>().Where(x => x.Id == global).ExecuteUpdateAsync(x => x.SetProperty(r => r.ReviewCode, "UNMATCHED_CHANNEL_BINDING"));
        }
        await using (var db = c.Context())
        {
            Assert.Equal(global, await Web06CodSourceTests.Importer(c, db).ImportAuthenticatedNotificationAsync(id));
            Assert.Empty(await Web06CodSourceTests.Importer(c, db).ListAsync());
            Assert.Equal(0m, await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId && x.AssetConfidence == BankReceiptAssetConfidence.CertifiedDistinct).SumAsync(x => x.Amount));
            Assert.Equal(45_000m, (await db.Set<BankVerifiedReceipt>().SingleAsync(x => x.Id == global)).Amount);
            var request = await c.RequestAsync<StorefrontCodBankReceiptRequest>(); request.GlobalBankReceiptId = global; request.ReceivingLegalEntityId = c.Seed.LegalEntityId; request.EvidenceReference = "Cannot prove a second transfer";
            await Assert.ThrowsAsync<ConflictAppException>(() => Web06FinanceSupport.Settlement(c, db).ReceiveBankRemittanceAsync(c.OrderId, request));
            Assert.Empty(await db.Set<StorefrontCodBankRouting>().ToListAsync()); Assert.Empty(await bankInspection.Consumptions(db).ToListAsync()); await bankInspection.AssertDeltaAsync(db, receipts: 1, amount: 45_000m);
        }
        await using var originalCheck = manualStore.Context(); Assert.Equal(45_000m, (await originalCheck.OrderPayments.SingleAsync()).Amount);
        // This deliberately does not claim a finalized POS sale or CashFlow 45k: legacy order remains Draft.
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_native_unmatched_first_blocks_later_manual_confirmation_on_same_physical_account_across_owned_stores(bool crossStore)
    {
        var c = await Web06Case.CreateAsync(fixture, quantity: .9m); var native = await Web06CodSourceTests.NativeAsync(c);
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        var manualStore = crossStore ? await Web06Case.CreateAsync(fixture, quantity: .9m) : c;
        var manual = await ManualAsync(manualStore, await PhysicalAccount(c));
        await using var host = await Web06NotificationHost.StartAsync(c); var id = await Web06CodSourceTests.Accepted(host, native, Web06CodSourceTests.Upload(45_000m));
        await using (var db = c.Context())
        {
            var global = await Web06CodSourceTests.Importer(c, db).ImportAuthenticatedNotificationAsync(id);
            Assert.Equal(BankReceiptAssetConfidence.CertifiedDistinct, (await db.Set<BankVerifiedReceipt>().SingleAsync(x => x.Id == global)).AssetConfidence);
        }
        await using (var db = manualStore.Context())
        {
            var qr = await db.PosPaymentQrRequests.SingleAsync(); Assert.Equal(PosPaymentQrStatus.Pending, qr.Status); Assert.NotEmpty(qr.AccountIdentitySnapshot!);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Payments(manualStore, db, manual.Terminal).ConfirmManualQrAsync(manual.Qr, default));
            Assert.Contains("nguồn thông báo ngân hàng độc lập", error.Message); Assert.Empty(await db.OrderPayments.ToListAsync());
        }
        await using var check = c.Context(); Assert.Equal(45_000m, await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId && x.AssetConfidence == BankReceiptAssetConfidence.CertifiedDistinct).SumAsync(x => x.Amount));
        Assert.Empty(await bankInspection.Consumptions(check).ToListAsync()); await bankInspection.AssertDeltaAsync(check, receipts: 1, amount: 45_000m);
    }
    internal static async Task<string> PhysicalAccount(Web06Case c) { await using var db = c.Context(); return (await db.StoreBankAccounts.SingleAsync(x => x.Id == c.Seed.BankAccountId)).AccountNumber; }
    internal static async Task<(int Qr, int Terminal)> ManualAsync(Web06Case c, string physicalAccount)
    {
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); await using var db = c.Context();
        var bank = await db.StoreBankAccounts.SingleAsync(x => x.Id == c.Seed.BankAccountId);
        if (bank.AccountNumber != physicalAccount) { bank.AccountNumber = physicalAccount; await db.SaveChangesAsync(); }
        bank.IsDefault = true; bank.VietQrBankBin = "970416"; bank.ConfirmMode = BankQrConfirmMode.Manual; bank.QrRenderMode = BankQrRenderMode.LocalEmvQr; await db.SaveChangesAsync();
        var shift = await db.POSShifts.SingleAsync(x => x.TerminalId == terminal);
        var order = new Order { StoreId = c.StoreId, POSShiftId = shift.Id, Status = OrderStatus.Draft, GrandTotal = 90_000m, Subtotal = 90_000m, BalanceDue = 90_000m };
        order.Lines.Add(new() { StoreId = c.StoreId, ProductId = c.Seed.ProductId, VariantId = c.Seed.VariantId, ItemName = "WEB06 real manual POS item", SellingUnitId = c.Seed.UnitId, UnitName = "unit", Quantity = 1, BaseQuantity = 1, Multiplier = 1, UnitPrice = 90_000m, LineTotal = 90_000m });
        db.Add(order); await db.SaveChangesAsync();
        var qr = await new POSPaymentQrService(new StoreBankAccountRepository(db), new POSPaymentQrRequestRepository(db), new LocalVietQrGenerator())
            .CreateLocalManualQrAsync(new OrderDraftDto { OrderId = order.Id, BalanceDue = 90_000m }, new() { Amount = 45_000m, ClientRequestId = Guid.NewGuid() });
        var persisted = await db.PosPaymentQrRequests.SingleAsync(x => x.Id == qr.Id);
        Assert.Equal(GlobalBankReceiptLedger.AccountIdentity(bank.BankCode, physicalAccount), persisted.AccountIdentitySnapshot); Assert.NotEmpty(persisted.AccountVersionSnapshot!);
        return (qr.Id, terminal);
    }
    internal static AcbPaymentService Payments(Web06Case c, AppDbContext db, int terminal)
    {
        var notifier = System.Reflection.DispatchProxy.Create<IPosRealtimeNotifier, Web05Proxy>(); ((Web05Proxy)(object)notifier).Call = (_, _) => Task.CompletedTask;
        return new(db, new AcbProtocol(new HttpClient(new Web05RejectAllTransport()), Web05Support.AdminProtection(Path.Combine(c.Checkout.Directory, "admin-keys"))),
            Web06FinanceSupport.NoExternal<IPOSService>(), new Web05PosRuntime(c.StoreId, terminal, c.Seed.UserId), notifier, new SqlAcbOrderLockProvider());
    }
}
