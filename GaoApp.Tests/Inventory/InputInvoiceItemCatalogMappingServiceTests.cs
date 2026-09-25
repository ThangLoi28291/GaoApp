using FluentAssertions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Helpers;
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

public sealed class InputInvoiceItemCatalogMappingServiceTests
{
    [Fact]
    public void Identity_normalizer_preserves_semantic_pack_size_distinctions()
    {
        InputInvoiceItemIdentityNormalizer.NormalizeText("  Thùng\t 20  ")
            .Should().Be("THÙNG 20");
        InputInvoiceItemIdentityNormalizer.NormalizeText("Thùng 20")
            .Should().NotBe(InputInvoiceItemIdentityNormalizer.NormalizeText("Thùng 24"));
        InputInvoiceItemIdentityNormalizer.NormalizeCode(" ab- 20 ")
            .Should().Be("AB- 20");
    }

    [Fact]
    public void Durable_mapping_uses_canonical_base_unit_signature()
    {
        var properties = typeof(InputInvoiceItemCatalogMap)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();

        properties.Should().Contain(
            nameof(InputInvoiceItemCatalogMap.ConfirmedUnitId),
            nameof(InputInvoiceItemCatalogMap.ConfirmedFactor),
            nameof(InputInvoiceItemCatalogMap.ConfirmedBaseUnitId));
        properties.Should().NotContain("DerivedBaseQuantity");
    }

    [Fact]
    public void Derived_base_quantity_is_computed_only()
    {
        var resolution = new InputInvoiceItemCatalogResolutionDto
        {
            XmlQuantity = 3m,
            ConfirmedFactor = 24m
        };

        resolution.DerivedBaseQuantity.Should().Be(72m);
        typeof(InputInvoiceItemCatalogMap).GetProperty("DerivedBaseQuantity")
            .Should().BeNull();
    }

    [Fact]
    public async Task Unknown_then_Human_confirm_persists_exact_signature_and_server_audit()
    {
        await using var fixture = await Fixture.CreateAsync();

        var unknown = await fixture.Service.ResolveForReceiptAsync(1, 100);
        unknown[200].State.Should().Be(
            InputInvoiceItemCatalogResolutionState.NeedsConfirmation);
        unknown[200].ReasonCode.Should().Be("MappingNotFound");

        var confirmed = await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();

        confirmed.State.Should().Be(InputInvoiceItemCatalogResolutionState.Confirmed);
        confirmed.ConfirmedUnitId.Should().Be(41);
        confirmed.ConfirmedFactor.Should().Be(24m);
        confirmed.ConfirmedBaseUnitId.Should().Be(40);
        confirmed.DerivedBaseQuantity.Should().Be(120m);
        var mapping = await fixture.Context.InputInvoiceItemCatalogMaps.SingleAsync();
        mapping.SupplierId.Should().Be(10);
        mapping.NormalizedSupplierItemCode.Should().Be("SUP-20");
        var audit = await fixture.Context.PurchaseReceiptAuditEvents.SingleAsync(x =>
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceItemMappingConfirmed);
        audit.ActorUserId.Should().Be(7);
        audit.OccurredAtUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
        audit.IsSuccess.Should().BeTrue();
        audit.NewValuesJson.Should().Contain("ConfirmedBaseUnitId");
    }

    [Fact]
    public async Task Exact_mapping_reuses_only_inside_same_Supplier_boundary()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();

        var sameSupplier = await fixture.Service.ResolveForReceiptAsync(1, 100);
        sameSupplier[200].State.Should().Be(
            InputInvoiceItemCatalogResolutionState.Confirmed);

