using System.Reflection;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

// R2.4-C2 coverage: owner guard and frozen receipt owner precede all posting side effects.
public sealed class PurchaseReceiptConfirmPrerequisiteTests
{
    private static readonly byte[] CurrentVersion = [1, 2, 3, 4];

    [Fact]
    public async Task Unresolved_provisional_blocks_inside_confirm_transaction_before_posting()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var fixture = CreateFixture(document, supplier);
        fixture.Repository.HasUnresolvedProvisional = true;

        var action = () => fixture.Service.ApproveCommercialAsync(
            document.Id, ValidCommercialRequest(supplier.Id));

        (await action.Should().ThrowAsync<BusinessRuleException>())
            .Which.SafeMessage.Should().Contain("UNRESOLVED_PROVISIONAL");
        fixture.Repository.BeginTransactionCalls.Should().Be(1);
        fixture.Repository.RollbackTransactionCalls.Should().Be(1);
        fixture.Repository.CommitTransactionCalls.Should().Be(0);
        fixture.Repository.AddedPayables.Should().BeEmpty();
        fixture.Movements.PreLockCalls.Should().Be(0);
        fixture.Movements.CreateCalls.Should().Be(0);
    }

    [Fact]
    public void Warehouse_date_resolution_is_read_only_and_does_not_add_a_persisted_receipt_field()
    {
        typeof(StockDocument).GetProperties().Select(x => x.Name)
            .Should().NotContain("WarehouseOccurredAtUtc");
        typeof(PurchaseReceiptWarehouseDatePolicy).GetMethod("Resolve")
            .Should().NotBeNull();
    }

    [Theory]
    [InlineData(true, "Người bán đã nhận tiền")]
    [InlineData(true, null)]
    [InlineData(false, null)]
    public async Task Direct_receipt_without_supplier_fails_even_when_paid_or_payee_is_present(
        bool paid,
        string? payee)
    {
        var document = CreateDirectReceipt();
        document.SupplierId = null;
        document.Supplier = null;
        var fixture = CreateFixture(document);
        var before = ReceiptSnapshot.Capture(document);

        var action = () => fixture.Service.ApproveCommercialAsync(
            document.Id,
            ValidCommercialRequest(null, paid, payee));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*chọn nhà cung cấp*");
        ReceiptSnapshot.Capture(document).Should().BeEquivalentTo(before);
        fixture.AssertNoMutationOrPosting();
        fixture.Repository.GetSupplierCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    public async Task Missing_or_invalid_RowVersion_fails_before_supplier_resolution(string? rowVersion)
    {
        var document = CreateDirectReceipt();
        var fixture = CreateFixture(document, CreateSupplier(51));
        var request = ValidCommercialRequest(51);
        request.RowVersion = rowVersion!;

        var action = () => fixture.Service.ApproveCommercialAsync(document.Id, request);

        await action.Should().ThrowAsync<BusinessRuleException>();
        fixture.Repository.GetSupplierCalls.Should().Be(0);
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Stale_RowVersion_fails_before_supplier_resolution()
    {
        var document = CreateDirectReceipt();
        var fixture = CreateFixture(document, CreateSupplier(51));
        var request = ValidCommercialRequest(51);
        request.RowVersion = Convert.ToBase64String([9, 9, 9]);

        var action = () => fixture.Service.ApproveCommercialAsync(document.Id, request);

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*người khác cập nhật*");
        fixture.Repository.GetSupplierCalls.Should().Be(0);
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Unresolved_or_foreign_store_supplier_fails_safely_before_mutation()
    {
        var document = CreateDirectReceipt();
        var fixture = CreateFixture(document);
        var before = ReceiptSnapshot.Capture(document);

        var action = () => fixture.Service.ApproveCommercialAsync(
            document.Id,
            ValidCommercialRequest(999));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*không tồn tại hoặc không thuộc cửa hàng hiện tại*");
        ReceiptSnapshot.Capture(document).Should().BeEquivalentTo(before);
        fixture.Repository.GetSupplierCalls.Should().Be(1);
        fixture.AssertNoMutationOrPosting();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Commercial_confirmation_rejects_cost_choice_without_its_source(
        bool includeVatWithoutVat,
        bool capitalizeFreightWithoutFreight)
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var fixture = CreateFixture(document, supplier);
        var request = ValidCommercialRequest(supplier.Id);
        request.IncludeVatInInventoryCost = includeVatWithoutVat;
        request.CapitalizeFreightInInventoryCost = capitalizeFreightWithoutFreight;

        var action = () => fixture.Service.ApproveCommercialAsync(document.Id, request);

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Chỉ có thể*");
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Valid_direct_supplier_is_accepted_and_existing_posting_flow_is_preserved()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var fixture = CreateFixture(document, supplier);

        await fixture.Service.ApproveCommercialAsync(
            document.Id,
            ValidCommercialRequest(supplier.Id, paid: false));

        document.Status.Should().Be(StockDocumentStatus.Confirmed);
        document.SupplierId.Should().Be(supplier.Id);
        document.Lines.Single().UnitPriceBeforeVat.Should().Be(12m);
        fixture.Repository.GetSupplierCalls.Should().Be(2,
            "commercial selection and the final service guard both resolve in the current Store");
        fixture.Repository.BeginTransactionCalls.Should().Be(1);
        fixture.Movements.PreLockCalls.Should().Be(1);
        fixture.Movements.CreateCalls.Should().Be(1);
        fixture.Repository.SaveCalls.Should().Be(1);
        fixture.Repository.CommitTransactionCalls.Should().Be(1);
        fixture.Repository.RollbackTransactionCalls.Should().Be(0);
        fixture.Repository.AddedPayables.Should().ContainSingle()
            .Which.SupplierId.Should().Be(supplier.Id);
    }

    [Fact]
    public async Task Final_authoritative_reconciliation_observes_applied_commercial_values_before_posting()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var observedAppliedPrice = false;
        var reconciliation = CreateProxy<IInputInvoiceReconciliationService>((method, _) =>
        {
            if (method.Name != nameof(IInputInvoiceReconciliationService
                    .EnsureConfirmableWithinTransactionAsync))
                throw new NotSupportedException(method.Name);
            observedAppliedPrice = document.Lines.Single().UnitPriceBeforeVat == 12m &&
                document.Lines.Single().LineTotal > 0m;
            return Task.FromResult(new InputInvoiceReconciliationDto
            {
                StockDocumentId = document.Id,
                State = InputInvoiceReconciliationState.NotApplicable
            });
        });
        var fixture = CreateFixtureWithReconciliation(document, reconciliation, supplier);

        await fixture.Service.ApproveCommercialAsync(
            document.Id, ValidCommercialRequest(supplier.Id));

        observedAppliedPrice.Should().BeTrue();
        fixture.Movements.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Xml_supplier_guard_should_fail_inside_transaction_before_any_posting()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var guardCalls = 0;
        var guard = CreateProxy<IInputInvoiceSupplierResolutionService>((method, _) =>
        {
            if (method.Name != nameof(
                    IInputInvoiceSupplierResolutionService.EnsureReceiptCanBeConfirmedAsync))
            {
                throw new NotSupportedException(method.Name);
            }

            guardCalls++;
            return Task.FromException(new BusinessRuleException(
                "Nhà cung cấp hóa đơn XML chưa sẵn sàng để xác nhận."));
        });
        var fixture = CreateFixture(document, guard, supplier);

        var action = () => fixture.Service.ApproveCommercialAsync(
            document.Id,
            ValidCommercialRequest(supplier.Id));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*XML chưa sẵn sàng*");
        guardCalls.Should().Be(1);
        fixture.Repository.BeginTransactionCalls.Should().Be(1);
        fixture.Repository.RollbackTransactionCalls.Should().Be(1);
        fixture.Repository.CommitTransactionCalls.Should().Be(0);
        fixture.Repository.SaveCalls.Should().Be(0);
        fixture.Repository.AddedPayables.Should().BeEmpty();
        fixture.Movements.PreLockCalls.Should().Be(0);
        fixture.Movements.CreateCalls.Should().Be(0);
        document.Status.Should().Be(StockDocumentStatus.PendingApproval);
    }

    [Fact]
    public async Task Actual_reconciliation_refresh_exception_rolls_back_before_posting_side_effects()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var calls = 0;
        var reconciliation = CreateProxy<IInputInvoiceReconciliationService>((method, _) =>
        {
            if (method.Name != nameof(IInputInvoiceReconciliationService
                    .EnsureConfirmableWithinTransactionAsync))
                throw new NotSupportedException(method.Name);
            calls++;
            return Task.FromException<GaoApp.Application.DTOs.Inventory.InputInvoices.InputInvoiceReconciliationDto>(
                new BusinessRuleException("Phiếu nhập đang liên kết nhiều hơn một hóa đơn."));
        });
        var fixture = CreateFixtureWithReconciliation(
            document, reconciliation, supplier);

        var action = () => fixture.Service.ApproveCommercialAsync(
            document.Id, ValidCommercialRequest(supplier.Id));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*liên kết nhiều hơn một hóa đơn*");
        calls.Should().Be(1);
        fixture.Repository.BeginTransactionCalls.Should().Be(1);
        fixture.Repository.RollbackTransactionCalls.Should().Be(1);
        fixture.Repository.CommitTransactionCalls.Should().Be(0);
        fixture.Repository.SaveCalls.Should().Be(0);
        fixture.Movements.CreateCalls.Should().Be(0);
        document.Status.Should().Be(StockDocumentStatus.PendingApproval);
    }

    [Theory]
    [InlineData(InputInvoiceReconciliationState.Incomplete)]
    [InlineData(InputInvoiceReconciliationState.Mismatch)]
    public async Task Reconciliation_completeness_state_does_not_block_posting(
        InputInvoiceReconciliationState state)
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var calls = 0;
        var reconciliation = CreateProxy<IInputInvoiceReconciliationService>((method, _) =>
        {
            if (method.Name != nameof(IInputInvoiceReconciliationService
                    .EnsureConfirmableWithinTransactionAsync))
                throw new NotSupportedException(method.Name);
            calls++;
            return Task.FromResult(new InputInvoiceReconciliationDto
            {
                StockDocumentId = document.Id,
                State = state
            });
        });
        var fixture = CreateFixtureWithReconciliation(
            document, reconciliation, supplier);

        await fixture.Service.ApproveCommercialAsync(
            document.Id, ValidCommercialRequest(supplier.Id));

        calls.Should().Be(1);
        document.Status.Should().Be(StockDocumentStatus.Confirmed);
        fixture.Repository.CommitTransactionCalls.Should().Be(1);
        fixture.Repository.RollbackTransactionCalls.Should().Be(0);
        fixture.Movements.CreateCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(false, false, 10)]
    [InlineData(true, false, 11)]
    [InlineData(false, true, 12)]
    [InlineData(true, true, 13)]
    public async Task Confirmation_posts_exact_selected_inventory_cost_without_changing_payables(
        bool includeVat,
        bool capitalizeFreight,
        decimal expectedBaseUnitCost)
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var line = document.Lines.Single();
        document.HasVat = true;
        document.IncludeVatInInventoryCost = includeVat;
        document.HasFreight = true;
        document.CapitalizeFreightInInventoryCost = capitalizeFreight;
        document.FreightTotal = 4m;
        document.FreightPayeeName = "Nhà vận chuyển";
        line.UnitPriceBeforeVat = 10m;
        line.TaxRate = 10m;
        line.VatAmount = 2m;
        line.UnitPriceAfterVat = 11m;
        line.UnitCost = 11m;
        line.LineTotal = 22m;
        line.FreightAllocation = capitalizeFreight ? 4m : 0m;
        var fixture = CreateFixture(document, supplier);

        await fixture.Service.ApproveAsync(document.Id, null, RowVersion(document));

        fixture.Movements.LastRequest.Should().NotBeNull();
        fixture.Movements.LastRequest!.UnitCost.Should().Be(expectedBaseUnitCost);
        fixture.Repository.AddedPayables.Should().HaveCount(2);
        fixture.Repository.AddedPayables.Select(x => x.Amount)
            .Should().BeEquivalentTo([22m, 4m]);
    }

    [Fact]
    public async Task Generic_confirmation_rejects_negative_balanced_freight_before_transaction()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        document.HasFreight = true;
        document.CapitalizeFreightInInventoryCost = true;
        document.FreightTotal = 20m;
        document.FreightPayeeName = "Nhà vận chuyển";
        document.Lines.Single().FreightAllocation = -10m;
        var secondLine = CreateValidLine(document);
        secondLine.Id = 22;
        secondLine.LineNo = 2;
        secondLine.ProductVariantId = 32;
        secondLine.FreightAllocation = 30m;
        document.Lines.Add(secondLine);
        var fixture = CreateFixture(document, supplier);

        var action = () => fixture.Service.ApproveAsync(document.Id, null, RowVersion(document));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*không được âm*");
        fixture.AssertNoMutationOrPosting();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Submit_rejects_incomplete_persisted_cost_policy_before_status_or_audit_mutation(int scenario)
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier, StockDocumentStatus.Draft);
        switch (scenario)
        {
            case 1:
                document.IncludeVatInInventoryCost = true;
                break;
            case 2:
                document.CapitalizeFreightInInventoryCost = true;
                break;
            case 3:
                document.HasFreight = true;
                document.CapitalizeFreightInInventoryCost = true;
                break;
            case 4:
                document.HasFreight = true;
                document.CapitalizeFreightInInventoryCost = true;
                document.FreightTotal = 20m;
                document.Lines.Single().FreightAllocation = 20m;
                break;
        }
        var fixture = CreateFixture(document, supplier);

        var action = () => fixture.Service.SubmitForApprovalAsync(
            document.Id,
            null,
            RowVersion(document));

        await action.Should().ThrowAsync<BusinessRuleException>();
        document.Status.Should().Be(StockDocumentStatus.Draft);
        PurchaseReceiptAuditEvidence.GetWorkflowIntent(document).Should().BeNull();
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Freight_update_rejects_incomplete_manual_allocation_ids_before_mutation()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier, StockDocumentStatus.Draft);
        var secondLine = CreateValidLine(document);
        secondLine.Id = 22;
        secondLine.LineNo = 2;
        secondLine.ProductVariantId = 32;
        document.Lines.Add(secondLine);
        var fixture = CreateFixture(document, supplier);

        var action = () => fixture.Service.UpdatePurchaseReceiptApprovalAsync(
            document.Id,
            new UpdatePurchaseReceiptApprovalRequest
            {
                RowVersion = RowVersion(document),
                HasFreight = true,
                CapitalizeFreightInInventoryCost = true,
                FreightTotal = 20m,
                FreightPayeeName = "Nhà vận chuyển",
                Allocations =
                [
                    new FreightAllocationInputDto
                    {
                        StockDocumentLineId = 21,
                        Amount = 20m
                    }
                ]
            });

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*không đầy đủ*");
        document.HasFreight.Should().BeFalse();
        document.Lines.Should().OnlyContain(x => x.FreightAllocation == 0m);
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Commercial_confirm_rejects_price_variance_without_explicit_acceptance()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var fixture = CreateFixture(document, supplier);
        fixture.Repository.LastPurchaseBasePrices[31] = 10m;
        var request = ValidCommercialRequest(supplier.Id);
        request.Lines.Single().ExpectedLastPurchaseUnitPriceBeforeVat = 10m;

        var action = () => fixture.Service.ApproveCommercialAsync(
            document.Id,
            request);

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*phải xác nhận*chênh lệch giá*");
        document.Status.Should().Be(StockDocumentStatus.PendingApproval);
        fixture.Repository.BeginTransactionCalls.Should().Be(0);
        fixture.Movements.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Commercial_confirm_accepts_price_variance_without_requiring_reason_and_audits_values()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var fixture = CreateFixture(document, supplier);
        fixture.Repository.LastPurchaseBasePrices[31] = 10m;
        var request = ValidCommercialRequest(supplier.Id);
        request.AcceptPriceVariance = true;
        request.ApprovalNote = null;
        request.Lines.Single().ExpectedLastPurchaseUnitPriceBeforeVat = 10m;

        await fixture.Service.ApproveCommercialAsync(document.Id, request);

        document.Status.Should().Be(StockDocumentStatus.Confirmed);
        PurchaseReceiptAuditEvidence.GetWorkflowIntent(document)?.Reason.Should().BeNull();
        PurchaseReceiptAuditEvidence.GetWorkflowIntent(document)?.Note.Should()
            .Contain("dòng 1: 10 -> 12");
        PurchaseReceiptAuditEvidence.GetWorkflowIntent(document)?.EvidenceValues.Should()
            .Contain(new KeyValuePair<string, object?>(
                "PriceVariance.Line.1.PreviousUnitPriceBeforeVat", 10m))
            .And.Contain(new KeyValuePair<string, object?>(
                "PriceVariance.Line.1.CurrentUnitPriceBeforeVat", 12m));
        fixture.Repository.PriceHistoryLockCalls.Should().Be(1);
        fixture.Repository.CommitTransactionCalls.Should().Be(1);
    }

    [Fact]
    public async Task Commercial_confirm_preserves_zero_snapshot_from_positive_fractional_history()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var line = document.Lines.Single();
        line.Quantity = 10m;
        line.Factor = 0.0001m;
        line.BaseQuantity = 0.001m;
        var fixture = CreateFixture(document, supplier);
        fixture.Repository.LastPurchaseBasePrices[31] = 0.01m;
        var request = ValidCommercialRequest(supplier.Id);
        request.Lines.Single().UnitPriceBeforeVat = 0.01m;
        request.Lines.Single().ExpectedLastPurchaseUnitPriceBeforeVat = 0m;

        var missingAcceptance = () => fixture.Service.ApproveCommercialAsync(
            document.Id,
            request);

        await missingAcceptance.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*phải xác nhận*chênh lệch giá*");
        document.Status.Should().Be(StockDocumentStatus.PendingApproval);
        fixture.Repository.BeginTransactionCalls.Should().Be(0);

        request.AcceptPriceVariance = true;
        await fixture.Service.ApproveCommercialAsync(document.Id, request);

        document.Status.Should().Be(StockDocumentStatus.Confirmed);
        var intent = PurchaseReceiptAuditEvidence.GetWorkflowIntent(document);
        intent.Should().NotBeNull();
        intent!.Note.Should().Contain("dòng 1: 0 ->");
        intent.EvidenceValues.Should().Contain(
            new KeyValuePair<string, object?>(
                "PriceVariance.Line.1.PreviousUnitPriceBeforeVat", 0m))
            .And.Contain(new KeyValuePair<string, object?>(
                "PriceVariance.Line.1.CurrentUnitPriceBeforeVat", 0.01m));
        fixture.Repository.PriceHistoryLockCalls.Should().Be(1);
        fixture.Repository.CommitTransactionCalls.Should().Be(1);
    }

    [Fact]
    public async Task Locked_revalidation_rejects_history_committed_after_early_validation()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var fixture = CreateFixture(document, supplier);
        fixture.Repository.LastPurchaseBasePrices[31] = 10m;
        fixture.Repository.OnPriceHistoryLock = () =>
            fixture.Repository.LastPurchaseBasePrices[31] = 11m;
        var request = ValidCommercialRequest(supplier.Id);
        request.AcceptPriceVariance = true;
        request.Lines.Single().ExpectedLastPurchaseUnitPriceBeforeVat = 10m;

        var action = () => fixture.Service.ApproveCommercialAsync(document.Id, request);

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*đã thay đổi*tải lại phiếu*");
        fixture.Repository.BeginTransactionCalls.Should().Be(1);
        fixture.Repository.PriceHistoryLockCalls.Should().Be(1);
        fixture.Repository.RollbackTransactionCalls.Should().Be(1);
        fixture.Movements.PreLockCalls.Should().Be(0);
        fixture.Movements.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Maximum_approval_note_yields_to_mandatory_price_and_overdelivery_evidence()
    {
        var supplier = CreateSupplier(51);
        var document = CreatePurchaseOrderReceipt(supplier);
        document.PurchaseOrder!.Lines.Single().OrderedQuantity = 1m;
        var fixture = CreateFixture(document, supplier);
        fixture.Repository.LastPurchaseBasePrices[31] = 10m;
        var request = ValidCommercialRequest(supplier.Id);
        request.AcceptPriceVariance = true;
        request.Lines.Single().ExpectedLastPurchaseUnitPriceBeforeVat = 10m;
        request.AcceptOverdelivery = true;
        request.ApprovalNote = new string('a', 1000);

        await fixture.Service.ApproveCommercialAsync(document.Id, request);

        var intent = PurchaseReceiptAuditEvidence.GetWorkflowIntent(document);
        intent.Should().NotBeNull();
        intent!.Note.Should().Contain("dòng 1: 10 -> 12");
        intent.Note.Should().Contain("lượng vượt tăng 1 base");
        intent.Note.Should().NotContain(new string('a', 1000));
        intent.Note!.Length.Should().BeLessThanOrEqualTo(1000);
        intent.EvidenceValues.Should().ContainKeys(
            "PriceVariance.Line.1.PreviousUnitPriceBeforeVat",
            "PriceVariance.Line.1.CurrentUnitPriceBeforeVat");
    }

    [Fact]
    public async Task Commercial_confirm_does_not_require_acceptance_when_converted_last_price_matches()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        document.Lines.Single().Factor = 3m;
        document.Lines.Single().BaseQuantity = 6m;
        var fixture = CreateFixture(document, supplier);
        fixture.Repository.LastPurchaseBasePrices[31] = 4m;
        var request = ValidCommercialRequest(supplier.Id);
        request.Lines.Single().ExpectedLastPurchaseUnitPriceBeforeVat = 12m;

        await fixture.Service.ApproveCommercialAsync(
            document.Id,
            request);

        document.Status.Should().Be(StockDocumentStatus.Confirmed);
        PurchaseReceiptAuditEvidence.GetWorkflowIntent(document)?.Note.Should().BeNull();
    }

    [Fact]
    public async Task Commercial_confirm_rejects_stale_last_price_snapshot_even_when_variance_is_accepted()
    {
        var supplier = CreateSupplier(51);
        var document = CreateDirectReceipt(supplier);
        var fixture = CreateFixture(document, supplier);
        fixture.Repository.LastPurchaseBasePrices[31] = 11m;
        var request = ValidCommercialRequest(supplier.Id);
        request.AcceptPriceVariance = true;
        request.Lines.Single().ExpectedLastPurchaseUnitPriceBeforeVat = 10m;

        var action = () => fixture.Service.ApproveCommercialAsync(document.Id, request);

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*đã thay đổi*tải lại phiếu*");
        fixture.Repository.BeginTransactionCalls.Should().Be(0);
        fixture.Movements.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Purchase_order_receipt_missing_supplier_fails_before_mutation()
    {
        var supplier = CreateSupplier(51);
        var document = CreatePurchaseOrderReceipt(supplier);
        document.SupplierId = null;
        document.Supplier = null;
        var fixture = CreateFixture(document, supplier);

        var action = () => fixture.Service.ApproveCommercialAsync(
            document.Id,
            ValidCommercialRequest(null));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*chọn nhà cung cấp*");
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Purchase_order_receipt_with_different_supplier_fails_before_mutation()
    {
        var orderSupplier = CreateSupplier(51);
        var otherSupplier = CreateSupplier(52);
        var document = CreatePurchaseOrderReceipt(orderSupplier);
        var fixture = CreateFixture(document, orderSupplier, otherSupplier);
        var before = ReceiptSnapshot.Capture(document);

        var action = () => fixture.Service.ApproveCommercialAsync(
            document.Id,
            ValidCommercialRequest(otherSupplier.Id));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Không thể đổi nhà cung cấp*");
        ReceiptSnapshot.Capture(document).Should().BeEquivalentTo(before);
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Purchase_order_receipt_with_matching_supplier_remains_valid()
    {
        var supplier = CreateSupplier(51);
        var document = CreatePurchaseOrderReceipt(supplier);
        var fixture = CreateFixture(document, supplier);

        await fixture.Service.ApproveCommercialAsync(
            document.Id,
            ValidCommercialRequest(supplier.Id));

        document.Status.Should().Be(StockDocumentStatus.Confirmed);
        document.PurchaseOrder!.Lines.Single().ReceivedQuantity.Should().Be(2m);
        fixture.Movements.CreateCalls.Should().Be(1);
        fixture.Repository.CommitTransactionCalls.Should().Be(1);
    }

    [Fact]
    public async Task Commercial_confirm_rejects_overdelivery_without_explicit_acceptance()
    {
        var supplier = CreateSupplier(51);
        var document = CreatePurchaseOrderReceipt(supplier);
        document.PurchaseOrder!.Lines.Single().OrderedQuantity = 1m;
        var fixture = CreateFixture(document, supplier);
        var request = ValidCommercialRequest(supplier.Id);

        var action = () => fixture.Service.ApproveCommercialAsync(document.Id, request);

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*phải xác nhận chấp nhận nhận vượt*");
        document.Status.Should().Be(StockDocumentStatus.PendingApproval);
        fixture.Movements.CreateCalls.Should().Be(0);
        fixture.Repository.CommitTransactionCalls.Should().Be(0);
        fixture.Repository.RollbackTransactionCalls.Should().Be(1);
    }

    [Fact]
    public async Task Commercial_confirm_accepts_overdelivery_without_requiring_a_reason()
    {
        var supplier = CreateSupplier(51);
        var document = CreatePurchaseOrderReceipt(supplier);
        document.PurchaseOrder!.Lines.Single().OrderedQuantity = 1m;
        var fixture = CreateFixture(document, supplier);
        var request = ValidCommercialRequest(supplier.Id);
        request.AcceptOverdelivery = true;
        request.OverdeliveryNote = null;

        await fixture.Service.ApproveCommercialAsync(document.Id, request);

        document.Status.Should().Be(StockDocumentStatus.Confirmed);
        document.PurchaseOrder.Lines.Single().ReceivedQuantity.Should().Be(2m);
        fixture.Movements.CreateCalls.Should().Be(1);
        fixture.Repository.CommitTransactionCalls.Should().Be(1);
        PurchaseReceiptAuditEvidence.GetWorkflowIntent(document)?.Reason.Should().BeNull();
        PurchaseReceiptAuditEvidence.GetWorkflowIntent(document)?.Note.Should()
            .Contain("lượng vượt tăng 1 base");
    }

    [Fact]
    public async Task Generic_final_confirm_guard_rejects_missing_supplier_before_transaction()
    {
        var document = CreateDirectReceipt();
        document.SupplierId = null;
        document.Supplier = null;
        var fixture = CreateFixture(document);

        var action = () => fixture.Service.ApproveAsync(
            document.Id,
            approvalNote: null,
            RowVersion(document));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*chọn nhà cung cấp*");
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Generic_final_confirm_guard_rejects_unresolved_supplier_before_transaction()
    {
        var document = CreateDirectReceipt();
        var fixture = CreateFixture(document);

        var action = () => fixture.Service.ApproveAsync(
            document.Id,
            approvalNote: null,
            RowVersion(document));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*không tồn tại hoặc không thuộc cửa hàng hiện tại*");
        fixture.AssertNoMutationOrPosting();
    }

    [Fact]
    public async Task Submit_for_approval_still_permits_missing_supplier()
    {
        var document = CreateDirectReceipt(status: StockDocumentStatus.Draft);
        document.SupplierId = null;
        document.Supplier = null;
        var fixture = CreateFixture(document);

        await fixture.Service.SubmitForApprovalAsync(
            document.Id,
            approvalNote: null,
            RowVersion(document));

        document.Status.Should().Be(StockDocumentStatus.PendingApproval);
        document.SupplierId.Should().BeNull();
        fixture.Repository.SaveCalls.Should().Be(1);
        fixture.Movements.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Draft_header_update_still_permits_null_supplier()
    {
        var document = CreateDirectReceipt(status: StockDocumentStatus.Draft);
        var fixture = CreateFixture(document, CreateSupplier(51));

        await fixture.Service.UpdateHeaderAsync(new UpdateStockDocumentHeaderRequest
        {
            StockDocumentId = document.Id,
            LegalEntityId = 5,
            WarehouseId = 10,
            SupplierId = null
        });

        document.SupplierId.Should().BeNull();
        fixture.Repository.SaveCalls.Should().Be(1);
        fixture.Repository.SupplierExistsCalls.Should().Be(0);
    }

    [Fact]
    public async Task Already_confirmed_retry_remains_no_op_even_without_supplier()
    {
        var document = CreateDirectReceipt(status: StockDocumentStatus.Confirmed);
        document.SupplierId = null;
        document.Supplier = null;
        var fixture = CreateFixture(document);

        await fixture.Service.ApproveAsync(
            document.Id,
            "newer browser note",
            rowVersion: null);

        document.Status.Should().Be(StockDocumentStatus.Confirmed);
        document.ApprovalNote.Should().BeNull();
        fixture.Repository.GetSupplierCalls.Should().Be(0);
        fixture.AssertNoMutationOrPosting();
    }

    private static ServiceFixture CreateFixture(
        StockDocument document,
        params Supplier[] resolvableSuppliers)
        => CreateFixtureCore(document, null, null, resolvableSuppliers);

    private static ServiceFixture CreateFixture(
        StockDocument document,
        IInputInvoiceSupplierResolutionService resolutionService,
        params Supplier[] resolvableSuppliers)
        => CreateFixtureCore(document, resolutionService, null, resolvableSuppliers);

    private static ServiceFixture CreateFixtureWithReconciliation(
        StockDocument document,
        IInputInvoiceReconciliationService reconciliation,
        params Supplier[] resolvableSuppliers)
        => CreateFixtureCore(document, null, reconciliation, resolvableSuppliers);

    private static ServiceFixture CreateFixtureCore(
        StockDocument document,
        IInputInvoiceSupplierResolutionService? resolutionService,
        IInputInvoiceReconciliationService? reconciliationService,
        params Supplier[] resolvableSuppliers)
    {
        var repository = new RecordingStockDocumentRepository(document, resolvableSuppliers);
        var movements = new RecordingInventoryMovementService();
        var warehouse = document.Warehouse ?? CreateWarehouse();

        var service = new StockDocumentService(
            repository,
            Unused<ILegalEntityRepository>(),
            CreateProxy<IWarehouseRepository>((method, _) =>
                method.Name == nameof(IWarehouseRepository.GetByIdAsync)
                    ? Task.FromResult<Warehouse?>(warehouse)
                    : throw new NotSupportedException(method.Name)),
            Unused<IBarcodeLookupService>(),
            Unused<IInventoryUnitResolver>(),
            movements,
            new InventoryMovementFactory(),
            Unused<IInventoryRevaluationService>(),
            Unused<IDocumentNumberSequenceRepository>(),
            new TenantContextStub(),
            CreateProxy<IInventoryValuationEntryRepository>((method, _) =>
                method.Name == nameof(IInventoryValuationEntryRepository.GetByReferenceAsync)
                    ? Task.FromResult(new List<InventoryValuationEntry>())
                    : throw new NotSupportedException(method.Name)),
            new CurrentUserStub(),
            resolutionService,
            inputInvoiceRepository: null,
            inputInvoiceReconciliationService: reconciliationService);

        return new ServiceFixture(service, repository, movements);
    }

    private static StockDocument CreateDirectReceipt(
        Supplier? supplier = null,
        StockDocumentStatus status = StockDocumentStatus.PendingApproval)
    {
        supplier ??= CreateSupplier(51);
        var warehouse = CreateWarehouse();
        var document = new StockDocument
        {
            Id = 11,
            StoreId = 1,
            DocumentNo = "NK-B2-001",
            Status = status,
            Type = StockDocumentType.Receipt,
            ReceiptSource = PurchaseReceiptSource.Direct,
            DirectReceiptReason = "Khác",
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            SupplierId = supplier.Id,
            Supplier = supplier,
            RowVersion = CurrentVersion.ToArray()
        };
        document.Lines.Add(CreateValidLine(document));
        return document;
    }

    private static StockDocument CreatePurchaseOrderReceipt(Supplier supplier)
    {
        var document = CreateDirectReceipt(supplier);
        var order = new PurchaseOrder
        {
            Id = 70,
            StoreId = 1,
            OrderNumber = "PO-B2-001",
            Status = PurchaseOrderStatus.Approved,
            SupplierId = supplier.Id,
            Supplier = supplier,
            ExpectedWarehouseId = document.WarehouseId,
            ExpectedWarehouse = document.Warehouse,
            LegalEntityId = document.Warehouse.LegalEntityId,
            LegalEntity = document.Warehouse.LegalEntity
        };
        var orderLine = new PurchaseOrderLine
        {
            Id = 71,
            StoreId = 1,
            PurchaseOrderId = order.Id,
            PurchaseOrder = order,
            LineNo = 1,
            ProductVariantId = 31,
            ProductNameSnapshot = "Gạo",
            UnitNameSnapshot = "kg",
            ConversionFactor = 1m,
            OrderedQuantity = 2m
        };
        order.Lines.Add(orderLine);
        document.ReceiptSource = PurchaseReceiptSource.PurchaseOrder;
        document.PurchaseOrderId = order.Id;
        document.PurchaseOrder = order;
        document.Lines.Single().PurchaseOrderLineId = orderLine.Id;
        document.Lines.Single().PurchaseOrderLine = orderLine;
        return document;
    }

    private static StockDocumentLine CreateValidLine(StockDocument document)
        => new()
        {
            Id = 21,
            StockDocumentId = document.Id,
            StockDocument = document,
            LineNo = 1,
            ProductVariantId = 31,
            ProductNameSnapshot = "Gạo",
            Quantity = 2m,
            Factor = 1m,
            BaseQuantity = 2m,
            UnitPriceBeforeVat = 10m,
            UnitPriceAfterVat = 10m,
            UnitCost = 10m,
            LineTotal = 20m
        };

    private static ApprovePurchaseReceiptCommercialRequest ValidCommercialRequest(
        int? supplierId,
        bool paid = false,
        string? payee = null)
        => new()
        {
            RowVersion = Convert.ToBase64String(CurrentVersion),
            SupplierId = supplierId,
            IsMerchandisePaid = paid,
            MerchandisePayeeName = payee,
            Lines =
            [
                new PurchaseReceiptFinancialLineInputDto
                {
                    StockDocumentLineId = 21,
                    UnitPriceBeforeVat = 12m
                }
            ]
        };

    private static Supplier CreateSupplier(int id)
        => new()
        {
            Id = id,
            StoreId = 1,
            Code = $"NCC-{id}",
            Name = $"Nhà cung cấp {id}",
            IsActive = true
        };

    private static Warehouse CreateWarehouse()
    {
        var owner = new LegalEntity
        {
            Id = 5,
            StoreId = 1,
            Code = "LE-01",
            Name = "HKD 01",
            LegalName = "HKD 01",
            IsActive = true
        };
        return new Warehouse
        {
            Id = 10,
            StoreId = 1,
            Code = "WH-01",
            Name = "Kho 01",
            IsActive = true,
            LegalEntityId = owner.Id,
            LegalEntity = owner
        };
    }

    private static string RowVersion(StockDocument document)
        => Convert.ToBase64String(document.RowVersion);

    private static T Unused<T>() where T : class
        => CreateProxy<T>((method, _) => throw new NotSupportedException(method.Name));

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, DelegateProxy>();
        ((DelegateProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private sealed record ServiceFixture(
        StockDocumentService Service,
        RecordingStockDocumentRepository Repository,
        RecordingInventoryMovementService Movements)
    {
        public void AssertNoMutationOrPosting()
        {
            Repository.SaveCalls.Should().Be(0);
            Repository.BeginTransactionCalls.Should().Be(0);
            Repository.CommitTransactionCalls.Should().Be(0);
            Repository.RollbackTransactionCalls.Should().Be(0);
            Repository.AddedPayables.Should().BeEmpty();
            Movements.PreLockCalls.Should().Be(0);
            Movements.CreateCalls.Should().Be(0);
        }
    }

    private sealed record ReceiptSnapshot(
        StockDocumentStatus Status,
        int? SupplierId,
        bool IsMerchandisePaid,
        string? MerchandisePayeeName,
        bool HasVat,
        decimal UnitPriceBeforeVat,
        decimal UnitCost,
        decimal LineTotal,
        byte[] RowVersion)
    {
        public static ReceiptSnapshot Capture(StockDocument document)
        {
            var line = document.Lines.Single();
            return new ReceiptSnapshot(
                document.Status,
                document.SupplierId,
                document.IsMerchandisePaid,
                document.MerchandisePayeeName,
                document.HasVat,
                line.UnitPriceBeforeVat,
                line.UnitCost,
                line.LineTotal,
                document.RowVersion.ToArray());
        }
    }

    private class DelegateProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = default!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler(targetMethod!, args);
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public int? UserId => 7;
        public string? UserName => "b2-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed class TenantContextStub : ITenantContext
    {
        public int? StoreId => 1;
        public bool IsHostAdmin => false;
        public string? Subdomain => "test";
    }

    private sealed class RecordingInventoryMovementService : IInventoryMovementService
    {
        public int PreLockCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public CreateInventoryMovementRequest? LastRequest { get; private set; }

        public Task PreLockBalancesAsync(
            IEnumerable<InventoryPostingLockKey> keys,
            CancellationToken ct = default)
        {
            keys.Should().NotBeEmpty();
            PreLockCalls++;
            return Task.CompletedTask;
        }

        public Task<InventoryMovementResultDto> CreateAsync(
            CreateInventoryMovementRequest request,
            CancellationToken ct = default)
        {
            CreateCalls++;
            LastRequest = request;
            return Task.FromResult(new InventoryMovementResultDto { IsCreated = true });
        }

        public Task<decimal> PeekOutboundUnitCostAsync(
            int warehouseId,
            int productVariantId,
            decimal quantity,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingStockDocumentRepository : IStockDocumentRepository
    {
        private readonly Dictionary<int, Supplier> _suppliers;

        public RecordingStockDocumentRepository(
            StockDocument document,
            IEnumerable<Supplier> suppliers)
        {
            Document = document;
            _suppliers = suppliers.ToDictionary(x => x.Id);
        }

        public StockDocument Document { get; }
        public int SaveCalls { get; private set; }
        public int BeginTransactionCalls { get; private set; }
        public int CommitTransactionCalls { get; private set; }
        public int RollbackTransactionCalls { get; private set; }
        public int GetSupplierCalls { get; private set; }
        public int SupplierExistsCalls { get; private set; }
        public List<PurchasePayable> AddedPayables { get; } = [];
        public Dictionary<int, decimal> LastPurchaseBasePrices { get; } = [];
        public int PriceHistoryLockCalls { get; private set; }
        public Action? OnPriceHistoryLock { get; set; }
        public bool HasUnresolvedProvisional { get; set; }

        public Task AddAsync(StockDocument entity, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<StockDocument?> GetByIdAsync(int id, CancellationToken ct = default)
            => Task.FromResult<StockDocument?>(Document);
        public Task<StockDocument?> GetDetailAsync(int id, CancellationToken ct = default)
            => Task.FromResult<StockDocument?>(Document);
        public Task<StockDocument?> GetForConfirmAsync(int id, CancellationToken ct = default)
            => Task.FromResult<StockDocument?>(Document);
        public Task<StockDocumentLine?> GetLineByIdAsync(int lineId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> GetNextLineNoAsync(int stockDocumentId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> WarehouseExistsAsync(int warehouseId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> SupplierExistsAsync(int supplierId, CancellationToken ct = default)
        {
            SupplierExistsCalls++;
            return Task.FromResult(_suppliers.ContainsKey(supplierId));
        }
        public Task<bool> HasUnresolvedProvisionalItemsAsync(
            int storeId, int stockDocumentId, CancellationToken ct = default)
            => Task.FromResult(HasUnresolvedProvisional);
        public Task<Supplier?> GetSupplierAsync(int supplierId, CancellationToken ct = default)
        {
            GetSupplierCalls++;
            return Task.FromResult(_suppliers.GetValueOrDefault(supplierId));
        }
        public Task<ProductVariant?> GetVariantForStockDocumentAsync(int productVariantId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProductUnitConversion?> GetConversionAsync(int productVariantId, int unitId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProductUnitConversion?> GetBaseConversionAsync(int productVariantId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Tax?> GetTaxAsync(int taxId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<List<Tax>> GetTaxesAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Dictionary<int, decimal>> GetLastPurchaseBaseUnitPricesBeforeVatAsync(
            IEnumerable<int> productVariantIds,
            CancellationToken ct = default)
            => Task.FromResult(productVariantIds
                .Distinct()
                .Where(LastPurchaseBasePrices.ContainsKey)
                .ToDictionary(x => x, x => LastPurchaseBasePrices[x]));
        public Task<bool> LockPurchasePriceHistoryVariantsAsync(
            int storeId,
            IReadOnlyCollection<int> productVariantIds,
            CancellationToken ct = default)
        {
            PriceHistoryLockCalls++;
            OnPriceHistoryLock?.Invoke();
            return Task.FromResult(storeId == 1 && productVariantIds.All(x => x > 0));
        }
        public Task<PurchaseOrder?> GetPurchaseOrderForReceiptAsync(int purchaseOrderId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task AddPurchasePayableAsync(PurchasePayable payable, CancellationToken ct = default)
        {
            AddedPayables.Add(payable);
            return Task.CompletedTask;
        }
        public Task<bool> PurchasePayableExistsAsync(string sourceKey, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<InventoryBalance?> GetInventoryBalanceAsync(int warehouseId, int productVariantId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task AddInventoryBalanceAsync(InventoryBalance entity, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task AddInventoryTransactionAsync(InventoryTransaction entity, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<List<StockDocument>> GetReceiptListAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task RemoveLineAsync(StockDocumentLine line, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> ExistsInventoryTransactionByReferenceLineAsync(
            InventoryReferenceType referenceType,
            string referenceId,
            int referenceLineId,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task BeginTransactionAsync(CancellationToken ct = default)
        {
            BeginTransactionCalls++;
            return Task.CompletedTask;
        }
        public Task CommitTransactionAsync(CancellationToken ct = default)
        {
            CommitTransactionCalls++;
            return Task.CompletedTask;
        }
        public Task RollbackTransactionAsync(CancellationToken ct = default)
        {
            RollbackTransactionCalls++;
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
        public Task MarkVariantsHasInputInvoiceAsync(
            IEnumerable<int> productVariantIds,
            int? userId,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
