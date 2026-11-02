using System.Net;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06AcceptanceRaceTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("token")] [InlineData("device-off")] [InlineData("account")]
    public async Task Actual_authenticated_HTTP_identity_changed_after_contact_save_is_revalidated_before_atomic_acceptance(string change)
    {
        var c = await Web06Case.CreateAsync(fixture); var native = await Web06CodSourceTests.NativeAsync(c);
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        var race = new AfterContactMutation(async () =>
        {
            await using var db = c.Context();
            if (change == "account") await db.StoreBankAccounts.Where(x => x.Id == c.Seed.BankAccountId).ExecuteUpdateAsync(x => x.SetProperty(b => b.AccountNumber, "000OTHERW06"));
            else if (change == "token") await db.Set<BankNotificationDevice>().Where(x => x.Id == native.Id).ExecuteUpdateAsync(x => x.SetProperty(d => d.TokenHash, new string('F', 64)));
            else await db.Set<BankNotificationDevice>().Where(x => x.Id == native.Id).ExecuteUpdateAsync(x => x.SetProperty(d => d.Enabled, false));
        });
        await using var host = await Web06NotificationHost.StartAsync(c, race);
        using var result = await host.UploadAsync(native.Id, native.Token, Web06CodSourceTests.Upload(200_000m)); Assert.Equal(HttpStatusCode.Conflict, result.StatusCode); Assert.Equal(1, race.Hits);
        await using var check = c.Context(); Assert.Empty(await check.Set<BankNotificationReceipt>().ToListAsync()); Assert.Empty(await check.Set<StorefrontNotificationAttestation>().ToListAsync()); Assert.Empty(await bankInspection.Receipts(check).ToListAsync()); await bankInspection.AssertDeltaAsync(check);
    }

    [Fact]
    public async Task Actual_HTTP_failure_writing_attestation_rolls_back_receipt_and_lost_ACK_retry_creates_one_original_proof()
    {
        var c = await Web06Case.CreateAsync(fixture); var native = await Web06CodSourceTests.NativeAsync(c); var fault = new FirstAttestationFailure();
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        var upload = Web06CodSourceTests.Upload(200_000m); await using var host = await Web06NotificationHost.StartAsync(c, fault);
        using (var failed = await host.UploadAsync(native.Id, native.Token, upload)) Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(1, fault.Hits);
        await using (var db = c.Context()) { Assert.Empty(await db.Set<BankNotificationReceipt>().ToListAsync()); Assert.Empty(await db.Set<StorefrontNotificationAttestation>().ToListAsync()); Assert.Empty(await bankInspection.Receipts(db).ToListAsync()); await bankInspection.AssertDeltaAsync(db); }
        var first = await Web06CodSourceTests.Accepted(host, native, upload); Assert.Equal(first, await Web06CodSourceTests.Accepted(host, native, upload));
        await using var check = c.Context(); Assert.Single(await check.Set<BankNotificationReceipt>().ToListAsync()); Assert.Single(await check.Set<StorefrontNotificationAttestation>().ToListAsync());
        var importer = Web06CodSourceTests.Importer(c, check); var global = await importer.ImportAuthenticatedNotificationAsync(first); Assert.Equal(global, await importer.ImportAuthenticatedNotificationAsync(first));
        Assert.Single(await bankInspection.Receipts(check).ToListAsync()); Assert.Single(await check.Set<StorefrontNotificationCertification>().ToListAsync()); await bankInspection.AssertDeltaAsync(check, receipts: 1, amount: 200_000m);
    }
    [Fact]
    public async Task Actual_authenticated_phone_after_existing_admin_acknowledgement_links_once_inside_acceptance_transaction()
    {
        var c = await Web06Case.CreateAsync(fixture, quantity: .9m); var native = await Web06CodSourceTests.NativeAsync(c);
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        var manual = await Web06ManualAmbiguityTests.ManualAsync(c, await Web06ManualAmbiguityTests.PhysicalAccount(c));
        string requestCode; int orderId;
        await using (var db = c.Context())
        {
            await Web06NotificationHost.Bridge(c.Seed, db, Path.Combine(c.Checkout.Directory, "admin-keys")).AdminApproveAsync(manual.Qr, 45_000m, null, default);
            var qr = await db.PosPaymentQrRequests.SingleAsync(); requestCode = qr.RequestCode; orderId = qr.OrderId;
            Assert.Null(qr.NotificationReceiptId); Assert.Null(qr.PaymentId); Assert.Equal("AdminApprovalPending", qr.NotificationConfirmationSource);
            Assert.Single(await db.Set<BankNotificationWait>().ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
        }
        var upload = Web06CodSourceTests.Upload(45_000m); upload = upload with { Content = upload.Content + $" POS-{orderId}-{requestCode}" };
        await using var host = await Web06NotificationHost.StartAsync(c);
        var id = await Web06CodSourceTests.Accepted(host, native, upload); Assert.Equal(id, await Web06CodSourceTests.Accepted(host, native, upload));
        await using var check = c.Context(); var receipt = Assert.Single(await check.Set<BankNotificationReceipt>().ToListAsync());
        var proof = Assert.Single(await check.Set<StorefrontNotificationAttestation>().ToListAsync()); var linkedQr = await check.PosPaymentQrRequests.SingleAsync();
        Assert.Equal(id, proof.BankNotificationReceiptId); Assert.Equal(id, linkedQr.NotificationReceiptId); Assert.NotNull(receipt.AppliedWaitId);
        Assert.Null(receipt.PaymentId); Assert.Equal("NotificationAdminPending", receipt.ConfirmationSource); Assert.Equal("NotificationAdminPending", linkedQr.NotificationConfirmationSource);
        Assert.Empty(await check.OrderPayments.ToListAsync()); Assert.Empty(await bankInspection.Receipts(check).ToListAsync()); await bankInspection.AssertDeltaAsync(check);
    }
    private sealed class AfterContactMutation(Func<Task> mutation) : SaveChangesInterceptor
    {
        internal int Hits;
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (Hits == 0 && data.Context!.ChangeTracker.Entries<BankNotificationDevice>().Any()) { Hits++; await mutation(); }
            return result;
        }
    }
    private sealed class AcceptanceFixtureFailure : Exception { }
    private sealed class FirstAttestationFailure : SaveChangesInterceptor
    {
        internal int Hits;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { if (Hits == 0 && data.Context!.ChangeTracker.Entries<StorefrontNotificationAttestation>().Any(x => x.State == EntityState.Added)) { Hits++; throw new AcceptanceFixtureFailure(); } return ValueTask.FromResult(result); }
    }
}
