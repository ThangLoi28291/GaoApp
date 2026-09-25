using System.Reflection;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Inventory;

public sealed class InputInvoicePostConfirmLifecycleTests
{
    [Fact]
    public void B3_contract_is_additive_and_represents_waiting_and_stale_association_outcomes()
    {
        var assembly = typeof(InputInvoicePickerContextDto).Assembly;
        var context = assembly.GetType(
            "GaoApp.Application.DTOs.Inventory.InputInvoices.InputInvoiceAssociationContextDto");
        var mutation = assembly.GetType(
            "GaoApp.Application.DTOs.Inventory.InputInvoices.InputInvoiceAssociationMutationResultDto");

        context.Should().NotBeNull();
        context!.GetProperty("LifecycleState").Should().NotBeNull();
        context.GetProperty("IsWaitingXml").Should().NotBeNull();
        context.GetProperty("CurrentInputInvoiceHeadId").Should().NotBeNull();
        mutation.Should().NotBeNull();
        mutation!.GetProperty("Outcome").Should().NotBeNull();

        typeof(IInputInvoicePickerService).GetMethods().Select(x => x.Name)
            .Should().Contain(["GetAssociationContextAsync", "RelinkAsync"]);
    }

    [Fact]
    public async Task Draft_AddLine_uses_the_shared_runtime_context_without_entering_the_late_association_guard()
    {
        await using var db = CreateDbContext();
        var receipt = new StockDocument
        {
            Id = 10,
            StoreId = 1,
            DocumentNo = "B3-RED-001",
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.Draft,
            WarehouseId = 1,
            HasVat = false,
            RowVersion = [1]
        };
        db.StockDocuments.Add(receipt);
        await db.SaveChangesAsync();

        var product = new Product { Id = 20, StoreId = 1, Name = "B3 product" };
        var variant = new ProductVariant
        {
            Id = 21,
            StoreId = 1,
            ProductId = product.Id,
            Product = product,
            Sku = "B3-SKU",
            ProductVariantName = "B3 variant"
        };
        var conversion = new ProductUnitConversion
        {
            Id = 22,
            StoreId = 1,
            ProductVariantId = variant.Id,
            ProductVariant = variant,
            UnitId = 23,
            Factor = 1m,
            IsBaseUnit = true
        };
        variant.UnitConversions.Add(conversion);

        var inputInvoiceRepository = new InputInvoiceRepository(db);
        var reconciliation = new InputInvoiceReconciliationService(
            inputInvoiceRepository,
            Unused<IInputInvoiceItemCatalogMappingService>(),
            new CurrentUserStub());
        var stockRepository = Proxy<IStockDocumentRepository>((method, _) => method.Name switch
        {
            nameof(IStockDocumentRepository.GetDetailAsync) =>
                Task.FromResult<StockDocument?>(receipt),
            nameof(IStockDocumentRepository.GetVariantForStockDocumentAsync) =>
                Task.FromResult<ProductVariant?>(variant),
            nameof(IStockDocumentRepository.GetConversionAsync) =>
                Task.FromResult<ProductUnitConversion?>(conversion),
            nameof(IStockDocumentRepository.GetNextLineNoAsync) => Task.FromResult(1),
            nameof(IStockDocumentRepository.SaveChangesAsync) => db.SaveChangesAsync(),
            _ => throw new InvalidOperationException(method.Name)
        });
        var unitResolver = Proxy<IInventoryUnitResolver>((method, _) => method.Name switch
        {
            nameof(IInventoryUnitResolver.ResolveAsync) =>
                Task.FromResult((conversion.UnitId, (string?)"unit", conversion.Factor)),
            _ => throw new InvalidOperationException(method.Name)
        });
        var service = new StockDocumentService(
            stockRepository,
            Unused<GaoApp.Application.Interfaces.Repositories.LegalEntities.ILegalEntityRepository>(),
            Unused<IWarehouseRepository>(),
            Unused<IBarcodeLookupService>(),
            unitResolver,
            Unused<IInventoryMovementService>(),
            Unused<IInventoryMovementFactory>(),
            Unused<IInventoryRevaluationService>(),
            Unused<IDocumentNumberSequenceRepository>(),
            new TenantContextStub(),
            Unused<IInventoryValuationEntryRepository>(),
            new CurrentUserStub(),
            inputInvoiceReconciliationService: reconciliation);

        var lineId = await service.AddLineAsync(receipt.Id,
            new AddStockDocumentLineRequest
            {
                ProductVariantId = variant.Id,
                UnitId = conversion.UnitId,
                Quantity = 2m,
                UnitCost = 100m
            });

        lineId.Should().BeGreaterThan(0);
        var persisted = await db.StockDocumentLines.SingleAsync();
        persisted.StockDocumentId.Should().Be(receipt.Id);
        persisted.Quantity.Should().Be(2m);
        receipt.Status.Should().Be(StockDocumentStatus.Draft);
    }

    [Fact]
    public async Task Explicit_late_association_transaction_still_rejects_posted_receipt_mutation()
    {
        await using var db = CreateDbContext();
        var receipt = new StockDocument
        {
            Id = 11,
            StoreId = 1,
            DocumentNo = "B3-SAFETY-001",
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.Confirmed,
            WarehouseId = 1,
            RowVersion = [1]
        };
        db.StockDocuments.Add(receipt);
        await db.SaveChangesAsync();
        receipt.DocumentTitle = "forbidden late mutation";

        var repository = new InputInvoiceRepository(db);
        await repository.BeginSupplierResolutionTransactionAsync();
        try
        {
            var action = () => repository.SaveChangesAsync();

            var error = await Assert.ThrowsAsync<BusinessRuleException>(action);
            error.Message.Should().Contain("Late XML association cannot mutate posted receipt effects")
                .And.Contain(nameof(StockDocument));
        }
        finally
        {
            await repository.RollbackSupplierResolutionTransactionAsync();
        }
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(
                $"b3-mutation-boundary-{Guid.NewGuid():N}",
                database => database.EnableNullChecks(false))
            .Options;
        return new AppDbContext(options, new TenantContextStub(), new CurrentUserStub());
    }

    private static T Unused<T>() where T : class
        => Proxy<T>((method, _) => throw new InvalidOperationException(method.Name));

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, HandlerProxy>();
        ((HandlerProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class HandlerProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler(targetMethod!, args);
    }

    private sealed class TenantContextStub : ITenantContext
    {
        public int? StoreId => 1;
        public bool IsHostAdmin => false;
        public string? Subdomain => "b3-test";
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public int? UserId => 7;
        public string? UserName => "b3-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
