using System.Reflection;
using System.Security.Claims;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.POSShiftClosingSlips;
using GaoApp.Application.Services.Invoices;
using GaoApp.Application.Services.POSShifts;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.Customers;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Repositories.LegalEntities;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.Products;
using GaoApp.Infrastructure.Repositories.Reports;
using GaoApp.Application.Services.Reports;
using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Infrastructure.Repositories.POSShiftHandoverSlips;
using GaoApp.Infrastructure.Repositories.Users;
using GaoApp.Infrastructure.Services.Invoices;
using GaoApp.Infrastructure.Services.Banking;
using GaoApp.Infrastructure.Services.Orders;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Inventory;
using GaoApp.Web.Common.POS;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

internal static class Web06FinanceSupport
{
    internal static readonly string[] Permissions = [.. Web06Support.PhysicalPermissions, StorefrontPermissions.PostSale, StorefrontPermissions.ManageSettings];
    internal static async Task<Web05Case> ConfigureQrAsync(Web04Case c)
    {
        var keys = Path.Combine(c.Directory, "admin-keys"); var protocol = Web05Support.AdminProtocol(keys);
        GaoApp.Application.Storefront.StorefrontBankRegistration registration;
        await using (var db = c.Context())
        {
            var bank = await db.StoreBankAccounts.SingleAsync(x => x.Id == c.Seed.BankAccountId); bank.AccountNumber = "000W06QR" + c.StoreId;
            db.Add(new StoreAcbSettings { StoreId = c.StoreId, Enabled = true, BankAccountId = bank.Id, MerchantId = "SYNTHETIC-W05", BeneficiaryName = "Synthetic owner",
                ClientId = "synthetic-client-W06", ClientSecretProtected = protocol.Protect(c.StoreId, "synthetic-client-secret-W06"), CallbackApiKeyProtected = protocol.Protect(c.StoreId, "synthetic-callback-key-W06"),
                XOwnerNumber = "SYNTHETIC-OWNER", XProviderId = "SYNTHETIC-PROVIDER", VirtualAccountPrefix = "W06" });
            await db.SaveChangesAsync(); registration = new(true, "SYNTHETIC-OPERATOR-ATTESTATION-NOT-BANK-ACCEPTANCE", "SYNTHETIC-W05", "WEB05-REGISTERED", "WEB05-USER", bank.Id, c.Seed.LegalEntityId,
                GlobalBankReceiptLedger.AccountIdentity(bank.BankCode, bank.AccountNumber));
            var settings = (await c.Seed.FullService(db).GetAsync()).Settings; settings.IsEnabled = false; settings.QrEnabled = true; settings.BankAccountId = bank.Id; settings.ReceivingLegalEntityId = c.Seed.LegalEntityId;
            settings.BankTerminalCode = registration.TerminalCode; settings.BankUserCode = registration.UserCode; settings.QrHoldMinutes = 30;
            settings.CollectionSourceLegalEntityIds = await db.LegalEntities.Where(x => x.IsActive).Select(x => x.Id).ToListAsync();
            await c.Seed.FullService(db).SaveAsync(settings); await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsEnabled, true));
        }
        return new(c, keys, registration);
    }
    internal static T NoExternal<T>() where T : class
    {
        var value = DispatchProxy.Create<T, Web05Proxy>();
        ((Web05Proxy)(object)value).Call = (method, _) => throw new InvalidOperationException("Unexpected unrelated/external dependency: " + typeof(T).Name + "." + method.Name);
        return value;
    }
    internal static StorefrontSettlementService Settlement(Web06Case c, AppDbContext db, int terminalId = 0,
        IEnumerable<string>? permissions = null, IEnumerable<IOrderFinalizeGuard>? guards = null, IStorefrontInvoiceProcessor? invoices = null)
    {
        var actor = new Web01Actor(c.Seed.UserId, terminalId: terminalId > 0 ? terminalId : null);
        var rights = new Web01Permissions(c.StoreId, c.Seed.UserId, permissions ?? Permissions);
        var tenant = new TenantContext(); tenant.SetStore(c.StoreId, "w06-fixture-" + c.StoreId);
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, c.Seed.UserId.ToString()), new("terminal_id", terminalId.ToString())], "WEB06-fixture")) };
        var runtime = new POSRuntimeContextAccessor(new HttpContextAccessor { HttpContext = context }, tenant);
        var bridge = new StorefrontSaleBridge(db, actor, new CurrentPOSContext(runtime), rights, new OrderNumberGenerator(db, tenant),
            guards ?? [new AcbFinalizeGuard(db)], TimeProvider.System);
        return new(db, actor, rights, new InventoryBalanceRepository(db), InventoryPosPostingContractTests.CreateRealMovementService(db),
            bridge, invoices ?? new StorefrontInvoiceProcessor(db, InvoiceService(c, db), TimeProvider.System), TimeProvider.System);
    }
    internal static IInvoiceService InvoiceService(Web06Case c, AppDbContext db)
    {
        var repository = new InvoiceRepository(db); var variants = new ProductVariantRepository(db);
        var protection = new InvoiceProviderCredentialProtector(Web05Support.AdminProtection(Path.Combine(c.Checkout.Directory, "invoice-keys")));
        var command = new InvoiceCommandService(repository, variants, new InvoiceProviderSettingRepository(db, protection),
            new OrderLegalEntityAllocationRepository(db), new LegalEntityRepository(db), new InvoiceInputStockRepository(db));
        return new InvoiceService(new InvoiceReadService(repository, variants), command,
            new InvoiceBuyerService(repository, new CustomerRepository(db), new InvoiceBuyerProfileRepository(db), NoExternal<ITaxCodeLookupService>()));
    }
    internal static async Task ConfigureInvoiceOwnersAsync(Web04Case c, IEnumerable<int>? ownerIds = null)
    {
        await using var db = c.Context();
        var selected = ownerIds?.ToHashSet();
        foreach (var owner in await db.LegalEntities.ToListAsync())
        {
            if (selected is not null && !selected.Contains(owner.Id)) continue;
            var setting = new InvoiceProviderSetting { StoreId = c.Seed.StoreId, ProviderCode = "VIETTEL", IsActive = true,
                BaseUrl = "https://invoice.invalid", Username = "synthetic-no-network", Password = "synthetic-only",
                SupplierTaxCode = "01" + owner.Id.ToString("D8", System.Globalization.CultureInfo.InvariantCulture), TemplateCode = "1/001", InvoiceSeries = "C26W06", InvoiceType = "1", CurrencyCode = "VND" };
            db.Add(setting); await db.SaveChangesAsync(); owner.TaxCode = setting.SupplierTaxCode; owner.InvoiceProviderSettingId = setting.Id;
        }
        var tax = new Tax { StoreId = c.StoreId, Code = "W06-GOODS", Name = "WEB06 zero VAT goods", Rate = 0, IsActive = true };
        db.Add(tax); await db.SaveChangesAsync();
        (await db.Products.SingleAsync(x => x.Id == c.Seed.ProductId)).TaxId = tax.Id;
        await db.SaveChangesAsync();
    }
    internal static async Task<int> ServiceSettingAsync(Web06Case c, decimal taxRate = 0, bool separateOwner = false)
    {
        await using var db = c.Context(); var ownerId = c.Seed.LegalEntityId;
        if (separateOwner)
        {
            var owner = new LegalEntity { StoreId = c.StoreId, Code = "W06-FEE", Name = "WEB06 independent service owner", LegalName = "WEB06 service C", IsActive = true, SalePriority = 90 };
            db.Add(owner); await db.SaveChangesAsync(); ownerId = owner.Id;
        }
        var tax = new Tax { StoreId = c.StoreId, Code = "W06-FEE", Name = "WEB06 inclusive service VAT", Rate = taxRate, IsActive = true };
        db.Add(tax); await db.SaveChangesAsync();
        await Settlement(c, db).SaveServiceSettingAsync(new() { LegalEntityId = ownerId, TaxId = tax.Id, Name = "WEB06 shipping service", InvoiceRoute = InvoiceIssuanceRoute.Automatic });
        await ConfigureInvoiceOwnersAsync(c.Checkout); return ownerId;
    }
    internal static async Task<int> OwnOpenShiftAsync(Web06Case c, int? warehouseId = null, bool anotherUser = false)
    {
        await using var db = c.Context(); var terminal = new POSTerminal { StoreId = c.StoreId, Code = "W06-" + Guid.NewGuid().ToString("N")[..12], Name = "WEB06 own real terminal" };
        db.Add(terminal); await db.SaveChangesAsync();
        db.Add(new POSShift { StoreId = c.StoreId, TerminalId = terminal.Id, WarehouseId = warehouseId ?? c.Seed.WarehouseId,
            OpenedByUserId = anotherUser ? c.CourierId : c.Seed.UserId, Status = POSShiftStatus.Open, OpeningCash = 0, OpenedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(); return terminal.Id;
    }
    internal static async Task<int> ActualCashInAsync(Web06Case c, int terminalId, decimal amount)
    {
        await using var db = c.Context();
        var service = new POSShiftService(new POSShiftRepository(db), new OrderRepository(db), new WarehouseRepository(db),
            new SelectedStore(c.StoreId), new Web01Actor(c.Seed.UserId, terminalId: terminalId), new UserRepository(db),
            NoExternal<IAuditLogService>(), new POSShiftHandoverSlipRepository(db), NoExternal<IPOSShiftClosingSlipService>());
        return (await service.AddCashTransactionAsync(new CreatePosShiftCashTransactionRequest { Type = POSShiftCashTransactionType.CashIn,
            Amount = amount, Reason = "WEB06 actual courier cash remittance", Note = "Synthetic fixture, real cash-in command" })).Id;
    }
    internal static async Task DeliveredAsync(Web06Case c)
    { await c.PrepareAsync(); await c.PickAllAsync(); await c.DispatchAsync(); await c.OutcomeAllAsync(true); }
    internal static async Task CollectAllAsync(Web06Case c, decimal? amount = null)
    {
        var request = await c.RequestAsync<StorefrontCodCollectionRequest>(); request.FulfillmentSourceId = (await c.DetailAsync()).Children.First().SourceId;
        await using var db = c.Context(); request.Amount = amount ?? (await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).TotalDue;
        request.CustomerPaidAt = DateTimeOffset.UtcNow; request.EvidenceReference = "WEB06 real staff customer-paid evidence";
        await Settlement(c, db).RecordCustomerCollectionAsync(c.OrderId, request);
    }
    internal static async Task ReceiveCashAsync(Web06Case c, int cashId)
    {
        var request = await c.RequestAsync<StorefrontCodCashReceiptRequest>(); request.POSShiftCashTransactionId = cashId;
        await using var db = c.Context(); await Settlement(c, db).ReceiveCashRemittanceAsync(c.OrderId, request);
    }
    internal static async Task<ProfitReportResponseDto> ProfitAsync(Web06Case c)
    {
        await Web05CashFlowTests.SnapshotIsolation(c.Seed); await using var db = c.Context();
        var periods = new SalesReportingPeriodPolicy();
        return await new ProfitReportReadService(new ProfitReportReadRepository(db), new SelectedStore(c.StoreId), periods, new ProfitReportAggregationPolicy(periods))
            .ReadAsync(new() { FromDate = DateTime.UtcNow.AddHours(7).Date, ToDate = DateTime.UtcNow.AddHours(7).Date, Compare = "none" });
    }
    internal static async Task<POSShiftReconciliationDto> ReconciliationAsync(Web06Case c, int terminal)
    {
        await using var db = c.Context(); var shift = await db.POSShifts.SingleAsync(x => x.TerminalId == terminal);
        return await new GaoApp.Infrastructure.Services.POSShifts.POSShiftReconciliationService(db, new SelectedStore(c.StoreId),
            new Web01Actor(c.Seed.UserId, terminalId: terminal), NoExternal<GaoApp.Application.Interfaces.Services.Security.IStoreAdminAccess>()).GetAsync(shift.Id);
    }
    private sealed class SelectedStore(int id) : ICurrentStore { public int StoreId => id; }
}
