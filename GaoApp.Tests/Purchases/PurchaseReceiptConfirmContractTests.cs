using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GaoApp.Tests.Purchases;

// R2.4-C2 coverage: confirm includes fresh owner resolution and receipt-only snapshot semantics.
public sealed class PurchaseReceiptConfirmContractTests
{
    [Fact]
    public void Confirm_separates_audit_time_from_locked_invoice_warehouse_date()
    {
        var source = Method(
            Parse("GaoApp.Application/Services/Inventory/StockDocumentService.cs"),
            "ApproveTrackedAsync").ToFullString();

        source.Should().Contain("auditNowUtc");
        source.Should().Contain("warehouseOccurredAtUtc");
        source.Should().Contain("LockReceiptForInputInvoiceMutationAsync");
        source.Should().Contain("PurchaseReceiptWarehouseDatePolicy.Resolve");
        source.Should().Contain("occurredAtUtc: warehouseOccurredAtUtc");
        source.Should().Contain("CreatePayablesIfNeededAsync(document, auditNowUtc, ct)");
    }

    [Fact]
    public void Commercial_and_final_confirm_paths_invoke_the_central_supplier_prerequisite()
    {
        var root = Parse("GaoApp.Application/Services/Inventory/StockDocumentService.cs");

        Method(root, "ApproveCommercialAsync").ToFullString().Should().Contain(
            "PurchaseReceiptConfirmPrerequisitePolicy.EnsureSupplierSelected(request.SupplierId)");
        Method(root, "ApproveTrackedAsync").ToFullString().Should().Contain(
            "PurchaseReceiptConfirmPrerequisitePolicy.EnsureSupplierSelected(document.SupplierId)");
    }

