using System.Text.RegularExpressions;

namespace GaoApp.Tests.Observability;

public sealed class ExceptionBoundarySourceContractTests
{
    private static readonly string[] MigratedRuntimeFiles =
    [
        "GaoApp.Application/Services/AttributeValues/AttributeValueService.cs",
        "GaoApp.Application/Services/Brands/BrandService.cs",
        "GaoApp.Application/Services/Inventory/InputInvoiceXmlService.cs",
        "GaoApp.Application/Services/Inventory/InventoryMovementService.cs",
        "GaoApp.Application/Services/Inventory/StockCountService.cs",
        "GaoApp.Application/Services/Inventory/StockDocumentService.cs",
        "GaoApp.Application/Services/Orders/OrderInventoryIssueService.cs",
        "GaoApp.Application/Services/Orders/POSService.cs",
        "GaoApp.Application/Services/Products/BarcodeLookupService.cs",
        "GaoApp.Application/Services/Products/ProductBarcodeVerificationService.cs",
        "GaoApp.Application/Services/Products/ProductService.cs",
        "GaoApp.Application/Services/Products/ProductUnitConversionService.cs",
        "GaoApp.Application/Services/Products/ProductVariantService.cs",
        "GaoApp.Application/Services/ProductAttributes/ProductAttributeService.cs",
        "GaoApp.Application/Services/Promotions/PromotionAdminService.cs",
        "GaoApp.Application/Services/Purchases/ProcurementCatalogService.cs",
        "GaoApp.Application/Services/Purchases/PurchaseOrderService.cs",
        "GaoApp.Application/Services/Purchases/PurchaseRequestService.cs",
        "GaoApp.Application/Services/Suppliers/SupplierService.cs",
        "GaoApp.Application/Services/Taxes/TaxService.cs",
        "GaoApp.Application/Services/Units/UnitService.cs",
        "GaoApp.Infrastructure/Repositories/AttributeValues/AttributeValueRepository.cs",
        "GaoApp.Infrastructure/Repositories/Brands/BrandRepository.cs",
        "GaoApp.Infrastructure/Repositories/ProductAttributes/ProductAttributeRepository.cs",
        "GaoApp.Infrastructure/Repositories/Products/ProductVariantRepository.cs",
        "GaoApp.Infrastructure/Repositories/Promotions/PromotionRepository.cs",
        "GaoApp.Infrastructure/Repositories/Suppliers/SupplierRepository.cs",
        "GaoApp.Infrastructure/Repositories/Taxes/TaxRepository.cs",
        "GaoApp.Infrastructure/Repositories/Units/UnitRepository.cs",
        "GaoApp.Web/Areas/Admin/Controllers/BarcodeNormalizationController.cs",
        "GaoApp.Web/Areas/Admin/Controllers/BarcodeVerificationController.cs",
        "GaoApp.Web/Areas/Admin/Controllers/InventoryIssueManagementController.cs",
        "GaoApp.Web/Areas/Admin/Controllers/POSController.cs",
        "GaoApp.Web/Areas/Admin/Controllers/ProductUnitConversionController.cs",
        "GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs",
        "GaoApp.Web/Areas/Admin/Controllers/PurchaseRequestsController.cs",
        "GaoApp.Web/Areas/Admin/Controllers/StockDocumentManagementController.cs",
        "GaoApp.Web/Areas/Admin/Controllers/StockDocumentsController.cs",
        "GaoApp.Web/Areas/Admin/Controllers/TaxController.cs",
        "GaoApp.Web/Areas/Admin/Controllers/WarehouseReceivingController.cs"
    ];

