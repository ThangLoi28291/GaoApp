using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06ModelTests(Web01Fixture fixture)
{
    public static TheoryData<Type> NativeRecords => new()
    {
        typeof(StorefrontFulfillmentWork), typeof(StorefrontReservationTransfer), typeof(StorefrontFulfillmentEvidence), typeof(StorefrontCustodyIncident),
        typeof(StorefrontServiceRevenueSetting), typeof(StorefrontServiceRevenue), typeof(StorefrontSaleBinding), typeof(StorefrontServiceFunding),
        typeof(StorefrontInvoiceWork), typeof(StorefrontCodCollection), typeof(StorefrontCodBankRouting), typeof(StorefrontCodReceipt),
        typeof(StorefrontCarrierWithholding), typeof(StorefrontNotificationAttestation), typeof(StorefrontNotificationCertification), typeof(StorefrontInventoryIssueSource)
    };
    [Theory, MemberData(nameof(NativeRecords))]
    public async Task Every_native_record_has_real_store_rowversion_and_no_shadow_store_identity(Type type)
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context(); var entity = db.Model.FindEntityType(type); Assert.NotNull(entity);
        var store = entity.FindProperty("StoreId"); Assert.NotNull(store); Assert.False(store.IsNullable); Assert.Equal(typeof(int), store.ClrType);
        var version = entity.FindProperty("RowVersion"); Assert.NotNull(version); Assert.True(version.IsConcurrencyToken);
        Assert.DoesNotContain(entity.GetProperties(), x => x.Name.StartsWith("StoreId", StringComparison.Ordinal) && x.Name != "StoreId");
        Assert.NotEmpty(entity.GetDeclaredQueryFilters());
        Assert.Contains(entity.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(Store) && key.Properties.Single().Name == "StoreId");
        foreach (var key in entity.GetForeignKeys().Where(x => x.PrincipalEntityType.FindProperty("StoreId") is not null))
            Assert.Contains(key.Properties, p => p.Name == "StoreId");
    }
    [Fact]
    public async Task Native_delivery_nullable_POS_origin_is_typed_and_original_unit_precision_preserves_fifteen_integer_digits()
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context(); var delivery = db.Model.FindEntityType(typeof(DeliveryOrder))!;
        Assert.True(delivery.FindProperty(nameof(DeliveryOrder.SourceCartId))!.IsNullable);
        Assert.True(delivery.FindProperty(nameof(DeliveryOrder.CreatedShiftId))!.IsNullable);
        Assert.True(delivery.FindProperty(nameof(DeliveryOrder.CreatedTerminalId))!.IsNullable);
        Assert.Equal(0, (int)DeliverySourceKind.Pos); Assert.Equal(1, (int)DeliverySourceKind.Storefront);
        Assert.True(delivery.FindProperty(nameof(DeliveryOrder.StorefrontOrderId))!.IsNullable);
        var nativeSource = Assert.Single(delivery.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(StorefrontFulfillmentSource));
        Assert.Equal(new[] { "StoreId", "StorefrontOrderId", "StorefrontFulfillmentSourceId" }, nativeSource.Properties.Select(x => x.Name));
        Assert.Equal(new[] { "StoreId", "StorefrontOrderId", "Id" }, nativeSource.PrincipalKey.Properties.Select(x => x.Name));
        var source = nativeSource.PrincipalEntityType;
        Assert.Contains(source.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(StorefrontOrder) && key.Properties.Select(x => x.Name).SequenceEqual(new[] { "StoreId", "StorefrontOrderId" }));
        Assert.Contains(source.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(DeliveryOrder) && key.Properties.Select(x => x.Name).SequenceEqual(new[] { "StoreId", "DeliveryOrderId", "SourceWarehouseId", "SourceLegalEntityId" }));
        Assert.Contains(source.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(Warehouse) && key.Properties.Select(x => x.Name).SequenceEqual(new[] { "StoreId", "SourceWarehouseId", "SourceLegalEntityId" }));
        foreach (var (type, name) in new[] { (typeof(InvoiceDetail), "Quantity"), (typeof(InvoiceHead), "TotalQuantity") })
        {
            var property = db.Model.FindEntityType(type)!.FindProperty(name)!; Assert.Equal(19, property.GetPrecision()); Assert.Equal(4, property.GetScale());
            Assert.Equal(15, property.GetPrecision() - property.GetScale());
        }
    }
}
