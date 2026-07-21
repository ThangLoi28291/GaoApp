using FluentAssertions;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

public sealed class ProvisionalPurchaseItemContractTests
{
    [Fact]
    public void Existing_clients_should_default_purchase_items_to_catalog()
    {
        new PurchaseRequestLineInputDto().ItemKind.Should().Be(PurchaseItemKind.Catalog);
        new PurchaseOrderLineInputDto().ItemKind.Should().Be(PurchaseItemKind.Catalog);
    }

    [Fact]
    public void Free_text_request_line_should_not_require_catalog_foreign_keys()
    {
        var line = new PurchaseRequestLine
        {
            ItemKind = PurchaseItemKind.FreeText,
            ProductNameSnapshot = "Mặt hàng mới",
            UnitNameSnapshot = "Thùng 24 chai",
            ConversionFactor = 1m,
            RequestedQuantity = 2m
        };

        line.ProductVariantId.Should().BeNull();
        line.ProductUnitConversionId.Should().BeNull();
        line.UnitId.Should().BeNull();
    }

    [Fact]
    public void Free_text_order_line_should_preserve_origin_after_catalog_resolution()
    {
        var line = new PurchaseOrderLine
        {
            ItemKind = PurchaseItemKind.FreeText,
            ProductNameSnapshot = "Tên đã đặt",
            UnitNameSnapshot = "Cái",
            ProductVariantId = 10,
            ProductUnitConversionId = 20,
            UnitId = 30,
            ResolvedAtUtc = DateTime.UtcNow,
            ResolvedByUserId = 99
        };

        line.ItemKind.Should().Be(PurchaseItemKind.FreeText);
        line.ResolvedAtUtc.Should().NotBeNull();
        line.ResolvedByUserId.Should().Be(99);
    }

    [Fact]
    public void Normal_products_should_remain_sellable_by_default()
        => new Product().IsSellable.Should().BeTrue();
}
