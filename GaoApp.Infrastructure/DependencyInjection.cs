using GaoApp.Application.Common;
using GaoApp.Application.Common.Abstractions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.AdminMenus;
using GaoApp.Application.Interfaces.Repositories.AttributeValues;
using GaoApp.Application.Interfaces.Repositories.Audit;
using GaoApp.Application.Interfaces.Repositories.Auth;
using GaoApp.Application.Interfaces.Repositories.Brands;
using GaoApp.Application.Interfaces.Repositories.Categories;
using GaoApp.Application.Interfaces.Repositories.Customers;
using GaoApp.Application.Interfaces.Repositories.Display;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Repositories.Media;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Application.Interfaces.Repositories.POSPaymentQrs;
using GaoApp.Application.Interfaces.Repositories.POSShiftClosingSlips;
using GaoApp.Application.Interfaces.Repositories.POSShiftHandoverSlips;
using GaoApp.Application.Interfaces.Repositories.POSTerminals;
using GaoApp.Application.Interfaces.Repositories.ProductAttributes;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Repositories.Promotions;
using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Application.Interfaces.Repositories.StoreBankAccounts;
using GaoApp.Application.Interfaces.Repositories.Suppliers;
using GaoApp.Application.Interfaces.Repositories.Taxes;
using GaoApp.Application.Interfaces.Repositories.Units;
using GaoApp.Application.Interfaces.Repositories.Users;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Infrastructure.Caching;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Repositories.Orders;
using GaoApp.Infrastructure.Identity;
using GaoApp.Infrastructure.Interceptors;
using GaoApp.Infrastructure.Network;
using GaoApp.Infrastructure.Repositories.AdminMenus;
using GaoApp.Infrastructure.Repositories.AttributeValues;
using GaoApp.Infrastructure.Repositories.Audit;
using GaoApp.Infrastructure.Repositories.Auth;
using GaoApp.Infrastructure.Repositories.Brands;
using GaoApp.Infrastructure.Repositories.Categories;
using GaoApp.Infrastructure.Repositories.Customers;
using GaoApp.Infrastructure.Repositories.Display;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Repositories.LegalEntities;
using GaoApp.Infrastructure.Repositories.Media;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.Purchases;
using GaoApp.Infrastructure.Repositories.POSPaymentQrs;
using GaoApp.Infrastructure.Repositories.POSShiftClosingSlips;
using GaoApp.Infrastructure.Repositories.POSShiftHandoverSlips;
using GaoApp.Infrastructure.Repositories.POSTerminals;
using GaoApp.Infrastructure.Repositories.ProductAttributes;
using GaoApp.Infrastructure.Repositories.Products;
using GaoApp.Infrastructure.Repositories.Promotions;
using GaoApp.Infrastructure.Repositories.Rewards;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Repositories.StoreBankAccounts;
using GaoApp.Infrastructure.Repositories.Suppliers;
using GaoApp.Infrastructure.Repositories.Taxes;
using GaoApp.Infrastructure.Repositories.Units;
using GaoApp.Infrastructure.Repositories.Users;
using GaoApp.Infrastructure.Security;
using GaoApp.Infrastructure.Services.Audit;
using GaoApp.Infrastructure.Services.Invoices;
using GaoApp.Infrastructure.Services.Orders;
using GaoApp.Infrastructure.Storage;
using GaoApp.Infrastructure.Tenant;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GaoApp.Application.Interfaces.Repositories.Reports;
using GaoApp.Infrastructure.Repositories.Reports;


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
        IConfiguration configuration,
        Microsoft.Extensions.Hosting.IHostEnvironment? hostEnvironment = null)
    {
        // =========================================================
        // HTTP CONTEXT ACCESSOR
        // Dùng cho các service hạ tầng cần đọc request hiện tại.
        // =========================================================
        services.AddHttpContextAccessor();

        // Cache
        services.AddMemoryCache();
        services.AddScoped<ICacheService, MemoryCacheService>();

        // Mật khẩu provider hóa đơn được bảo vệ trước khi ghi database.
        // Resolver + state dùng chung bảo đảm validation và key repository
        // luôn trỏ cùng một đường dẫn. Không tạo directory ở bước đăng ký DI.
        services.AddSingleton<IDataProtectionKeysPathResolver,
            DataProtectionKeysPathResolver>();
        services.AddSingleton<IDataProtectionKeysDirectoryValidator,
            DataProtectionKeysDirectoryValidator>();
        services.AddSingleton<DataProtectionKeysPathState>();
        services.AddSingleton<
            Microsoft.Extensions.Options.IConfigureOptions<
                Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>,
            ConfigureDataProtectionKeyManagementOptions>();

        var dataProtection = services.AddDataProtection()
            .SetApplicationName("GaoApp");

        if (OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }

        dataProtection.AddHostKeyEncryption(configuration, hostEnvironment);

        services.AddSingleton<IInvoiceProviderCredentialProtector,
            InvoiceProviderCredentialProtector>();

        // =========================================================
        // TENANT / CURRENT STORE
        // AppDbContext đang nhận ITenantContext qua constructor,
        // nên phần này bắt buộc phải được đăng ký ở Infrastructure.
        // =========================================================
        services.AddScoped<TenantContext>();
        services.AddScoped<GaoApp.Application.Interfaces.Services.Security.IStoreAdminAccess,
            GaoApp.Infrastructure.Security.StoreAdminAccess>();
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
        services.AddSingleton<UploadPathResolver>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IMediaAssetRepository, MediaAssetRepository>();
        services.AddOptions<GaoApp.Application.Common.Options.MediaCleanupOptions>()
            .Bind(configuration.GetSection(GaoApp.Application.Common.Options.MediaCleanupOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GaoApp.Application.Common.Options.MediaCleanupOptions>>().Value);
        services.AddScoped<GaoApp.Infrastructure.Services.Media.MediaLibraryService>();
        services.AddScoped<IProductImageRepository, ProductImageRepository>();
        services.AddScoped<IProductBarcodeVerificationRepository, ProductBarcodeVerificationRepository>();
        services.AddScoped<GaoApp.Application.Interfaces.Services.Products.IReceiptBarcodeProposalService, GaoApp.Infrastructure.Services.Products.ReceiptBarcodeProposalService>();
        services.AddScoped<GaoApp.Application.Interfaces.Services.Purchases.IReceiptIntakeCatalog, GaoApp.Infrastructure.Services.Products.ReceiptIntakeCatalog>();

        // =========================================================
        // ORDER / POS
        // =========================================================
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ICustomerDepositService, GaoApp.Infrastructure.Services.Orders.CustomerDepositService>();
        services.AddScoped<ICustomerReceivableService, GaoApp.Infrastructure.Services.Orders.CustomerReceivableService>();
        services.AddScoped<IOrderPaymentRepository, OrderPaymentRepository>();
        services.AddScoped<IOrderLegalEntityAllocationRepository, OrderLegalEntityAllocationRepository>();
        services.AddScoped<IOrderLegalEntityAllocationReversalRepository, OrderLegalEntityAllocationReversalRepository>();
        services.AddScoped<ILegalEntityReconciliationRepository, LegalEntityReconciliationRepository>();
        services.AddScoped<ILegalEntityCanaryRepository, LegalEntityCanaryRepository>();
        services.AddScoped<IOrderNumberGenerator, OrderNumberGenerator>();
        services.AddScoped<IPOSShiftRepository, POSShiftRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerManagementRepository, CustomerManagementRepository>();
        services.AddScoped<IPOSAuditLogRepository, POSAuditLogRepository>();
        services.AddScoped<IStoreBankAccountRepository, StoreBankAccountRepository>();
        services.AddScoped<IStoreBankAccountIndexReadRepository, StoreBankAccountIndexReadRepository>();
        services.AddScoped<ISalesReportReadRepository, SalesReportReadRepository>();
        services.AddOptions<GaoApp.Application.Common.Options.ProfitReportLimits>()
            .Bind(configuration.GetSection(GaoApp.Application.Common.Options.ProfitReportLimits.SectionName))
            .Validate(x => x.IsValid(), "Invalid Reports:Profit resource limits.").ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GaoApp.Application.Common.Options.ProfitReportLimits>>().Value);
        services.AddSingleton<GaoApp.Application.Services.Reports.ProfitReportExecutionGate>();
        services.AddScoped<IProfitReportReadRepository, ProfitReportReadRepository>();

        services.AddScoped<IPOSPaymentQrRequestRepository, POSPaymentQrRequestRepository>();
        services.AddScoped<IDisplayPromotionRepository, DisplayPromotionRepository>();
        services.AddScoped<IPromotionRepository, PromotionRepository>();

        services.AddScoped<IInvoiceProviderSettingRepository, InvoiceProviderSettingRepository>();
        services.AddOptions<ExternalHttpResilienceOptions>()
            .Bind(configuration.GetSection(ExternalHttpResilienceOptions.SectionName))
            .Validate(
                options => options.HasValidTimeouts(),
                $"External HTTP timeouts must be between 1 and {ExternalHttpResilienceOptions.MaximumTimeoutSeconds} seconds.")
            .ValidateOnStart();

        services.AddTransient<GaoApp.Infrastructure.Security.ViettelEndpointGuardHandler>();
        services.AddHttpClient<IViettelInvoiceAuthClient, ViettelInvoiceAuthClient>((sp, client) =>
        {
            client.Timeout = sp
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ExternalHttpResilienceOptions>>()
                .Value
                .AuthenticationTimeout;
        }).AddHttpMessageHandler<GaoApp.Infrastructure.Security.ViettelEndpointGuardHandler>()
          .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<IViettelInvoicePreviewClient, ViettelInvoicePreviewClient>((sp, client) =>
        {
            client.Timeout = sp
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ExternalHttpResilienceOptions>>()
                .Value
                .NonIdempotentWriteTimeout;
        }).AddHttpMessageHandler<GaoApp.Infrastructure.Security.ViettelEndpointGuardHandler>()
          .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<IInvoiceIntegrationLogRepository, InvoiceIntegrationLogRepository>();
        services.AddHttpClient<IViettelInvoiceIssueClient, ViettelInvoiceIssueClient>((sp, client) =>
        {
            client.Timeout = sp
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ExternalHttpResilienceOptions>>()
                .Value
                .NonIdempotentWriteTimeout;
        }).AddHttpMessageHandler<GaoApp.Infrastructure.Security.ViettelEndpointGuardHandler>()
          .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<IInvoiceFileStorage, InvoiceFileStorage>();
        services.AddScoped<IInputInvoiceDocumentLibrary, FileSystemInputInvoiceDocumentLibrary>();

        services.AddHttpClient<IViettelOfficialFileClient, ViettelOfficialFileClient>((sp, client) =>
        {
            client.Timeout = sp
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ExternalHttpResilienceOptions>>()
                .Value
                .FileDownloadTimeout;
        }).AddHttpMessageHandler<GaoApp.Infrastructure.Security.ViettelEndpointGuardHandler>()
          .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<IViettelInvoiceLookupClient, ViettelInvoiceLookupClient>((sp, client) =>
        {
            client.Timeout = sp
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ExternalHttpResilienceOptions>>()
                .Value
                .SafeReadTimeout;
        }).AddHttpMessageHandler<GaoApp.Infrastructure.Security.ViettelEndpointGuardHandler>()
          .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<IViettelInvoiceEmailClient, ViettelInvoiceEmailClient>((sp, client) =>
        {
            client.Timeout = sp
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ExternalHttpResilienceOptions>>()
                .Value
                .NonIdempotentWriteTimeout;
        }).AddHttpMessageHandler<GaoApp.Infrastructure.Security.ViettelEndpointGuardHandler>()
          .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<IViettelInvoiceListClient, ViettelInvoiceListClient>((sp, client) =>
        {
            client.Timeout = sp
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ExternalHttpResilienceOptions>>()
                .Value
                .SafeReadTimeout;
        }).AddHttpMessageHandler<GaoApp.Infrastructure.Security.ViettelEndpointGuardHandler>()
          .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<IInvoiceCorrectionRepository, InvoiceCorrectionRepository>();
        services.Configure<TaxCodeLookupOptions>(
    configuration.GetSection("TaxCodeLookup"));

        services.AddHttpClient<ITaxCodeLookupService, VietQrTaxCodeLookupService>((sp, client) =>
        {
            var options = sp
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<TaxCodeLookupOptions>>()
                .Value;

            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");

            var timeoutSeconds = options.TimeoutSeconds <= 0
                ? 10
                : Math.Min(
                    options.TimeoutSeconds,
                    ExternalHttpResilienceOptions.MaximumTimeoutSeconds);

            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });
        services.AddScoped<IInvoiceBuyerProfileRepository, InvoiceBuyerProfileRepository>();
        // =========================================================
        // REWARDS
        // =========================================================
        services.AddScoped<IRewardSettingsRepository, RewardSettingsRepository>();
        services.AddScoped<ICustomerRewardLedgerRepository, CustomerRewardLedgerRepository>();
        services.AddScoped<ICustomerRewardVoucherRepository, CustomerRewardVoucherRepository>();
        services.AddScoped<IRewardVoucherIndexReadRepository, RewardVoucherIndexReadRepository>();
        services.AddScoped<IRewardOrderRepository, RewardOrderRepository>();
        services.AddScoped<IPOSShiftHandoverSlipRepository, POSShiftHandoverSlipRepository>();
        services.AddScoped<IPOSShiftClosingSlipRepository, POSShiftClosingSlipRepository>();

        // =========================================================
        // INVENTORY
        // =========================================================
        services.AddScoped<IWarehouseRepository, WarehouseRepository>();
        services.AddScoped<ILegalEntityRepository, LegalEntityRepository>();
        services.AddScoped<IInventoryBalanceRepository, InventoryBalanceRepository>();
        services.AddScoped<IInventoryInquiryReadRepository, InventoryInquiryReadRepository>();
        services.AddScoped<IInventoryLedgerIndexReadRepository, InventoryLedgerIndexReadRepository>();
        services.AddScoped<IStockTransferIndexReadRepository, StockTransferIndexReadRepository>();
        services.AddScoped<IStockCountIndexReadRepository, StockCountIndexReadRepository>();
        services.AddScoped<IInventoryAdjustmentIndexReadRepository, InventoryAdjustmentIndexReadRepository>();
        services.AddScoped<IInventoryTransactionRepository, InventoryTransactionRepository>();
        services.AddScoped<IInventoryPostingTransactionCoordinator, InventoryPostingTransactionCoordinator>();
        services.AddScoped<IStockDocumentRepository, StockDocumentRepository>();
        services.AddScoped<IPurchaseReceivingWorkbenchRepository, PurchaseReceivingWorkbenchRepository>();
        services.AddScoped<IStockDocumentProvisionalItemRepository, StockDocumentProvisionalItemRepository>();
        services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddScoped<IPurchaseOrderIndexReadRepository, PurchaseOrderIndexReadRepository>();
        services.AddScoped<IPurchaseRequestRepository, PurchaseRequestRepository>();
        services.AddScoped<IPurchaseRequestIndexReadRepository, PurchaseRequestIndexReadRepository>();
        services.AddScoped<IStockDocumentLookupRepository, StockDocumentLookupRepository>();
        services.AddScoped<IInventoryAdjustmentDocumentRepository, InventoryAdjustmentDocumentRepository>();
        services.AddScoped<IInventoryAdjustmentDocumentNumberRepository,
    InventoryAdjustmentDocumentNumberRepository>();
        services.AddScoped<IInventoryCostSuggestionRepository, InventoryCostSuggestionRepository>();

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
        services.AddScoped<IInputInvoiceRepository, InputInvoiceRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IAutoInvoiceRepository, AutoInvoiceRepository>();
        services.AddScoped<IInvoiceInputStockRepository, InvoiceInputStockRepository>();
        services.AddScoped<IInvoiceInputStockReadRepository, InvoiceInputStockReadRepository>();
        services.AddScoped<IAdminMenuRepository, AdminMenuRepository>();
        services.AddScoped<IAdminMenuPermissionRepository, AdminMenuPermissionRepository>();

        // =========================================================
        // UNIT OF WORK
        // =========================================================
        services.AddScoped<IAppUnitOfWork, AppUnitOfWork>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // =========================================================
        // SECURITY / USERS / AUTH
        // =========================================================
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRoleIndexReadRepository, RoleIndexReadRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IUserInStoreRepository, UserInStoreRepository>();
        services.AddScoped<IEmployeeIndexReadRepository, EmployeeIndexReadRepository>();
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

            options.UseGaoAppSqlServer(connectionString);

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

            options.UseGaoAppSqlServer(connectionString);

            // KHÔNG add interceptor ở context này
        }, ServiceLifetime.Scoped);

        return services;
    }
}
