using GaoApp.Domain.Entities;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Tests.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Storefront;

[CollectionDefinition("StorefrontWeb07", DisableParallelization = true)]
public sealed class Web07Collection : ICollectionFixture<Web01Fixture> { }

internal static class Web07Support
{
    internal static void NoCapability(string text, string token, string boundary)
    {
        Assert.False(text.Contains(token, StringComparison.Ordinal) || text.Contains(Uri.EscapeDataString(token), StringComparison.Ordinal),
            "Guest capability must be absent from " + boundary + "; value suppressed from diagnostics.");
    }
    internal static string Artifact(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(Web03Support.Root(), ".artifacts/storefront-rollout/WEB07/tests", relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); return path;
    }
    internal static string Runtime(string purpose)
    {
        var path = Artifact("runtime/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "fixture-purpose.txt"), purpose); return path;
    }
    internal static readonly string[] Permissions = [.. Web06Support.PhysicalPermissions,
        StorefrontPermissions.ApproveCancellation, StorefrontPermissions.ApproveRefund, StorefrontPermissions.ConfirmRefund,
        StorefrontPermissions.PostSale, PermissionCodes.Report.Treasury.Manage];
    internal static StorefrontCancellationService Cancellation(Web07Case c, AppDbContext db, bool guest = false,
        IEnumerable<string>? permissions = null, int? actorId = null, bool authenticated = true)
    {
        var userId = actorId ?? c.Seed.UserId;
        var actor = new Web01Actor(guest ? null : userId, !guest && authenticated);
        var rights = new Web01Permissions(c.StoreId, userId, permissions ?? (guest ? [] : Permissions));
        var balances = new InventoryBalanceRepository(db);
        return new(db, actor, balances, c.Clock, guest ? null : rights);
    }
    internal static StorefrontRefundService Refunds(Web07Case c, AppDbContext db,
        IEnumerable<string>? permissions = null, int? actorId = null, bool authenticated = true)
    {
        var userId = actorId ?? c.Seed.UserId; var actor = new Web01Actor(userId, authenticated);
        var rights = new Web01Permissions(c.StoreId, userId, permissions ?? Permissions);
        var keys = c.Banking?.AdminKeys ?? Path.Combine(c.Checkout.Directory, "admin-keys");
        return new(db, actor, rights, Web05Support.AdminProtocol(keys), c.Clock);
    }
}

// Genuine checkout/funding and physical builders from the prior stages. No
// positive cancellation, return, refund, payout, or closure proof is seeded.
internal sealed record Web07Case(Web01Case Seed, Web04Case Checkout, StorefrontPlacedOrder Placed,
    int OrderId, string Directory, Web06Case? Physical, Web05Case? Banking)
{
    internal int StoreId => Seed.StoreId;
    internal string Token => Web04Support.GuestToken(Placed);
    internal TimeProvider Clock => Checkout.Clock;
    internal bool SynchronizeCommandClockWithActualLegacyTime { get; set; } = true;
    internal AppDbContext Context(IInterceptor? interceptor = null) => Seed.Database.CreateTenantContext(StoreId, interceptor);

    // QR here deliberately means an actually prepaid order. Pending QR uses
    // CreateUnpaidQrAsync so no test silently resets a paid order's flags.
    internal static async Task<Web07Case> CreatePhysicalAsync(Web01Fixture fixture, bool prepaidQr = false,
        bool multipleSources = false, bool national = false, bool negative = false, decimal stock = 10m,
        decimal quantity = 4m, decimal shipping = 30_000m, bool gift = false, Func<Web04Case, Task>? beforeQuote = null)
    {
        var directory = Web07Support.Runtime("WEB07 real physical-capable checkout, no cancellation/refund proof seeded");
        await AttemptAsync(fixture, directory);
        var physical = await Web06Case.CreateAsync(fixture, quantity: quantity, stock: stock, negative: negative,
            national: national, multipleSources: multipleSources, shipping: shipping, sameSkuGift: gift,
            payment: prepaidQr ? StorefrontPaymentMethod.Qr : StorefrontPaymentMethod.Cod,
            beforeQuote: async c =>
            {
                await using var db = c.Context();
                var account = await db.StoreBankAccounts.SingleAsync(x => x.Id == c.Seed.BankAccountId);
                account.AccountNumber = "000W07" + c.StoreId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await db.SaveChangesAsync();
                if (beforeQuote is not null) await beforeQuote(c);
            });
        var result = new Web07Case(physical.Seed, physical.Checkout, physical.Placed, physical.OrderId,
            directory, physical, physical.QrCase);
        await result.LineageAsync(); return result;
    }

    // Existing WEB05 arranged availability is sufficient for a checkout-only
    // pending QR. This fixture does not claim a physical FIFO/cost transition.
    internal static async Task<Web07Case> CreateUnpaidQrAsync(Web01Fixture fixture)
    {
        var directory = Web07Support.Runtime("WEB07 genuinely pending QR; existing checkout-only availability fixture");
        await AttemptAsync(fixture, directory);
        var banking = await fixture.Web05Async(); var quote = await banking.QuoteAsync();
        var result = await banking.PlaceAsync(Web04Case.PlaceRequest(quote));
        Assert.Equal(StorefrontPlacementStatus.Placed, result.Status); Assert.NotNull(result.Order);
        await using var db = banking.Context();
        var order = await db.StorefrontOrders.AsNoTracking().SingleAsync(x => x.Code == result.Order.Code);
        Assert.Equal(StorefrontOrderPhase.AwaitingPayment, order.Phase);
        var value = new Web07Case(banking.Seed, banking.Web, result.Order, order.Id, directory, null, banking);
        await value.LineageAsync(); return value;
    }

    internal async Task<T> CommandAsync<T>(Guid? key = null) where T : StorefrontOperationRequest, new()
    {
        // Real POS cash/physical commands use the actual UTC clock. Normal
        // lifecycle commands share that time; expiry tests explicitly opt out
        // before advancing the controllable bank/checkout clock.
        if (SynchronizeCommandClockWithActualLegacyTime) Checkout.Clock.Utc = DateTime.UtcNow;
        await using var db = Context(); var order = await db.StorefrontOrders.AsNoTracking().SingleAsync(x => x.Id == OrderId);
        Assert.Equal(8, order.RowVersion.Length);
        return new() { ClientRequestId = key ?? Guid.NewGuid(), ExpectedRevision = order.Revision,
            ExpectedVersion = Convert.ToBase64String(order.RowVersion), Note = "WEB07 controlled actual command evidence" };
    }
    internal async Task<StorefrontGuestCancellationRequest> CancelRequestAsync(string reason = "WEB07 khách yêu cầu hủy", Guid? key = null)
    {
        var request = await CommandAsync<StorefrontGuestCancellationRequest>(key); request.Token = Token; request.Reason = reason; return request;
    }
    internal async Task<StorefrontCancellationDecisionRequest> CancellationDecisionAsync(int id, string reason, Guid? key = null)
    {
        var request = await CommandAsync<StorefrontCancellationDecisionRequest>(key); request.CancellationRequestId = id; request.Reason = reason; return request;
    }
    internal async Task<StorefrontRefundDecisionRequest> RefundDecisionAsync(int id, string reason, Guid? key = null)
    {
        var request = await CommandAsync<StorefrontRefundDecisionRequest>(key); request.RefundId = id; request.Reason = reason; return request;
    }
    internal async Task<StorefrontRefundExecutionRequest> ExecutionRequestAsync(int refundId, StorefrontRefundPayoutSource source,
        int ownerId, int? payerAccountId, string externalId, DateTimeOffset? actualExecutedAt, Guid? key = null)
    {
        var request = await CommandAsync<StorefrontRefundExecutionRequest>(key);
        request.RefundId = refundId; request.SourceKind = source.Kind; request.SourceId = source.Id; request.SourceVersion = source.Version;
        request.PayerLegalEntityId = ownerId; request.PayerBankAccountId = payerAccountId; request.ExternalTransactionId = externalId;
        if (payerAccountId.HasValue)
        {
            await using var db = Context(); var bank = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == payerAccountId.Value);
            Assert.Equal(8, bank.RowVersion.Length); request.PayerBankAccountVersion = Convert.ToBase64String(bank.RowVersion);
        }
        else request.PayerBankAccountVersion = null;
        request.ExecutedAt = actualExecutedAt; request.EvidenceReference = "WEB07 authorized synthetic execution evidence; actual recorded debit";
        return request;
    }
    internal async Task<string> SnapshotAsync()
    {
        await using var db = Context(); var order = await db.StorefrontOrders.AsNoTracking().SingleAsync(x => x.Id == OrderId);
        return JsonSerializer.Serialize(new
        {
            Order = new { order.Id, order.Phase, order.PaymentState, order.RefundState, order.Revision, Version = Convert.ToBase64String(order.RowVersion) },
            Holds = await db.InventoryReservations.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReferenceType, x.ReferenceId, x.Status, x.ReservedQty }).ToListAsync(),
            Balances = await db.InventoryBalances.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OnHandQty, x.ReservedQty, x.InventoryValue }).ToListAsync(),
            Physical = await db.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReferenceType, x.ReferenceId }).ToListAsync(),
            Refunds = await db.Set<StorefrontRefund>().AsNoTracking().Where(x => x.StorefrontOrderId == OrderId).OrderBy(x => x.Id).Select(x => new { x.Id, x.State, x.Amount, x.Reason }).ToListAsync(),
            RefundEvidence = await db.Set<StorefrontRefundEvidence>().AsNoTracking().Where(x => x.StorefrontOrderId == OrderId).OrderBy(x => x.Id).Select(x => new { x.Id, x.Amount, x.ExecutedAtUtc }).ToListAsync(),
            Funding = await db.Set<StorefrontRefundFunding>().AsNoTracking().Where(x => x.StorefrontOrderId == OrderId).OrderBy(x => x.Id).Select(x => new { x.Id, x.RefundId, x.Kind, x.GlobalBankReceiptId, x.CodReceiptId, x.Amount }).ToListAsync(),
            Requests = await db.Set<StorefrontCancellationRequest>().AsNoTracking().Where(x => x.StorefrontOrderId == OrderId).OrderBy(x => x.Id).Select(x => new { x.Id, x.ClientRequestId, x.Reason }).ToListAsync(),
            Decisions = await db.Set<StorefrontCancellationDecision>().AsNoTracking().Where(x => x.StorefrontOrderId == OrderId).OrderBy(x => x.Id).Select(x => new { x.Id, x.Kind, x.ActorUserId, x.AuthorisesPhysicalReturn }).ToListAsync(),
            Commands = await db.Set<StorefrontCommandReceipt>().AsNoTracking().Where(x => x.StorefrontOrderId == OrderId).OrderBy(x => x.Id).Select(x => new { x.Id, x.Operation, x.ClientRequestId, x.RequestHash, x.OutcomeJson }).ToListAsync(),
            Events = await db.Set<StorefrontEvent>().AsNoTracking().Where(x => x.StorefrontOrderId == OrderId).OrderBy(x => x.Id).Select(x => new { x.Id, x.Kind, x.Revision, x.CausationId }).ToListAsync(),
            Sales = await db.Orders.CountAsync(), Payments = await db.OrderPayments.CountAsync(),
            Shifts = await db.POSShifts.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.CashInTotal, x.CashOutTotal,
                x.CashSalesTotal, x.NonCashSalesTotal, x.ClosingCashExpected, x.RowVersion }).ToListAsync(),
            Cash = await db.POSShiftCashTransactions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Type, x.Amount }).ToListAsync(),
            Treasury = await db.TreasuryEntries.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Amount, x.IsVoucherLink, x.ReversedAtUtc }).ToListAsync(),
            // Whole-database before/after counts protect against unexpected wrong-account
            // or wrong-origin money creation without assuming the serial DB is empty.
            GlobalReceipts = await db.Set<BankVerifiedReceipt>().CountAsync(),
            GlobalAmount = await db.Set<BankVerifiedReceipt>().SumAsync(x => x.Amount),
            GlobalClaims = await db.Set<BankReceiptConsumption>().CountAsync()
        }).Replace(Token, "[guest-capability]", StringComparison.Ordinal);
    }

    internal async Task<string> StockAndCustodySnapshotAsync()
    {
        await using var db = Context();
        return JsonSerializer.Serialize(new
        {
            Holds = await db.InventoryReservations.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Balances = await db.InventoryBalances.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Transactions = await db.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Dispatch = await db.DeliveryDispatchCostFragments.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Returns = await db.DeliveryReturnedCostFragments.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Incidents = await db.Set<StorefrontCustodyIncident>().AsNoTracking().Where(x => x.StorefrontOrderId == OrderId).OrderBy(x => x.Id).ToListAsync(),
            Payments = await db.OrderPayments.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Cash = await db.POSShiftCashTransactions.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Treasury = await db.TreasuryEntries.AsNoTracking().OrderBy(x => x.Id).ToListAsync()
        });
    }

    private static Task AttemptAsync(Web01Fixture fixture, string directory)
    {
        var name = new SqlConnectionStringBuilder(fixture.Database.ConnectionString).InitialCatalog;
        if (!Regex.IsMatch(name, "^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Unowned fixture database");
        return File.WriteAllTextAsync(Path.Combine(directory, "owned-case-attempt.json"), JsonSerializer.Serialize(new
        {
            AtUtc = DateTime.UtcNow, ProcessId = Environment.ProcessId,
            ProcessStartedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(), Database = name,
            ReusedFixtureRoots = new[] { ".artifacts/storefront-rollout/WEB05/tests/runtime", ".artifacts/storefront-rollout/WEB06/tests/runtime" }
        }));
    }
    private async Task LineageAsync()
    {
        // Actual report service reads in a SQL snapshot transaction. Enable that
        // database option only through the existing owned-GUID guard; no money,
        // stock, source confidence, constraint, or application row is changed.
        await Web05CashFlowTests.SnapshotIsolation(Seed);
        await using var db = Context(); var order = await db.StorefrontOrders.AsNoTracking().SingleAsync(x => x.Id == OrderId);
        await File.WriteAllTextAsync(Path.Combine(Directory, "owned-case-lineage.json"), JsonSerializer.Serialize(new
        {
            AtUtc = DateTime.UtcNow, ProcessId = Environment.ProcessId, StoreId, OrderId,
            GuestTokenHash = order.GuestTokenHash, PhysicalCase = Physical is not null,
            CheckoutRuntime = Checkout.Directory, AdminKeyRoot = Banking?.AdminKeys,
            HasActuallyPrepaidQr = Physical?.QrCase is not null,
            NoCancellationOrRefundProofSeeded = true, NoRawGuestTokenPersisted = true
        }));
    }
}
