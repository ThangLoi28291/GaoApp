using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Enums;
using FluentAssertions;

namespace GaoApp.Tests.Inventory;

public sealed class InventoryIdempotencyKeyFactoryTests
{
    [Fact]
    public void Canonical_v1_should_match_golden_sha256_vector()
    {
        InventoryIdempotencyKeyFactory.Version.Should().Be(1);

        var key = InventoryIdempotencyKeyFactory.Create(
            7,
            11,
            13,
            (InventoryTransactionType)3,
            (InventoryReferenceType)5,
            "REF-é",
            17,
            "part-A");

        key.Should().HaveCount(InventoryIdempotencyKeyFactory.KeyLength);
        Convert.ToHexString(key).Should().Be(
            "5577BA73782C2C4E423D7C808BCE3E1B47156163C0DF439BD3EDBB8B8A9F0D89");
    }

    [Fact]
    public void Canonical_v1_should_normalize_strings_to_unicode_nfc()
    {
        var composed = CreateRequest("REF-é", "part-é");
        var decomposed = CreateRequest(
            "REF-e\u0301",
            "part-e\u0301");

        InventoryIdempotencyKeyFactory.Create(9, composed)
            .Should().Equal(
                InventoryIdempotencyKeyFactory.Create(9, decomposed));
    }

    [Fact]
    public void Canonical_v1_should_keep_null_and_empty_subkeys_distinct()
    {
        var nullSubkey = CreateRequest("REF-1", null);
        var emptySubkey = CreateRequest("REF-1", string.Empty);

        InventoryIdempotencyKeyFactory.Create(9, nullSubkey)
            .Should().NotEqual(
                InventoryIdempotencyKeyFactory.Create(9, emptySubkey));
    }

    [Fact]
    public void Canonical_v1_should_keep_null_and_present_line_ids_distinct()
    {
        var nullLine = CreateRequest("REF-1", "PART-1");
        nullLine.ReferenceLineId = null;
        var presentLine = CreateRequest("REF-1", "PART-1");

        InventoryIdempotencyKeyFactory.Create(9, nullLine)
            .Should().NotEqual(
                InventoryIdempotencyKeyFactory.Create(
                    9,
                    presentLine));
    }

    [Fact]
    public void Canonical_v1_should_preserve_case()
    {
        var upper = CreateRequest("REF-ABC", "PART");
        var lower = CreateRequest("ref-abc", "part");

        InventoryIdempotencyKeyFactory.Create(9, upper)
            .Should().NotEqual(
                InventoryIdempotencyKeyFactory.Create(9, lower));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" ", null)]
    [InlineData(" REF", null)]
    [InlineData("REF ", null)]
    [InlineData("REF", " PART")]
    [InlineData("REF", "PART ")]
    public void Canonical_v1_should_reject_invalid_reference_boundaries(
        string? referenceId,
        string? subkey)
    {
        var request = CreateRequest(referenceId, subkey);

        var action = () =>
            InventoryIdempotencyKeyFactory.Create(9, request);

        action.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void Canonical_v1_should_include_every_identity_field()
    {
        var baseline = CreateRequest("REF-1", "PART-1");
        var baselineKey =
            InventoryIdempotencyKeyFactory.Create(9, baseline);

        var warehouseVariant = CreateRequest("REF-1", "PART-1");
        warehouseVariant.WarehouseId = 12;
        var productVariant = CreateRequest("REF-1", "PART-1");
        productVariant.ProductVariantId = 14;
        var transactionTypeVariant =
            CreateRequest("REF-1", "PART-1");
        transactionTypeVariant.TransactionType =
            (InventoryTransactionType)4;
        var referenceTypeVariant =
            CreateRequest("REF-1", "PART-1");
        referenceTypeVariant.ReferenceType =
            (InventoryReferenceType)6;
        var referenceLineVariant =
            CreateRequest("REF-1", "PART-1");
        referenceLineVariant.ReferenceLineId = 18;

        var variants = new[]
        {
            warehouseVariant,
            productVariant,
            transactionTypeVariant,
            referenceTypeVariant,
            CreateRequest("REF-2", "PART-1"),
            referenceLineVariant,
            CreateRequest("REF-1", "PART-2")
        };

        variants.Should().OnlyContain(
            request => !baselineKey.SequenceEqual(
                InventoryIdempotencyKeyFactory.Create(9, request)));
        InventoryIdempotencyKeyFactory.Create(10, baseline)
            .Should().NotEqual(baselineKey);
    }

    [Fact]
    public void Canonical_v1_should_encode_fields_in_declared_order()
    {
        var first = InventoryIdempotencyKeyFactory.Create(
            7,
            11,
            13,
            (InventoryTransactionType)3,
            (InventoryReferenceType)5,
            "REF-1",
            17,
            "PART-1");
        var reorderedValues =
            InventoryIdempotencyKeyFactory.Create(
                11,
                7,
                13,
                (InventoryTransactionType)3,
                (InventoryReferenceType)5,
                "REF-1",
                17,
                "PART-1");

        first.Should().NotEqual(reorderedValues);
    }

    [Fact]
    public void Canonical_v1_should_enforce_reference_length_limits()
    {
        var maximum = CreateRequest(
            new string('R', 64),
            new string('S', 100));

        InventoryIdempotencyKeyFactory.Create(9, maximum)
            .Should().HaveCount(32);

        var longReference = CreateRequest(
            new string('R', 65),
            null);
        var longSubkey = CreateRequest(
            "REF-1",
            new string('S', 101));

        Action longReferenceAction = () =>
            InventoryIdempotencyKeyFactory.Create(
                9,
                longReference);
        Action longSubkeyAction = () =>
            InventoryIdempotencyKeyFactory.Create(
                9,
                longSubkey);

        longReferenceAction.Should()
            .Throw<BusinessRuleException>();
        longSubkeyAction.Should()
            .Throw<BusinessRuleException>();
    }

    private static CreateInventoryMovementRequest CreateRequest(
        string? referenceId,
        string? subkey)
        => new()
        {
            WarehouseId = 11,
            ProductVariantId = 13,
            TransactionType = (InventoryTransactionType)3,
            ReferenceType = (InventoryReferenceType)5,
            ReferenceId = referenceId,
            ReferenceLineId = 17,
            ReferenceSubKey = subkey,
            QuantityChange = 2m,
            UnitCost = 10m
        };
}
