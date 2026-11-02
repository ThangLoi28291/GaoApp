using System.Net;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using GaoApp.Application.Services.Orders;
using GaoApp.Application.Services.Promotions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Promotions;
using GaoApp.Infrastructure.Services.Banking;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QrRegistration = GaoApp.Application.Storefront.StorefrontBankRegistration;

namespace GaoApp.Tests.Storefront;

[CollectionDefinition("StorefrontWeb05", DisableParallelization = true)]
public sealed class Web05Collection : ICollectionFixture<Web01Fixture> { }

internal static class Web05Support
{
    internal static string Artifact(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(Web03Support.Root(), ".artifacts/storefront-rollout/WEB05/tests", relative));
        StorefrontTestResourceMetadata.BeforeDirectory(Path.GetDirectoryName(path)!, "WEB05 artifact parent");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); return path;
    }
    internal static string RuntimePath(string purpose)
    {
        var path = Artifact("runtime/" + Guid.NewGuid().ToString("N")); StorefrontTestResourceMetadata.BeforeDirectory(path, purpose); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "fixture-purpose.txt"), purpose); return path;
    }
    internal static IDataProtectionProvider AdminProtection(string directory)
    {
        StorefrontTestResourceMetadata.BeforeDirectory(directory, "Configured admin key root");
        Directory.CreateDirectory(directory);
        StorefrontTestResourceMetadata.Provider(directory, "GaoApp");
        return DataProtectionProvider.Create(new DirectoryInfo(directory), x => x.SetApplicationName("GaoApp"));
    }
    internal static AcbProtocolCore AdminProtocol(string directory, HttpMessageHandler? transport = null)
        => new(new HttpClient(transport ?? new Web05RejectAllTransport()), AdminProtection(directory));
    internal static string VerifiedRaw(AcbProtocolCore protocol, int store, string protectedEvidence)
    {
        using var envelope = JsonDocument.Parse(protocol.Unprotect(store, protectedEvidence));
        var raw = envelope.RootElement.GetProperty("Raw").GetString()!; Assert.False(envelope.RootElement.GetProperty("Truncated").GetBoolean());
        Assert.Equal(Encoding.UTF8.GetByteCount(raw), envelope.RootElement.GetProperty("RawBytes").GetInt32());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))), envelope.RootElement.GetProperty("RawSha256").GetString()); return raw;
    }

    internal static async Task<Web05Case> Web05Async(this Web01Fixture fixture)
        => await (await fixture.CaseAsync()).Web05Async();
    internal static async Task<Web05Case> Web05Async(this Web01Case seed)
    {
        var pack = await seed.PackAsync(); await seed.EnableSyntheticCatalogAsync();
        var directory = RuntimePath("QR checkout/service"); var guestKeys = Path.Combine(directory, "guest-keys"); StorefrontTestResourceMetadata.BeforeDirectory(guestKeys, "WEB05 guest key root"); Directory.CreateDirectory(guestKeys);
        var web = new Web04Case(seed, pack.Conversion, pack.Unit, directory, Path.Combine(directory, "synthetic-addresses.json"), guestKeys, new());
        await web.WriteAddressAsync("SYNTHETIC-WEB05-V1"); var adminKeys = Path.Combine(directory, "admin-keys");
        var protocol = AdminProtocol(adminKeys);
        QrRegistration registration;
        await using (var db = seed.Context())
        {
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == seed.VariantId); variant.Price = 20000; variant.WholesalePrice = 1; variant.HasInputInvoice = true;
            var conversion = await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.ConversionId); conversion.Price = 25000; conversion.IsBaseUnit = true;
            var bank = await db.StoreBankAccounts.SingleAsync(x => x.Id == seed.BankAccountId);
            // Each independent scenario receives a distinct synthetic physical account. Alias/collision tests opt in explicitly.
            bank.AccountNumber = "000W05" + seed.StoreId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            db.InventoryBalances.Add(new() { StoreId = seed.StoreId, WarehouseId = seed.WarehouseId, ProductVariantId = seed.VariantId, OnHandQty = 100 });
            db.Add(new StoreAcbSettings { StoreId = seed.StoreId, Enabled = true, BankAccountId = seed.BankAccountId,
                MerchantId = "SYNTHETIC-W05", BeneficiaryName = "Synthetic owner", ClientId = "synthetic-client-W05",
                ClientSecretProtected = protocol.Protect(seed.StoreId, "synthetic-client-secret-W05"), CallbackApiKeyProtected = protocol.Protect(seed.StoreId, "synthetic-callback-key-W05-" + seed.StoreId),
                XOwnerNumber = "SYNTHETIC-OWNER", XProviderId = "SYNTHETIC-PROVIDER", VirtualAccountPrefix = "W05" });
            await db.SaveChangesAsync(); await seed.Products(db).SaveAsync(seed.ProductId, seed.Publication(pack: pack.Conversion));
            registration = new(true, "SYNTHETIC-OPERATOR-ATTESTATION-NOT-BANK-ACCEPTANCE", "SYNTHETIC-W05", "WEB05-REGISTERED", "WEB05-USER",
                seed.BankAccountId, seed.LegalEntityId, GlobalBankReceiptLedger.AccountIdentity(bank.BankCode, bank.AccountNumber));
        }
        var local = await seed.ZoneAsync("LOCAL", "01"); await using (var db = seed.Context()) await seed.FullService(db).SaveZoneAsync(local);
        var result = new Web05Case(web, adminKeys, registration); await result.ConfigureAsync(); return result;
    }
}

