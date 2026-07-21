using FluentAssertions;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Inventory;

public sealed class StockReceiptLegalEntityTests
{
    [Fact]
    public void BuildFormOptions_ShouldDefaultToPurchaseLegalEntityAndItsWarehouse()
    {
        var hkd1 = CreateLegalEntity(1, "HKD1", priority: 1, isPurchaseDefault: false, warehouseId: 11);
        var hkd2 = CreateLegalEntity(2, "HKD2", priority: 2, isPurchaseDefault: true, warehouseId: 22);

        var result = StockReceiptLegalEntityPolicy.BuildFormOptions(new[] { hkd1, hkd2 });

        result.DefaultLegalEntityId.Should().Be(2);
        result.LegalEntities.Single(x => x.Id == 2).DefaultWarehouseId.Should().Be(22);
        result.Warehouses.Single(x => x.Id == 22).LegalEntityId.Should().Be(2);
        var validateDefault = () => StockReceiptLegalEntityPolicy.EnsureWarehouseSelectable(
            hkd2.Id,
            hkd2.Warehouses.Single());
        validateDefault.Should().NotThrow();
    }

    [Fact]
    public void BuildFormOptions_ShouldExposeEachLegalEntityDefaultForClientAutoSwitch()
    {
        var hkd1 = CreateLegalEntity(1, "HKD1", priority: 1, isPurchaseDefault: false, warehouseId: 11);
        var hkd2 = CreateLegalEntity(2, "HKD2", priority: 2, isPurchaseDefault: true, warehouseId: 22);

        var result = StockReceiptLegalEntityPolicy.BuildFormOptions(new[] { hkd1, hkd2 });

        result.LegalEntities.Single(x => x.Id == 1).DefaultWarehouseId.Should().Be(11);
        result.Warehouses.Where(x => x.LegalEntityId == 1)
            .Select(x => x.Id)
            .Should().Equal(11);
        result.LegalEntities.Single(x => x.Id == 2).DefaultWarehouseId.Should().Be(22);
        result.Warehouses.Where(x => x.LegalEntityId == 2)
            .Select(x => x.Id)
            .Should().Equal(22);
    }

    [Fact]
    public void BuildFormOptions_ShouldHideInactiveEntitiesAndWarehouses()
    {
        var active = CreateLegalEntity(1, "HKD1", priority: 1, isPurchaseDefault: true, warehouseId: 11);
        active.Warehouses.Add(new Warehouse
        {
            Id = 12,
            LegalEntityId = active.Id,
            LegalEntity = active,
            Code = "K12",
            Name = "Kho ngừng hoạt động",
            IsActive = false
        });
        var inactive = CreateLegalEntity(2, "HKD2", priority: 2, isPurchaseDefault: false, warehouseId: 22);
        inactive.IsActive = false;

        var result = StockReceiptLegalEntityPolicy.BuildFormOptions(new[] { active, inactive });

        result.LegalEntities.Select(x => x.Id).Should().Equal(1);
        result.Warehouses.Select(x => x.Id).Should().Equal(11);
    }

    [Fact]
    public void EnsureWarehouseSelectable_ShouldRejectWarehouseOwnedByAnotherLegalEntity()
    {
        var hkd2 = CreateLegalEntity(2, "HKD2", priority: 2, isPurchaseDefault: true, warehouseId: 22);
        var warehouse = hkd2.Warehouses.Single();

        var action = () => StockReceiptLegalEntityPolicy.EnsureWarehouseSelectable(1, warehouse);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*không thuộc HKD*");
    }

    [Fact]
    public void EnsureWarehouseSelectable_ShouldRejectInactiveOwnerBeforePosting()
    {
        var hkd = CreateLegalEntity(1, "HKD1", priority: 1, isPurchaseDefault: true, warehouseId: 11);
        hkd.IsActive = false;

        var action = () => StockReceiptLegalEntityPolicy.EnsureWarehouseSelectable(
            hkd.Id,
            hkd.Warehouses.Single());

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*HKD sở hữu kho đã ngừng hoạt động*");
    }

    [Fact]
    public void PurchaseMovement_ShouldUseSelectedWarehouseAndPreserveReceiptMath()
    {
        const int selectedWarehouseId = 22;
        const decimal quantity = 3m;
        const decimal factor = 4m;
        const decimal unitCost = 28_000m;
        var baseQuantity = quantity * factor;
        var lineTotal = quantity * unitCost;
        var baseUnitCost = lineTotal / baseQuantity;

        var movement = new InventoryMovementFactory().CreatePurchaseReceipt(
            selectedWarehouseId,
            productVariantId: 101,
            qtyBase: baseQuantity,
            unitCost: baseUnitCost,
            documentId: "500",
            lineId: 501,
            documentNo: "NK-UAT",
            lineNo: 1);

        baseQuantity.Should().Be(12m);
        lineTotal.Should().Be(84_000m);
        movement.WarehouseId.Should().Be(selectedWarehouseId);
        movement.QuantityChange.Should().Be(baseQuantity);
        movement.UnitCost.Should().Be(7_000m);
        movement.TransactionType.Should().Be(InventoryTransactionType.PurchaseReceipt);
    }

    private static LegalEntity CreateLegalEntity(
        int id,
        string code,
        int priority,
        bool isPurchaseDefault,
        int warehouseId)
    {
        var legalEntity = new LegalEntity
        {
            Id = id,
            Code = code,
            Name = code,
            LegalName = code,
            SalePriority = priority,
            IsDefaultForPurchase = isPurchaseDefault,
            IsActive = true,
            DefaultWarehouseId = warehouseId
        };

        legalEntity.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            LegalEntityId = id,
            LegalEntity = legalEntity,
            Code = $"K{warehouseId}",
            Name = $"Kho {code}",
            IsActive = true
        });

        return legalEntity;
    }
}
