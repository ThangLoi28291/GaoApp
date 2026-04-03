using GaoApp.Application.Common;
using GaoApp.Application.Common.Abstractions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.AttributeValues;
using GaoApp.Application.Interfaces.Repositories.Audit;
using GaoApp.Application.Interfaces.Repositories.Auth;
using GaoApp.Application.Interfaces.Repositories.Brands;
using GaoApp.Application.Interfaces.Repositories.Categories;
using GaoApp.Application.Interfaces.Repositories.Customers;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Media;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Repositories.POSTerminals;
using GaoApp.Application.Interfaces.Repositories.ProductAttributes;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Application.Interfaces.Repositories.Suppliers;
using GaoApp.Application.Interfaces.Repositories.Taxes;
using GaoApp.Application.Interfaces.Repositories.Units;
using GaoApp.Application.Interfaces.Repositories.Users;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Repositories.Orders;
using GaoApp.Infrastructure.Identity;
using GaoApp.Infrastructure.Interceptors;
using GaoApp.Infrastructure.Network;
using GaoApp.Infrastructure.Repositories.AttributeValues;
using GaoApp.Infrastructure.Repositories.Audit;
using GaoApp.Infrastructure.Repositories.Auth;
using GaoApp.Infrastructure.Repositories.Brands;
using GaoApp.Infrastructure.Repositories.Categories;
using GaoApp.Infrastructure.Repositories.Customers;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Media;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.POSTerminals;
using GaoApp.Infrastructure.Repositories.ProductAttributes;
using GaoApp.Infrastructure.Repositories.Products;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Repositories.Suppliers;
using GaoApp.Infrastructure.Repositories.Taxes;
using GaoApp.Infrastructure.Repositories.Units;
using GaoApp.Infrastructure.Repositories.Users;
using GaoApp.Infrastructure.Services.Audit;
using GaoApp.Infrastructure.Services.Orders;
using GaoApp.Infrastructure.Storage;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace GaoApp.Infrastructure;