internal sealed record Web05Case(Web04Case Web, string AdminKeys, QrRegistration Registration)
{
    internal Web01Case Seed => Web.Seed;
    internal int StoreId => Seed.StoreId;
    internal AppDbContext Context() => Seed.Context();
    internal StorefrontCheckoutService Service(AppDbContext db, IStorefrontBankRegistration? registrations = null)
    {
        var promotions = new PromotionRepository(db);
        return new(db, new StorefrontCatalogService(db), new StorefrontAddressCatalog(Web.Configuration), promotions,
            new PromotionEngine(promotions), new InventoryBalanceRepository(db), new OrderLegalEntityAllocationService(), Web.Protection(), Web.Clock,
            registrations ?? new Web05Registration(StoreId, Registration));
    }
    internal StorefrontQuoteRequest Request() { var request = Web.Request(); request.PaymentMethod = StorefrontPaymentMethod.Qr; return request; }
    internal async Task<StorefrontQuoteDto> QuoteAsync(StorefrontQuoteRequest? request = null)
    { await using var db = Context(); return await Service(db).QuoteAsync(request ?? Request()); }
    internal async Task<StorefrontPlacementResult> PlaceAsync(StorefrontPlaceOrderRequest request)
    { await using var db = Context(); return await Service(db).PlaceAsync(request); }
    internal async Task ProcessAsync(int intentId, HttpMessageHandler transport, IInterceptor? interceptor = null)
    {
        await using var db = Seed.Database.CreateTenantContext(StoreId, interceptor);
        if (interceptor is Web05SaveFailureProbe probe) db.SaveChangesFailed += (_, args) => probe.Observe(args.Exception);
        await new StorefrontBankProcessor(db, Web05Support.AdminProtocol(AdminKeys, transport), Web.Clock).ProcessAsync(intentId);
    }
    internal async Task ConfigureAsync()
    {
        await using var db = Context(); var settings = (await Seed.FullService(db).GetAsync()).Settings;
        settings.IsEnabled = false; settings.QrEnabled = true; settings.CodEnabled = true; settings.QrHoldMinutes = 30;
        settings.BankAccountId = Seed.BankAccountId; settings.ReceivingLegalEntityId = Seed.LegalEntityId;
        settings.BankTerminalCode = Registration.TerminalCode; settings.BankUserCode = Registration.UserCode;
        settings.CollectionSourceLegalEntityIds = [Seed.LegalEntityId];
        await Seed.FullService(db).SaveAsync(settings);
        await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsEnabled, true));
    }
}

