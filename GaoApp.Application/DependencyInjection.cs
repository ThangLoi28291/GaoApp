using FluentValidation;
using GaoApp.Application.DTOs.AttributeValues;
using GaoApp.Application.DTOs.Brands;
using GaoApp.Application.DTOs.ProductAttributes;
using GaoApp.Application.DTOs.Units;
using GaoApp.Application.Validators.AttributeValues;
using GaoApp.Application.Validators.Brands;
using GaoApp.Application.Validators.ProductAttributes;
using GaoApp.Application.Validators.Units;
using GaoApp.Application.Interfaces.Services.AttributeValues;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Auth;
using GaoApp.Application.Interfaces.Services.Brands;
using GaoApp.Application.Interfaces.Services.Categories;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Media;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Application.Interfaces.Services.ProductAttributes;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Interfaces.Services.Suppliers;
using GaoApp.Application.Interfaces.Services.Taxes;
using GaoApp.Application.Interfaces.Services.Units;
using GaoApp.Application.Services.AttributeValues;
using GaoApp.Application.Services.Audit;
using GaoApp.Application.Services.Auth;
using GaoApp.Application.Services.Brands;
using GaoApp.Application.Services.Categories;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.Media;
using GaoApp.Application.Services.Orders;
using GaoApp.Application.Services.POSShifts;
using GaoApp.Application.Services.ProductAttributes;
using GaoApp.Application.Services.Products;
using GaoApp.Application.Services.Security;
using GaoApp.Application.Services.Suppliers;
using GaoApp.Application.Services.Taxes;
using GaoApp.Application.Services.Units;
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
        services.AddScoped<IPOSShiftService, POSShiftService>();

        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IStockDocumentService, StockDocumentService>();
        services.AddScoped<IProductUnitConversionService, ProductUnitConversionService>();
        services.AddScoped<IBarcodeLookupService, BarcodeLookupService>();
        services.AddScoped<IInventoryAdjustmentService, InventoryAdjustmentService>();
        services.AddScoped<IInventoryUnitResolver, InventoryUnitResolver>();
        services.AddScoped<IInventoryMovementService, InventoryMovementService>();
        services.AddScoped<IInventoryMovementNoteBuilder, InventoryMovementNoteBuilder>();
        services.AddScoped<IInventoryMovementFactory, InventoryMovementFactory>();
        services.AddScoped<IStockCountService, StockCountService>();
        services.AddScoped<IInventoryReservationService, InventoryReservationService>();
        services.AddScoped<IReturnableValuationFragmentService, ReturnableValuationFragmentService>();
        services.AddScoped<IReturnCostAllocator, ReturnCostAllocator>();
        services.AddScoped<IInventoryRevaluationService, InventoryRevaluationService>();
        services.AddScoped<IOrderInventoryIssueService, OrderInventoryIssueService>();
        services.AddScoped<IStockTransferService, StockTransferService>();

        services.AddScoped<ICurrentStorePermissionService, CurrentStorePermissionService>();
        services.AddScoped<IRoleAdminService, RoleAdminService>();
        services.AddScoped<IRolePermissionAdminService, RolePermissionAdminService>();
        services.AddScoped<IUserInStoreAdminService, UserInStoreAdminService>();

        services.AddScoped<IBarcodeHistoryService, BarcodeHistoryService>();
        services.AddScoped<IBarcodeGovernanceService, BarcodeGovernanceService>();
        services.AddScoped<IProductUnitBarcodeReadService, ProductUnitBarcodeReadService>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISalesReturnService, SalesReturnService>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        services.AddAutoMapper(typeof(DependencyInjection).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}