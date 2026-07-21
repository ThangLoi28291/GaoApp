using FluentValidation;
using GaoApp.Application.DTOs.AttributeValues;
using GaoApp.Application.DTOs.Brands;
using GaoApp.Application.DTOs.ProductAttributes;
using GaoApp.Application.DTOs.Units;
using GaoApp.Application.Interfaces.Repositories.POSPaymentQrs;
using GaoApp.Application.Interfaces.Services.AdminMenus;
using GaoApp.Application.Interfaces.Services.AttributeValues;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Auth;
using GaoApp.Application.Interfaces.Services.Brands;
using GaoApp.Application.Interfaces.Services.Categories;
using GaoApp.Application.Interfaces.Services.Display;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.LegalEntities;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Interfaces.Services.Media;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.POSPaymentQrs;
using GaoApp.Application.Interfaces.Services.POSShiftClosingSlips;
using GaoApp.Application.Interfaces.Services.POSShiftHandoverSlips;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Application.Interfaces.Services.ProductAttributes;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Interfaces.Services.Promotions;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Application.Interfaces.Services.Rewards;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Interfaces.Services.StoreBankAccounts;
using GaoApp.Application.Interfaces.Services.Suppliers;
using GaoApp.Application.Interfaces.Services.Taxes;
using GaoApp.Application.Interfaces.Services.Units;
using GaoApp.Application.Services.AdminMenus;
using GaoApp.Application.Services.AttributeValues;
using GaoApp.Application.Services.Audit;
using GaoApp.Application.Services.Auth;
using GaoApp.Application.Services.Brands;
using GaoApp.Application.Services.Categories;
using GaoApp.Application.Services.Display;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.LegalEntities;
using GaoApp.Application.Services.Invoices;
using GaoApp.Application.Services.Media;
using GaoApp.Application.Services.Orders;
using GaoApp.Application.Services.POSPaymentQrs;
using GaoApp.Application.Services.POSShiftClosingSlips;
using GaoApp.Application.Services.POSShiftHandoverSlips;
using GaoApp.Application.Services.POSShifts;
using GaoApp.Application.Services.ProductAttributes;
using GaoApp.Application.Services.Products;
using GaoApp.Application.Services.Promotions;
using GaoApp.Application.Services.Purchases;
using GaoApp.Application.Services.Rewards;
using GaoApp.Application.Services.Security;
using GaoApp.Application.Services.StoreBankAccounts;
using GaoApp.Application.Services.Suppliers;
using GaoApp.Application.Services.Taxes;
using GaoApp.Application.Services.Units;
using GaoApp.Application.Validators.AttributeValues;
using GaoApp.Application.Validators.Brands;
using GaoApp.Application.Validators.ProductAttributes;
using GaoApp.Application.Validators.Units;
using GaoApp.Infrastructure.Services.POSPaymentQrs;
using GaoApp.Infrastructure.Services.Products;
using Microsoft.Extensions.DependencyInjection;

