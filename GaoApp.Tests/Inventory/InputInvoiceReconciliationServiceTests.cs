using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Inventory;

public sealed class InputInvoiceReconciliationServiceTests
{
    [Theory]
    [InlineData(InputInvoiceReconciliationState.NotApplicable)]
    [InlineData(InputInvoiceReconciliationState.Incomplete)]
    [InlineData(InputInvoiceReconciliationState.Matched)]
    [InlineData(InputInvoiceReconciliationState.Mismatch)]
    [InlineData(InputInvoiceReconciliationState.AcceptedMismatch)]
    public void Every_known_reconciliation_state_is_confirm_ready(
        InputInvoiceReconciliationState state)
    {
        new InputInvoiceReconciliationDto { State = state }
            .ConfirmReady.Should().BeTrue();
    }

    [Fact]
    public void Unknown_future_reconciliation_state_remains_fail_closed()
    {
        new InputInvoiceReconciliationDto
            {
                State = (InputInvoiceReconciliationState)int.MaxValue
            }
            .ConfirmReady.Should().BeFalse();
    }

    [Fact]
    public void Confirmed_late_reconciliation_contract_is_explicitly_read_only()
    {
        typeof(InputInvoiceReconciliationDto).GetProperty("IsConfirmedReadOnly")
            .Should().NotBeNull();
        typeof(InputInvoiceReconciliationDto).GetProperty("IsLateAssociationException")
            .Should().NotBeNull();
    }

    [Fact]
    public void Policy_enforces_approved_quantity_and_money_tolerances()
    {
        Assert.Equal(0.0001m, InputInvoiceReconciliationPolicy.QuantityTolerance);
        Assert.Equal(1m, InputInvoiceReconciliationPolicy.MoneyTolerance);
        Assert.True(InputInvoiceReconciliationPolicy.QuantityMatches(0.0001m));
        Assert.False(InputInvoiceReconciliationPolicy.QuantityMatches(0.0002m));
        Assert.True(InputInvoiceReconciliationPolicy.MoneyMatches(1m));
        Assert.False(InputInvoiceReconciliationPolicy.MoneyMatches(1.01m));
    }

    [Fact]
    public void Central_contract_exposes_calculate_refresh_accept_and_confirm_gate()
    {
        var methods = typeof(IInputInvoiceReconciliationService)
            .GetMethods().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("CalculateAsync", methods);
        Assert.Contains("RefreshWithinTransactionAsync", methods);
        Assert.Contains("AcceptMismatchWithinTransactionAsync", methods);
        Assert.Contains("EnsureConfirmableWithinTransactionAsync", methods);
        Assert.Contains("InvalidateWithinTransactionAsync", methods);
        Assert.Contains("PreviewCommercialAsync", methods);
    }

    [Fact]
    public async Task Commercial_preview_uses_unsaved_prices_without_mutating_or_saving_receipt()
    {
        var scenario = Scenario.Create();
        var persisted = scenario.Receipt.Lines.ToDictionary(x => x.Id,
            x => (x.UnitPriceBeforeVat, x.LineTotal, x.VatAmount));
        var request = scenario.PreviewRequest(hasVat: false, unitPriceBeforeVat: 8_000m);

        var result = await scenario.Service.PreviewCommercialAsync(1, 10, request);

        result.IsCommercialPreview.Should().BeTrue();
        result.Header!.ReceiptSubtotalBeforeVat.Should().Be(48_000m);
        result.ProductSummaries.Single().ReceiptBaseUnitPriceBeforeVat.Should().Be(4_800m);
        scenario.SaveCalls.Should().Be(0);
        scenario.Receipt.Lines.ToDictionary(x => x.Id,
                x => (x.UnitPriceBeforeVat, x.LineTotal, x.VatAmount))
            .Should().BeEquivalentTo(persisted);
        scenario.Rows.Should().BeEmpty();
        scenario.Audits.Should().BeEmpty();
    }

    [Fact]
    public async Task Commercial_preview_recalculates_vat_from_current_tax_selection()
    {
        var scenario = Scenario.Create();
        var request = scenario.PreviewRequest(hasVat: true, unitPriceBeforeVat: 100m,
            taxId: 5);

        var result = await scenario.Service.PreviewCommercialAsync(1, 10, request);

        result.Header!.ReceiptSubtotalBeforeVat.Should().Be(600m);
        result.Header.ReceiptVatAmount.Should().Be(60m);
    }

    [Theory]
    [InlineData("stale-row-version")]
    [InlineData("missing-line")]
    [InlineData("invalid-price")]
    [InlineData("invalid-tax")]
    [InlineData("confirmed")]
    public async Task Commercial_preview_rejects_untrusted_or_stale_input(string failure)
    {
        var scenario = Scenario.Create();
        var request = scenario.PreviewRequest(hasVat: true, unitPriceBeforeVat: 100m,
            taxId: 5);
        switch (failure)
        {
            case "stale-row-version":
                request.RowVersion = Convert.ToBase64String([9, 9, 9]);
                break;
            case "missing-line":
                request.Lines.RemoveAt(0);
                break;
            case "invalid-price":
                request.Lines[0].UnitPriceBeforeVat = 0m;
                break;
            case "invalid-tax":
                request.Lines[0].TaxId = 999;
                break;
            case "confirmed":
                scenario.Receipt.Status = StockDocumentStatus.Confirmed;
                break;
        }

        var action = () => scenario.Service.PreviewCommercialAsync(1, 10, request);

        await action.Should().ThrowAsync<BusinessRuleException>();
        scenario.SaveCalls.Should().Be(0);
    }

    [Fact]
    public void Evidence_fingerprint_is_deterministic_and_changes_with_evidence()
    {
        var first = new GaoApp.Application.DTOs.Inventory.InputInvoices.InputInvoiceReconciliationDto
        {
            StockDocumentId = 10,
            InputInvoiceHeadId = 20,
            State = InputInvoiceReconciliationState.Mismatch,
            Header = new()
            {
                ReceiptSubtotalBeforeVat = 100m,
                XmlTotalBeforeTax = 99m,
                SubtotalDifference = 1m
            }
        };
        var same = new GaoApp.Application.DTOs.Inventory.InputInvoices.InputInvoiceReconciliationDto
        {
            StockDocumentId = 10,
            InputInvoiceHeadId = 20,
            State = InputInvoiceReconciliationState.Mismatch,
            Header = new()
            {
                ReceiptSubtotalBeforeVat = 100m,
                XmlTotalBeforeTax = 99m,
                SubtotalDifference = 1m
            }
        };

        var firstFingerprint = InputInvoiceReconciliationPolicy.Fingerprint(first);
        Assert.Equal(firstFingerprint, InputInvoiceReconciliationPolicy.Fingerprint(same));
        same.Header.XmlTotalBeforeTax = 98m;
        Assert.NotEqual(firstFingerprint, InputInvoiceReconciliationPolicy.Fingerprint(same));
    }

