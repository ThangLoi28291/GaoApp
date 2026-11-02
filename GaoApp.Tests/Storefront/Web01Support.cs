using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Infrastructure.Services.Banking;
using GaoApp.Tests.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[CollectionDefinition("StorefrontWeb01", DisableParallelization = true)]
public sealed class Web01Collection : ICollectionFixture<Web01Fixture> { }

public sealed class Web01Fixture : IAsyncLifetime
{
    internal InventoryPostingLocalDb Database { get; } = new();
    public Task InitializeAsync() => Database.MigrateAsync();
    public async Task DisposeAsync() => await Database.DisposeAsync();

    internal async Task<Web01Case> CaseAsync()
    {
        var seed = await Database.SeedInventoryCatalogAsync(allowNegativeInventory: true);
        await using var db = Database.CreateTenantContext(seed.StoreId);
        var role = new Role { StoreId = seed.StoreId, Code = "WEB01", Name = "WEB01 synthetic role" };
        var user = new User { UserName = "web01-" + Guid.NewGuid().ToString("N"), PasswordHash = "synthetic-only", FullName = "WEB01 synthetic user" };
        db.UserInStores.Add(new() { StoreId = seed.StoreId, User = user, Role = role });
        var bank = new StoreBankAccount { StoreId = seed.StoreId, BankCode = "ACB", BankName = "Synthetic ACB", AccountNumber = "000TEST", AccountName = "Synthetic owner" };
        db.StoreBankAccounts.Add(bank);
        await db.SaveChangesAsync();
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == seed.WarehouseId);
        var variant = await db.ProductVariants.SingleAsync(x => x.Id == seed.ProductVariantId);
        var product = await db.Products.SingleAsync(x => x.Id == variant.ProductId);
        var unit = new ProductUnitConversion { StoreId = seed.StoreId, ProductVariantId = variant.Id, UnitId = product.BaseUnitId, Factor = 1, Price = 20 };
        db.ProductUnitConversions.Add(unit);
        await db.SaveChangesAsync();
        return new(Database, seed.StoreId, seed.WarehouseId, warehouse.LegalEntityId, product.Id, variant.Id, product.BaseUnitId,
            unit.Id, bank.Id, user.Id);
    }
}

