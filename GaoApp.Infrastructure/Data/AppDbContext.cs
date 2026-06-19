using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;


using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace GaoApp.Infrastructure.Data;

public class AppDbContext : DbContext
{
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantContext tenant,
        ICurrentUser currentUser)
        : base(options)
    {
        _tenant = tenant;
        _currentUser = currentUser;
    }

    /// <summary>
    /// StoreId hiện tại lấy từ TenantContext.
    /// Nếu null => host admin / design-time / non-tenant context.
    /// </summary>
    public int? CurrentStoreId => _tenant?.StoreId;

    /// <summary>
    /// Lấy UserId hiện tại phục vụ audit fields.
    /// </summary>
    private int? GetCurrentUserId()
    {
        return _currentUser.UserId;
    }

    public DbSet<Store> Stores => Set<Store>();

    // GIỮ 1 DbSet thống nhất, tránh duplicate UserStores/UserInStores
    public DbSet<UserInStore> UserInStores => Set<UserInStore>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Tax> Taxes => Set<Tax>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<AttributeValue> AttributeValues => Set<AttributeValue>();

    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductVariantAttributeValue> ProductVariantAttributeValues => Set<ProductVariantAttributeValue>();

    public DbSet<ProductVariantBarcodeHistory> ProductVariantBarcodeHistories => Set<ProductVariantBarcodeHistory>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderPayment> OrderPayments => Set<OrderPayment>();
    public DbSet<POSShift> POSShifts => Set<POSShift>();
    public DbSet<POSShiftCashTransaction> POSShiftCashTransactions => Set<POSShiftCashTransaction>();
    public DbSet<POSAuditLog> POSAuditLogs => Set<POSAuditLog>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
    public DbSet<StockDocument> StockDocuments => Set<StockDocument>();
    public DbSet<StockDocumentLine> StockDocumentLines => Set<StockDocumentLine>();
    public DbSet<ProductUnitConversion> ProductUnitConversions => Set<ProductUnitConversion>();
    public DbSet<ProductVariantUnitBarcode> ProductVariantUnitBarcodes => Set<ProductVariantUnitBarcode>();
    public DbSet<NegativeInventoryLog> NegativeInventoryLogs => Set<NegativeInventoryLog>();
    public DbSet<StockCountDocument> StockCountDocuments => Set<StockCountDocument>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();
    public DbSet<StockTransferDocument> StockTransferDocuments => Set<StockTransferDocument>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<POSTerminal> POSTerminals => Set<POSTerminal>();
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<SalesReturnLine> SalesReturnLines => Set<SalesReturnLine>();
    public DbSet<SalesReturnPayment> SalesReturnPayments => Set<SalesReturnPayment>();
    public DbSet<OrderInventoryIssue> OrderInventoryIssues => Set<OrderInventoryIssue>();
    public DbSet<OrderInventoryIssueLine> OrderInventoryIssueLines => Set<OrderInventoryIssueLine>();
    public DbSet<OrderInventoryIssueAction> OrderInventoryIssueActions => Set<OrderInventoryIssueAction>();
    public DbSet<InventoryValuationEntry> InventoryValuationEntries => Set<InventoryValuationEntry>();
    public DbSet<DocumentNumberSequence> DocumentNumberSequences => Set<DocumentNumberSequence>();
    public DbSet<OrderInventoryIssueLineAllocation> OrderInventoryIssueLineAllocations => Set<OrderInventoryIssueLineAllocation>();
    public DbSet<InventoryCostLayer> InventoryCostLayers => Set<InventoryCostLayer>();
    public DbSet<InventoryCostLayerAllocation> InventoryCostLayerAllocations => Set<InventoryCostLayerAllocation>();
    public DbSet<StoreBankAccount> StoreBankAccounts => Set<StoreBankAccount>();
    public DbSet<PosPaymentQrRequest> PosPaymentQrRequests => Set<PosPaymentQrRequest>();
    public DbSet<DisplayPromotion> DisplayPromotions => Set<DisplayPromotion>();
    public DbSet<InputInvoiceHead> InputInvoiceHeads => Set<InputInvoiceHead>();
    public DbSet<InputInvoiceDetail> InputInvoiceDetails => Set<InputInvoiceDetail>();
    public DbSet<StockDocumentInputInvoiceMap> StockDocumentInputInvoiceMaps => Set<StockDocumentInputInvoiceMap>();
    public DbSet<StockDocumentLineInputInvoiceMap> StockDocumentLineInputInvoiceMaps => Set<StockDocumentLineInputInvoiceMap>();
    public DbSet<InvoiceHead> InvoiceHeads => Set<InvoiceHead>();
    public DbSet<InvoiceDetail> InvoiceDetails => Set<InvoiceDetail>();
    public DbSet<AdminMenuItem> AdminMenuItems => Set<AdminMenuItem>();
    public DbSet<InventoryAdjustmentDocument> InventoryAdjustmentDocuments => Set<InventoryAdjustmentDocument>();
    public DbSet<InventoryAdjustmentLine> InventoryAdjustmentLines => Set<InventoryAdjustmentLine>();
    public DbSet<POSTerminalDevice> POSTerminalDevices => Set<POSTerminalDevice>();
    public DbSet<RewardSettings> RewardSettings => Set<RewardSettings>();
    public DbSet<CustomerRewardLedger> CustomerRewardLedgers => Set<CustomerRewardLedger>();
    public DbSet<CustomerRewardVoucher> CustomerRewardVouchers => Set<CustomerRewardVoucher>();
    public DbSet<OrderRewardVoucher> OrderRewardVouchers => Set<OrderRewardVoucher>();
    public DbSet<POSShiftCashDenomination> POSShiftCashDenominations => Set<POSShiftCashDenomination>();
    public DbSet<POSShiftHandoverSlip> POSShiftHandoverSlips => Set<POSShiftHandoverSlip>();

    public DbSet<POSShiftHandoverSlipDenomination> POSShiftHandoverSlipDenominations => Set<POSShiftHandoverSlipDenomination>();
    public DbSet<POSShiftClosingSlip> POSShiftClosingSlips => Set<POSShiftClosingSlip>();
    public DbSet<POSShiftClosingSlipDenomination> POSShiftClosingSlipDenominations => Set<POSShiftClosingSlipDenomination>();
    public DbSet<ProductBarcodeVerificationRequest> ProductBarcodeVerificationRequests => Set<ProductBarcodeVerificationRequest>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionItem> PromotionItems => Set<PromotionItem>();
    public DbSet<PromotionComboRule> PromotionComboRules => Set<PromotionComboRule>();
    public DbSet<InvoiceProviderSetting> InvoiceProviderSettings => Set<InvoiceProviderSetting>();

    public DbSet<InvoiceIntegrationLog> InvoiceIntegrationLogs => Set<InvoiceIntegrationLog>();
    public DbSet<InvoiceCorrectionCase> InvoiceCorrectionCases => Set<InvoiceCorrectionCase>();


    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Tự apply toàn bộ IEntityTypeConfiguration trong assembly Infrastructure
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Cấu hình nền tối thiểu còn để tại DbContext cho an toàn
        builder.Entity<Store>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.SubDomain).HasMaxLength(60).IsRequired();
            e.Property(x => x.SubDomainNormalized).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.SubDomainNormalized).IsUnique();
        });

        builder.Entity<UserInStore>(e =>
        {
            e.HasIndex(x => new { x.StoreId, x.UserId }).IsUnique();
            e.Property(x => x.IsActive).HasDefaultValue(true);
        });

        builder.Entity<Category>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.SortOrder).HasDefaultValue(0);

            e.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.Name }).IsUnique();

            e.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<DisplayPromotion>(entity =>
        {
            entity.ToTable("DisplayPromotions");

            entity.Property(x => x.Title)
                .HasMaxLength(250)
                .IsRequired();

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.MediaType)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(x => x.MediaUrl)
                .HasMaxLength(500);

            entity.Property(x => x.ButtonText)
                .HasMaxLength(100);

            entity.Property(x => x.BackgroundColor)
                .HasMaxLength(30);

            entity.Property(x => x.TextColor)
                .HasMaxLength(30);

            entity.HasIndex(x => new
            {
                x.StoreId,
                x.IsActive,
                x.SortOrder
            });
        });

        DisableCascadeDeleteToStore(builder);
        ApplyGlobalFilters(builder);
    }

    public override int SaveChanges()
    {
        ApplyAuditAndTenantRules();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndTenantRules();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditAndTenantRules()
    {
        var now = DateTime.UtcNow;
        var userId = GetCurrentUserId();

        foreach (var entry in ChangeTracker.Entries())
        {
            // =====================================================
            // 1. AUDIT + SOFT DELETE
            // =====================================================
            if (entry.Entity is BaseEntity baseEntity)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        baseEntity.CreatedAtUtc = now;
                        baseEntity.CreatedBy = userId;
                        baseEntity.IsDeleted = false;
                        break;

                    case EntityState.Modified:
                        baseEntity.UpdatedAtUtc = now;
                        baseEntity.UpdatedBy = userId;
                        break;

                    case EntityState.Deleted:
                        {
                            // ⚠️ Ngoại lệ: cho phép hard delete mapping này
                            if (entry.Entity is ProductVariantAttributeValue)
                                break;

                            // 🔥 convert sang soft delete
                            entry.State = EntityState.Modified;
                            baseEntity.IsDeleted = true;
                            baseEntity.DeletedAtUtc = now;
                            baseEntity.DeletedBy = userId;
                            break;
                        }
                }
            }

            // =====================================================
            // 2. 🔥 TENANT HARD GUARD (CRITICAL)
            // =====================================================
            if (entry.Entity is BaseStoreEntity storeEntity)
            {
                var currentStoreId = _tenant?.StoreId;

                // ✅ CHO PHÉP SYSTEM CONTEXT
                if (!currentStoreId.HasValue)
                {
                    continue;
                }

                if (entry.State == EntityState.Added)
                {
                    if (storeEntity.StoreId > 0 && storeEntity.StoreId != currentStoreId.Value)
                    {
                        throw new InvalidOperationException(
                            $"StoreId={storeEntity.StoreId} không khớp TenantContext={currentStoreId.Value}");
                    }

                    storeEntity.StoreId = currentStoreId.Value;
                }

                if (entry.State == EntityState.Modified)
                {
                    var originalStoreId = (int)entry.OriginalValues["StoreId"];

                    if (originalStoreId != currentStoreId.Value)
                    {
                        throw new InvalidOperationException(
                            $"Tenant mismatch UPDATE");
                    }

                    entry.Property("StoreId").IsModified = false;
                }

                if (entry.State == EntityState.Deleted)
                {
                    var originalStoreId = (int)entry.OriginalValues["StoreId"];

                    if (originalStoreId != currentStoreId.Value)
                    {
                        throw new InvalidOperationException(
                            $"Tenant mismatch DELETE");
                    }
                }
            }
        }
    }

    private void ApplyGlobalFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            var isBaseEntity = typeof(BaseEntity).IsAssignableFrom(clrType);
            var isStoreEntity = typeof(BaseStoreEntity).IsAssignableFrom(clrType);

            if (!isBaseEntity && !isStoreEntity)
                continue;

            var param = Expression.Parameter(clrType, "e");
            Expression? body = null;

            // Soft delete: e.IsDeleted == false
            if (isBaseEntity)
            {
                var isDeletedProp = Expression.Property(param, nameof(BaseEntity.IsDeleted));
                var notDeleted = Expression.Equal(
                    isDeletedProp,
                    Expression.Constant(false));

                body = notDeleted;
            }

            // Tenant filter: CurrentStoreId == null || e.StoreId == CurrentStoreId
            if (isStoreEntity)
            {
                var storeProp = Expression.Property(param, nameof(BaseStoreEntity.StoreId));

                var currentStoreId = Expression.Property(
                    Expression.Constant(this),
                    nameof(CurrentStoreId));

                var nullCheck = Expression.Equal(
                    currentStoreId,
                    Expression.Constant(null, typeof(int?)));

                var storeIdValue = Expression.Convert(currentStoreId, typeof(int));
                var storeMatch = Expression.Equal(storeProp, storeIdValue);

                var storeCondition = Expression.OrElse(nullCheck, storeMatch);

                body = body == null
                    ? storeCondition
                    : Expression.AndAlso(body, storeCondition);
            }

            if (body != null)
            {
                var lambda = Expression.Lambda(body, param);
                entityType.SetQueryFilter(lambda);
            }
        }
    }

    private static void DisableCascadeDeleteToStore(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var fk in entityType.GetForeignKeys())
            {
                if (fk.PrincipalEntityType.ClrType == typeof(Store))
                {
                    fk.DeleteBehavior = DeleteBehavior.NoAction;
                }
            }
        }
    }
}