namespace GaoApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ISupplierService, SupplierService>();

        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<IValidator<BrandEditDto>, BrandEditDtoValidator>();

        services.AddScoped<IUnitService, UnitService>();
        services.AddScoped<IValidator<UnitEditDto>, UnitEditDtoValidator>();

        services.AddScoped<IProductAttributeService, ProductAttributeService>();
        services.AddScoped<IValidator<ProductAttributeEditDto>, ProductAttributeEditDtoValidator>();

        services.AddScoped<IAttributeValueService, AttributeValueService>();
        services.AddScoped<IValidator<AttributeValueEditDto>, AttributeValueEditDtoValidator>();

        services.AddScoped<ITaxService, TaxService>();

        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IProductVariantService, ProductVariantService>();

        services.AddScoped<ITempUploadService, TempUploadService>();
        services.AddScoped<IProductImageService, ProductImageService>();

        services.AddScoped<IPOSService, POSService>();
        services.AddSingleton<IOrderLegalEntityAllocationService, OrderLegalEntityAllocationService>();
        services.AddScoped<IOrderLegalEntityFinalizeService, OrderLegalEntityFinalizeService>();
        services.AddScoped<IOrderLegalEntityReversalService, OrderLegalEntityReversalService>();
        services.AddScoped<IPOSShiftService, POSShiftService>();

        services.AddScoped<IStoreBankAccountService, StoreBankAccountService>();
        services.AddScoped<ILocalVietQrGenerator, LocalVietQrGenerator>();
        services.AddScoped<IPOSPaymentQrService, POSPaymentQrService>();
        services.AddScoped<IDisplayPromotionService, DisplayPromotionService>();

        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<ILegalEntityService, LegalEntityService>();
        services.AddScoped<ILegalEntityReconciliationService, LegalEntityReconciliationService>();
        services.AddScoped<ILegalEntityCanaryService, LegalEntityCanaryService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IStockDocumentService, StockDocumentService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IProcurementCatalogService, ProcurementCatalogService>();
        services.AddScoped<IPurchaseRequestService, PurchaseRequestService>();
        services.AddScoped<IProductUnitConversionService, ProductUnitConversionService>();
        services.AddScoped<IBarcodeLookupService, BarcodeLookupService>();
        services.AddScoped<IInventoryAdjustmentService, InventoryAdjustmentService>();
        services.AddScoped<IInventoryUnitResolver, InventoryUnitResolver>();
        services.AddScoped<IInventoryMovementService, InventoryMovementService>();
        services.AddScoped<IInventoryMovementNoteBuilder, InventoryMovementNoteBuilder>();
        services.AddScoped<IInventoryMovementFactory, InventoryMovementFactory>();
        services.AddScoped<IStockCountService, StockCountService>();
        services.AddScoped<IInventoryReservationService, InventoryReservationService>();
        services.AddScoped<IInventoryAdjustmentDocumentService, InventoryAdjustmentDocumentService>();
        services.AddScoped<IInventoryAdjustmentDocumentNumberService, InventoryAdjustmentDocumentNumberService>();
        services.AddScoped<IReturnableValuationFragmentService, ReturnableValuationFragmentService>();
        services.AddScoped<IReturnCostAllocator, ReturnCostAllocator>();
        services.AddScoped<IInventoryRevaluationService, InventoryRevaluationService>();
        services.AddScoped<IOrderInventoryIssueService, OrderInventoryIssueService>();
        services.AddScoped<IStockTransferService, StockTransferService>();
        services.AddScoped<IStockDocumentLookupService, StockDocumentLookupService>();
        services.AddScoped<IInputInvoiceXmlService, InputInvoiceXmlService>();
        services.AddScoped<IInvoiceReadService, InvoiceReadService>();
        services.AddScoped<IInvoiceCommandService, InvoiceCommandService>();
        services.AddScoped<IDraftInvoiceReturnSyncService, DraftInvoiceReturnSyncService>();
        services.AddScoped<IInvoiceBuyerService, InvoiceBuyerService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IInvoiceViettelDashboardService, InvoiceViettelDashboardService>();
        services.AddScoped<IAdminMenuService, AdminMenuService>();
        services.AddScoped<IInventoryCostSuggestionService, InventoryCostSuggestionService>();

        services.AddScoped<ICurrentStorePermissionService, CurrentStorePermissionService>();
        services.AddScoped<IRoleAdminService, RoleAdminService>();
        services.AddScoped<IRolePermissionAdminService, RolePermissionAdminService>();
        services.AddScoped<IUserInStoreAdminService, UserInStoreAdminService>();
        services.AddScoped<ICustomerRewardService, CustomerRewardService>();
        services.AddScoped<IOrderRewardCalculator, OrderRewardCalculator>();
        services.AddScoped<IPOSShiftHandoverSlipService, POSShiftHandoverSlipService>();
        services.AddScoped<IPOSShiftClosingSlipService, POSShiftClosingSlipService>();
        services.AddScoped<IInvoiceProviderSettingService, InvoiceProviderSettingService>();
        services.AddScoped<IViettelInvoicePayloadBuilder, ViettelInvoicePayloadBuilder>();
        services.AddScoped<IViettelInvoicePreviewService, ViettelInvoicePreviewService>();
        services.AddScoped<IViettelInvoiceIssueService, ViettelInvoiceIssueService>();
        services.AddScoped<IViettelOfficialFileService, ViettelOfficialFileService>();
        services.AddScoped<IViettelInvoiceSyncService, ViettelInvoiceSyncService>();
        services.AddScoped<IInvoiceIntegrationLogService, InvoiceIntegrationLogService>();
        services.AddScoped<IViettelInvoiceEmailService, ViettelInvoiceEmailService>();
        services.AddScoped<IViettelInvoiceListSyncService, ViettelInvoiceListSyncService>();
        services.AddScoped<IInvoiceDashboardService, InvoiceDashboardService>();
        services.AddScoped<IInvoiceIntegrationLogCleanupService, InvoiceIntegrationLogCleanupService>();
        services.AddScoped<IInvoiceCorrectionService, InvoiceCorrectionService>();



        services.AddScoped<IBarcodeHistoryService, BarcodeHistoryService>();
        services.AddScoped<IBarcodeGovernanceService, BarcodeGovernanceService>();
        services.AddScoped<IProductUnitBarcodeReadService, ProductUnitBarcodeReadService>();
        services.AddScoped<IProductBarcodeVerificationService, ProductBarcodeVerificationService>();
        services.AddScoped<IPromotionEngine, PromotionEngine>();
        services.AddScoped<IPromotionAdminService, PromotionAdminService>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISalesReturnService, SalesReturnService>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