internal sealed record Web01Case(InventoryPostingLocalDb Database, int StoreId, int WarehouseId,
    int LegalEntityId, int ProductId, int VariantId, int UnitId, int ConversionId, int BankAccountId, int UserId)
{
    internal AppDbContext Context() => Database.CreateTenantContext(StoreId);
    internal StorefrontSettingsService Service(AppDbContext db, params string[] permissions)
        => new(db, new Web01Actor(UserId), new Web01Permissions(StoreId, UserId, permissions));
    internal StorefrontSettingsService FullService(AppDbContext db)
        => Service(db, StorefrontPermissions.ViewSettings, StorefrontPermissions.ManageSettings, StorefrontPermissions.ManageShipping);
    internal StorefrontSettingsRequest Request(string version = "") => new()
    {
        Version = version, DisplayName = "WEB01 synthetic shop", SourceWarehouseId = WarehouseId,
        ContactPhone = "0901234567", CodEnabled = true, AllowNegativeInventory = true,
        ShippingFeeTreatment = StorefrontShippingFeeTreatment.StoreServiceFee
    };
    internal async Task<StorefrontShippingZoneRequest> ZoneAsync(string code = "LOCAL", string province = "79", string ward = "", StorefrontDeliveryMethod method = StorefrontDeliveryMethod.Local)
    {
        await using var db = Context(); var version = (await FullService(db).GetAsync()).Settings.Version;
        return new() { SettingsVersion = version, Code = code, Name = "Synthetic delivery zone", ProvinceCode = province, WardCode = ward, DeliveryMethod = method,
            MinDeliveryDays = 1, MaxDeliveryDays = 3, BaseFee = 20_000, AdditionalFee = 5_000, FirstWeightKg = 1, AdditionalStepKg = 0.5m };
    }
    internal async Task SaveDefaultsAsync()
    { await using var db = Context(); await FullService(db).SaveAsync(Request()); }
    internal async Task ConfigureAcbAsync()
    {
        await using var db = Context();
        db.Add(new StoreAcbSettings { StoreId = StoreId, Enabled = true, BankAccountId = BankAccountId,
            MerchantId = "SYNTHETIC", BeneficiaryName = "Synthetic owner", ClientId = "synthetic-only", ClientSecretProtected = "not-a-real-secret" });
        await db.SaveChangesAsync();
    }
    internal async Task<(int Product, int Variant, int SellingUnit)> PublishAsync(bool published, decimal? baseKg = null, decimal? unitKg = null)
    {
        await using var db = Context();
        var product = new StorefrontProduct { StoreId = StoreId, ProductId = ProductId, IsPublished = published };
        db.Add(product); await db.SaveChangesAsync();
        var variant = new StorefrontVariant { StoreId = StoreId, StorefrontProductId = product.Id, ProductId = ProductId, VariantId = VariantId,
            IsPublished = published, BaseUnitIsPublished = published, BaseUnitWeightKg = baseKg };
        db.Add(variant); await db.SaveChangesAsync();
        var unit = new StorefrontSellingUnit { StoreId = StoreId, StorefrontVariantId = variant.Id, VariantId = VariantId,
            ProductUnitConversionId = ConversionId, UnitId = UnitId, IsPublished = published, WeightKg = unitKg };
        db.Add(unit); await db.SaveChangesAsync(); return (product.Id, variant.Id, unit.Id);
    }
    internal async Task<Web01Commerce> CommerceAsync()
    {
        await SaveDefaultsAsync();
        await using var db = Context(); await FullService(db).SaveZoneAsync(await ZoneAsync());
        var rate = await db.StorefrontShippingRates.SingleAsync();
        var quote = new StorefrontQuote { StoreId = StoreId, Token = Guid.NewGuid(), SettingsRevision = 1, ShippingRateId = rate.Id,
            Fingerprint = new string('a', 64), ExpiresAtUtc = DateTime.UtcNow.AddHours(1), GoodsAmount = 200_000, ShippingCharge = 30_000,
            TotalDue = 230_000, SnapshotJson = "{\"synthetic\":true}" };
        db.Add(quote); await db.SaveChangesAsync();
        var order = new StorefrontOrder { StoreId = StoreId, Code = "W01-" + Guid.NewGuid().ToString("N"),
            GuestTokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), QuoteId = quote.Id,
            Phase = StorefrontOrderPhase.AwaitingPayment, PaymentMethod = StorefrontPaymentMethod.Qr, PaymentState = StorefrontPaymentState.Pending,
            DeliveryMethod = StorefrontDeliveryMethod.Local, RecipientName = "Synthetic recipient", RecipientPhone = "0901234567",
            RecipientAddress = "Synthetic test address", GoodsAmount = quote.GoodsAmount, ShippingCharge = quote.ShippingCharge, TotalDue = quote.TotalDue };
        db.Add(order); await db.SaveChangesAsync();
        var line = new StorefrontOrderLine { StoreId = StoreId, StorefrontOrderId = order.Id, ProductId = ProductId, VariantId = VariantId,
            ProductUnitConversionId = ConversionId, SellingUnitId = UnitId, ItemName = "Synthetic line", UnitName = "Unit", Quantity = 1,
            BaseQuantity = 1, UnitPrice = 200_000, NetAmount = 200_000, WeightKgPerSellingUnit = 0.2m };
        var fulfillment = new StorefrontFulfillmentSource { StoreId = StoreId, StorefrontOrderId = order.Id, SourceWarehouseId = WarehouseId,
            SourceLegalEntityId = LegalEntityId, DispatchPostingKey = "WEB01:" + Guid.NewGuid().ToString("N") };
        var authority = new StorefrontBankCollectionAuthority { StoreId = StoreId, BankAccountId = BankAccountId, ReceivingLegalEntityId = LegalEntityId, SourceLegalEntityId = LegalEntityId, IsActive = true };
        db.AddRange(line, fulfillment, authority); await db.SaveChangesAsync();
        // Schema fixtures retain their original amounts and leave allocation to
        // each test. WEB05 requires a real global reference and receiving proof.
        var bank = await db.StoreBankAccounts.SingleAsync(x => x.Id == BankAccountId);
        var sequence = await Database.ExecuteScalarAsync<long>("SELECT NEXT VALUE FOR dbo.StorefrontBankCorrelationSequence");
        var reference = "W" + sequence.ToString("D12", System.Globalization.CultureInfo.InvariantCulture);
        var binding = new StorefrontBankBinding(BankAccountId, LegalEntityId, GlobalBankReceiptLedger.AccountIdentity(bank.BankCode, bank.AccountNumber),
            bank.BankCode, bank.BankName, bank.AccountNumber, bank.AccountName, "WEB01-SYNTHETIC", bank.AccountName, "BANK-WEB", "REGISTERED-WEB", "WEB01",
            new string('F', 64), "SYNTHETIC SCHEMA FIXTURE, NOT REAL BANK REGISTRATION", 60, [LegalEntityId], [authority.Id + ":" + Convert.ToBase64String(authority.RowVersion)]);
        var intent = new StorefrontPaymentIntent { StoreId = StoreId, StorefrontOrderId = order.Id, ProviderReference = reference, BankAccountId = BankAccountId, ReceivingLegalEntityId = LegalEntityId,
            AmountDue = 230_000, ExpiresAtUtc = DateTime.UtcNow.AddHours(1), BankTerminalCode = "BANK-WEB", BankUserCode = "REGISTERED-WEB",
            BindingSnapshotJson = System.Text.Json.JsonSerializer.Serialize(binding, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) };
        db.Add(intent); await db.SaveChangesAsync();
        db.Add(new BankPaymentReference { OriginStoreId = StoreId, Reference = reference, AccountIdentity = binding.AccountIdentity, Channel = BankPaymentChannel.Storefront, StorefrontPaymentIntentId = intent.Id }); await db.SaveChangesAsync();
        var protocol = new AcbProtocolCore(new HttpClient(new Web05RejectAllTransport()), new EphemeralDataProtectionProvider());
        var recorded = await new GlobalBankReceiptLedger(db, protocol).RecordVerifiedAsync(new(GlobalBankReceiptLedger.AccountSnapshot(bank), reference, BankPaymentChannel.Storefront,
            Guid.NewGuid().ToString("N"), 230_000m, "COMPLETED", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture), "synthetic-only"));
        var global = await db.Set<BankVerifiedReceipt>().SingleAsync(x => x.Id == recorded.Id);
        var receipt = new StorefrontBankReceipt { StoreId = StoreId, GlobalBankReceiptId = global.Id, Provider = "ACB", BankAccountId = BankAccountId, ReceivingLegalEntityId = LegalEntityId,
            ExternalTransactionId = global.ExternalTransactionId, EvidenceFingerprint = global.Fingerprint, ProtectedEvidence = global.ProtectedEvidence, Amount = 230_000,
            OccurredAtUtc = global.OccurredAtUtc!.Value, IsVerified = true };
        db.Add(receipt); await db.SaveChangesAsync();
        return new(quote.Id, order.Id, line.Id, intent.Id, receipt.Id, fulfillment.Id, rate.Id);
    }

    internal async Task<StorefrontBankReceipt> AdditionalSchemaReceiptAsync(AppDbContext db, int intentId, decimal amount)
    {
        var intent = await db.Set<StorefrontPaymentIntent>().SingleAsync(x => x.Id == intentId); var bank = await db.StoreBankAccounts.SingleAsync(x => x.Id == BankAccountId);
        var protocol = new AcbProtocolCore(new HttpClient(new Web05RejectAllTransport()), new EphemeralDataProtectionProvider());
        var recorded = await new GlobalBankReceiptLedger(db, protocol).RecordVerifiedAsync(new(GlobalBankReceiptLedger.AccountSnapshot(bank), intent.ProviderReference, BankPaymentChannel.Storefront,
            Guid.NewGuid().ToString("N"), amount, "COMPLETED", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture), "synthetic additional schema receipt"));
        var global = await db.Set<BankVerifiedReceipt>().SingleAsync(x => x.Id == recorded.Id);
        var receipt = new StorefrontBankReceipt { StoreId = StoreId, GlobalBankReceiptId = global.Id, Provider = "ACB", BankAccountId = BankAccountId, ReceivingLegalEntityId = LegalEntityId,
            ExternalTransactionId = global.ExternalTransactionId, EvidenceFingerprint = global.Fingerprint, ProtectedEvidence = global.ProtectedEvidence, Amount = amount, OccurredAtUtc = global.OccurredAtUtc!.Value, IsVerified = true };
        db.Add(receipt); await db.SaveChangesAsync(); return receipt;
    }
    internal async Task<StorefrontPaymentIntent> AdditionalInactiveSchemaIntentAsync(AppDbContext db, int originalId)
    {
        var source = await db.Set<StorefrontPaymentIntent>().AsNoTracking().SingleAsync(x => x.Id == originalId);
        var sequence = await Database.ExecuteScalarAsync<long>("SELECT NEXT VALUE FOR dbo.StorefrontBankCorrelationSequence");
        var intent = new StorefrontPaymentIntent { StoreId = StoreId, StorefrontOrderId = source.StorefrontOrderId, IsActive = false, Provider = source.Provider,
            ProviderReference = "W" + sequence.ToString("D12", System.Globalization.CultureInfo.InvariantCulture), BankAccountId = source.BankAccountId, ReceivingLegalEntityId = source.ReceivingLegalEntityId,
            AmountDue = source.AmountDue, ExpiresAtUtc = source.ExpiresAtUtc, BankTerminalCode = source.BankTerminalCode, BankUserCode = source.BankUserCode, BindingSnapshotJson = source.BindingSnapshotJson };
        var binding = System.Text.Json.JsonSerializer.Deserialize<StorefrontBankBinding>(source.BindingSnapshotJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        db.Add(intent); await db.SaveChangesAsync(); db.Add(new BankPaymentReference { OriginStoreId = StoreId, Reference = intent.ProviderReference, AccountIdentity = binding.AccountIdentity, Channel = BankPaymentChannel.Storefront, StorefrontPaymentIntentId = intent.Id }); await db.SaveChangesAsync(); return intent;
    }
}

internal sealed record Web01Commerce(int QuoteId, int OrderId, int LineId, int IntentId, int ReceiptId, int FulfillmentId, int RateId);

internal sealed class Web01Actor(int? userId, bool authenticated = true, int? terminalId = null) : ICurrentUser
{
    public int? UserId => userId;
    public string? UserName => "WEB01 synthetic actor";
    public int? TerminalId => terminalId;
    public string? TerminalCode => null;
    public bool IsAuthenticated => authenticated;
}

internal sealed class Web01Permissions(int storeId, int userId, IEnumerable<string> codes) : ICurrentStorePermissionService
{
    private readonly HashSet<string> allowed = new(codes, StringComparer.Ordinal);
    public Task<bool> HasPermissionAsync(int store, int user, string code, CancellationToken ct = default)
        => Task.FromResult(store == storeId && user == userId && allowed.Contains(code));
    public Task<List<string>> GetPermissionsAsync(int store, int user, CancellationToken ct = default)
        => Task.FromResult(store == storeId && user == userId ? allowed.ToList() : []);
}
