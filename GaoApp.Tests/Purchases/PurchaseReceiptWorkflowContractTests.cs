using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptWorkflowContractTests
{
    [Fact]
    public void Item_mapping_has_a_distinct_pending_only_workflow_policy()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Application/Services/Purchases/PurchaseReceiptWorkflowPolicy.cs");
        var physical = Slice(
            source,
            "public static bool CanEditPhysicalFields",
            "public static void EnsurePhysicalFieldsEditable");

        physical.Should().Contain("StockDocumentStatus.Draft");
        physical.Should().Contain("StockDocumentStatus.Rejected");
        physical.Should().NotContain("StockDocumentStatus.PendingApproval");
        source.Should().Contain("CanManageInputInvoiceMapping");
        source.Should().Contain("EnsureInputInvoiceMappingEditable");
        var mapping = Slice(
            source,
            "public static bool CanManageInputInvoiceMapping",
            "public static void EnsureInputInvoiceMappingEditable");
        mapping.Should().Contain("StockDocumentStatus.PendingApproval");
        mapping.Should().NotContain("StockDocumentStatus.Draft");
        mapping.Should().NotContain("StockDocumentStatus.Rejected");
        mapping.Should().NotContain("StockDocumentStatus.Confirmed");
    }

    [Fact]
    public void Mapping_mutators_use_mapping_policy_and_not_physical_editability()
    {
        var xml = ReadRepositoryFile(
            "GaoApp.Application/Services/Inventory/InputInvoiceXmlService.cs");
        var catalog = ReadRepositoryFile(
            "GaoApp.Application/Services/Inventory/InputInvoiceItemCatalogMappingService.cs");
        var update = Slice(xml, "public async Task UpdateLineMapAsync(",
            "public async Task BulkUpdateLineMapsAsync(");
        var bulk = xml[xml.IndexOf(
            "public async Task BulkUpdateLineMapsAsync(", StringComparison.Ordinal)..];
        var confirm = Slice(catalog,
            "public async Task<InputInvoiceItemCatalogResolutionDto> ConfirmWithinTransactionAsync(",
            "public async Task AutoApplyKnownMappingsWithinTransactionAsync(");

        update.Should().Contain("EnsureInputInvoiceMappingEditable");
        update.Should().NotContain("EnsurePhysicalFieldsEditable");
        bulk.Should().Contain("EnsureInputInvoiceMappingEditable");
        confirm.Should().Contain("EnsureInputInvoiceMappingEditable");
        confirm.Should().NotContain("EnsurePhysicalFieldsEditable");
    }

    [Fact]
    public void Product_and_unit_lookup_supports_receipt_scoped_approval_authority()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Web/Areas/Admin/Controllers/StockDocumentsController.cs");
        var units = Slice(source,
            "public async Task<IActionResult> GetVariantUnits(",
            "[HttpGet(\"receipts\")]");
        var search = Slice(source,
            "public async Task<IActionResult> SearchProducts(",
            "[HttpGet(\"{id:int}/audit-events\")]");

        units.Should().Contain("stockDocumentId");
        units.Should().Contain("HasMappingLookupPermissionAsync");
        search.Should().Contain("stockDocumentId");
        search.Should().Contain("HasMappingLookupPermissionAsync");
        source.Should().Contain("PermissionCodes.Purchase.Receipt.Approve");
        source.Should().Contain("PermissionCodes.Inventory.StockDocument.Approve");
    }

    [Fact]
    public void Management_view_exposes_only_bounded_pending_ownership_editing()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Edit.cshtml");
        var editFlags = Slice(
            source,
            "var canEditHeaderByStatus",
            "var canSubmitApprovalByStatus");

        editFlags.Should().Contain("StockDocumentStatus.Draft");
        editFlags.Should().Contain("StockDocumentStatus.Rejected");
        editFlags.Should().NotContain("StockDocumentStatus.PendingApproval");
        source.Should().Contain("var canEditPendingOwnership =");
        source.Should().Contain("!isPurchaseOrderReceipt &&");
        source.Should().Contain("hasApprovePermission &&");
        source.Should().Contain("Model.Status == StockDocumentStatus.PendingApproval");
        source.Should().Contain("id=\"CanEditPendingOwnership\"");

        var lineFlags = Slice(
            source,
            "var canEditLinesByStatus",
            "var canSubmitApprovalByStatus");
        lineFlags.Should().NotContain("StockDocumentStatus.PendingApproval");
    }

    [Fact]
    public void Pending_ownership_JavaScript_edits_only_Warehouse_and_Supplier()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Web/wwwroot/Admin/js/stock-document-management.js");
        var readonlyState = Slice(
            source,
            "function applyEditReadonlyState()",
            "async function initReceiptOwnershipForEdit()");
        var autoSave = Slice(
            source,
            "function bindEditHeaderAutoSave()",
            "function queueEditHeaderSave()");

        readonlyState.Should().Contain("CanEditPendingOwnership");
        readonlyState.Should().Contain("WarehouseId");
        readonlyState.Should().Contain("SupplierId");
        readonlyState.Should().Contain("LegalEntityId");
        readonlyState.Should().Contain("DocumentNote");
        readonlyState.Should().Contain("ApprovalNote");
        autoSave.Should().Contain("CanEditPendingOwnership");
        autoSave.Should().Contain("warehouseEl.on('change', queueEditHeaderSave)");
        autoSave.Should().Contain("supplierEl.on('change', queueEditHeaderSave)");
        autoSave.Should().Contain("if (canEditHeader)");
    }

    [Fact]
    public void Header_auto_save_refreshes_authoritative_picker_state_and_surfaces_errors()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Web/wwwroot/Admin/js/stock-document-management.js");
        var save = Slice(
            source,
            "async function saveEditHeader()",
            "/* =========================================================\n * EDIT - LINKED INPUT INVOICES");

        save.Should().Contain("showEditHeaderMessage(");
        save.Should().Contain("restoreEditHeaderOwnershipControls()");
        save.Should().Contain("window.location.reload()");
    }

    [Fact]
    public void Pending_header_endpoint_reuses_source_specific_approve_permission()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Web/Areas/Admin/Controllers/StockDocumentManagementController.cs");
        var updateHeader = Slice(
            source,
            "public async Task<IActionResult> UpdateHeader(",
            "[HttpPost(\"{id:int}/freight\")]");

        updateHeader.Should().Contain("StockDocumentStatus.PendingApproval");
        updateHeader.Should().Contain("PermissionCodes.Inventory.StockDocument.Approve");
        updateHeader.Should().Contain("PermissionCodes.Purchase.Receipt.Approve");
        updateHeader.Should().Contain("AuthorizeAsync");
        updateHeader.Should().Contain("return Forbid()");
    }

    [Fact]
    public void LinesTable_CanEdit_uses_update_permission_and_excludes_PendingApproval()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Web/Areas/Admin/Controllers/StockDocumentManagementController.cs");
        var editAssignment = Slice(source, "CanEdit =", "CanViewCost =");

        source.Should().Contain("PermissionCodes.Purchase.Receipt.Update");
        source.Should().Contain("PermissionCodes.Inventory.StockDocument.Update");
        editAssignment.Should().Contain("StockDocumentStatus.Draft");
        editAssignment.Should().Contain("StockDocumentStatus.Rejected");
        editAssignment.Should().NotContain("StockDocumentStatus.PendingApproval");
        editAssignment.Should().NotContain("hasApprovePermission");
    }

    [Fact]
    public void Revision_request_JavaScript_posts_RowVersion()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Web/wwwroot/Admin/js/warehouse-receiving-detail.js");
        var request = Slice(
            source,
            "async function sendRevisionRequest()",
            "function showReceivingToast");

        request.Should().Contain("/request-revision");
        request.Should().Contain("rowVersion: window.warehouseReceivingDetail?.rowVersion");
    }

    [Fact]
    public void Revision_resolution_JavaScript_posts_RowVersion_for_return_and_dismiss()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Web/wwwroot/Admin/js/stock-document-management.js");
        var resolution = source[source.IndexOf(
            "function bindRevisionRequestActions()",
            StringComparison.Ordinal)..];

        resolution.Should().Contain("returnToEdit: true");
        resolution.Should().Contain("returnToEdit: false");
        resolution.Split("rowVersion: window.stockDocumentPage.rowVersion")
            .Should().HaveCount(3,
                "both resolution payloads must carry the current concurrency token");
    }

    [Fact]
    public void Every_physical_mutator_uses_the_central_workflow_policy()
    {
        var root = CSharpSyntaxTree.ParseText(ReadRepositoryFile(
            "GaoApp.Application/Services/Inventory/StockDocumentService.cs"))
            .GetCompilationUnitRoot();
        var expected = new[]
        {
            "AddLineAsync",
            "AddLineByBarcodeAsync",
            "UpdateLineAsync",
            "DeleteLineAsync",
            "UpdateHeaderAsync"
        };

        foreach (var name in expected)
        {
            var method = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(x => x.Identifier.ValueText == name);
            method.ToFullString().Should().Contain(
                "PurchaseReceiptWorkflowPolicy.EnsurePhysicalFieldsEditable(",
                $"{name} must enforce status before physical mutation");
        }
    }

    [Fact]
    public void Commercial_approval_payload_does_not_accept_physical_identity_or_quantity()
    {
        var root = CSharpSyntaxTree.ParseText(ReadRepositoryFile(
            "GaoApp.Application/DTOs/Inventory/PurchaseReceiptApprovalDtos.cs"))
            .GetCompilationUnitRoot();
        var payloadTypes = new[]
        {
            "ApprovePurchaseReceiptCommercialRequest",
            "PurchaseReceiptFinancialLineInputDto"
        };
        var forbidden = new[]
        {
            "ProductVariantId",
            "ProductId",
            "UnitId",
            "ProductUnitConversionId",
            "Factor",
            "Quantity",
            "BaseQuantity"
        };

        foreach (var typeName in payloadTypes)
        {
            var type = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(x => x.Identifier.ValueText == typeName);
            var properties = type.Members
                .OfType<PropertyDeclarationSyntax>()
                .Select(x => x.Identifier.ValueText)
                .ToArray();
            properties.Should().NotContain(x => forbidden.Contains(x));
        }
    }

    [Fact]
    public void Confirm_path_preserves_begin_prelock_post_save_and_commit_order()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Application/Services/Inventory/StockDocumentService.cs");
        var confirm = Slice(source, "private async Task ApproveTrackedAsync(", "public async Task RejectAsync(");
        var begin = confirm.IndexOf("_stockDocumentRepository.BeginTransactionAsync(ct)", StringComparison.Ordinal);
        var preLock = confirm.IndexOf("_inventoryMovementService.PreLockBalancesAsync(", StringComparison.Ordinal);
        var movement = confirm.IndexOf("_inventoryMovementService.CreateAsync(movementRequest, ct)", StringComparison.Ordinal);
        var save = confirm.IndexOf("_stockDocumentRepository.SaveChangesAsync(ct)", movement, StringComparison.Ordinal);
        var commit = confirm.IndexOf("_stockDocumentRepository.CommitTransactionAsync(ct)", save, StringComparison.Ordinal);

        begin.Should().BeGreaterThanOrEqualTo(0);
        preLock.Should().BeGreaterThan(begin);
        movement.Should().BeGreaterThan(preLock);
        save.Should().BeGreaterThan(movement);
        commit.Should().BeGreaterThan(save);
    }

    [Fact]
    public void Manager_receipt_list_and_detail_render_purchase_order_identity_without_replacing_document_identity()
    {
        var listDto = ReadRepositoryFile(
            "GaoApp.Application/DTOs/Inventory/StockDocumentListItemDto.cs");
        var detailDto = ReadRepositoryFile(
            "GaoApp.Application/DTOs/Inventory/StockDocumentDto.cs");
        var repository = ReadRepositoryFile(
            "GaoApp.Infrastructure/Repositories/Inventory/StockDocumentRepository.cs");
        var service = ReadRepositoryFile(
            "GaoApp.Application/Services/Inventory/StockDocumentService.cs");
        var script = ReadRepositoryFile(
            "GaoApp.Web/wwwroot/Admin/js/stock-document-management.js");
        var detailView = ReadRepositoryFile(
            "GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Edit.cshtml");

        listDto.Should().Contain("PurchaseOrderTitle");
        listDto.Should().Contain("PurchaseOrderNumber");
        detailDto.Should().Contain("PurchaseOrderTitle");
        var receiptListQuery = Slice(
            repository,
            "public async Task<List<StockDocument>> GetReceiptListAsync(",
            "public Task RemoveLineAsync(");
        receiptListQuery.Should().Contain(".Include(x => x.PurchaseOrder)");
        service.Should().Contain("PurchaseOrderTitle = x.PurchaseOrder?.Title");
        service.Should().Contain("PurchaseOrderTitle = document.PurchaseOrder?.Title");

        script.Should().Contain("x.purchaseOrderTitle");
        script.Should().Contain("x.purchaseOrderNumber");
        script.Should().Contain("x.supplierName");
        script.Should().Contain("setText('sdInfoTitle', item.purchaseOrderTitle");
        script.Should().Contain("Phiếu nhận");
        script.Should().Contain("x.hasRevisionRequest");
        script.Should().Contain("renderStatusBadge");

        detailView.Should().Contain("Nhận hàng —");
        detailView.Should().Contain("Model.PurchaseOrderTitle");
        detailView.Should().Contain("Model.PurchaseOrderNumber");
        detailView.Should().Contain("Model.SupplierName");
        detailView.Should().Contain("Model.DocumentNo");
        detailView.Should().Contain("Model.WarehouseName");
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "GaoApp.sln")))
        {
            current = current.Parent;
        }

        current.Should().NotBeNull(
            "the test must run under a GaoApp repository checkout");
        return File.ReadAllText(Path.Combine(
            current!.FullName,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
