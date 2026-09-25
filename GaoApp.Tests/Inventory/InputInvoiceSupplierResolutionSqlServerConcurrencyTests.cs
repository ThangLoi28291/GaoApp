using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Inventory;

[Collection("R1FinalDatabasePreflight")]
public sealed class InputInvoiceSupplierResolutionSqlServerConcurrencyTests
{
    [Fact]
    public void Resolution_event_should_be_a_dedicated_append_only_entity()
    {
        typeof(InputInvoiceSupplierResolutionEvent).GetProperty("InputInvoiceHeadId")
            .Should().NotBeNull();
        typeof(InputInvoiceSupplierResolutionEvent).GetProperty("OldSupplierId")
            .Should().NotBeNull();
        typeof(InputInvoiceSupplierResolutionEvent).GetProperty("NewSupplierId")
            .Should().NotBeNull();
        typeof(InputInvoiceSupplierResolutionEvent).GetProperty("Reason")
            .Should().NotBeNull();
    }

    [Fact]
    public async Task Parallel_canonical_bindings_should_commit_one_idempotent_supplier_identity()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        var seed = await SeedAsync(database);

        await using var firstContext = CreateContext(database, seed.StoreId);
        await using var secondContext = CreateContext(database, seed.StoreId);
        var firstService = CreateService(firstContext);
        var secondService = CreateService(secondContext);

        var outcomes = await Task.WhenAll(
            BindAsync(firstService, seed),
            BindAsync(secondService, seed));

        outcomes.Should().AllSatisfy(static outcome => outcome.Should().BeNull());

        await using var verification = CreateContext(database, seed.StoreId);
        var invoice = await verification.InputInvoiceHeads
            .SingleAsync(x => x.Id == seed.InvoiceId);
        invoice.ResolvedSupplierId.Should().Be(seed.FirstSupplierId);
        (await verification.InputInvoiceSupplierResolutionEvents
                .CountAsync(x => x.InputInvoiceHeadId == seed.InvoiceId))
            .Should().Be(1);
        (await verification.StockDocuments
                .Where(x => x.Id == seed.ReceiptId)
                .Select(x => x.SupplierId)
                .SingleAsync())
            .Should().Be(seed.FirstSupplierId);

        var resolutionEvent = await verification.InputInvoiceSupplierResolutionEvents
            .SingleAsync(x => x.InputInvoiceHeadId == seed.InvoiceId);
        resolutionEvent.Reason = "tamper";
        var modify = () => verification.SaveChangesAsync();
        await modify.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");
        verification.ChangeTracker.Clear();
        resolutionEvent = await verification.InputInvoiceSupplierResolutionEvents
            .SingleAsync(x => x.InputInvoiceHeadId == seed.InvoiceId);
        verification.Remove(resolutionEvent);
        var delete = () => verification.SaveChangesAsync();
        await delete.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");
    }

    private static async Task<Exception?> BindAsync(
        InputInvoiceSupplierResolutionService service,
        ResolutionSeed seed)
    {
        try
        {
            await service.BindCanonicalSupplierAsync(
                seed.StoreId,
                seed.ReceiptId,
                seed.InvoiceId);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static InputInvoiceSupplierResolutionService CreateService(
        AppDbContext context)
        => new(
            new InputInvoiceRepository(context),
            new UserStub());

    private static AppDbContext CreateContext(
        PreflightAcceptanceDatabase database,
        int storeId)
        => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(database.ConnectionString)
                .Options,
            new TenantStub(storeId),
            new UserStub());

    private static async Task<ResolutionSeed> SeedAsync(
        PreflightAcceptanceDatabase database)
    {
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var store = new Store
        {
            Name = "Resolution concurrency",
            SubDomain = $"resolution-{Guid.NewGuid():N}",
            SubDomainNormalized = $"RESOLUTION-{Guid.NewGuid():N}",
            IsActive = true
        };
        context.Stores.Add(store);
        await context.SaveChangesAsync();
        var legalEntity = new LegalEntity
        {
            StoreId = store.Id,
            Code = "LE-RES",
            Name = "Legal entity",
            LegalName = "Legal entity",
            IsActive = true
        };
        context.LegalEntities.Add(legalEntity);
        await context.SaveChangesAsync();
        var warehouse = new Warehouse
        {
            StoreId = store.Id,
            LegalEntityId = legalEntity.Id,
            Code = "WH-RES",
            Name = "Warehouse",
            IsActive = true
        };
        var firstSupplier = new Supplier
        {
            StoreId = store.Id,
            Code = "NCC-RES-A",
            Name = "Supplier A",
            TaxCode = "031.277-0607",
            IsActive = true
        };
        var secondSupplier = new Supplier
        {
            StoreId = store.Id,
            Code = "NCC-RES-B",
            Name = "Supplier B",
            TaxCode = "031-277 0607",
            IsActive = true
        };
        context.AddRange(warehouse, firstSupplier, secondSupplier);
        await context.SaveChangesAsync();
        var receipt = new StockDocument
        {
            StoreId = store.Id,
            DocumentNo = "PN-RES-CONCURRENT",
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.PendingApproval,
            ReceiptSource = PurchaseReceiptSource.Direct,
            WarehouseId = warehouse.Id,
            SupplierId = firstSupplier.Id
        };
        var invoice = new InputInvoiceHead
        {
            StoreId = store.Id,
            SellerName = "XML Supplier",
            SellerTaxCode = "0312770607"
        };
        context.AddRange(receipt, invoice);
        await context.SaveChangesAsync();
        context.StockDocumentInputInvoiceMaps.Add(
            new StockDocumentInputInvoiceMap
            {
                StoreId = store.Id,
                StockDocumentId = receipt.Id,
                InputInvoiceHeadId = invoice.Id
            });
        await context.SaveChangesAsync();
        return new ResolutionSeed(
            store.Id,
            receipt.Id,
            invoice.Id,
            firstSupplier.Id,
            secondSupplier.Id);
    }

    private sealed class TenantStub(int storeId) : ITenantContext
    {
        public int? StoreId => storeId;
        public bool IsHostAdmin => false;
        public string? Subdomain => "resolution-concurrency";
    }

    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 602;
        public string? UserName => "resolution-concurrency";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed record ResolutionSeed(
        int StoreId,
        int ReceiptId,
        int InvoiceId,
        int FirstSupplierId,
        int SecondSupplierId);
}
