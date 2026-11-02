using System.Net;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06CodReviewTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Genuine_native_COD_claim_then_trusted_same_ID_conflict_is_visible_as_Review_and_never_posts_or_erases_received_money()
    {
        var c = await Web06Case.CreateAsync(fixture); await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout);
        await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c); var native = await Web06CodSourceTests.NativeAsync(c);
        await using var device = await Web06NotificationHost.StartAsync(c); var notification = await Web06CodSourceTests.Accepted(device, native, Web06CodSourceTests.Upload(200_000m)); int global;
        await using (var db = c.Context()) global = await Web06CodSourceTests.Importer(c, db).ImportAuthenticatedNotificationAsync(notification);
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        var request = await c.RequestAsync<StorefrontCodBankReceiptRequest>(); request.GlobalBankReceiptId = global; request.ReceivingLegalEntityId = c.Seed.LegalEntityId; request.EvidenceReference = "Actual accepted native bank remittance";
        await using (var db = c.Context())
        {
            await Web06FinanceSupport.Settlement(c, db).ReceiveBankRemittanceAsync(c.OrderId, request); Assert.True((await Web06FinanceSupport.Settlement(c, db).GetAsync(c.OrderId)).CanPostSale);
            var original = await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.Id == global);
            var bank = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == c.Seed.BankAccountId);
            // Trusted server-evidence replay seam after actual authenticated intake.
            // No public client supplies verification flags; no raw row is edited.
            var conflict = await new GlobalBankReceiptLedger(db, Web05Support.AdminProtocol(Path.Combine(c.Checkout.Directory, "admin-keys"))).RecordVerifiedAsync(new(
                GlobalBankReceiptLedger.AccountSnapshot(bank), original.ProviderReference, original.Channel, original.ExternalTransactionId, 200_001m,
                "COMPLETED", original.RawBankTime, "Controlled authenticated conflicting evidence after genuine native intake", original.IdentityKind));
            Assert.True(conflict.Conflict);
        }
        await using (var db = c.Context())
        {
            var receipt = await db.Set<BankVerifiedReceipt>().SingleAsync(x => x.Id == global); Assert.Equal(200_000m, receipt.Amount); Assert.True(receipt.NeedsReview);
            Assert.Equal(BankReceiptAssetConfidence.CertifiedDistinct, receipt.AssetConfidence); Assert.Equal(200_001m, (await bankInspection.Conflicts(db).SingleAsync(x => x.BankVerifiedReceiptId == global)).ObservedAmount); await bankInspection.AssertDeltaAsync(db, conflicts: 1);
            var summary = await Web06FinanceSupport.Settlement(c, db).GetAsync(c.OrderId); Assert.Equal(200_000m, summary.ActualStoreReceived); Assert.False(summary.CanPostSale); Assert.False(string.IsNullOrWhiteSpace(summary.BlockReason));
            Assert.Equal(StorefrontPaymentState.Review, (await c.Service(db).GetAsync(c.OrderId)).PaymentState);
            var guest = await c.Checkout.Service(db).GetGuestOrderAsync(Web04Support.GuestToken(c.Placed)); Assert.NotNull(guest); Assert.Equal(StorefrontPaymentState.Review, guest.PaymentState);
            Assert.Equal(StorefrontOrderPhase.Delivered, guest.Phase);
        }
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); await using var admin = await Web06AdminHost.StartAsync(c, terminal); using var http = admin.Client();
        using (var page = await http.GetAsync("/admin/storefront/fulfillment/" + c.OrderId))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode); var html = await page.Content.ReadAsStringAsync();
            var postForm = System.Text.RegularExpressions.Regex.Match(html, "<form[^>]*id=\"sf-post-sale-form\"[\\s\\S]*?</form>"); Assert.True(postForm.Success);
            Assert.Matches("<button[^>]+disabled", postForm.Value);
        }
        var before = await c.PhysicalSnapshotAsync(); await using var check = c.Context(); var post = await c.RequestAsync<StorefrontOperationRequest>();
        await Assert.ThrowsAsync<ConflictAppException>(() => Web06FinanceSupport.Settlement(c, check, terminal).PostSaleAsync(c.OrderId, post)); Assert.Equal(before, await c.PhysicalSnapshotAsync());
        Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.InvoiceHeads.ToListAsync()); Assert.Single(await check.Set<StorefrontCodReceipt>().ToListAsync());
        Assert.Single(await check.DeliveryJournalEntries.ToListAsync()); Assert.Equal(6m, (await check.InventoryBalances.SingleAsync()).OnHandQty);
    }
}
