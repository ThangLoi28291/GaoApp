using GaoApp.Domain.Delivery;

namespace GaoApp.Tests.Delivery;

[Trait("Category", "DeliveryD08")]
public sealed class DeliveryD08PolicyTests
{
    [Fact]
    public void Quantities_include_prior_receipts_and_conserve_export_without_counting_return_twice()
    {
        DeliveryHandoverPolicy.Quantities([new(1, 4, .5m, 2.5m, 1.5m), new(2, 3, 0, 0, 3)]);
        foreach (var line in new[] { new DeliveryHandoverQuantity(1, 4, 1, 3.5m, .5m),
            new(1, 4, 0, 3, .5m), new(1, 4, 0, 5, 0), new(1, 4, 0, -1, 5) })
            Assert.Throws<DeliveryRuleException>(() => DeliveryHandoverPolicy.Quantities([line]));
        Assert.Equal("LINE_INVALID", Assert.Throws<DeliveryRuleException>(() => DeliveryHandoverPolicy.Quantities([new(1, 4, 0, 4, 0), new(1, 4, 0, 4, 0)])).Code);
        Assert.Equal("GOODS_UNRECONCILED", Assert.Throws<DeliveryRuleException>(() => DeliveryHandoverPolicy.Quantities([new(1, 4, 1, 4, 0)])).Code);
    }

    [Fact]
    public void Saved_price_allocation_has_exact_net_rounding_free_gifts_and_no_zero_sale_repricing()
    {
        var full = DeliveryHandoverPolicy.Charge(3, 3, 494000, 435667);
        Assert.Equal(new DeliveryHandoverCharge(494000, 58333, 435667), full);
        var partial = DeliveryHandoverPolicy.Charge(3, 1, 494000, 435667);
        Assert.Equal(new DeliveryHandoverCharge(164667, 19445, 145222), partial);
        Assert.Equal(new DeliveryHandoverCharge(10000, 10000, 0), DeliveryHandoverPolicy.Charge(1, 1, 10000, 0));
        Assert.Equal(new DeliveryHandoverCharge(0, 0, 0), DeliveryHandoverPolicy.Charge(3, 0, 494000, 435667));
        Assert.Equal("PRICE_SNAPSHOT_INVALID", Assert.Throws<DeliveryRuleException>(() => DeliveryHandoverPolicy.Charge(1, 1, 100, 101)).Code);
        Assert.Equal("PRICE_SNAPSHOT_INVALID", Assert.Throws<DeliveryRuleException>(() => DeliveryHandoverPolicy.Charge(1, 2, 100, 90)).Code);
    }

    [Fact]
    public void Exact_base_quantity_survives_nonrepresentable_sale_units_and_payment_names_are_strict()
    {
        Assert.Equal((.0001m, 1m, true), DeliveryHandoverPolicy.SaleQuantity(.0001m, 6));
        Assert.Equal((.5m, 6m, false), DeliveryHandoverPolicy.SaleQuantity(3, 6));
        Assert.Equal(DeliverySettlementMethod.Cash, DeliveryHandoverPolicy.Method("Cash"));
        Assert.Equal(DeliverySettlementMethod.ConfirmedBankTransfer, DeliveryHandoverPolicy.Method("BankTransfer"));
        Assert.Equal(DeliverySettlementMethod.CustomerCredit, DeliveryHandoverPolicy.Method("CustomerCredit"));
        foreach (var value in new[] { "cash", " Cash", "Mixed", "Advance", "" })
            Assert.Equal("PAYMENT_METHOD_INVALID", Assert.Throws<DeliveryRuleException>(() => DeliveryHandoverPolicy.Method(value)).Code);
        Assert.Equal("QUANTITY_INVALID", Assert.Throws<DeliveryRuleException>(() => DeliveryHandoverPolicy.SaleQuantity(1, 0)).Code);
    }
}