    [Fact]
    public void Migrated_boundaries_do_not_catch_invalid_operation()
    {
        var violations = Sources()
            .Where(item => Regex.IsMatch(
                item.Source,
                @"catch\s*\(\s*InvalidOperationException",
                RegexOptions.CultureInvariant))
            .Select(item => item.RelativePath)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"InvalidOperationException catches: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Migrated_boundaries_do_not_publish_raw_exception_message()
    {
        var unsafePatterns = new[]
        {
            @"ModelState\.AddModelError[\s\S]{0,200}\bex\.Message",
            @"TempData[\s\S]{0,200}\bex\.Message",
            @"ViewData[\s\S]{0,200}\bex\.Message",
            @"BadRequest\([\s\S]{0,200}\bex\.Message",
            @"Json\([\s\S]{0,200}\bex\.Message",
            @"Error\.(?:Validation|Failure|Conflict)[\s\S]{0,200}\bex\.Message",
            @"(?:ErrorMessage|Message)\s*=\s*ex\.Message"
        };
        var violations = new List<string>();

        foreach (var item in Sources())
        {
            if (unsafePatterns.Any(pattern => Regex.IsMatch(
                    item.Source,
                    pattern,
                    RegexOptions.CultureInvariant)))
            {
                violations.Add(item.RelativePath);
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Raw exception message boundaries: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Global_error_owner_uses_sanitized_structured_diagnostic()
    {
        var source = ReadSource(
            "GaoApp.Web/Middlewares/GlobalExceptionMiddleware.cs");
        var diagnosticSource = ReadSource(
            "GaoApp.Web/Middlewares/SafeExceptionDiagnostic.cs");

        Assert.DoesNotMatch(
            new Regex(
                @"_logger\.LogError\(\s*exception\s*,",
                RegexOptions.CultureInvariant),
            source);
        Assert.DoesNotContain(
            "exception.ToString()",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "SafeExceptionDiagnosticBuilder.Build(exception)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("StackFrames={StackFrames}", source, StringComparison.Ordinal);
        Assert.Contains("Fingerprint={Fingerprint}", source, StringComparison.Ordinal);
        Assert.Contains(
            "new StackTrace(exception, fNeedFileInfo: false)",
            diagnosticSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "exception.Message",
            diagnosticSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "InnerException.Message",
            diagnosticSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Staged_concurrency_boundaries_use_typed_409_contract()
    {
        var allSources = string.Join(
            Environment.NewLine,
            Sources().Select(item => item.Source));
        var stockDocuments = ReadSource(
            "GaoApp.Web/Areas/Admin/Controllers/StockDocumentsController.cs");
        var taxService = ReadSource(
            "GaoApp.Application/Services/Taxes/TaxService.cs");

        Assert.DoesNotContain(
            "GetType().Name == \"DbUpdateConcurrencyException\"",
            allSources,
            StringComparison.Ordinal);
        Assert.Contains(
            "catch (ConcurrencyException)",
            taxService,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "throw new BusinessRuleException(\"Dữ liệu đã bị thay đổi",
            taxService,
            StringComparison.Ordinal);
        Assert.Matches(
            new Regex(
                @"catch\s*\(\s*DbUpdateConcurrencyException\s*\)[\s\S]{0,200}return\s+Conflict\(",
                RegexOptions.CultureInvariant),
            stockDocuments);
    }

    [Fact]
    public void Pos_search_caller_cancellation_is_not_converted_to_499()
    {
        var source = ReadSource(
            "GaoApp.Web/Areas/Admin/Controllers/POSController.cs");

        Assert.DoesNotContain(
            "ClientClosedRequestStatusCode",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "StatusCode(499",
            source,
            StringComparison.Ordinal);
        Assert.Matches(
            new Regex(
                @"SearchProducts[\s\S]{0,500}SearchProductsForPOSAsync\([\s\S]{0,100}return\s+Ok",
                RegexOptions.CultureInvariant),
            source);
    }

    [Fact]
    public void Migrated_runtime_does_not_reset_stack_traces_with_throw_ex()
    {
        var violations = Sources()
            .Where(item => Regex.IsMatch(
                item.Source,
                @"\bthrow\s+ex\s*;",
                RegexOptions.CultureInvariant))
            .Select(item => item.RelativePath)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"throw ex statements: {string.Join(", ", violations)}");
    }

    private static IEnumerable<(string RelativePath, string Source)> Sources()
        => MigratedRuntimeFiles.Select(path => (path, ReadSource(path)));

    private static string ReadSource(string relativePath)
        => File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GaoApp.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Repository root was not found for source-contract tests.");
    }
}
