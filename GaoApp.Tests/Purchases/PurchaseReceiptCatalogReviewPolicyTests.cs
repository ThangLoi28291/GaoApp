using FluentAssertions;
using GaoApp.Application.Services.Inventory;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptCatalogReviewPolicyTests
{
    [Fact]
    public void Official_linked_variant_replaces_legacy_snapshot_without_review()
    {
        var result = PurchaseReceiptCatalogReviewPolicy.Resolve(
            "Sản phẩm mới (6975495592801)",
            "Nước giặt OMO Matic 3,6 kg",
            "Nước giặt OMO");

        result.DisplayName.Should().Be("Nước giặt OMO Matic 3,6 kg");
        result.RequiresReview.Should().BeFalse();
        result.UsesLegacySnapshot.Should().BeTrue();
    }

    [Fact]
    public void Official_product_name_is_fallback_when_variant_name_is_still_temporary()
    {
        var result = PurchaseReceiptCatalogReviewPolicy.Resolve(
            "Sản phẩm mới (6975495592801)",
            "Sản phẩm mới (6975495592801)",
            "Nước giặt OMO");

        result.DisplayName.Should().Be("Nước giặt OMO");
        result.RequiresReview.Should().BeFalse();
    }

    [Fact]
    public void Temporary_product_and_variant_require_manager_review()
    {
        var result = PurchaseReceiptCatalogReviewPolicy.Resolve(
            "Sản phẩm mới (6975495592801)",
            "Sản phẩm mới (6975495592801)",
            "Sản phẩm mới (6975495592801)");

        result.DisplayName.Should().Be("Sản phẩm mới (6975495592801)");
        result.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void Official_snapshot_remains_usable_when_old_catalog_names_are_empty()
    {
        var result = PurchaseReceiptCatalogReviewPolicy.Resolve(
            "Ấm siêu tốc Cuckoo ST17",
            null,
            null);

        result.DisplayName.Should().Be("Ấm siêu tốc Cuckoo ST17");
        result.RequiresReview.Should().BeFalse();
    }
}