    [Fact]
    public async Task Central_service_is_constructed_and_invoked_for_no_xml_receipt_without_changing_workflow()
    {
        var receipt = new StockDocument
        {
            Id = 10,
            StoreId = 1,
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.PendingApproval
        };
        var repository = Proxy<IInputInvoiceRepository>((method, _) => method.Name switch
        {
            nameof(IInputInvoiceRepository.LockReceiptForReconciliationAsync) =>
                Task.FromResult<StockDocument?>(receipt),
            nameof(IInputInvoiceRepository.GetReconciliationsAsync) =>
                Task.FromResult(new List<StockDocumentInputInvoiceReconciliation>()),
            _ => throw new InvalidOperationException(method.Name)
        });
        var mapping = Proxy<IInputInvoiceItemCatalogMappingService>((method, _) =>
            throw new InvalidOperationException(
                $"No-XML flow must not resolve item mappings: {method.Name}"));
        IInputInvoiceReconciliationService service =
            new InputInvoiceReconciliationService(repository, mapping, new UserStub());

        var result = await service.CalculateAsync(1, 10);

        Assert.Equal(InputInvoiceReconciliationState.NotApplicable, result.State);
        Assert.True(result.ConfirmReady);
    }

    [Fact]
    public async Task Multiple_receipt_lines_reconcile_by_aggregated_base_quantity()
    {
        var scenario = Scenario.Create();

        var result = await scenario.Service.CalculateAsync(1, 10);

        Assert.Equal(InputInvoiceReconciliationState.Matched, result.State);
        var detail = Assert.Single(result.Details);
        Assert.Equal(10m, detail.ReceiptBaseQuantity);
        Assert.Equal(10m, detail.DerivedBaseQuantity);
        Assert.Equal(0m, detail.QuantityDifference);
        Assert.Equal(InputInvoiceDetailReconciliationState.Matched, detail.State);
        Assert.True(result.ConfirmReady);
    }

    [Fact]
    public async Task True_cross_unit_quantity_uses_same_product_base_quantity_not_conversion_identity()
    {
        var scenario = Scenario.Create();
        ConfigureCrossUnitScenario(scenario, xmlQuantity: 1m,
            xmlBeforeVatAmount: 1_440_000m, xmlVatRate: "10%", xmlVatAmount: 144_000m);

        var result = await scenario.Service.CalculateAsync(1, 10);

        var detail = Assert.Single(result.Details);
        Assert.Equal(71, scenario.Receipt.Lines.Single().ProductUnitConversionId);
        Assert.Equal(72, scenario.CatalogMappings[30].ProductUnitConversionId);
        Assert.Equal(48m, detail.ReceiptBaseQuantity);
        Assert.Equal(48m, detail.DerivedBaseQuantity);
        Assert.Equal(0m, detail.QuantityDifference);
        Assert.Equal(InputInvoiceDetailReconciliationState.Matched, detail.State);
        Assert.Equal(InputInvoiceReconciliationState.Matched, result.State);
    }

    [Fact]
    public async Task Cross_unit_quantity_mismatch_is_not_misclassified_as_price_mismatch()
    {
        var scenario = Scenario.Create();
        ConfigureCrossUnitScenario(scenario, xmlQuantity: 2m,
            xmlBeforeVatAmount: 2_880_000m, xmlVatRate: "10%", xmlVatAmount: 144_000m);

        var result = await scenario.Service.CalculateAsync(1, 10);

        var detail = Assert.Single(result.Details);
        Assert.Equal(48m, detail.ReceiptBaseQuantity);
        Assert.Equal(96m, detail.DerivedBaseQuantity);
        Assert.Equal(-48m, detail.QuantityDifference);
        Assert.Equal(InputInvoiceDetailReconciliationState.QuantityMismatch, detail.State);
        Assert.Equal(30_000m, ReadNullableDecimal(detail,
            "ReceiptBaseUnitPriceBeforeVat"));
        Assert.Equal(30_000m, ReadNullableDecimal(detail,
            "XmlBaseUnitPriceBeforeVat"));
    }

    [Fact]
    public async Task Comparable_base_unit_price_matches_across_loc_and_thung()
    {
        var scenario = Scenario.Create();
        ConfigureCrossUnitScenario(scenario, xmlQuantity: 1m,
            xmlBeforeVatAmount: 1_440_000m, xmlVatRate: "10%", xmlVatAmount: 144_000m);

        var detail = Assert.Single((await scenario.Service.CalculateAsync(1, 10)).Details);

        Assert.Equal(30_000m, ReadNullableDecimal(detail,
            "ReceiptBaseUnitPriceBeforeVat"));
        Assert.Equal(30_000m, ReadNullableDecimal(detail,
            "XmlBaseUnitPriceBeforeVat"));
        Assert.Equal(0m, ReadNullableDecimal(detail, "BaseUnitPriceDifference"));
        Assert.Equal(InputInvoiceDetailReconciliationState.Matched, detail.State);
    }

    [Fact]
    public async Task Comparable_base_unit_price_mismatch_is_reconciliation_evidence_only()
    {
        var scenario = Scenario.Create();
        ConfigureCrossUnitScenario(scenario, xmlQuantity: 1m,
            xmlBeforeVatAmount: 1_920_000m, xmlVatRate: "10%", xmlVatAmount: 144_000m);

        var result = await scenario.Service.CalculateAsync(1, 10);

        var detail = Assert.Single(result.Details);
        Assert.Equal(30_000m, ReadNullableDecimal(detail,
            "ReceiptBaseUnitPriceBeforeVat"));
        Assert.Equal(40_000m, ReadNullableDecimal(detail,
            "XmlBaseUnitPriceBeforeVat"));
        Assert.Equal(-10_000m, ReadNullableDecimal(detail, "BaseUnitPriceDifference"));
        Assert.Equal(InputInvoiceDetailReconciliationState.AmountMismatch, detail.State);
        Assert.True(result.ConfirmReady);
    }

    [Fact]
    public async Task Vat_mismatch_remains_visible_when_cross_unit_base_price_matches()
    {
        var scenario = Scenario.Create();
        ConfigureCrossUnitScenario(scenario, xmlQuantity: 1m,
            xmlBeforeVatAmount: 1_440_000m, xmlVatRate: "8%", xmlVatAmount: 115_200m);

        var detail = Assert.Single((await scenario.Service.CalculateAsync(1, 10)).Details);

        Assert.Equal(0m, ReadNullableDecimal(detail, "BaseUnitPriceDifference"));
        Assert.Equal(InputInvoiceDetailReconciliationState.VatMismatch, detail.State);
    }

