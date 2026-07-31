using FluentAssertions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Inventory;

public sealed class InputInvoiceXmlTenantGuardTests
{
    private const int StoreId = 1;
    private const int ReceiptId = 100;
    private const int ReceiptLineId = 101;
    private const int DetailId = 301;

    [Fact]
    public async Task GetInputInvoiceDetailAsync_CrossStoreDetail_ReturnsNull()
    {
        await using var context = CreateContext(out var tenant);
        SeedReceipt(context, StoreId, ReceiptId, ReceiptLineId, warehouseId: 11, legalEntityId: 1);
        SeedInvoice(context, storeId: 2, headId: 200, detailId: DetailId);
        SeedInvoiceLink(context, StoreId, ReceiptId, headId: 200);
        await context.SaveChangesAsync();
        SetTenantStore(context, tenant);

        var repository = new InputInvoiceRepository(context);

        var result = await repository.GetInputInvoiceDetailAsync(
            StoreId,
            ReceiptId,
            ReceiptLineId,
            DetailId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetInputInvoiceDetailAsync_UnlinkedInvoice_ReturnsNull()
    {
        await using var context = CreateContext(out var tenant);
        SeedReceipt(context, StoreId, ReceiptId, ReceiptLineId, warehouseId: 11, legalEntityId: 1);
        SeedInvoice(context, StoreId, headId: 200, detailId: DetailId);
        await context.SaveChangesAsync();
        SetTenantStore(context, tenant);

        var repository = new InputInvoiceRepository(context);

        var result = await repository.GetInputInvoiceDetailAsync(
            StoreId,
            ReceiptId,
            ReceiptLineId,
            DetailId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetInputInvoiceDetailAsync_LineFromDifferentReceipt_ReturnsNull()
    {
        await using var context = CreateContext(out var tenant);
        SeedReceipt(context, StoreId, ReceiptId, ReceiptLineId, warehouseId: 11, legalEntityId: 1);
        SeedReceipt(context, StoreId, documentId: 110, lineId: 111, warehouseId: 11, legalEntityId: 1);
        SeedInvoice(context, StoreId, headId: 200, detailId: DetailId);
        SeedInvoiceLink(context, StoreId, ReceiptId, headId: 200);
        await context.SaveChangesAsync();
        SetTenantStore(context, tenant);

        var repository = new InputInvoiceRepository(context);

        var result = await repository.GetInputInvoiceDetailAsync(
            StoreId,
            ReceiptId,
            stockDocumentLineId: 111,
            inputInvoiceDetailId: DetailId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetInputInvoiceDetailAsync_InvoiceLinkedAcrossLegalEntities_ReturnsNull()
    {
        await using var context = CreateContext(out var tenant);
        SeedReceipt(context, StoreId, ReceiptId, ReceiptLineId, warehouseId: 11, legalEntityId: 1);
        SeedReceipt(context, StoreId, documentId: 110, lineId: 111, warehouseId: 12, legalEntityId: 2);
        SeedInvoice(context, StoreId, headId: 200, detailId: DetailId);
        SeedInvoiceLink(context, StoreId, ReceiptId, headId: 200);
        SeedInvoiceLink(context, StoreId, documentId: 110, headId: 200);
        await context.SaveChangesAsync();
        SetTenantStore(context, tenant);

        var repository = new InputInvoiceRepository(context);

        var result = await repository.GetInputInvoiceDetailAsync(
            StoreId,
            ReceiptId,
            ReceiptLineId,
            DetailId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateLineMapAsync_RejectedDetail_DoesNotMutateMapOrExposeForeignId()
    {
        await using var context = CreateContext(out var tenant);
        SeedReceipt(context, StoreId, ReceiptId, ReceiptLineId, warehouseId: 11, legalEntityId: 1);
        SeedReceipt(context, StoreId, documentId: 110, lineId: 111, warehouseId: 12, legalEntityId: 2);
        SeedInvoice(context, StoreId, headId: 200, detailId: DetailId);
        SeedInvoiceLink(context, StoreId, ReceiptId, headId: 200);
        SeedInvoiceLink(context, StoreId, documentId: 110, headId: 200);
        SeedLineMap(context);
        await context.SaveChangesAsync();
        SetTenantStore(context, tenant);
        var map = await context.StockDocumentLineInputInvoiceMaps.SingleAsync();

        var service = new InputInvoiceXmlService(new InputInvoiceRepository(context));
        var request = new UpdateStockDocumentLineInputInvoiceMapRequest
        {
            StockDocumentLineId = ReceiptLineId,
            UseInputInvoice = true,
            InputInvoiceDetailId = DetailId
        };

        Func<Task> action = () => service.UpdateLineMapAsync(
            StoreId,
            ReceiptId,
            request);

        var exception = await action.Should().ThrowAsync<BusinessRuleException>();
        exception.Which.SafeMessage.Should().Be("Không thể map dòng XML cho phiếu hiện tại.");
        exception.Which.SafeMessage.Should().NotContain(DetailId.ToString());
        map.UseInputInvoice.Should().BeFalse();
        map.InputInvoiceDetailId.Should().BeNull();
        map.MatchStatus.Should().Be(InputInvoiceMatchStatus.Excluded);
        map.QuantityDifference.Should().Be(7m);
        map.AmountDifference.Should().Be(70m);
        map.Note.Should().Be("unchanged");
        context.Entry(map).State.Should().Be(EntityState.Unchanged);
    }

    [Fact]
    public async Task UpdateLineMapAsync_ValidTenantLinkedDetail_UpdatesMap()
    {
        await using var context = CreateContext(out var tenant);
        SeedReceipt(context, StoreId, ReceiptId, ReceiptLineId, warehouseId: 11, legalEntityId: 1);
        SeedReceipt(context, StoreId, documentId: 110, lineId: 111, warehouseId: 13, legalEntityId: 1);
        SeedInvoice(context, StoreId, headId: 200, detailId: DetailId);
        SeedInvoiceLink(context, StoreId, ReceiptId, headId: 200);
        SeedInvoiceLink(context, StoreId, documentId: 110, headId: 200);
        SeedLineMap(context);
        await context.SaveChangesAsync();
        SetTenantStore(context, tenant);
        var map = await context.StockDocumentLineInputInvoiceMaps.SingleAsync();

        var service = new InputInvoiceXmlService(new InputInvoiceRepository(context));
        var request = new UpdateStockDocumentLineInputInvoiceMapRequest
        {
            StockDocumentLineId = ReceiptLineId,
            UseInputInvoice = true,
            InputInvoiceDetailId = DetailId
        };

        await service.UpdateLineMapAsync(StoreId, ReceiptId, request);

        map.UseInputInvoice.Should().BeTrue();
        map.InputInvoiceDetailId.Should().Be(DetailId);
        map.MatchStatus.Should().Be(InputInvoiceMatchStatus.Matched);
        map.QuantityDifference.Should().Be(0m);
        map.AmountDifference.Should().Be(0m);
        map.Note.Should().BeNull();
        context.Entry(map).State.Should().Be(EntityState.Unchanged);
    }

    private static InMemoryAppDbContext CreateContext(out TenantContext tenant)
    {
        tenant = new TenantContext();
        tenant.SetHostAdmin();
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new InMemoryAppDbContext(options, tenant, new TestCurrentUser());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private static void SetTenantStore(
        InMemoryAppDbContext context,
        TenantContext tenant)
    {
        tenant.SetStore(StoreId, "store-one");
        context.ChangeTracker.Clear();
    }

    private static void SeedReceipt(
        InMemoryAppDbContext context,
        int storeId,
        int documentId,
        int lineId,
        int warehouseId,
        int legalEntityId)
    {
        if (!context.Warehouses.Local.Any(x => x.Id == warehouseId))
        {
            context.Warehouses.Add(new Warehouse
            {
                Id = warehouseId,
                StoreId = storeId,
                LegalEntityId = legalEntityId,
                Code = $"WH-{warehouseId}",
                Name = $"Warehouse {warehouseId}"
            });
        }

        context.StockDocuments.Add(new StockDocument
        {
            Id = documentId,
            StoreId = storeId,
            DocumentNo = $"PN-{documentId}",
            Type = StockDocumentType.Receipt,
            WarehouseId = warehouseId
        });
        context.StockDocumentLines.Add(new StockDocumentLine
        {
            Id = lineId,
            StockDocumentId = documentId,
            LineNo = 1,
            ProductVariantId = 1,
            Quantity = 10m,
            Factor = 1m,
            BaseQuantity = 10m,
            UnitCost = 10m,
            LineTotal = 100m,
            ProductNameSnapshot = "Receipt item"
        });
    }

    private static void SeedInvoice(
        InMemoryAppDbContext context,
        int storeId,
        int headId,
        int detailId)
    {
        context.InputInvoiceHeads.Add(new InputInvoiceHead
        {
            Id = headId,
            StoreId = storeId,
            InvoiceNumber = $"HD-{headId}"
        });
        context.InputInvoiceDetails.Add(new InputInvoiceDetail
        {
            Id = detailId,
            InputInvoiceHeadId = headId,
            LineNo = 1,
            ItemName = "XML item",
            Quantity = 10m,
            UnitPrice = 10m,
            LineAmount = 100m
        });
    }

    private static void SeedInvoiceLink(
        InMemoryAppDbContext context,
        int storeId,
        int documentId,
        int headId)
    {
        context.StockDocumentInputInvoiceMaps.Add(new StockDocumentInputInvoiceMap
        {
            StoreId = storeId,
            StockDocumentId = documentId,
            InputInvoiceHeadId = headId
        });
    }

    private static StockDocumentLineInputInvoiceMap SeedLineMap(
        InMemoryAppDbContext context)
    {
        var map = new StockDocumentLineInputInvoiceMap
        {
            StoreId = StoreId,
            StockDocumentId = ReceiptId,
            StockDocumentLineId = ReceiptLineId,
            UseInputInvoice = false,
            MatchStatus = InputInvoiceMatchStatus.Excluded,
            QuantityDifference = 7m,
            AmountDifference = 70m,
            Note = "unchanged"
        };
        context.StockDocumentLineInputInvoiceMaps.Add(map);
        return map;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 1;
        public string? UserName => "r2.0-c1-tenant-guard";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