        fixture.Invoice.ResolvedSupplierId = 11;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var otherSupplier = await fixture.Service.ResolveForReceiptAsync(1, 100);
        otherSupplier[200].State.Should().Be(
            InputInvoiceItemCatalogResolutionState.NeedsConfirmation);
        otherSupplier[200].ReasonCode.Should().Be("SupplierReceiptMismatch");
    }

    [Fact]
    public async Task Association_only_persists_without_creating_durable_item_map()
    {
        await using var fixture = await Fixture.CreateAsync();
        var xml = new InputInvoiceXmlService(
            fixture.Repository,
            itemCatalogMappingService: fixture.Service);

        await xml.UpdateLineMapAsync(1, 100, new()
        {
            StockDocumentLineId = 101,
            UseInputInvoice = true,
            InputInvoiceDetailId = 200,
            RememberItemCatalogMapping = false
        });

        var lineMap = await fixture.Context.StockDocumentLineInputInvoiceMaps.SingleAsync();
        lineMap.UseInputInvoice.Should().BeTrue();
        lineMap.InputInvoiceDetailId.Should().Be(200);
        (await fixture.Context.InputInvoiceItemCatalogMaps.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Association_with_remember_uses_xml_unit_conversion_not_receipt_conversion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var packUnit = new Unit
        {
            Id = 43, StoreId = 1, Code = "LOC4", Name = "Lốc",
            IsActive = true, RowVersion = []
        };
        var packConversion = new ProductUnitConversion
        {
            Id = 62, StoreId = 1, ProductVariantId = 50, UnitId = 43,
            Factor = 4m, IsActive = true, RowVersion = []
        };
        fixture.Context.AddRange(packUnit, packConversion);
        fixture.Line.ProductUnitConversionId = 62;
        fixture.Line.UnitId = 43;
        fixture.Line.UnitNameSnapshot = "Lốc";
        fixture.Line.Factor = 4m;
        fixture.Line.Quantity = 12m;
        fixture.Line.BaseQuantity = 48m;
        await fixture.Context.SaveChangesAsync();
        var xml = new InputInvoiceXmlService(
            fixture.Repository,
            itemCatalogMappingService: fixture.Service);

        await xml.UpdateLineMapAsync(1, 100, new()
        {
            StockDocumentLineId = 101,
            UseInputInvoice = true,
            InputInvoiceDetailId = 200,
            RememberItemCatalogMapping = true,
            ProductVariantId = 999,
            ProductUnitConversionId = 999
        });

        var lineMap = await fixture.Context.StockDocumentLineInputInvoiceMaps.SingleAsync();
        lineMap.InputInvoiceDetailId.Should().Be(200);
        var mapping = await fixture.Context.InputInvoiceItemCatalogMaps.SingleAsync();
        mapping.ProductVariantId.Should().Be(fixture.Line.ProductVariantId);
        mapping.ProductUnitConversionId.Should().Be(61);
        mapping.ProductUnitConversionId.Should().NotBe(
            fixture.Line.ProductUnitConversionId);
        mapping.ConfirmedUnitId.Should().Be(41);
        mapping.ConfirmedFactor.Should().Be(24m);
    }

    [Fact]
    public async Task Remember_does_not_require_receipt_side_conversion_identity()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Line.ProductUnitConversionId = null;
        await fixture.Context.SaveChangesAsync();
        var xml = new InputInvoiceXmlService(
            fixture.Repository,
            itemCatalogMappingService: fixture.Service);

        await xml.UpdateLineMapAsync(1, 100, new()
        {
            StockDocumentLineId = 101,
            UseInputInvoice = true,
            InputInvoiceDetailId = 200,
            RememberItemCatalogMapping = true
        });

        (await fixture.Context.StockDocumentLineInputInvoiceMaps.SingleAsync())
            .InputInvoiceDetailId.Should().Be(200);
        (await fixture.Context.InputInvoiceItemCatalogMaps.SingleAsync())
            .ProductUnitConversionId.Should().Be(61);
    }

    [Fact]
    public async Task Remember_does_not_reuse_stale_receipt_factor_for_xml_unit()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Line.Factor = 12m;
        fixture.Line.BaseQuantity = fixture.Line.Quantity * fixture.Line.Factor;
        await fixture.Context.SaveChangesAsync();
        var xml = new InputInvoiceXmlService(
            fixture.Repository,
            itemCatalogMappingService: fixture.Service);

        await xml.UpdateLineMapAsync(1, 100, new()
        {
            StockDocumentLineId = 101,
            UseInputInvoice = true,
            InputInvoiceDetailId = 200,
            RememberItemCatalogMapping = true
        });

        (await fixture.Context.StockDocumentLineInputInvoiceMaps.SingleAsync())
            .InputInvoiceDetailId.Should().Be(200);
        var mapping = await fixture.Context.InputInvoiceItemCatalogMaps.SingleAsync();
        mapping.ProductUnitConversionId.Should().Be(61);
        mapping.ConfirmedFactor.Should().Be(24m);
    }

    [Fact]
    public async Task Clearing_current_association_preserves_historical_durable_mapping()
    {
        await using var fixture = await Fixture.CreateAsync();
        var xml = new InputInvoiceXmlService(
            fixture.Repository,
            itemCatalogMappingService: fixture.Service);
        await xml.UpdateLineMapAsync(1, 100, new()
        {
            StockDocumentLineId = 101,
            UseInputInvoice = true,
            InputInvoiceDetailId = 200,
            RememberItemCatalogMapping = true
        });

        await xml.UpdateLineMapAsync(1, 100, new()
        {
            StockDocumentLineId = 101,
            UseInputInvoice = false,
            ExclusionReason = "Không ghép dòng này"
        });

        (await fixture.Context.InputInvoiceItemCatalogMaps.CountAsync()).Should().Be(1);
        var lineMap = await fixture.Context.StockDocumentLineInputInvoiceMaps.SingleAsync();
        lineMap.InputInvoiceDetailId.Should().BeNull();
        lineMap.UseInputInvoice.Should().BeFalse();
    }

    [Fact]
    public async Task Reconfirming_same_key_and_same_target_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();

        var second = await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();

        second.State.Should().Be(InputInvoiceItemCatalogResolutionState.Confirmed);
        (await fixture.Context.InputInvoiceItemCatalogMaps.CountAsync()).Should().Be(1);
        (await fixture.Context.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceItemMappingConfirmed))
            .Should().Be(1);
    }

    [Fact]
    public async Task Human_confirmed_no_code_name_unit_alias_is_suggestion_only()
    {
        await using var fixture = await Fixture.CreateAsync(code: null);
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        fixture.Context.InputInvoiceDetails.Add(new InputInvoiceDetail
        {
            Id = 201,
            InputInvoiceHeadId = 150,
            LineNo = 2,
            ItemName = " Gạo ST25 ",
            NormalizedItemName = "GẠO ST25",
            UnitName = "Thùng 24",
            NormalizedUnitName = "THÙNG 24",
            Quantity = 2m,
            RowVersion = []
        });
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.ResolveForReceiptAsync(1, 100);
        await fixture.Service.AutoApplyKnownMappingsWithinTransactionAsync(1, 100, 150);
        await fixture.Repository.SaveChangesAsync();

        result[201].State.Should().Be(
            InputInvoiceItemCatalogResolutionState.NeedsConfirmation);
        result[201].ReasonCode.Should().Be("NameUnitSuggestionRequiresConfirmation");
        result[201].ProductVariantId.Should().Be(50);
        result[201].ProductUnitConversionId.Should().Be(61);
        (await fixture.Context.InputInvoiceItemCatalogMaps.SingleAsync())
            .NormalizedSupplierItemCode.Should().BeNull();
        (await fixture.Context.StockDocumentLineInputInvoiceMaps.SingleAsync())
            .InputInvoiceDetailId.Should().BeNull();
    }

    [Fact]
    public async Task Exact_code_and_unit_mapping_has_precedence_over_cross_unit_candidates()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        fixture.Context.InputInvoiceItemCatalogMaps.Add(NewMapping(
            normalizedUnit: "LON", productVariantId: 999,
            conversionId: 999, unitId: 40, factor: 1m));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.ResolveForReceiptAsync(1, 100);

        result[200].State.Should().Be(InputInvoiceItemCatalogResolutionState.Confirmed);
        result[200].ProductVariantId.Should().Be(50);
        result[200].ProductUnitConversionId.Should().Be(61);
    }

    [Fact]
    public async Task Missing_code_unit_inherits_unique_product_and_resolves_new_unit_without_persisting()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        AddConversion(fixture, 43, 62, "LOC4", "Lốc", 4m);
        var detail = fixture.Context.InputInvoiceDetails.Single(x => x.Id == 200);
        detail.UnitName = " Lốc ";
        detail.NormalizedUnitName = "LỐC";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.ResolveForReceiptAsync(1, 100);

        result[200].State.Should().Be(
            InputInvoiceItemCatalogResolutionState.NeedsConfirmation);
        result[200].ReasonCode.Should().Be("CrossUnitProductInherited");
        result[200].ProductVariantId.Should().Be(50);
        result[200].ProductUnitConversionId.Should().Be(62);
        result[200].ConfirmedFactor.Should().Be(4m);
        (await fixture.Context.InputInvoiceItemCatalogMaps.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task New_unit_candidate_is_persisted_only_after_explicit_remember()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        AddConversion(fixture, 43, 62, "LOC4", "Lốc", 4m);
        var detail = fixture.Context.InputInvoiceDetails.Single(x => x.Id == 200);
        detail.UnitName = "Lốc";
        detail.NormalizedUnitName = "LỐC";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var candidate = await fixture.Service.ResolveForReceiptAsync(1, 100);
        candidate[200].ProductUnitConversionId.Should().Be(62);
        (await fixture.Context.InputInvoiceItemCatalogMaps.CountAsync()).Should().Be(1);

        var xml = new InputInvoiceXmlService(
            fixture.Repository,
            itemCatalogMappingService: fixture.Service);
        await xml.UpdateLineMapAsync(1, 100, new()
        {
            StockDocumentLineId = 101,
            UseInputInvoice = true,
            InputInvoiceDetailId = 200,
            RememberItemCatalogMapping = true
        });

        var mappings = await fixture.Context.InputInvoiceItemCatalogMaps
            .OrderBy(x => x.NormalizedSupplierUnitName).ToListAsync();
        mappings.Should().HaveCount(2);
        var learned = mappings.Single(x => x.NormalizedSupplierUnitName == "LỐC");
        learned.ProductVariantId.Should().Be(50);
        learned.ProductUnitConversionId.Should().Be(62);
        learned.ConfirmedFactor.Should().Be(4m);
    }

    [Fact]
    public async Task Multiple_product_candidates_for_same_authoritative_code_are_ambiguous()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        var secondVariant = new ProductVariant
        {
            Id = 51, StoreId = 1, ProductId = 49, Sku = "GAO-OTHER",
            ProductVariantName = "Gạo khác", IsActive = true, RowVersion = []
        };
        var secondConversion = new ProductUnitConversion
        {
            Id = 63, StoreId = 1, ProductVariantId = 51, UnitId = 40,
            Factor = 1m, IsBaseUnit = true, IsActive = true, RowVersion = []
        };
        fixture.Context.AddRange(secondVariant, secondConversion,
            NewMapping("LON", 51, 63, 40, 1m));
        AddConversion(fixture, 43, 62, "LOC4", "Lốc", 4m);
        var detail = fixture.Context.InputInvoiceDetails.Single(x => x.Id == 200);
        detail.UnitName = "Lốc";
        detail.NormalizedUnitName = "LỐC";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.ResolveForReceiptAsync(1, 100);

        result[200].State.Should().Be(
            InputInvoiceItemCatalogResolutionState.NeedsConfirmation);
        result[200].ReasonCode.Should().Be("CrossUnitProductAmbiguous");
        result[200].ProductVariantId.Should().BeNull();
    }

    [Fact]
    public async Task No_code_mapping_does_not_inherit_product_across_units()
    {
        await using var fixture = await Fixture.CreateAsync(code: null);
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        AddConversion(fixture, 43, 62, "LOC4", "Lốc", 4m);
        var detail = fixture.Context.InputInvoiceDetails.Single(x => x.Id == 200);
        detail.UnitName = "Lốc";
        detail.NormalizedUnitName = "LỐC";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.ResolveForReceiptAsync(1, 100);

        result[200].ReasonCode.Should().Be("MappingNotFound");
        result[200].ProductVariantId.Should().BeNull();
    }

    [Fact]
    public async Task Xml_unit_code_match_is_deterministic_but_cross_field_collision_is_ambiguous()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        AddConversion(fixture, 43, 62, "LOC4", "Lốc", 4m);
        var detail = fixture.Context.InputInvoiceDetails.Single(x => x.Id == 200);
        detail.UnitName = " loc4 ";
        detail.NormalizedUnitName = "LOC4";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var unique = await fixture.Service.ResolveForReceiptAsync(1, 100);
        unique[200].ProductUnitConversionId.Should().Be(62);

        AddConversion(fixture, 44, 64, "OTHER", "LOC4", 8m);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var ambiguous = await fixture.Service.ResolveForReceiptAsync(1, 100);

        ambiguous[200].ReasonCode.Should().Be("XmlUnitConversionAmbiguous");
        ambiguous[200].ProductUnitConversionId.Should().BeNull();
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Inactive")]
    [InlineData("NonPositive")]
    [InlineData("WrongVariant")]
    public async Task Xml_unit_resolution_rejects_unavailable_or_invalid_conversion(
        string invalidCase)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        var detail = fixture.Context.InputInvoiceDetails.Single(x => x.Id == 200);
        detail.UnitName = "Vỉ";
        detail.NormalizedUnitName = "VỈ";
        if (invalidCase != "Missing")
        {
            var variantId = invalidCase == "WrongVariant" ? 999 : 50;
            var active = invalidCase != "Inactive";
            fixture.Context.AddRange(
                new Unit
                {
                    Id = 45, StoreId = 1, Code = "VI", Name = "Vỉ",
                    IsActive = active, RowVersion = []
                },
                new ProductUnitConversion
                {
                    Id = 65, StoreId = 1, ProductVariantId = variantId,
                    UnitId = 45,
                    Factor = invalidCase == "NonPositive" ? 0m : 10m,
                    IsActive = active,
                    RowVersion = []
                });
        }
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.ResolveForReceiptAsync(1, 100);

        result[200].ReasonCode.Should().Be("XmlUnitConversionNotFound");
        result[200].ProductUnitConversionId.Should().BeNull();
    }

    [Theory]
    [InlineData(StockDocumentStatus.Draft)]
    [InlineData(StockDocumentStatus.Rejected)]
    public async Task Operator_phases_cannot_confirm_item_mapping(
        StockDocumentStatus status)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Receipt.Status = status;
        await fixture.Context.SaveChangesAsync();

        var action = () => fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*chờ duyệt*");
        (await fixture.Context.InputInvoiceItemCatalogMaps.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("Unit")]
    [InlineData("Factor")]
    [InlineData("BaseUnit")]
    public async Task Any_conversion_signature_drift_requires_reconfirmation(string drift)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();

        if (drift == "Unit") fixture.BoxConversion.UnitId = 42;
        if (drift == "Factor") fixture.BoxConversion.Factor = 20m;
        if (drift == "BaseUnit") fixture.Product.BaseUnitId = 42;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.ResolveForReceiptAsync(1, 100);

        result[200].State.Should().Be(
            InputInvoiceItemCatalogResolutionState.NeedsConfirmation);
        result[200].ReasonCode.Should().Be("ConversionSignatureDrift");
    }

    [Fact]
    public async Task Inactive_target_requires_reconfirmation_and_is_not_auto_applied()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        fixture.BoxConversion.IsActive = false;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.ResolveForReceiptAsync(1, 100);
        await fixture.Service.AutoApplyKnownMappingsWithinTransactionAsync(1, 100, 150);
        await fixture.Repository.SaveChangesAsync();

        result[200].ReasonCode.Should().Be("TargetInactive");
        (await fixture.Context.StockDocumentLineInputInvoiceMaps.SingleAsync())
            .InputInvoiceDetailId.Should().BeNull();
    }

    [Fact]
    public async Task Receipt_line_product_variant_mismatch_persists_neither_mapping_nor_physical_change()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Line.ProductVariantId = 999;
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();

        result.ReasonCode.Should().Be("ReceiptLineTargetMismatch");
        (await fixture.Context.InputInvoiceItemCatalogMaps.CountAsync()).Should().Be(0);
        var line = await fixture.Context.StockDocumentLines.SingleAsync(x => x.Id == 101);
        line.ProductVariantId.Should().Be(999);
        line.ProductUnitConversionId.Should().Be(61);
        line.UnitId.Should().Be(41);
        line.Factor.Should().Be(24m);
    }

    [Fact]
    public async Task Confirmed_receipt_is_read_only_for_mapping_confirmation()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Receipt.Status = StockDocumentStatus.Confirmed;
        fixture.Receipt.ConfirmedLegalEntityId = 20;
        await fixture.Context.SaveChangesAsync();

        var action = () => fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);

        await action.Should().ThrowAsync<BusinessRuleException>();
        (await fixture.Context.InputInvoiceItemCatalogMaps.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Confirmed_receipt_does_not_auto_apply_item_mapping_metadata()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        fixture.Receipt.Status = StockDocumentStatus.Confirmed;
        fixture.Receipt.ConfirmedLegalEntityId = 20;
        await fixture.Context.SaveChangesAsync();

        await fixture.Service.AutoApplyKnownMappingsWithinTransactionAsync(1, 100, 150);
        await fixture.Repository.SaveChangesAsync();

        var lineMap = await fixture.Context.StockDocumentLineInputInvoiceMaps.SingleAsync();
        lineMap.InputInvoiceDetailId.Should().BeNull();
        (await fixture.Context.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceItemMappingAutoApplied))
            .Should().Be(0);
    }

    [Fact]
    public async Task Exact_known_mapping_auto_applies_to_unique_same_conversion_receipt_line()
    {
        await using var fixture = await Fixture.CreateAsync();
        AddReceiptLine(
            fixture,
            id: 102,
            lineNo: 2,
            conversionId: 60,
            unitId: 40,
            unitName: "Lon",
            factor: 1m,
            quantity: 120m);
        await fixture.Context.SaveChangesAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();

        await fixture.Service.AutoApplyKnownMappingsWithinTransactionAsync(1, 100, 150);
        await fixture.Repository.SaveChangesAsync();

        var lineMaps = await fixture.Context.StockDocumentLineInputInvoiceMaps
            .OrderBy(x => x.StockDocumentLineId)
            .ToListAsync();
        lineMaps.Should().HaveCount(2);
        lineMaps[0].UseInputInvoice.Should().BeTrue();
        lineMaps[0].InputInvoiceDetailId.Should().Be(200);
        lineMaps[1].UseInputInvoice.Should().BeFalse();
        lineMaps[1].InputInvoiceDetailId.Should().BeNull();
        (await fixture.Context.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceItemMappingAutoApplied))
            .Should().Be(1);
    }

    [Fact]
    public async Task Cross_unit_inherited_unique_conversion_auto_associates_without_durable_map()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        AddConversion(fixture, 43, 62, "LOC4", "Lốc", 4m);
        fixture.Line.ProductUnitConversionId = 62;
        fixture.Line.UnitId = 43;
        fixture.Line.UnitNameSnapshot = "Lốc";
        fixture.Line.Factor = 4m;
        fixture.Line.Quantity = 30m;
        fixture.Line.BaseQuantity = 120m;
        AddInvoiceDetail(fixture, 201, 2, "Lốc", "LỐC", 30m);
        await fixture.Context.SaveChangesAsync();

        var before = await fixture.Service.ResolveForReceiptAsync(1, 100);
        before[201].State.Should().Be(
            InputInvoiceItemCatalogResolutionState.NeedsConfirmation);
        before[201].ReasonCode.Should().Be("CrossUnitProductInherited");
        before[201].ProductUnitConversionId.Should().Be(62);

        await fixture.Service.AutoApplyKnownMappingsWithinTransactionAsync(1, 100, 150);
        await fixture.Repository.SaveChangesAsync();

        var lineMap = await fixture.Context.StockDocumentLineInputInvoiceMaps.SingleAsync();
        lineMap.UseInputInvoice.Should().BeTrue();
        lineMap.InputInvoiceDetailId.Should().Be(201);
        (await fixture.Context.InputInvoiceItemCatalogMaps.CountAsync()).Should().Be(1);
        (await fixture.Context.InputInvoiceItemCatalogMaps.AnyAsync(x =>
            x.ProductUnitConversionId == 62)).Should().BeFalse();
    }

    [Fact]
    public async Task One_receipt_and_two_xml_details_for_same_conversion_remain_manual()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        AddInvoiceDetail(fixture, 201, 2, "Thùng 24", "THÙNG 24", 5m);
        await fixture.Context.SaveChangesAsync();

        await fixture.Service.AutoApplyKnownMappingsWithinTransactionAsync(1, 100, 150);
        await fixture.Repository.SaveChangesAsync();

        var lineMap = await fixture.Context.StockDocumentLineInputInvoiceMaps.SingleAsync();
        lineMap.UseInputInvoice.Should().BeTrue();
        lineMap.InputInvoiceDetailId.Should().BeNull();
        (await fixture.Context.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceItemMappingAutoApplied))
            .Should().Be(0);
    }

    [Fact]
    public async Task Two_receipt_lines_and_one_xml_detail_for_same_conversion_remain_manual()
    {
        await using var fixture = await Fixture.CreateAsync();
        AddReceiptLine(
            fixture,
            id: 102,
            lineNo: 2,
            conversionId: 61,
            unitId: 41,
            unitName: "Thùng 24",
            factor: 24m,
            quantity: 5m);
        await fixture.Context.SaveChangesAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();

        await fixture.Service.AutoApplyKnownMappingsWithinTransactionAsync(1, 100, 150);
        await fixture.Repository.SaveChangesAsync();

        var lineMaps = await fixture.Context.StockDocumentLineInputInvoiceMaps
            .OrderBy(x => x.StockDocumentLineId)
            .ToListAsync();
        lineMaps.Should().OnlyContain(x => x.InputInvoiceDetailId == null);
        (await fixture.Context.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceItemMappingAutoApplied))
            .Should().Be(0);
    }

    [Fact]
    public async Task Equal_base_quantity_does_not_auto_associate_different_conversions()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        AddConversion(fixture, 43, 62, "LOC4", "Lốc", 4m);
        fixture.Line.ProductUnitConversionId = 62;
        fixture.Line.UnitId = 43;
        fixture.Line.UnitNameSnapshot = "Lốc";
        fixture.Line.Factor = 4m;
        fixture.Line.Quantity = 30m;
        fixture.Line.BaseQuantity = 120m;
        await fixture.Context.SaveChangesAsync();

        await fixture.Service.AutoApplyKnownMappingsWithinTransactionAsync(1, 100, 150);
        await fixture.Repository.SaveChangesAsync();

        var lineMap = await fixture.Context.StockDocumentLineInputInvoiceMaps.SingleAsync();
        lineMap.InputInvoiceDetailId.Should().BeNull();
        lineMap.UseInputInvoice.Should().BeTrue();
    }

    [Fact]
    public async Task Existing_valid_association_is_idempotently_preserved()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        var lineMap = await fixture.Context.StockDocumentLineInputInvoiceMaps.SingleAsync();
        lineMap.UseInputInvoice = true;
        lineMap.InputInvoiceDetailId = 200;
        lineMap.MatchStatus = InputInvoiceMatchStatus.Matched;
        await fixture.Context.SaveChangesAsync();

        await fixture.Service.AutoApplyKnownMappingsWithinTransactionAsync(1, 100, 150);
        await fixture.Repository.SaveChangesAsync();

        lineMap.InputInvoiceDetailId.Should().Be(200);
        lineMap.MatchStatus.Should().Be(InputInvoiceMatchStatus.Matched);
        (await fixture.Context.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceItemMappingAutoApplied))
            .Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_exclusion_or_conflicting_association_is_preserved(
        bool conflictingAssociation)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ConfirmWithinTransactionAsync(
            1, 100, 101, 200, 50, 61, null);
        await fixture.Repository.SaveChangesAsync();
        var lineMap = await fixture.Context.StockDocumentLineInputInvoiceMaps.SingleAsync();
        if (conflictingAssociation)
        {
            AddInvoiceDetail(fixture, 201, 2, "Lon", "LON", 120m,
                supplierItemCode: "OTHER");
            lineMap.UseInputInvoice = true;
            lineMap.InputInvoiceDetailId = 201;
            lineMap.MatchStatus = InputInvoiceMatchStatus.QuantityMismatch;
        }
        else
        {
            lineMap.UseInputInvoice = false;
            lineMap.InputInvoiceDetailId = null;
            lineMap.MatchStatus = InputInvoiceMatchStatus.Excluded;
            lineMap.ExclusionReason = "Không thuộc XML";
        }
        await fixture.Context.SaveChangesAsync();

        await fixture.Service.AutoApplyKnownMappingsWithinTransactionAsync(1, 100, 150);
        await fixture.Repository.SaveChangesAsync();

        lineMap.InputInvoiceDetailId.Should().Be(
            conflictingAssociation ? 201 : null);
        lineMap.MatchStatus.Should().Be(conflictingAssociation
            ? InputInvoiceMatchStatus.QuantityMismatch
            : InputInvoiceMatchStatus.Excluded);
        (await fixture.Context.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.EventType == PurchaseReceiptAuditEventType.InputInvoiceItemMappingAutoApplied))
            .Should().Be(0);
    }

    private static ProductUnitConversion AddConversion(
        Fixture fixture,
        int unitId,
        int conversionId,
        string code,
        string name,
        decimal factor,
        bool active = true)
    {
        var unit = new Unit
        {
            Id = unitId, StoreId = 1, Code = code, Name = name,
            IsActive = active, RowVersion = []
        };
        var conversion = new ProductUnitConversion
        {
            Id = conversionId, StoreId = 1, ProductVariantId = 50,
            UnitId = unitId, Factor = factor, IsActive = active, RowVersion = []
        };
        fixture.Context.AddRange(unit, conversion);
        return conversion;
    }

    private static StockDocumentLine AddReceiptLine(
        Fixture fixture,
        int id,
        int lineNo,
        int conversionId,
        int unitId,
        string unitName,
        decimal factor,
        decimal quantity)
    {
        var line = new StockDocumentLine
        {
            Id = id,
            StockDocumentId = 100,
            LineNo = lineNo,
            ProductVariantId = 50,
            ProductUnitConversionId = conversionId,
            UnitId = unitId,
            UnitNameSnapshot = unitName,
            Factor = factor,
            Quantity = quantity,
            BaseQuantity = quantity * factor,
            UnitCost = 100m,
            LineTotal = quantity * 100m,
            ProductNameSnapshot = "Gạo ST25",
            RowVersion = []
        };
        fixture.Context.AddRange(
            line,
            new StockDocumentLineInputInvoiceMap
            {
                Id = id + 200,
                StoreId = 1,
                StockDocumentId = 100,
                StockDocumentLineId = id,
                UseInputInvoice = false,
                MatchStatus = InputInvoiceMatchStatus.None,
                RowVersion = []
            });
        return line;
    }

    private static InputInvoiceDetail AddInvoiceDetail(
        Fixture fixture,
        int id,
        int lineNo,
        string unitName,
        string normalizedUnitName,
        decimal quantity,
        string? supplierItemCode = "SUP-20")
    {
        var detail = new InputInvoiceDetail
        {
            Id = id,
            InputInvoiceHeadId = 150,
            LineNo = lineNo,
            SupplierItemCode = supplierItemCode,
            NormalizedSupplierItemCode =
                InputInvoiceItemIdentityNormalizer.NormalizeCode(supplierItemCode),
            ItemName = "Gạo ST25",
            NormalizedItemName = "GẠO ST25",
            UnitName = unitName,
            NormalizedUnitName = normalizedUnitName,
            Quantity = quantity,
            UnitPrice = 100m,
            LineAmount = quantity * 100m,
            RowVersion = []
        };
        fixture.Context.InputInvoiceDetails.Add(detail);
        return detail;
    }

    private static InputInvoiceItemCatalogMap NewMapping(
        string normalizedUnit,
        int productVariantId,
        int conversionId,
        int unitId,
        decimal factor) => new()
    {
        StoreId = 1,
        SupplierId = 10,
        SupplierItemCode = "SUP-20",
        NormalizedSupplierItemCode = "SUP-20",
        SupplierItemName = "Gạo ST25",
        NormalizedSupplierItemName = "GẠO ST25",
        SupplierUnitName = normalizedUnit,
        NormalizedSupplierUnitName = normalizedUnit,
        ProductVariantId = productVariantId,
        ProductUnitConversionId = conversionId,
        ConfirmedUnitId = unitId,
        ConfirmedFactor = factor,
        ConfirmedBaseUnitId = 40,
        IsActive = true,
        RowVersion = []
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            InMemoryAppDbContext context,
            InputInvoiceRepository repository,
            InputInvoiceItemCatalogMappingService service,
            InputInvoiceHead invoice,
            StockDocument receipt,
            StockDocumentLine line,
            Product product,
            ProductUnitConversion boxConversion)
        {
            Context = context;
            Repository = repository;
            Service = service;
            Invoice = invoice;
            Receipt = receipt;
            Line = line;
            Product = product;
            BoxConversion = boxConversion;
        }

        public InMemoryAppDbContext Context { get; }
        public InputInvoiceRepository Repository { get; }
        public InputInvoiceItemCatalogMappingService Service { get; }
        public InputInvoiceHead Invoice { get; }
        public StockDocument Receipt { get; }
        public StockDocumentLine Line { get; }
        public Product Product { get; }
        public ProductUnitConversion BoxConversion { get; }

        public static async Task<Fixture> CreateAsync(string? code = "SUP-20")
        {
            var tenant = new TenantContext();
            tenant.SetStore(1, "item-map");
            var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var context = new InMemoryAppDbContext(options, tenant, new UserStub());
            var store = new Store
            {
                Id = 1, Name = "Store", SubDomain = "item-map",
                SubDomainNormalized = "ITEM-MAP", IsActive = true, RowVersion = []
            };
            var supplierA = new Supplier
            {
                Id = 10, StoreId = 1, Code = "SUP-A", Name = "Supplier A",
                IsActive = true, RowVersion = []
            };
            var supplierB = new Supplier
            {
                Id = 11, StoreId = 1, Code = "SUP-B", Name = "Supplier B",
                IsActive = true, RowVersion = []
            };
            var legalEntity = new LegalEntity
            {
                Id = 20, StoreId = 1, Code = "LE", Name = "LE", LegalName = "LE",
                IsActive = true, RowVersion = []
            };
            var warehouse = new Warehouse
            {
                Id = 30, StoreId = 1, LegalEntityId = 20,
                Code = "WH", Name = "Warehouse", IsActive = true, RowVersion = []
            };
            var baseUnit = new Unit
            {
                Id = 40, StoreId = 1, Code = "LON", Name = "Lon",
                IsActive = true, IsBase = true, RowVersion = []
            };
            var boxUnit = new Unit
            {
                Id = 41, StoreId = 1, Code = "TH24", Name = "Thùng 24",
                IsActive = true, RowVersion = []
            };
            var alternateBase = new Unit
            {
                Id = 42, StoreId = 1, Code = "CHAI", Name = "Chai",
                IsActive = true, IsBase = true, RowVersion = []
            };
            var category = new Category
            {
                Id = 45, StoreId = 1, Code = "CAT", Name = "Category",
                IsActive = true, RowVersion = []
            };
            var product = new Product
            {
                Id = 49, StoreId = 1, Name = "Gạo ST25", Alias = "GAO-ST25",
                CategoryId = 45, SupplierId = 10, BaseUnitId = 40,
                IsActive = true, RowVersion = []
            };
            var variant = new ProductVariant
            {
                Id = 50, StoreId = 1, ProductId = 49, Sku = "GAO-ST25-01",
                ProductVariantName = "Gạo ST25", IsActive = true, RowVersion = []
            };
            var baseConversion = new ProductUnitConversion
            {
                Id = 60, StoreId = 1, ProductVariantId = 50, UnitId = 40,
                Factor = 1m, IsBaseUnit = true, IsActive = true, RowVersion = []
            };
            var boxConversion = new ProductUnitConversion
            {
                Id = 61, StoreId = 1, ProductVariantId = 50, UnitId = 41,
                Factor = 24m, IsDefaultForSale = true, IsActive = true, RowVersion = []
            };
            var receipt = new StockDocument
            {
                Id = 100, StoreId = 1, DocumentNo = "PN-100",
                Type = StockDocumentType.Receipt,
                Status = StockDocumentStatus.PendingApproval,
                WarehouseId = 30, SupplierId = 10, RowVersion = []
            };
            var line = new StockDocumentLine
            {
                Id = 101, StockDocumentId = 100, LineNo = 1,
                ProductVariantId = 50, ProductUnitConversionId = 61, UnitId = 41,
                UnitNameSnapshot = "Thùng 24", Factor = 24m, Quantity = 5m,
                BaseQuantity = 120m, UnitCost = 100m, LineTotal = 500m,
                ProductNameSnapshot = "Gạo ST25", RowVersion = []
            };
            var invoice = new InputInvoiceHead
            {
                Id = 150, StoreId = 1, InvoiceNumber = "HD-150",
                ResolvedSupplierId = 10,
                SupplierResolutionStatus = InputInvoiceSupplierResolutionStatus.Resolved,
                SellerName = "Supplier A", RowVersion = []
            };
            var detail = new InputInvoiceDetail
            {
                Id = 200, InputInvoiceHeadId = 150, LineNo = 1,
                SupplierItemCode = code,
                NormalizedSupplierItemCode =
                    InputInvoiceItemIdentityNormalizer.NormalizeCode(code),
                ItemName = "Gạo ST25", NormalizedItemName = "GẠO ST25",
                UnitName = "Thùng 24", NormalizedUnitName = "THÙNG 24",
                Quantity = 5m, UnitPrice = 100m, LineAmount = 500m, RowVersion = []
            };
            context.AddRange(store, supplierA, supplierB, legalEntity, warehouse,
                baseUnit, boxUnit, alternateBase, category, product, variant,
                baseConversion, boxConversion, receipt, line, invoice, detail,
                new StockDocumentInputInvoiceMap
                {
                    Id = 300, StoreId = 1, StockDocumentId = 100,
                    InputInvoiceHeadId = 150, RowVersion = []
                },
                new StockDocumentLineInputInvoiceMap
                {
                    Id = 301, StoreId = 1, StockDocumentId = 100,
                    StockDocumentLineId = 101, UseInputInvoice = true,
                    MatchStatus = InputInvoiceMatchStatus.None, RowVersion = []
                });
            await context.SaveChangesAsync();
            var repository = new InputInvoiceRepository(context);
            return new Fixture(context, repository,
                new InputInvoiceItemCatalogMappingService(repository, new UserStub()),
                invoice, receipt, line, product, boxConversion);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 7;
        public string? UserName => "item-map-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