    [Fact]
    public async Task Association_only_can_reconcile_xml_unit_without_durable_item_map()
    {
        var scenario = Scenario.Create();
        ConfigureCrossUnitScenario(scenario, xmlQuantity: 1m,
            xmlBeforeVatAmount: 1_440_000m, xmlVatRate: "10%", xmlVatAmount: 144_000m);
        scenario.CatalogMappings.Clear();
        scenario.UnitTargets.Add(new InputInvoiceItemCatalogTarget(
            new ProductVariant { Id = 60 },
            new ProductUnitConversion
            {
                Id = 72, ProductVariantId = 60, UnitId = 82,
                Factor = 48m, IsActive = true
            },
            new Product { Id = 61, BaseUnitId = 80 },
            new Unit { Id = 82, Name = "Thùng", Code = "THUNG", IsActive = true },
            new Unit { Id = 80, Name = "Hộp", Code = "HOP", IsActive = true }));

        var result = await scenario.Service.CalculateAsync(1, 10);

        var detail = Assert.Single(result.Details);
        Assert.Null(detail.MappingId);
        Assert.Equal(48m, detail.DerivedBaseQuantity);
        Assert.Equal(InputInvoiceDetailReconciliationState.Matched, detail.State);
        Assert.True(result.ConfirmReady);
    }

