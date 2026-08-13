using FluentAssertions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Services.Purchases;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptConfirmPrerequisitePolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Missing_or_non_positive_supplier_is_denied(int? supplierId)
    {
        var action = () =>
            PurchaseReceiptConfirmPrerequisitePolicy.EnsureSupplierSelected(supplierId);

        action.Should().Throw<BusinessRuleException>()
            .WithMessage("*chọn nhà cung cấp*");
    }

    [Fact]
    public void Positive_supplier_is_accepted_for_store_scoped_resolution()
    {
        var action = () =>
            PurchaseReceiptConfirmPrerequisitePolicy.EnsureSupplierSelected(1);

        action.Should().NotThrow();
    }

    [Fact]
    public void Policy_has_no_payee_input_or_payee_alternative()
    {
        var method = typeof(PurchaseReceiptConfirmPrerequisitePolicy)
            .GetMethod(nameof(PurchaseReceiptConfirmPrerequisitePolicy.EnsureSupplierSelected));

        method.Should().NotBeNull();
        method!.GetParameters().Should().ContainSingle()
            .Which.Name.Should().Be("supplierId");
    }
}