/// <summary>
/// Đăng ký dependency thuộc tầng Infrastructure.
///
/// Nguyên tắc:
/// - DbContext
/// - Repository
/// - UnitOfWork
/// - File storage
/// - Tenant concrete implementation
/// - Current user / network / audit runtime adapter
/// - EF interceptor
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // =========================================================
        // HTTP CONTEXT ACCESSOR
        // Dùng cho các service hạ tầng cần đọc request hiện tại.
        // =========================================================
        services.AddHttpContextAccessor();

        // Cache
        services.AddMemoryCache();
        services.AddScoped<ICacheService, MemoryCacheService>();

        // =========================================================
        // TENANT / CURRENT STORE
        // AppDbContext đang nhận ITenantContext qua constructor,
        // nên phần này bắt buộc phải được đăng ký ở Infrastructure.
        // =========================================================
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextWriter>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ICurrentStore, CurrentStore>();

        // =========================================================
        // CATEGORY
        // =========================================================
        services.AddScoped<ICategoryRepository, CategoryRepository>();

        // =========================================================
        // SUPPLIER
        // =========================================================
        services.AddScoped<ISupplierRepository, SupplierRepository>();

        // =========================================================
        // BRAND
        // =========================================================
        services.AddScoped<IBrandRepository, BrandRepository>();

        // =========================================================
        // TAX
        // =========================================================
        services.AddScoped<ITaxRepository, TaxRepository>();

        // =========================================================
        // PRODUCT ATTRIBUTE / ATTRIBUTE VALUE
        // =========================================================
        services.AddScoped<IProductAttributeRepository, ProductAttributeRepository>();
        services.AddScoped<IAttributeValueRepository, AttributeValueRepository>();

        // =========================================================
        // UNIT
        // =========================================================
        services.AddScoped<IUnitRepository, UnitRepository>();

        // =========================================================
        // PRODUCT
        // =========================================================
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductVariantRepository, ProductVariantRepository>();
        services.AddScoped<IVariantUsageChecker, VariantUsageChecker>();

        // =========================================================
        // MEDIA / STORAGE
        // =========================================================
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IMediaAssetRepository, MediaAssetRepository>();
        services.AddScoped<IProductImageRepository, ProductImageRepository>();

        // =========================================================
        // ORDER / POS
        // =========================================================
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderPaymentRepository, OrderPaymentRepository>();
        services.AddScoped<IOrderNumberGenerator, OrderNumberGenerator>();
        services.AddScoped<IPOSShiftRepository, POSShiftRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IPOSAuditLogRepository, POSAuditLogRepository>();

        // =========================================================
        // INVENTORY
        // =========================================================
        services.AddScoped<IWarehouseRepository, WarehouseRepository>();
        services.AddScoped<IInventoryBalanceRepository, InventoryBalanceRepository>();
        services.AddScoped<IInventoryTransactionRepository, InventoryTransactionRepository>();
        services.AddScoped<IStockDocumentRepository, StockDocumentRepository>();

        services.AddScoped<IProductUnitConversionRepository, ProductUnitConversionRepository>();
        services.AddScoped<IProductVariantUnitBarcodeRepository, ProductVariantUnitBarcodeRepository>();
        services.AddScoped<IProductBarcodeLookupRepository, ProductBarcodeLookupRepository>();

        services.AddScoped<INegativeInventoryLogRepository, NegativeInventoryLogRepository>();
        services.AddScoped<IStockCountRepository, StockCountRepository>();
        services.AddScoped<IInventoryValuationEntryRepository, InventoryValuationEntryRepository>();
        services.AddScoped<IOrderInventoryIssueRepository, OrderInventoryIssueRepository>();
        services.AddScoped<IStockTransferRepository, StockTransferRepository>();
        services.AddScoped<IInventoryReservationRepository, InventoryReservationRepository>();
        services.AddScoped<IDocumentNumberSequenceRepository, DocumentNumberSequenceRepository>();
        services.AddScoped<IInventoryCostLayerRepository, InventoryCostLayerRepository>();
        services.AddScoped<IInventoryCostLayerAllocationRepository, InventoryCostLayerAllocationRepository>();

        // =========================================================
        // UNIT OF WORK
        // =========================================================
        services.AddScoped<IAppUnitOfWork, AppUnitOfWork>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // =========================================================
        // SECURITY / USERS / AUTH
        // =========================================================
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IUserInStoreRepository, UserInStoreRepository>();
        services.AddScoped<IAuthUserRepository, AuthUserRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPOSTerminalRepository, POSTerminalRepository>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IProductVariantBarcodeHistoryRepository, ProductVariantBarcodeHistoryRepository>();
        services.AddScoped<IRolePermissionRepository, RolePermissionRepository>();
 

        // =========================================================
        // AUTH CONTEXT / NETWORK
        // =========================================================
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IClientNetworkInfo, ClientNetworkInfo>();

        // =========================================================
        // SALES RETURN
        // =========================================================
        services.AddScoped<ISalesReturnRepository, SalesReturnRepository>();

        // =========================================================
        // AUDIT
        // AuditSaveChangesInterceptor đang được gắn vào DbContext options.
        // =========================================================
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditExecutionContextAccessor, HttpAuditExecutionContextAccessor>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        // =========================================================
        // DB CONTEXT
        // AppDbContext nằm ở Infrastructure.
        // Nó đang phụ thuộc ITenantContext và dùng audit interceptor.
        // =========================================================
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            // 🔥 Fail fast (rất quan trọng)
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection chưa được cấu hình hoặc đang rỗng.");
            }

            options.UseSqlServer(connectionString);

            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });
        services.AddDbContextFactory<AuditLogDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection chưa được cấu hình hoặc đang rỗng.");
            }

            options.UseSqlServer(connectionString);

            // KHÔNG add interceptor ở context này
        }, ServiceLifetime.Scoped);

        return services;
    }
}