    [Fact]
    public async Task Read_model_exposes_bounded_receipt_line_display_context()
    {
        var scenario = Scenario.Create();

        var result = await scenario.Service.CalculateAsync(1, 10);

        var detail = Assert.Single(result.Details);
        var receiptLinesProperty = detail.GetType().GetProperty("ReceiptLines");
        Assert.NotNull(receiptLinesProperty);
        var receiptLines = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                receiptLinesProperty!.GetValue(detail))
            .Cast<object>()
            .OrderBy(ReadIntId)
            .ToList();
        Assert.Collection(receiptLines,
            first => AssertReceiptLineContext(first, 50, "Gạo", "kg", 5m, 1m, 5m),
            second => AssertReceiptLineContext(second, 51, "Gạo", "thùng", 1m, 5m, 5m));
    }

    [Fact]
    public async Task Before_vat_money_and_numeric_vat_mismatch_are_detected()
    {
        var scenario = Scenario.Create();
        scenario.Invoice.Details.Single().LineAmount = 97m;
        scenario.Invoice.Details.Single().VatAmount = 8m;
        scenario.Invoice.TotalBeforeTax = 97m;
        scenario.Invoice.TotalTaxAmount = 8m;
        scenario.Invoice.TotalPaymentAmount = 105m;

        var result = await scenario.Service.CalculateAsync(1, 10);

        Assert.Equal(InputInvoiceReconciliationState.Mismatch, result.State);
        var detail = Assert.Single(result.Details);
        Assert.Equal(3m, detail.AmountDifference);
        Assert.Equal(2m, detail.VatAmountDifference);
        Assert.Equal(InputInvoiceDetailReconciliationState.VatMismatch,
            detail.State);
    }

    [Fact]
    public async Task Unsupported_vat_and_unexplained_header_total_require_review()
    {
        var scenario = Scenario.Create();
        scenario.Invoice.Details.Single().VatRate = "KCT";
        scenario.Invoice.TotalPaymentAmount = 999m;

        var result = await scenario.Service.CalculateAsync(1, 10);

        Assert.Equal(InputInvoiceReconciliationState.Mismatch, result.State);
        Assert.Equal(InputInvoiceDetailReconciliationState.NeedsReview,
            Assert.Single(result.Details).State);
        Assert.True(result.Header!.NeedsReview);
        Assert.True(result.ConfirmReady);
    }

    [Fact]
    public async Task Selected_line_without_detail_and_unmatched_xml_are_incomplete()
    {
        var scenario = Scenario.Create();
        foreach (var map in scenario.Receipt.LineInputInvoiceMaps)
            map.InputInvoiceDetailId = null;

        var result = await scenario.Service.CalculateAsync(1, 10);

        Assert.Equal(InputInvoiceReconciliationState.Incomplete, result.State);
        Assert.Contains(result.Details, x =>
            x.State == InputInvoiceDetailReconciliationState.Unmatched);
        Assert.True(result.ConfirmReady);
    }

    [Fact]
    public async Task Manager_acceptance_requires_mismatch_reason_and_is_invalidated_by_evidence_drift()
    {
        var scenario = Scenario.Create();
        scenario.Invoice.Details.Single().LineAmount = 97m;
        scenario.Invoice.TotalBeforeTax = 97m;
        scenario.Invoice.TotalPaymentAmount = 107m;
        var mismatch = await scenario.Service.GetForReceiptAsync(1, 10);
        Assert.Equal(InputInvoiceReconciliationState.Mismatch, mismatch.State);

        var accepted = await scenario.Service.AcceptMismatchWithinTransactionAsync(
            1, 10, "Manager xác nhận chênh lệch", mismatch.EvidenceFingerprint);

        Assert.Equal(InputInvoiceReconciliationState.AcceptedMismatch, accepted.State);
        Assert.Equal("Manager xác nhận chênh lệch", accepted.AcceptanceReason);
        Assert.Equal(7, accepted.AcceptedByUserId);
        var acceptanceAudit = Assert.Single(scenario.Audits, x => x.EventType ==
            PurchaseReceiptAuditEventType.InputInvoiceReconciliationAccepted);
        Assert.Equal("Manager xác nhận chênh lệch", acceptanceAudit.Reason);
        using (var payload = JsonDocument.Parse(acceptanceAudit.NewValuesJson))
        {
            var root = payload.RootElement;
            Assert.Equal(20, root.GetProperty("InputInvoiceHeadId").GetInt32());
            Assert.Equal("Mismatch", root.GetProperty("OldState").GetString());
            Assert.Equal("AcceptedMismatch", root.GetProperty("NewState").GetString());
            Assert.Equal(InputInvoiceReconciliationPolicy.QuantityTolerance,
                root.GetProperty("QuantityTolerance").GetDecimal());
            Assert.Equal(InputInvoiceReconciliationPolicy.MoneyTolerance,
                root.GetProperty("MoneyTolerance").GetDecimal());
            Assert.Equal(mismatch.EvidenceFingerprint,
                root.GetProperty("EvidenceFingerprint").GetString());

            var detailEvidence = Assert.Single(
                root.GetProperty("Details").EnumerateArray().ToArray());
            Assert.Equal(30,
                detailEvidence.GetProperty("InputInvoiceDetailId").GetInt32());
            Assert.Equal(new[] { 50, 51 }, detailEvidence
                .GetProperty("StockDocumentLineIds")
                .EnumerateArray().Select(x => x.GetInt32()).ToArray());
            Assert.True(detailEvidence.TryGetProperty("State", out _));
            Assert.True(detailEvidence.TryGetProperty("QuantityDifference", out _));
            Assert.True(detailEvidence.TryGetProperty("AmountDifference", out _));
            Assert.True(detailEvidence.TryGetProperty("ReceiptVatRate", out _));
            Assert.True(detailEvidence.TryGetProperty("XmlVatRate", out _));
            Assert.True(detailEvidence.TryGetProperty("VatAmountDifference", out _));

            var headerEvidence = root.GetProperty("Header");
            Assert.True(headerEvidence.TryGetProperty("SubtotalDifference", out _));
            Assert.True(headerEvidence.TryGetProperty("VatDifference", out _));
            Assert.True(headerEvidence.TryGetProperty("PaymentDifference", out _));
            Assert.True(headerEvidence.TryGetProperty("NeedsReview", out _));
        }

        scenario.Invoice.Details.Single().LineAmount = 96m;
        scenario.Invoice.TotalBeforeTax = 96m;
        scenario.Invoice.TotalPaymentAmount = 106m;
        var drifted = await scenario.Service.GetForReceiptAsync(1, 10);

        Assert.Equal(InputInvoiceReconciliationState.Mismatch, drifted.State);
        Assert.Null(scenario.Rows.Single().AcceptedEvidenceFingerprint);
        Assert.Contains(scenario.Audits, x => x.EventType ==
            PurchaseReceiptAuditEventType
                .InputInvoiceReconciliationAcceptanceInvalidated);
    }

    [Fact]
    public async Task Incomplete_reconciliation_can_never_be_manager_accepted()
    {
        var scenario = Scenario.Create();
        foreach (var map in scenario.Receipt.LineInputInvoiceMaps)
            map.InputInvoiceDetailId = null;
        var incomplete = await scenario.Service.GetForReceiptAsync(1, 10);

        var action = () => scenario.Service.AcceptMismatchWithinTransactionAsync(
            1, 10, "Không được phép", incomplete.EvidenceFingerprint);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(action);
        Assert.Contains("chưa đầy đủ", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(scenario.Audits, x => x.EventType ==
            PurchaseReceiptAuditEventType.InputInvoiceReconciliationAccepted);
    }

    [Fact]
    public async Task Confirmed_receipt_without_legacy_snapshot_is_calculated_read_only()
    {
        var scenario = Scenario.Create();
        scenario.Receipt.Status = StockDocumentStatus.Confirmed;

        var result = await scenario.Service.GetForReceiptAsync(1, 10);

        Assert.Equal(InputInvoiceReconciliationState.Matched, result.State);
        Assert.Empty(scenario.Rows);
        Assert.Empty(scenario.Audits);
    }

    [Fact]
    public async Task Product_aggregate_combines_multiple_receipt_and_xml_units_without_double_counting()
    {
        var scenario = Scenario.Create();
        ConfigureMixedProductAggregateScenario(scenario);

        var result = await scenario.Service.CalculateAsync(1, 10);

        var product = Assert.Single(ReadProductSummaries(result));
        Assert.Equal(60, ReadProperty<int>(product, "ProductVariantId"));
        Assert.Equal("Sữa A", ReadProperty<string>(product, "ProductDisplayName"));
        Assert.Equal(2, ReadProperty<int>(product, "ReceiptLineCount"));
        Assert.Equal(2, ReadProperty<int>(product, "XmlDetailCount"));
        Assert.Equal(108m, ReadProperty<decimal>(product, "ReceiptBaseQuantity"));
        Assert.Equal(108m, ReadProperty<decimal>(product, "XmlBaseQuantity"));
        Assert.Equal(0m, ReadProperty<decimal>(product, "QuantityDifference"));
        Assert.Equal("Matched", ReadProperty<string>(product, "QuantityStatus"));
        Assert.Equal(30_000m,
            ReadProperty<decimal?>(product, "ReceiptBaseUnitPriceBeforeVat"));
        Assert.Equal(30_000m,
            ReadProperty<decimal?>(product, "XmlBaseUnitPriceBeforeVat"));
        Assert.Equal("Matched", ReadProperty<string>(product, "PriceStatus"));
        Assert.Equal("Mismatch", ReadProperty<string>(product, "VatStatus"));
        Assert.Equal(1, ReadProperty<int>(result, "ProductCount"));
        Assert.Equal(1, ReadProperty<int>(result, "MatchedProductCount"));
        Assert.Equal(0, ReadProperty<int>(result, "DifferingProductCount"));
        Assert.Equal(0, ReadProperty<int>(result, "UnresolvedXmlDetailCount"));
        Assert.True(result.ConfirmReady);
    }

    [Fact]
    public async Task Product_aggregate_includes_unassigned_same_product_receipt_lines_from_authoritative_item_maps()
    {
        var scenario = Scenario.Create();
        ConfigureMixedProductAggregateScenario(scenario);
        foreach (var lineMap in scenario.Receipt.LineInputInvoiceMaps)
        {
            lineMap.UseInputInvoice = false;
            lineMap.InputInvoiceDetailId = null;
            lineMap.InputInvoiceDetail = null;
            lineMap.MatchStatus = InputInvoiceMatchStatus.None;
            lineMap.ExclusionReason = null;
        }

        var result = await scenario.Service.CalculateAsync(1, 10);

        Assert.All(result.Details, detail =>
        {
            Assert.Equal(InputInvoiceDetailReconciliationState.Unmatched, detail.State);
            Assert.Empty(detail.ReceiptLines);
        });
        var product = Assert.Single(ReadProductSummaries(result));
        Assert.Equal(60, ReadProperty<int>(product, "ProductVariantId"));
        Assert.Equal(2, ReadProperty<int>(product, "ReceiptLineCount"));
        Assert.Equal(2, ReadProperty<int>(product, "XmlDetailCount"));
        Assert.Equal(108m, ReadProperty<decimal>(product, "ReceiptBaseQuantity"));
        Assert.Equal(108m, ReadProperty<decimal>(product, "XmlBaseQuantity"));
        Assert.Equal(0, result.ExcludedLineCount);
        Assert.True(result.ConfirmReady);
        Assert.All(scenario.Receipt.LineInputInvoiceMaps, lineMap =>
        {
            Assert.False(lineMap.UseInputInvoice);
            Assert.Null(lineMap.InputInvoiceDetailId);
            Assert.Equal(InputInvoiceMatchStatus.None, lineMap.MatchStatus);
        });
    }

    [Fact]
    public async Task Product_aggregate_excludes_only_explicitly_excluded_same_product_receipt_line()
    {
        var scenario = Scenario.Create();
        ConfigureMixedProductAggregateScenario(scenario);
        foreach (var lineMap in scenario.Receipt.LineInputInvoiceMaps)
        {
            lineMap.UseInputInvoice = false;
            lineMap.InputInvoiceDetailId = null;
            lineMap.InputInvoiceDetail = null;
            lineMap.MatchStatus = InputInvoiceMatchStatus.None;
            lineMap.ExclusionReason = null;
        }
        var excludedMap = scenario.Receipt.LineInputInvoiceMaps
            .Single(x => x.StockDocumentLineId == 51);
        excludedMap.MatchStatus = InputInvoiceMatchStatus.Excluded;
        excludedMap.ExclusionReason = "Không thuộc hóa đơn";

        var result = await scenario.Service.CalculateAsync(1, 10);

        var product = Assert.Single(ReadProductSummaries(result));
        Assert.Equal(1, ReadProperty<int>(product, "ReceiptLineCount"));
        Assert.Equal(96m, ReadProperty<decimal>(product, "ReceiptBaseQuantity"));
        Assert.Equal(1, result.ExcludedLineCount);
        Assert.Equal(51, Assert.Single(result.ExcludedLines).StockDocumentLineId);
        Assert.True(result.ConfirmReady);
    }

    [Theory]
    [InlineData(96, 31_500, "XmlShort", "XmlHigher", -12)]
    [InlineData(120, 28_500, "XmlExcess", "XmlLower", 12)]
    public async Task Product_aggregate_reports_quantity_and_price_direction(
        decimal xmlBaseQuantity,
        decimal xmlBasePrice,
        string quantityStatus,
        string priceStatus,
        decimal expectedQuantityDifference)
    {
        var scenario = Scenario.Create();
        ConfigureSingleProductAggregateScenario(
            scenario, receiptBaseQuantity: 108m, receiptBasePrice: 30_000m,
            xmlBaseQuantity, xmlBasePrice);

        var result = await scenario.Service.CalculateAsync(1, 10);

        var product = Assert.Single(ReadProductSummaries(result));
        Assert.Equal(expectedQuantityDifference,
            ReadProperty<decimal>(product, "QuantityDifference"));
        Assert.Equal(quantityStatus, ReadProperty<string>(product, "QuantityStatus"));
        Assert.Equal(xmlBasePrice - 30_000m,
            ReadProperty<decimal?>(product, "BaseUnitPriceDifference"));
        Assert.Equal(priceStatus, ReadProperty<string>(product, "PriceStatus"));
        Assert.Equal(0, ReadProperty<int>(result, "MatchedProductCount"));
        Assert.Equal(1, ReadProperty<int>(result, "DifferingProductCount"));
    }

    [Fact]
    public async Task Product_aggregate_excludes_conflicting_manual_and_item_map_membership()
    {
        var scenario = Scenario.Create();
        scenario.CatalogMappings[30].ProductVariantId = 61;
        scenario.UnitTargets.Add(new InputInvoiceItemCatalogTarget(
            new ProductVariant { Id = 60 },
            new ProductUnitConversion
            {
                Id = 70, ProductVariantId = 60, UnitId = 80,
                Factor = 5m, IsActive = true
            },
            new Product { Id = 61, BaseUnitId = 81 },
            new Unit { Id = 80, Name = "thùng", Code = "THUNG", IsActive = true },
            new Unit { Id = 81, Name = "kg", Code = "KG", IsActive = true }));

        var result = await scenario.Service.CalculateAsync(1, 10);

        var product = Assert.Single(ReadProductSummaries(result));
        Assert.Equal(60, ReadProperty<int>(product, "ProductVariantId"));
        Assert.Equal(0, ReadProperty<int>(product, "XmlDetailCount"));
        Assert.Equal(1, ReadProperty<int>(result, "UnresolvedXmlDetailCount"));
    }

    [Fact]
    public async Task Product_aggregate_excludes_ambiguous_manual_membership_without_guessing()
    {
        var scenario = Scenario.Create();
        scenario.Receipt.Lines.OrderBy(x => x.Id).Last().ProductVariantId = 61;

        var result = await scenario.Service.CalculateAsync(1, 10);

        Assert.Equal(2, ReadProductSummaries(result).Count);
        Assert.All(ReadProductSummaries(result), product =>
            Assert.Equal(0, ReadProperty<int>(product, "XmlDetailCount")));
        Assert.Equal(1, ReadProperty<int>(result, "UnresolvedXmlDetailCount"));
    }

    [Fact]
    public async Task Product_aggregate_does_not_treat_no_code_suggestion_as_authoritative_without_manual_evidence()
    {
        var scenario = Scenario.Create();
        scenario.Receipt.LineInputInvoiceMaps.Clear();
        var mapping = scenario.CatalogMappings[30];
        mapping.State = InputInvoiceItemCatalogResolutionState.NeedsConfirmation;
        mapping.ReasonCode = "NameUnitSuggestionRequiresConfirmation";

        var result = await scenario.Service.CalculateAsync(1, 10);

        Assert.Empty(ReadProductSummaries(result));
        Assert.Equal(1, ReadProperty<int>(result, "UnresolvedXmlDetailCount"));
    }

    [Fact]
    public async Task Product_aggregate_allows_no_code_suggestion_only_with_safe_manual_conversion()
    {
        var scenario = Scenario.Create();
        var mapping = scenario.CatalogMappings[30];
        mapping.State = InputInvoiceItemCatalogResolutionState.NeedsConfirmation;
        mapping.ReasonCode = "NameUnitSuggestionRequiresConfirmation";
        scenario.UnitTargets.Add(new InputInvoiceItemCatalogTarget(
            new ProductVariant { Id = 60 },
            new ProductUnitConversion
            {
                Id = 70, ProductVariantId = 60, UnitId = 80,
                Factor = 5m, IsActive = true
            },
            new Product { Id = 61, BaseUnitId = 81 },
            new Unit { Id = 80, Name = "thùng", Code = "THUNG", IsActive = true },
            new Unit { Id = 81, Name = "kg", Code = "KG", IsActive = true }));

        var result = await scenario.Service.CalculateAsync(1, 10);

        var product = Assert.Single(ReadProductSummaries(result));
        Assert.Equal(60, ReadProperty<int>(product, "ProductVariantId"));
        Assert.Equal(1, ReadProperty<int>(product, "XmlDetailCount"));
        Assert.Equal(0, ReadProperty<int>(result, "UnresolvedXmlDetailCount"));
    }

    [Fact]
    public async Task Product_aggregate_keeps_unassigned_receipt_line_and_vat_review_visible()
    {
        var scenario = Scenario.Create();
        scenario.Receipt.LineInputInvoiceMaps.OrderBy(x => x.StockDocumentLineId)
            .Last().InputInvoiceDetailId = null;
        scenario.Invoice.Details.Single().VatRate = "KCT";

        var result = await scenario.Service.CalculateAsync(1, 10);

        var product = Assert.Single(ReadProductSummaries(result));
        Assert.Equal(2, ReadProperty<int>(product, "ReceiptLineCount"));
        Assert.Equal(10m, ReadProperty<decimal>(product, "ReceiptBaseQuantity"));
        Assert.Equal("NeedsReview", ReadProperty<string>(product, "VatStatus"));
        Assert.True(result.ConfirmReady);
    }

    [Fact]
    public async Task Product_aggregate_zero_denominators_do_not_invent_a_matched_price()
    {
        var scenario = Scenario.Create();
        ConfigureSingleProductAggregateScenario(
            scenario, receiptBaseQuantity: 0m, receiptBasePrice: 30_000m,
            xmlBaseQuantity: 0m, xmlBasePrice: 30_000m);

        var product = Assert.Single(ReadProductSummaries(
            await scenario.Service.CalculateAsync(1, 10)));

        Assert.Null(ReadProperty<decimal?>(product,
            "ReceiptBaseUnitPriceBeforeVat"));
        Assert.Null(ReadProperty<decimal?>(product,
            "XmlBaseUnitPriceBeforeVat"));
        Assert.Equal("InsufficientData", ReadProperty<string>(product, "PriceStatus"));
    }

    [Fact]
    public async Task Product_aggregate_vat_is_matched_only_when_all_member_evidence_matches()
    {
        var scenario = Scenario.Create();

        var product = Assert.Single(ReadProductSummaries(
            await scenario.Service.CalculateAsync(1, 10)));

        Assert.Equal("Matched", ReadProperty<string>(product, "VatStatus"));
    }

    private sealed class Scenario
    {
        private Scenario(StockDocument receipt, InputInvoiceHead invoice,
            IInputInvoiceReconciliationService service,
            List<StockDocumentInputInvoiceReconciliation> rows,
            List<PurchaseReceiptAuditEvent> audits,
            Dictionary<int, InputInvoiceItemCatalogResolutionDto> catalogMappings,
            List<InputInvoiceItemCatalogTarget> unitTargets,
            PreviewState previewState)
        {
            Receipt = receipt;
            Invoice = invoice;
            Service = service;
            Rows = rows;
            Audits = audits;
            CatalogMappings = catalogMappings;
            UnitTargets = unitTargets;
            PreviewState = previewState;
        }

        public StockDocument Receipt { get; }
        public InputInvoiceHead Invoice { get; }
        public IInputInvoiceReconciliationService Service { get; }
        public List<StockDocumentInputInvoiceReconciliation> Rows { get; }
        public List<PurchaseReceiptAuditEvent> Audits { get; }
        public Dictionary<int, InputInvoiceItemCatalogResolutionDto> CatalogMappings { get; }
        public List<InputInvoiceItemCatalogTarget> UnitTargets { get; }
        private PreviewState PreviewState { get; }
        public int SaveCalls => PreviewState.SaveCalls;

        public InputInvoiceCommercialPreviewRequest PreviewRequest(
            bool hasVat,
            decimal unitPriceBeforeVat,
            int? taxId = null) => new()
        {
            StockDocumentId = Receipt.Id,
            RowVersion = Convert.ToBase64String(Receipt.RowVersion),
            HasVat = hasVat,
            Lines = Receipt.Lines.OrderBy(x => x.Id).Select(x =>
                new InputInvoiceCommercialPreviewLineRequest
                {
                    StockDocumentLineId = x.Id,
                    UnitPriceBeforeVat = unitPriceBeforeVat,
                    TaxId = taxId
                }).ToList()
        };

        public static Scenario Create()
        {
            var invoice = new InputInvoiceHead
            {
                Id = 20, StoreId = 1, InvoiceNumber = "HD-20",
                TotalBeforeTax = 100m, TotalTaxAmount = 10m,
                TotalPaymentAmount = 110m
            };
            var detail = new InputInvoiceDetail
            {
                Id = 30, InputInvoiceHeadId = 20, InputInvoiceHead = invoice,
                LineNo = 1, ItemName = "Gạo", UnitName = "thùng",
                Quantity = 2m, LineAmount = 100m, VatRate = "10%", VatAmount = 10m
            };
            invoice.Details.Add(detail);
            var receipt = new StockDocument
            {
                Id = 10, StoreId = 1, Type = StockDocumentType.Receipt,
                Status = StockDocumentStatus.PendingApproval,
                SubtotalBeforeVat = 100m, VatAmount = 10m,
                RowVersion = [1, 2, 3]
            };
            var link = new StockDocumentInputInvoiceMap
            {
                Id = 40, StoreId = 1, StockDocumentId = 10,
                InputInvoiceHeadId = 20, InputInvoiceHead = invoice,
                StockDocument = receipt
            };
            receipt.InputInvoiceMaps.Add(link);
            for (var index = 0; index < 2; index++)
            {
                var line = new StockDocumentLine
                {
                    Id = 50 + index, StockDocumentId = 10, LineNo = index + 1,
                    ProductVariantId = 60, ProductUnitConversionId = 70,
                    UnitId = 80 + index, Factor = index == 0 ? 1m : 5m,
                    Quantity = index == 0 ? 5m : 1m,
                    BaseQuantity = 5m, LineTotal = 55m, VatAmount = 5m,
                    TaxRate = 10m, ProductNameSnapshot = "Gạo",
                    UnitNameSnapshot = index == 0 ? "kg" : "thùng"
                };
                receipt.Lines.Add(line);
                receipt.LineInputInvoiceMaps.Add(new StockDocumentLineInputInvoiceMap
                {
                    Id = 90 + index, StoreId = 1, StockDocumentId = 10,
                    StockDocument = receipt, StockDocumentLineId = line.Id,
                    StockDocumentLine = line, InputInvoiceDetailId = detail.Id,
                    InputInvoiceDetail = detail, UseInputInvoice = true
                });
            }
            var rows = new List<StockDocumentInputInvoiceReconciliation>();
            var audits = new List<PurchaseReceiptAuditEvent>();
            var unitTargets = new List<InputInvoiceItemCatalogTarget>();
            var previewState = new PreviewState();
            var repository = Proxy<IInputInvoiceRepository>((method, args) => method.Name switch
            {
                nameof(IInputInvoiceRepository.BeginSupplierResolutionTransactionAsync) =>
                    Task.CompletedTask,
                nameof(IInputInvoiceRepository.CommitSupplierResolutionTransactionAsync) =>
                    Task.CompletedTask,
                nameof(IInputInvoiceRepository.RollbackSupplierResolutionTransactionAsync) =>
                    Task.CompletedTask,
                nameof(IInputInvoiceRepository.LockReceiptForReconciliationAsync) =>
                    Task.FromResult<StockDocument?>(receipt),
                nameof(IInputInvoiceRepository.GetReconciliationsAsync) =>
                    Task.FromResult(rows),
                nameof(IInputInvoiceRepository.AddReconciliationAsync) =>
                    AddRow(rows,
                        (StockDocumentInputInvoiceReconciliation)args![0]!),
                nameof(IInputInvoiceRepository.AddDetailReconciliationsAsync) =>
                    AddDetails(rows,
                        (IEnumerable<StockDocumentInputInvoiceDetailReconciliation>)args![0]!),
                nameof(IInputInvoiceRepository.AddPurchaseReceiptAuditEventAsync) =>
                    AddAudit(audits, (PurchaseReceiptAuditEvent)args![0]!),
                nameof(IInputInvoiceRepository.GetInputInvoiceItemCatalogTargetsByUnitAsync) =>
                    Task.FromResult<IReadOnlyList<InputInvoiceItemCatalogTarget>>(unitTargets),
                nameof(IInputInvoiceRepository.SaveChangesAsync) => RecordSave(previewState),
                _ => throw new InvalidOperationException(method.Name)
            });
            var mappings = new Dictionary<int, InputInvoiceItemCatalogResolutionDto>
                {
                    [detail.Id] = new()
                    {
                        InputInvoiceDetailId = detail.Id,
                        MappingId = 100,
                        State = InputInvoiceItemCatalogResolutionState.Confirmed,
                        ProductVariantId = 60,
                        ProductUnitConversionId = 70,
                        ConfirmedUnitId = 80,
                        ConfirmedFactor = 5m,
                        ConfirmedBaseUnitId = 81
                    }
                };
            var itemMapping = Proxy<IInputInvoiceItemCatalogMappingService>((method, _) =>
                method.Name == nameof(IInputInvoiceItemCatalogMappingService
                    .ResolveForReceiptAsync)
                    ? Task.FromResult<IReadOnlyDictionary<int,
                        InputInvoiceItemCatalogResolutionDto>>(mappings)
                    : throw new InvalidOperationException(method.Name));
            var stockDocuments = Proxy<IStockDocumentRepository>((method, args) =>
                method.Name switch
                {
                    nameof(IStockDocumentRepository.GetTaxAsync) =>
                        Task.FromResult<Tax?>((int)args![0]! == 5
                            ? new Tax
                            {
                                Id = 5, StoreId = 1, Name = "VAT 10%",
                                Rate = 10m, IsActive = true
                            }
                            : null),
                    _ => throw new InvalidOperationException(method.Name)
                });
            return new Scenario(receipt, invoice,
                new InputInvoiceReconciliationService(repository, itemMapping,
                    new UserStub(), stockDocuments), rows, audits, mappings, unitTargets,
                previewState);
        }

        private static Task RecordSave(PreviewState state)
        {
            state.SaveCalls++;
            return Task.CompletedTask;
        }

        private static Task AddRow(
            ICollection<StockDocumentInputInvoiceReconciliation> rows,
            StockDocumentInputInvoiceReconciliation row)
        {
            rows.Add(row);
            return Task.CompletedTask;
        }

        private static Task AddDetails(
            IReadOnlyCollection<StockDocumentInputInvoiceReconciliation> rows,
            IEnumerable<StockDocumentInputInvoiceDetailReconciliation> details)
        {
            var row = rows.Single();
            foreach (var detail in details)
                if (!row.Details.Contains(detail)) row.Details.Add(detail);
            return Task.CompletedTask;
        }

        private static Task AddAudit(ICollection<PurchaseReceiptAuditEvent> audits,
            PurchaseReceiptAuditEvent audit)
        {
            audit.ActorUserId = 7;
            audit.OccurredAtUtc = DateTime.UtcNow;
            audits.Add(audit);
            return Task.CompletedTask;
        }
    }

    private sealed class PreviewState
    {
        public int SaveCalls { get; set; }
    }

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

    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 7;
        public string? UserName => "recon-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private static int ReadIntId(object value)
        => (int)(value.GetType().GetProperty("StockDocumentLineId")?.GetValue(value)
            ?? throw new InvalidOperationException("StockDocumentLineId is required."));

    private static decimal? ReadNullableDecimal(object value, string propertyName)
        => (decimal?)value.GetType().GetProperty(propertyName)?.GetValue(value)
            ?? throw new InvalidOperationException($"{propertyName} is required.");

    private static List<object> ReadProductSummaries(object value)
    {
        var property = value.GetType().GetProperty("ProductSummaries");
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                property!.GetValue(value))
            .Cast<object>()
            .ToList();
    }

    private static T ReadProperty<T>(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return (T)property!.GetValue(value)!;
    }

    private static void ConfigureMixedProductAggregateScenario(Scenario scenario)
    {
        scenario.Invoice.Details.Clear();
        scenario.Receipt.Lines.Clear();
        scenario.Receipt.LineInputInvoiceMaps.Clear();
        scenario.CatalogMappings.Clear();

        AddReceiptLine(scenario, 50, 1, "Thùng", 2m, 48m,
            2_880_000m, 288_000m, 30);
        AddReceiptLine(scenario, 51, 2, "Hộp", 12m, 1m,
            360_000m, 36_000m, 31);
        AddXmlDetail(scenario, 30, 1, "Thùng", 1m, 48m,
            1_440_000m, 144_000m);
        AddXmlDetail(scenario, 31, 2, "Hộp", 60m, 1m,
            1_800_000m, 180_000m);
        SetHeaderTotals(scenario, 3_240_000m, 324_000m);
    }

    private static void ConfigureSingleProductAggregateScenario(
        Scenario scenario,
        decimal receiptBaseQuantity,
        decimal receiptBasePrice,
        decimal xmlBaseQuantity,
        decimal xmlBasePrice)
    {
        scenario.Invoice.Details.Clear();
        scenario.Receipt.Lines.Clear();
        scenario.Receipt.LineInputInvoiceMaps.Clear();
        scenario.CatalogMappings.Clear();
        var receiptBeforeVat = receiptBaseQuantity * receiptBasePrice;
        var receiptVat = receiptBeforeVat / 10m;
        var xmlBeforeVat = xmlBaseQuantity * xmlBasePrice;
        var xmlVat = xmlBeforeVat / 10m;
        AddReceiptLine(scenario, 50, 1, "Hộp", receiptBaseQuantity, 1m,
            receiptBeforeVat, receiptVat, 30);
        AddXmlDetail(scenario, 30, 1, "Hộp", xmlBaseQuantity, 1m,
            xmlBeforeVat, xmlVat);
        scenario.Receipt.SubtotalBeforeVat = receiptBeforeVat;
        scenario.Receipt.VatAmount = receiptVat;
        scenario.Invoice.TotalBeforeTax = xmlBeforeVat;
        scenario.Invoice.TotalTaxAmount = xmlVat;
        scenario.Invoice.TotalPaymentAmount = xmlBeforeVat + xmlVat;
    }

    private static void AddReceiptLine(
        Scenario scenario,
        int id,
        int lineNo,
        string unitName,
        decimal quantity,
        decimal factor,
        decimal beforeVat,
        decimal vat,
        int? inputInvoiceDetailId)
    {
        var line = new StockDocumentLine
        {
            Id = id, StockDocumentId = 10, LineNo = lineNo,
            ProductVariantId = 60, ProductUnitConversionId = 70 + lineNo,
            UnitId = 80 + lineNo, Factor = factor, Quantity = quantity,
            BaseQuantity = quantity * factor, LineTotal = beforeVat + vat,
            VatAmount = vat, TaxRate = 10m, ProductNameSnapshot = "Sữa A",
            UnitNameSnapshot = unitName
        };
        scenario.Receipt.Lines.Add(line);
        scenario.Receipt.LineInputInvoiceMaps.Add(new StockDocumentLineInputInvoiceMap
        {
            Id = 90 + lineNo, StoreId = 1, StockDocumentId = 10,
            StockDocument = scenario.Receipt, StockDocumentLineId = id,
            StockDocumentLine = line, InputInvoiceDetailId = inputInvoiceDetailId,
            UseInputInvoice = true
        });
    }

    private static void AddXmlDetail(
        Scenario scenario,
        int id,
        int lineNo,
        string unitName,
        decimal quantity,
        decimal factor,
        decimal beforeVat,
        decimal vat)
    {
        var detail = new InputInvoiceDetail
        {
            Id = id, InputInvoiceHeadId = 20, InputInvoiceHead = scenario.Invoice,
            LineNo = lineNo, ItemName = "Sữa A", UnitName = unitName,
            Quantity = quantity, LineAmount = beforeVat,
            VatRate = "10%", VatAmount = vat
        };
        scenario.Invoice.Details.Add(detail);
        var map = scenario.Receipt.LineInputInvoiceMaps.SingleOrDefault(x =>
            x.InputInvoiceDetailId == id);
        if (map is not null) map.InputInvoiceDetail = detail;
        scenario.CatalogMappings[id] = new InputInvoiceItemCatalogResolutionDto
        {
            InputInvoiceDetailId = id,
            MappingId = 100 + lineNo,
            State = InputInvoiceItemCatalogResolutionState.Confirmed,
            ProductVariantId = 60,
            ProductName = "Sữa A",
            ProductUnitConversionId = 70 + lineNo,
            ConfirmedUnitId = 80 + lineNo,
            ConfirmedUnitName = unitName,
            ConfirmedFactor = factor,
            ConfirmedBaseUnitId = 89,
            ConfirmedBaseUnitName = "Hộp"
        };
    }

    private static void SetHeaderTotals(
        Scenario scenario,
        decimal beforeVat,
        decimal vat)
    {
        scenario.Receipt.SubtotalBeforeVat = beforeVat;
        scenario.Receipt.VatAmount = vat;
        scenario.Invoice.TotalBeforeTax = beforeVat;
        scenario.Invoice.TotalTaxAmount = vat;
        scenario.Invoice.TotalPaymentAmount = beforeVat + vat;
    }

    private static void ConfigureCrossUnitScenario(
        Scenario scenario,
        decimal xmlQuantity,
        decimal xmlBeforeVatAmount,
        string xmlVatRate,
        decimal xmlVatAmount)
    {
        var line = scenario.Receipt.Lines.OrderBy(x => x.Id).First();
        foreach (var other in scenario.Receipt.Lines.Where(x => x.Id != line.Id).ToList())
            scenario.Receipt.Lines.Remove(other);
        foreach (var other in scenario.Receipt.LineInputInvoiceMaps
                     .Where(x => x.StockDocumentLineId != line.Id).ToList())
            scenario.Receipt.LineInputInvoiceMaps.Remove(other);
        line.ProductUnitConversionId = 71;
        line.UnitId = 81;
        line.UnitNameSnapshot = "Lốc";
        line.Factor = 4m;
        line.Quantity = 12m;
        line.BaseQuantity = 48m;
        line.LineTotal = 1_584_000m;
        line.VatAmount = 144_000m;
        line.TaxRate = 10m;

        var xml = scenario.Invoice.Details.Single();
        xml.UnitName = "Thùng";
        xml.Quantity = xmlQuantity;
        xml.LineAmount = xmlBeforeVatAmount;
        xml.VatRate = xmlVatRate;
        xml.VatAmount = xmlVatAmount;
        scenario.Invoice.TotalBeforeTax = xmlBeforeVatAmount;
        scenario.Invoice.TotalTaxAmount = xmlVatAmount;
        scenario.Invoice.TotalPaymentAmount = xmlBeforeVatAmount + xmlVatAmount;
        scenario.Receipt.SubtotalBeforeVat = 1_440_000m;
        scenario.Receipt.VatAmount = 144_000m;

        var mapping = scenario.CatalogMappings[xml.Id];
        mapping.ProductVariantId = line.ProductVariantId;
        mapping.ProductUnitConversionId = 72;
        mapping.ConfirmedUnitId = 82;
        mapping.ConfirmedFactor = 48m;
        mapping.ConfirmedBaseUnitId = 80;
    }

    private static void AssertReceiptLineContext(
        object value,
        int expectedId,
        string expectedProduct,
        string expectedUnit,
        decimal expectedQuantity,
        decimal expectedFactor,
        decimal expectedBaseQuantity)
    {
        var type = value.GetType();
        Assert.Equal(expectedId,
            type.GetProperty("StockDocumentLineId")?.GetValue(value));
        Assert.Equal(expectedProduct,
            type.GetProperty("ProductName")?.GetValue(value));
        Assert.Equal(expectedUnit,
            type.GetProperty("UnitName")?.GetValue(value));
        Assert.Equal(expectedQuantity,
            type.GetProperty("Quantity")?.GetValue(value));
        Assert.Equal(expectedFactor,
            type.GetProperty("Factor")?.GetValue(value));
        Assert.Equal(expectedBaseQuantity,
            type.GetProperty("BaseQuantity")?.GetValue(value));
    }
}
