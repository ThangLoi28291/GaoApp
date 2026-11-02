using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Services.Invoices;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06AutoInvoiceTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(0)] [InlineData(8)]
    public async Task Real_native_fee_auto_queue_keeps_zero_VAT_group_provenance_and_nonzero_individual_payload_balanced(decimal rate)
    {
        var first = await Web06Case.CreateAsync(fixture, quantity: 1, hasInputInvoice: false, shipping: 30_000m);
        await Web06FinanceSupport.ServiceSettingAsync(first, rate); var cases = new List<Web06Case> { first };
        for (var index = 0; index < 2; index++)
        {
            first.Checkout.Clock.Utc = DateTime.UtcNow; var quote = await first.Checkout.QuoteAsync(first.Checkout.Request(1));
            var placed = await first.Checkout.PlaceAsync(Web04Case.PlaceRequest(quote)); Assert.Equal(StorefrontPlacementStatus.Placed, placed.Status);
            await using var db = first.Context(); var id = await db.StorefrontOrders.Where(x => x.Code == placed.Order!.Code).Select(x => x.Id).SingleAsync();
            cases.Add(new() { Checkout = first.Checkout, Placed = placed.Order!, OrderId = id, CourierId = first.CourierId, OrderedBase = 1, InitialStock = first.InitialStock });
        }
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(first);
        foreach (var c in cases)
        {
            await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
            await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 80_000m));
            await using var db = c.Context(); await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, await c.RequestAsync<StorefrontOperationRequest>());
        }
        await using var context = first.Context();
        var originals = await context.InvoiceHeads.Include(x => x.Details).ToListAsync(); Assert.Equal(3, originals.Count);
        Assert.All(originals, head => { Assert.Null(head.OrderId); Assert.NotNull(head.StorefrontServiceRevenueId); Assert.Equal(30_000m, head.GrandTotal); Assert.Equal(InvoiceIssuanceRoute.Automatic, head.NativeServiceInvoiceRoute); });
        Assert.Equal(3, await context.Set<StorefrontServiceRevenue>().CountAsync());
        context.AutoInvoiceSettings.Add(new() { StoreId = first.StoreId, IsEnabled = true, MinimumAgeMinutes = 0, SeparateAmountThreshold = 100_000m, GroupTargetAmount = 90_000m,
            SendIntervalSeconds = 1, ScopeMode = AutoInvoiceScopeMode.Today, TimeZoneId = "SE Asia Standard Time", ClosingTimeLocal = new(23, 59, 59) }); await context.SaveChangesAsync();
        var client = new RecordingIssueClient(); var service = Auto(first, context, client);
        var dashboard = await service.GetDashboardAsync(new()); Assert.Equal(3, dashboard.Queue.Count);
        Assert.All(dashboard.Queue, row => Assert.Equal(rate == 0, row.IsGroupedConsumer));
        for (var attempt = 0; attempt < 4; attempt++) Assert.True((await service.RunOnceAsync(force: true)).IsSuccess);
        Assert.Equal(rate == 0 ? 1 : 3, client.Payloads.Count);
        Assert.Equal(90_000m, client.Payloads.Sum(x => x.Payload.ItemInfo.Sum(i => i.ItemTotalAmountWithTax)));
        foreach (var sent in client.Payloads)
        {
            var items = sent.Payload.ItemInfo; Assert.NotEmpty(items); Assert.All(items, x => Assert.Equal(1m, x.Quantity));
            Assert.Equal(items.Sum(x => x.ItemTotalAmountWithTax), items.Sum(x => x.ItemTotalAmountWithoutTax + x.TaxAmount));
            var actual = await context.InvoiceHeads.AsNoTracking().Include(x => x.Details).SingleAsync(x => x.Id == sent.Head);
            Assert.Equal(items.Sum(x => x.ItemTotalAmountWithTax), actual.GrandTotal);
            Assert.All(actual.Details, x => { Assert.Equal(InvoiceDetailSourceType.FromStorefrontServiceRevenue, x.SourceType); Assert.NotNull(x.StorefrontServiceRevenueId); Assert.Null(x.OrderLineId); });
            if (rate > 0) { Assert.Single(items); Assert.Equal(27_777.78m, actual.SubTotal); Assert.Equal(2_222.22m, actual.VatAmount); }
            else Assert.Equal(3, actual.Details.Count);
        }
        var operations = await context.AutoInvoiceOperations.ToListAsync(); Assert.Equal(rate == 0 ? 1 : 3, operations.Count);
        Assert.All(operations, x => { Assert.Equal(AutoInvoiceOperationStatus.Succeeded, x.Status); Assert.Equal(rate == 0 ? AutoInvoiceOperationKind.Group : AutoInvoiceOperationKind.Single, x.Kind); });
        Assert.Equal(3, await context.Orders.CountAsync()); Assert.Equal(3, await context.InventoryTransactions.CountAsync(x => x.ReferenceType == InventoryReferenceType.Delivery));
        Assert.Equal(240_000m, (await context.POSShifts.SingleAsync()).CashInTotal);
        // An issued real native source may be replaced without becoming Manual
        // or creating new revenue. Unsupported amount changes have no source budget.
        var originalId = client.Payloads[0].Head; var correction = new InvoiceCorrectionService(new InvoiceCorrectionRepository(context));
        var issued = await context.InvoiceHeads.AsNoTracking().SingleAsync(x => x.Id == originalId); Assert.Null(issued.CodeOfTax);
        var provider = new InvoiceProviderSettingRepository(context, new InvoiceProviderCredentialProtector(Web05Support.AdminProtection(Path.Combine(first.Checkout.Directory, "invoice-keys"))));
        var lookup = new AcceptedLookupClient(issued);
        var synchronized = await new ViettelInvoiceSyncService(new InvoiceRepository(context), provider, lookup).SyncByTransactionUuidAsync(originalId);
        Assert.True(synchronized.IsSuccess, synchronized.Error?.Message); Assert.Equal(1, lookup.Calls); Assert.Equal(issued.TransactionUuid, lookup.ObservedUuid);
        Assert.Equal("SYNTHETIC-CQT-" + originalId, (await context.InvoiceHeads.AsNoTracking().SingleAsync(x => x.Id == originalId)).CodeOfTax);
        var countBefore = await context.InvoiceHeads.CountAsync();
        var denied = await correction.CreateAsync(new() { OriginalInvoiceHeadId = originalId, Type = InvoiceCorrectionType.AdjustmentAmount,
            Reason = "No native amount-adjustment source budget", AgreementDocumentNo = "W06-NO-BUDGET", AgreementDate = DateTime.UtcNow.AddHours(7).Date });
        Assert.True(denied.IsFailure); Assert.Equal("Invoice.NativeServiceAdjustment", denied.Error.Code); Assert.Equal(countBefore, await context.InvoiceHeads.CountAsync());
        var originalInvoice = await context.InvoiceHeads.AsNoTracking().Include(x => x.Details).SingleAsync(x => x.Id == originalId);
        var replacement = await correction.CreateAsync(new() { OriginalInvoiceHeadId = originalId, Type = InvoiceCorrectionType.Replacement,
            Reason = "WEB06 exact native source replacement", AgreementDocumentNo = "W06-REPLACEMENT", AgreementDate = DateTime.UtcNow.AddHours(7).Date });
        Assert.True(replacement.IsSuccess, replacement.Error?.Message);
        var replaced = await context.InvoiceHeads.AsNoTracking().Include(x => x.Details).SingleAsync(x => x.Id == replacement.Value.NewInvoiceHeadId);
        Assert.Equal(originalId, replaced.OriginalInvoiceHeadId); Assert.Equal(originalInvoice.LegalEntityId, replaced.LegalEntityId); Assert.Equal(originalInvoice.GrandTotal, replaced.GrandTotal);
        Assert.Equal(originalInvoice.NativeServiceInvoiceRoute, replaced.NativeServiceInvoiceRoute); Assert.Equal(originalInvoice.NativeServiceRecognizedAtUtc, replaced.NativeServiceRecognizedAtUtc);
        Assert.Equal(originalInvoice.StorefrontServiceRevenueId, replaced.StorefrontServiceRevenueId);
        Assert.Equal(originalInvoice.Details.Select(x => x.StorefrontServiceRevenueId).Order(), replaced.Details.Select(x => x.StorefrontServiceRevenueId).Order());
        Assert.All(replaced.Details, x => { Assert.Equal(InvoiceDetailSourceType.FromStorefrontServiceRevenue, x.SourceType); Assert.NotNull(x.StorefrontServiceRevenueId); Assert.Null(x.OrderLineId); Assert.Equal(1m, x.Quantity); });
        var payload = await new ViettelInvoicePayloadBuilder(Web06FinanceSupport.InvoiceService(first, context), provider, new InvoiceIntegrationLogRepository(context)).BuildAsync(replaced.Id);
        Assert.True(payload.IsSuccess, payload.Error?.Message); Assert.Equal(3, await context.Set<StorefrontServiceRevenue>().CountAsync()); Assert.Equal(3, await context.Orders.CountAsync());
        Assert.Equal(rate == 0 ? 1 : 3, client.Payloads.Count); // Payload generation is not an external issue call.
    }
    internal static AutoInvoiceService Auto(Web06Case c, AppDbContext db, RecordingIssueClient client)
    {
        var tenant = new TenantContext(); tenant.SetStore(c.StoreId, "web06-" + c.StoreId); var unit = new AppUnitOfWork(db);
        var provider = new InvoiceProviderSettingRepository(db, new InvoiceProviderCredentialProtector(Web05Support.AdminProtection(Path.Combine(c.Checkout.Directory, "invoice-keys"))));
        var invoices = Web06FinanceSupport.InvoiceService(c, db); var stock = new InvoiceInputStockRepository(db);
        var issue = new ViettelInvoiceIssueService(new InvoiceRepository(db), provider, new InvoiceCorrectionRepository(db), stock, unit,
            new ViettelInvoicePayloadBuilder(invoices, provider, new InvoiceIntegrationLogRepository(db)), client);
        return new(new AutoInvoiceRepository(db), stock, issue, Web06FinanceSupport.NoExternal<IViettelInvoiceSyncService>(), unit, tenant,
            new Web01Actor(c.Seed.UserId), new Web04Clock { Utc = DateTime.UtcNow.AddMinutes(2) }, provider);
    }
    // Only the provider boundary is replaced. Production queue/grouping, payload,
    // amount/stock validation and persisted issue/outcome services execute.
    internal sealed class RecordingIssueClient : IViettelInvoiceIssueClient
    {
        internal readonly List<(int Head, ViettelInvoicePayloadDto Payload)> Payloads = [];
        public Task<Result<ViettelInvoiceIssueResultDto>> IssueInvoiceAsync(int id, string url, string user, string password, InvoiceProviderAuthMode mode, string taxCode, ViettelInvoicePayloadDto payload, CancellationToken ct = default)
        {
            Assert.Equal("https://invoice.invalid", url.TrimEnd('/')); Payloads.Add((id, payload));
            return Task.FromResult(Result<ViettelInvoiceIssueResultDto>.Success(new() { InvoiceHeadId = id, IsSuccess = true,
                InvoiceNo = "SYNTHETIC-W06-" + id, TransactionId = "SYNTHETIC-" + id, RawResponse = "{\"synthetic\":true}" }));
        }
    }
    private sealed class AcceptedLookupClient(InvoiceHead issued) : IViettelInvoiceLookupClient
    {
        internal int Calls; internal string? ObservedUuid;
        public Task<Result<ViettelInvoiceLookupResultDto>> SearchByTransactionUuidAsync(int id, string url, string username, string password,
            InvoiceProviderAuthMode mode, string taxCode, string uuid, CancellationToken ct = default)
        {
            Calls++; ObservedUuid = uuid; Assert.Equal(issued.Id, id); Assert.Equal(issued.TransactionUuid, uuid); Assert.Equal("https://invoice.invalid", url.TrimEnd('/'));
            return Task.FromResult(Result<ViettelInvoiceLookupResultDto>.Success(new() { InvoiceHeadId = id, IsFound = true, TransactionUuid = uuid,
                InvoiceNo = issued.ProviderInvoiceNo, TransactionId = issued.ProviderTransactionId, IssueDateUtc = issued.IssuedAtUtc,
                CodeOfTax = "SYNTHETIC-CQT-" + id, RawResponse = "{\"synthetic\":true,\"taxAuthorityAccepted\":true}" }));
        }
    }
}
