using FluentAssertions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace GaoApp.Tests.Inventory;

public sealed class InputInvoiceSupplierResolutionServiceTests
{
    [Fact]
    public async Task Null_canonical_supplier_should_bind_to_receipt_once_without_mutating_receipt()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Seed(resolvedSupplierId: null);
        await fixture.SaveAsync();

        await fixture.Service.BindCanonicalSupplierAsync(1, 10, 20);
        await fixture.Service.BindCanonicalSupplierAsync(1, 10, 20);

        var invoice = await fixture.Context.InputInvoiceHeads.SingleAsync(x => x.Id == 20);
        invoice.ResolvedSupplierId.Should().Be(30);
        invoice.SupplierResolutionStatus.Should().Be(InputInvoiceSupplierResolutionStatus.Resolved);
        (await fixture.Context.StockDocuments.SingleAsync(x => x.Id == 10))
            .SupplierId.Should().Be(30);
        (await fixture.Context.InputInvoiceSupplierResolutionEvents.CountAsync())
            .Should().Be(1);
    }

    [Fact]
    public async Task Same_canonical_supplier_should_reuse_without_semantic_audit()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Seed(resolvedSupplierId: 30);
        await fixture.SaveAsync();

        await fixture.Service.BindCanonicalSupplierAsync(1, 10, 20);

        (await fixture.Context.InputInvoiceSupplierResolutionEvents.CountAsync())
            .Should().Be(0);
    }

    [Fact]
    public async Task Caller_owned_transaction_should_bind_without_nested_commit()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Seed(resolvedSupplierId: null);
        await fixture.SaveAsync();
        var repository = new InputInvoiceRepository(fixture.Context);
        var service = new InputInvoiceSupplierResolutionService(repository, new UserStub());

        await repository.BeginSupplierResolutionTransactionAsync();
        await repository.LockReceiptForInputInvoiceMutationAsync(1, 10);
        await service.BindCanonicalSupplierWithinTransactionAsync(1, 10, 20);
        await repository.CommitSupplierResolutionTransactionAsync();

        (await fixture.Context.InputInvoiceHeads.SingleAsync(x => x.Id == 20))
            .ResolvedSupplierId.Should().Be(30);
    }

    [Fact]
    public async Task Different_canonical_supplier_should_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Seed(resolvedSupplierId: 31);
        await fixture.SaveAsync();

        var action = () => fixture.Service.BindCanonicalSupplierAsync(1, 10, 20);

        await action.Should().ThrowAsync<BusinessRuleException>();
        (await fixture.Context.InputInvoiceHeads.SingleAsync(x => x.Id == 20))
            .ResolvedSupplierId.Should().Be(31);
        (await fixture.Context.StockDocuments.SingleAsync(x => x.Id == 10))
            .SupplierId.Should().Be(30);
    }

    [Fact]
    public async Task Confirm_should_allow_no_invoice_or_same_supplier_and_block_null_or_different()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Seed(resolvedSupplierId: 30, link: false);
        await fixture.SaveAsync();
        await fixture.Service.EnsureReceiptCanBeConfirmedAsync(1, 10, 30);

        fixture.Context.StockDocumentInputInvoiceMaps.Add(new StockDocumentInputInvoiceMap
        {
            StoreId = 1, StockDocumentId = 10, InputInvoiceHeadId = 20
        });
        await fixture.Context.SaveChangesAsync();
        await fixture.Service.EnsureReceiptCanBeConfirmedAsync(1, 10, 30);

        var invoice = await fixture.Context.InputInvoiceHeads.SingleAsync(x => x.Id == 20);
        invoice.ResolvedSupplierId = null;
        await fixture.Context.SaveChangesAsync();
        var nullAction = () => fixture.Service.EnsureReceiptCanBeConfirmedAsync(1, 10, 30);
        await nullAction.Should().ThrowAsync<BusinessRuleException>();

        invoice.ResolvedSupplierId = 31;
        await fixture.Context.SaveChangesAsync();
        var differentAction = () => fixture.Service.EnsureReceiptCanBeConfirmedAsync(1, 10, 30);
        await differentAction.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Library_import_should_be_idempotent_support_many_receipts_and_not_mutate_confirmed_posting_values()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Seed(resolvedSupplierId: null, link: false);
        var firstReceipt = fixture.Context.StockDocuments.Local.Single(x => x.Id == 10);
        firstReceipt.Status = StockDocumentStatus.Confirmed;
        fixture.Context.StockDocumentLines.Add(new StockDocumentLine
        {
            Id = 101, StockDocumentId = 10, LineNo = 1,
            ProductVariantId = 1, ProductNameSnapshot = "Rice",
            Quantity = 3m, Factor = 1m, BaseQuantity = 3m,
            UnitCost = 120m, LineTotal = 360m
        });
        fixture.Context.StockDocuments.Add(new StockDocument
        {
            Id = 11, StoreId = 1, DocumentNo = "PN-11",
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.PendingApproval,
            ReceiptSource = PurchaseReceiptSource.Direct,
            WarehouseId = 3, SupplierId = 30
        });
        fixture.Context.StockDocumentLines.Add(new StockDocumentLine
        {
            Id = 111, StockDocumentId = 11, LineNo = 1,
            ProductVariantId = 1, ProductNameSnapshot = "Rice",
            Quantity = 2m, Factor = 1m, BaseQuantity = 2m,
            UnitCost = 110m, LineTotal = 220m
        });
        await fixture.SaveAsync();

        var repository = new InputInvoiceRepository(fixture.Context);
        var canonical = new InputInvoiceSupplierResolutionService(repository, new UserStub());
        var xml = new InputInvoiceXmlService(
            repository, canonical, new InputInvoiceXmlDocumentParser());
        var bytes = Encoding.UTF8.GetBytes("""
            <HDon><DLHDon><TTChung><KHMSHDon>1</KHMSHDon><KHHDon>C26MVP</KHHDon><SHDon>9001</SHDon><NLap>2026-08-22</NLap></TTChung><NDHDon><NBan><Ten>Supplier</Ten><MST>0312770607</MST></NBan><NMua><Ten>Buyer</Ten></NMua><DSHHDVu><HHDVu><STT>1</STT><THHDVu>Rice</THHDVu><DVTinh>kg</DVTinh><SLuong>3</SLuong><DGia>120</DGia><ThTien>360</ThTien><TSuat>8%</TSuat></HHDVu></DSHHDVu><TToan><TgTCThue>360</TgTCThue><TgTThue>28.8</TgTThue><TgTTTBSo>388.8</TgTTTBSo></TToan></NDHDon></DLHDon></HDon>
            """);

        var first = await xml.ImportAndLinkAsync(1, 10, 30, "0312770607", "invoice.xml", bytes);
        var repeat = await xml.ImportAndLinkAsync(1, 10, 30, "0312770607", "invoice.xml", bytes);
        var otherReceipt = await xml.ImportAndLinkAsync(1, 11, 30, "0312770607", "invoice.xml", bytes);

        repeat.InputInvoiceHeadId.Should().Be(first.InputInvoiceHeadId);
        repeat.WasAlreadyLinked.Should().BeTrue();
        otherReceipt.InputInvoiceHeadId.Should().Be(first.InputInvoiceHeadId);
        (await fixture.Context.StockDocumentInputInvoiceMaps.CountAsync()).Should().Be(2);
        (await fixture.Context.StockDocumentLineInputInvoiceMaps.CountAsync()).Should().Be(2);
        (await fixture.Context.InputInvoiceSupplierResolutionEvents.CountAsync()).Should().Be(1);
        (await fixture.Context.PurchaseReceiptAuditEvents
            .CountAsync(x => x.EventType == PurchaseReceiptAuditEventType.InputInvoiceLinked))
            .Should().Be(2);
        var persistedInvoice = await fixture.Context.InputInvoiceHeads
            .SingleAsync(x => x.Id == first.InputInvoiceHeadId);
        persistedInvoice.ResolvedSupplierId.Should().Be(30);
        var confirmedLine = await fixture.Context.StockDocumentLines.SingleAsync(x => x.Id == 101);
        confirmedLine.Quantity.Should().Be(3m);
        confirmedLine.UnitCost.Should().Be(120m);
        confirmedLine.LineTotal.Should().Be(360m);
        (await fixture.Context.StockDocuments.SingleAsync(x => x.Id == 10))
            .SupplierId.Should().Be(30);
        (await fixture.Context.StockDocuments.SingleAsync(x => x.Id == 10))
            .Status.Should().Be(StockDocumentStatus.Confirmed);
        (await fixture.Context.InventoryTransactions.CountAsync()).Should().Be(0);
        (await fixture.Context.InventoryBalances.CountAsync()).Should().Be(0);
        (await fixture.Context.InventoryValuationEntries.CountAsync()).Should().Be(0);
        (await fixture.Context.InventoryCostLayers.CountAsync()).Should().Be(0);
        (await fixture.Context.InventoryCostLayerAllocations.CountAsync()).Should().Be(0);
        (await fixture.Context.PurchasePayables.CountAsync()).Should().Be(0);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TenantContext _tenant;
        private Fixture(InMemoryAppDbContext context, TenantContext tenant)
        {
            Context = context;
            _tenant = tenant;
            Service = new InputInvoiceSupplierResolutionService(
                new InputInvoiceRepository(context), new UserStub());
        }

        public InMemoryAppDbContext Context { get; }
        public InputInvoiceSupplierResolutionService Service { get; }

        public static Task<Fixture> CreateAsync()
        {
            var tenant = new TenantContext();
            tenant.SetHostAdmin();
            var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            return Task.FromResult(new Fixture(
                new InMemoryAppDbContext(options, tenant, new UserStub()), tenant));
        }

        public void Seed(int? resolvedSupplierId, bool link = true)
        {
            Context.Stores.Add(new Store
            {
                Id = 1, Name = "Store", SubDomain = "store",
                SubDomainNormalized = "STORE", IsActive = true
            });
            Context.LegalEntities.Add(new LegalEntity
            {
                Id = 2, StoreId = 1, Code = "LE", Name = "LE",
                LegalName = "LE", IsActive = true
            });
            Context.Warehouses.Add(new Warehouse
            {
                Id = 3, StoreId = 1, LegalEntityId = 2,
                Code = "WH", Name = "WH", IsActive = true
            });
            Context.Suppliers.AddRange(
                new Supplier { Id = 30, StoreId = 1, Code = "S30", Name = "S30", TaxCode = "0312770607", IsActive = true },
                new Supplier { Id = 31, StoreId = 1, Code = "S31", Name = "S31", TaxCode = "0319999999", IsActive = true });
            Context.StockDocuments.Add(new StockDocument
            {
                Id = 10, StoreId = 1, DocumentNo = "PN-10",
                Type = StockDocumentType.Receipt,
                Status = StockDocumentStatus.PendingApproval,
                ReceiptSource = PurchaseReceiptSource.Direct,
                WarehouseId = 3, SupplierId = 30
            });
            Context.InputInvoiceHeads.Add(new InputInvoiceHead
            {
                Id = 20, StoreId = 1, SellerTaxCode = "0312770607",
                ResolvedSupplierId = resolvedSupplierId,
                SupplierResolutionStatus = resolvedSupplierId.HasValue
                    ? InputInvoiceSupplierResolutionStatus.Resolved
                    : InputInvoiceSupplierResolutionStatus.NotEvaluated
            });
            if (link)
                Context.StockDocumentInputInvoiceMaps.Add(new StockDocumentInputInvoiceMap
                {
                    StoreId = 1, StockDocumentId = 10, InputInvoiceHeadId = 20
                });
        }

        public async Task SaveAsync()
        {
            await Context.SaveChangesAsync();
            _tenant.SetStore(1, "store");
            Context.ChangeTracker.Clear();
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 602;
        public string? UserName => "picker-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