internal sealed record Web05Prepared(Web05Case Case, StorefrontPlacedOrder Placed, int OrderId, int IntentId, string Reference, Web05BankTransport Bank)
{
    internal static async Task<Web05Prepared> CreateAsync(Web01Fixture fixture)
        => await CreateAsync(await fixture.Web05Async());
    internal static async Task<Web05Prepared> CreateAsync(Web05Case c)
    {
        var placed = await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync())); Assert.Equal(StorefrontPlacementStatus.Placed, placed.Status);
        int intentId; int orderId; string reference; string account;
        await using (var db = c.Context())
        {
            var intent = await db.Set<StorefrontPaymentIntent>().AsNoTracking().SingleAsync(); intentId = intent.Id; reference = intent.ProviderReference; orderId = intent.StorefrontOrderId;
            account = (await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == c.Seed.BankAccountId)).AccountNumber;
        }
        var bank = new Web05BankTransport(account, c.Web.Clock); await c.ProcessAsync(intentId, bank);
        await using (var db = c.Context())
        {
            var state = await db.Set<StorefrontQrProcessing>().AsNoTracking().SingleAsync(); Assert.Equal((int)StorefrontQrState.Pending, state.State); Assert.NotNull(state.QrPng);
        }
        return new(c, placed.Order!, orderId, intentId, reference, bank);
    }
}

// Observation only: never suppress a failure or change the bank/business result.
internal sealed class Web05SaveFailureProbe : SaveChangesInterceptor
{
    private readonly List<object> failures = [];
    private readonly HashSet<Exception> observed = new(ReferenceEqualityComparer.Instance);
    internal void Observe(Exception error)
    {
        lock (failures)
        {
            if (!observed.Add(error)) return;
            failures.Add(new { Type = error.GetType().FullName,
                Entries = error is DbUpdateException update ? update.Entries.Select(x => x.Metadata.ClrType.Name).ToArray() : [],
                InnerType = error.InnerException?.GetType().FullName,
                SqlNumber = (error.InnerException as Microsoft.Data.SqlClient.SqlException)?.Number });
        }
    }
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    { Observe(eventData.Exception); return Task.CompletedTask; }
    internal async Task WriteAsync(Web05Case c, int intentId, Web05BankTransport transport, string scenario)
    {
        object[] errors; lock (failures) errors = [.. failures];
        await using var db = c.Context();
        var evidence = new
        {
            Scenario = scenario, Errors = errors,
            Processing = await db.Set<StorefrontQrProcessing>().AsNoTracking().Where(x => x.PaymentIntentId == intentId)
                .Select(x => new { x.State, x.ReviewCode, x.Attempts, x.LastAttemptAtUtc, x.NextAttemptAtUtc, x.NextScanBankDate }).ToListAsync(),
            Orders = await db.StorefrontOrders.AsNoTracking().Select(x => new { x.Id, x.PaymentState, x.Phase, x.Revision }).ToListAsync(),
            GlobalReceipts = await db.Set<BankVerifiedReceipt>().AsNoTracking().Where(x => x.OriginStoreId == c.StoreId)
                .Select(x => new { x.Amount, x.NeedsReview, x.AssetConfidence }).ToListAsync(),
            LocalReceiptCount = await db.Set<StorefrontBankReceipt>().CountAsync(), AllocationCount = await db.Set<StorefrontPaymentAllocation>().CountAsync(),
            ConsumptionCount = await db.Set<BankReceiptConsumption>().CountAsync(x => x.OriginStoreId == c.StoreId), Requests = transport.Requests.ToArray()
        };
        await File.WriteAllTextAsync(Web05Support.Artifact("repros/review-save-" + Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(evidence));
    }
}

internal sealed class Web05Registration(int storeId, QrRegistration? value) : IStorefrontBankRegistration
{
    public QrRegistration? Get(int store) => store == storeId ? value : null;
}

// There is no fallback transport: every unconfigured network operation fails before DNS or socket use.
internal sealed class Web05RejectAllTransport : HttpMessageHandler
{
    internal int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        throw new InvalidOperationException("Unexpected WEB05 fixture transport operation: " + request.Method + " " + request.RequestUri?.AbsolutePath);
    }
}
