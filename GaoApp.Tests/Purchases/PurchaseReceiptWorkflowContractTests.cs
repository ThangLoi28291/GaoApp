using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptWorkflowContractTests
{
    [Fact]
    public void Management_view_excludes_PendingApproval_from_physical_edit_flags()
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