    [Fact]
    public void Confirmed_retry_precedes_new_supplier_enforcement_in_both_paths()
    {
        var root = Parse("GaoApp.Application/Services/Inventory/StockDocumentService.cs");

        foreach (var methodName in new[] { "ApproveCommercialAsync", "ApproveTrackedAsync" })
        {
            var source = Method(root, methodName).ToFullString();
            source.IndexOf("StockDocumentStatus.Confirmed", StringComparison.Ordinal)
                .Should().BeLessThan(source.IndexOf(
                    "PurchaseReceiptConfirmPrerequisitePolicy.EnsureSupplierSelected(",
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Commercial_guard_resolves_supplier_before_any_receipt_or_line_mutation()
    {
        var source = Method(
            Parse("GaoApp.Application/Services/Inventory/StockDocumentService.cs"),
            "ApproveCommercialAsync").ToFullString();
        var prerequisite = source.IndexOf(
            "PurchaseReceiptConfirmPrerequisitePolicy.EnsureSupplierSelected(request.SupplierId)",
            StringComparison.Ordinal);
        var resolution = source.IndexOf(
            "_stockDocumentRepository.GetSupplierAsync(request.SupplierId!.Value, ct)",
            StringComparison.Ordinal);
        var firstMutation = source.IndexOf("document.HasVat = request.HasVat", StringComparison.Ordinal);

        prerequisite.Should().BeGreaterThanOrEqualTo(0);
        resolution.Should().BeGreaterThan(prerequisite);
        firstMutation.Should().BeGreaterThan(resolution);
    }

    [Fact]
    public void Final_guard_precedes_transaction_prelock_posting_and_save()
    {
        var source = Method(
            Parse("GaoApp.Application/Services/Inventory/StockDocumentService.cs"),
            "ApproveTrackedAsync").ToFullString();
        var guard = source.IndexOf(
            "PurchaseReceiptConfirmPrerequisitePolicy.EnsureSupplierSelected(document.SupplierId)",
            StringComparison.Ordinal);
        var begin = source.IndexOf("_stockDocumentRepository.BeginTransactionAsync(ct)", StringComparison.Ordinal);
        var preLock = source.IndexOf("_inventoryMovementService.PreLockBalancesAsync(", StringComparison.Ordinal);
        var movement = source.IndexOf("_inventoryMovementService.CreateAsync(movementRequest, ct)", StringComparison.Ordinal);
        var save = source.IndexOf("_stockDocumentRepository.SaveChangesAsync(ct)", StringComparison.Ordinal);

        guard.Should().BeGreaterThanOrEqualTo(0);
        begin.Should().BeGreaterThan(guard);
        preLock.Should().BeGreaterThan(begin);
        movement.Should().BeGreaterThan(preLock);
        save.Should().BeGreaterThan(movement);
    }

    [Fact]
    public void Workbench_describes_supplier_as_always_required_and_payee_as_non_substitute()
    {
        var source = Read("GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_CommercialApprovalWorkbench.cshtml");

        source.Should().Contain("Nhà cung cấp bắt buộc trước khi duyệt, kể cả phiếu đã trả tiền.");
        source.Should().Contain("không thay thế nhà cung cấp");
        source.Should().NotContain("Bắt buộc khi còn nợ tiền hàng");
        source.Should().NotContain("Dùng khi trả ngay và chưa có NCC");
    }

    [Fact]
    public void JavaScript_rejects_missing_supplier_independently_of_paid_state_and_has_no_payee_escape()
    {
        var source = Read("GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js");
        var validation = Slice(source, "function validateCommercialApproval(", "function fail(");

        validation.Should().Contain("if (!supplierId)");
        validation.Should().Contain("commercialSupplierId");
        validation.Should().NotContain("paid && !supplierId");
        validation.Should().NotContain("!paid && !supplierId");
        validation.Should().NotContain("const payee =");
        source.Should().Contain("required.classList.remove('d-none')");
        source.Should().NotContain("required.classList.toggle('d-none', paid)");
    }

    [Fact]
    public void Commercial_payload_keeps_supplier_and_RowVersion_but_excludes_physical_fields()
    {
        var source = Read("GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js");
        var payload = Slice(source, "function buildCommercialPayload()", "function validateCommercialApproval(");

        payload.Should().Contain("rowVersion:");
        payload.Should().Contain("supplierId:");
        foreach (var forbidden in new[]
                 {
                     "productVariantId:", "productId:", "unitId:",
                     "productUnitConversionId:", "factor:", "quantity:", "baseQuantity:"
                 })
        {
            payload.Should().NotContain(forbidden);
        }
    }

    [Fact]
    public void Draft_create_update_and_commercial_DTO_supplier_fields_remain_nullable()
    {
        foreach (var (path, typeName) in new[]
                 {
                     ("GaoApp.Application/DTOs/Inventory/CreateStockDocumentRequest.cs", "CreateStockDocumentRequest"),
                     ("GaoApp.Application/DTOs/Inventory/UpdateStockDocumentHeaderRequest.cs", "UpdateStockDocumentHeaderRequest"),
                     ("GaoApp.Application/DTOs/Inventory/PurchaseReceiptApprovalDtos.cs", "ApprovePurchaseReceiptCommercialRequest")
                 })
        {
            var type = Parse(path).DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(x => x.Identifier.ValueText == typeName);
            var supplier = type.Members.OfType<PropertyDeclarationSyntax>()
                .Single(x => x.Identifier.ValueText == "SupplierId");
            supplier.Type.ToString().Should().Be("int?");
        }
    }

    [Fact]
    public void Draft_create_update_and_submit_paths_do_not_gain_confirm_prerequisite()
    {
        var root = Parse("GaoApp.Application/Services/Inventory/StockDocumentService.cs");

        foreach (var methodName in new[]
                 {
                     "CreateReceiptAsync", "UpdateHeaderAsync", "SubmitForApprovalAsync"
                 })
        {
            Method(root, methodName).ToFullString().Should().NotContain(
                "PurchaseReceiptConfirmPrerequisitePolicy");
        }
    }

    [Fact]
    public void C2_begin_prelock_post_save_commit_and_rollback_structure_is_preserved()
    {
        var source = Method(
            Parse("GaoApp.Application/Services/Inventory/StockDocumentService.cs"),
            "ApproveTrackedAsync").ToFullString();
        var begin = source.IndexOf("_stockDocumentRepository.BeginTransactionAsync(ct)", StringComparison.Ordinal);
        var preLock = source.IndexOf("_inventoryMovementService.PreLockBalancesAsync(", StringComparison.Ordinal);
        var movement = source.IndexOf("_inventoryMovementService.CreateAsync(movementRequest, ct)", StringComparison.Ordinal);
        var payable = source.IndexOf("CreatePayablesIfNeededAsync(document, auditNowUtc, ct)", StringComparison.Ordinal);
        var save = source.IndexOf("_stockDocumentRepository.SaveChangesAsync(ct)", StringComparison.Ordinal);
        var commit = source.IndexOf("_stockDocumentRepository.CommitTransactionAsync(ct)", StringComparison.Ordinal);
        var rollback = source.IndexOf("_stockDocumentRepository.RollbackTransactionAsync(ct)", StringComparison.Ordinal);

        begin.Should().BeGreaterThanOrEqualTo(0);
        preLock.Should().BeGreaterThan(begin);
        movement.Should().BeGreaterThan(preLock);
        payable.Should().BeGreaterThan(movement);
        save.Should().BeGreaterThan(payable);
        commit.Should().BeGreaterThan(save);
        rollback.Should().BeGreaterThan(commit);
    }

    private static CompilationUnitSyntax Parse(string relativePath)
        => CSharpSyntaxTree.ParseText(Read(relativePath)).GetCompilationUnitRoot();

    private static MethodDeclarationSyntax Method(CompilationUnitSyntax root, string name)
        => root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(x => x.Identifier.ValueText == name);

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static string Read(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GaoApp.sln")))
            current = current.Parent;

        current.Should().NotBeNull("the test must run under a GaoApp repository checkout");
        return File.ReadAllText(Path.Combine(
            current!.FullName,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
