using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;


using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
        : this((DbContextOptions)options, tenant, currentUser)
    {
    }

    /// <summary>
    /// Cho phép context chuyên biệt trong test dùng đúng DbContextOptions của
    /// chính context đó. Constructor public phía trên vẫn là đường chạy runtime.
    /// </summary>
    protected AppDbContext(
        DbContextOptions options,
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

    public int? CurrentUserId => _currentUser.UserId;
    public string? CurrentUserName => _currentUser.UserName;
    public bool IsCurrentUserAuthenticated => _currentUser.IsAuthenticated;

    /// <summary>
    /// Lấy UserId hiện tại phục vụ audit fields.
    /// </summary>
    private int? GetCurrentUserId()
    {
        return _currentUser.UserId;
    }

    public DbSet<Store> Stores => Set<Store>();
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<LegalEntityActivationEvent> LegalEntityActivationEvents => Set<LegalEntityActivationEvent>();

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
    public DbSet<OrderLegalEntityAllocation> OrderLegalEntityAllocations => Set<OrderLegalEntityAllocation>();
    public DbSet<OrderLegalEntityAllocationReversal> OrderLegalEntityAllocationReversals => Set<OrderLegalEntityAllocationReversal>();
    public DbSet<SalesReturnRestockFragment> SalesReturnRestockFragments => Set<SalesReturnRestockFragment>();
    public DbSet<OrderPayment> OrderPayments => Set<OrderPayment>();
    public DbSet<POSShift> POSShifts => Set<POSShift>();
    public DbSet<POSShiftCashTransaction> POSShiftCashTransactions => Set<POSShiftCashTransaction>();
    public DbSet<POSAuditLog> POSAuditLogs => Set<POSAuditLog>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<InvoiceInputStockSupplementalMovement> InvoiceInputStockSupplementalMovements => Set<InvoiceInputStockSupplementalMovement>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
    public DbSet<StockDocument> StockDocuments => Set<StockDocument>();
    public DbSet<StockDocumentLine> StockDocumentLines => Set<StockDocumentLine>();
    public DbSet<PurchaseReceiptPricingPlan> PurchaseReceiptPricingPlans => Set<PurchaseReceiptPricingPlan>();
    public DbSet<PurchaseReceiptBillLine> PurchaseReceiptBillLines => Set<PurchaseReceiptBillLine>();
    public DbSet<PurchaseReceiptPricingPlanLine> PurchaseReceiptPricingPlanLines => Set<PurchaseReceiptPricingPlanLine>();
    public DbSet<PurchaseReceiptPricingRule> PurchaseReceiptPricingRules => Set<PurchaseReceiptPricingRule>();
    public DbSet<PurchaseReceiptPricingRuleSource> PurchaseReceiptPricingRuleSources => Set<PurchaseReceiptPricingRuleSource>();
    public DbSet<PurchaseReceiptGiftValuation> PurchaseReceiptGiftValuations => Set<PurchaseReceiptGiftValuation>();
    public DbSet<PurchaseReceiptAuditEvent> PurchaseReceiptAuditEvents =>
        Set<PurchaseReceiptAuditEvent>();
    public DbSet<PurchaseReceivingAction> PurchaseReceivingActions =>
        Set<PurchaseReceivingAction>();
    public DbSet<StockDocumentProvisionalItem> StockDocumentProvisionalItems =>
        Set<StockDocumentProvisionalItem>();
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
    public DbSet<InputInvoiceLibraryEntry> InputInvoiceLibraryEntries => Set<InputInvoiceLibraryEntry>();
    public DbSet<InputInvoiceLibraryReview> InputInvoiceLibraryReviews => Set<InputInvoiceLibraryReview>();
    public DbSet<InputInvoiceDetail> InputInvoiceDetails => Set<InputInvoiceDetail>();
    public DbSet<InputInvoiceItemCatalogMap> InputInvoiceItemCatalogMaps =>
        Set<InputInvoiceItemCatalogMap>();
    public DbSet<InputInvoiceSupplierResolutionEvent> InputInvoiceSupplierResolutionEvents =>
        Set<InputInvoiceSupplierResolutionEvent>();
    public DbSet<StockDocumentInputInvoiceMap> StockDocumentInputInvoiceMaps => Set<StockDocumentInputInvoiceMap>();
    public DbSet<StockDocumentLineInputInvoiceMap> StockDocumentLineInputInvoiceMaps => Set<StockDocumentLineInputInvoiceMap>();
    public DbSet<StockDocumentInputInvoiceReconciliation> StockDocumentInputInvoiceReconciliations =>
        Set<StockDocumentInputInvoiceReconciliation>();
    public DbSet<StockDocumentInputInvoiceDetailReconciliation> StockDocumentInputInvoiceDetailReconciliations =>
        Set<StockDocumentInputInvoiceDetailReconciliation>();
    public DbSet<InvoiceHead> InvoiceHeads => Set<InvoiceHead>();
    public DbSet<InvoiceDetail> InvoiceDetails => Set<InvoiceDetail>();
    public DbSet<InvoiceBuyerSelfServiceRequest> InvoiceBuyerSelfServiceRequests =>
    Set<InvoiceBuyerSelfServiceRequest>();
    public DbSet<AutoInvoiceSettings> AutoInvoiceSettings => Set<AutoInvoiceSettings>();
    public DbSet<AutoInvoiceOperation> AutoInvoiceOperations => Set<AutoInvoiceOperation>();
    public DbSet<AutoInvoiceOperationSource> AutoInvoiceOperationSources => Set<AutoInvoiceOperationSource>();
    public DbSet<AutoInvoiceWorkerState> AutoInvoiceWorkerStates => Set<AutoInvoiceWorkerState>();
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
    public DbSet<InvoiceBuyerProfile> InvoiceBuyerProfiles => Set<InvoiceBuyerProfile>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseOrderAction> PurchaseOrderActions => Set<PurchaseOrderAction>();
    public DbSet<PurchasePayable> PurchasePayables => Set<PurchasePayable>();
    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();
    public DbSet<PurchaseRequestLine> PurchaseRequestLines => Set<PurchaseRequestLine>();
    public DbSet<PurchaseRequestAction> PurchaseRequestActions => Set<PurchaseRequestAction>();


    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Tự apply toàn bộ IEntityTypeConfiguration trong assembly Infrastructure
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        builder.Entity<PurchaseReceiptAuditEvent>()
            .HasQueryFilter(x =>
                CurrentStoreId == null || x.StoreId == CurrentStoreId);

        builder.Entity<InputInvoiceSupplierResolutionEvent>()
            .HasQueryFilter(x =>
                CurrentStoreId == null || x.StoreId == CurrentStoreId);

        // Cấu hình nền tối thiểu còn để tại DbContext cho an toàn
        builder.Entity<Store>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ReceiptName).HasMaxLength(200);
            e.Property(x => x.GuestWifiName).HasMaxLength(128);
            e.Property(x => x.GuestWifiPassword).HasMaxLength(128);
            e.Property(x => x.ReceiptAddress).HasMaxLength(300);
            e.Property(x => x.ReceiptPhone).HasMaxLength(50);
            e.Property(x => x.ReceiptTemplateKey).HasMaxLength(100);
            e.Property(x => x.SubDomain).HasMaxLength(60).IsRequired();
            e.Property(x => x.SubDomainNormalized).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.SubDomainNormalized).IsUnique();
            e.Property(x => x.IsMultiLegalEntityEnabled)
                .IsRequired()
                .HasDefaultValue(false);
            e.Property(x => x.MultiLegalEntityActivatedAtUtc);
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
        => SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ChangeTracker.DetectChanges();

        var pendingChanges = CapturePendingChanges();
        ProtectPurchaseReceiptAuditEvents(pendingChanges);
        ProtectInputInvoiceSupplierResolutionEvents(pendingChanges);
        ValidateTenantOwnership(pendingChanges);
        ApplyAuditAndTenantRules(pendingChanges);

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
        => SaveChangesAsync(
            acceptAllChangesOnSuccess: true,
            cancellationToken: cancellationToken);

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();

        var pendingChanges = CapturePendingChanges();
        ProtectPurchaseReceiptAuditEvents(pendingChanges);
        ProtectInputInvoiceSupplierResolutionEvents(pendingChanges);
        await ValidateTenantOwnershipAsync(
            pendingChanges,
            cancellationToken);

        ApplyAuditAndTenantRules(pendingChanges);

        return await base.SaveChangesAsync(
            acceptAllChangesOnSuccess,
            cancellationToken: cancellationToken);
    }

    private List<PendingChange> CapturePendingChanges()
    {
        return ChangeTracker.Entries()
            .Where(static entry =>
                entry.State is EntityState.Added or
                    EntityState.Modified or
                    EntityState.Deleted)
            .Select(static entry => new PendingChange(entry))
            .ToList();
    }

    private void ProtectPurchaseReceiptAuditEvents(
        IReadOnlyList<PendingChange> pendingChanges)
    {
        foreach (var pendingChange in pendingChanges)
        {
            if (pendingChange.Entry.Entity is not PurchaseReceiptAuditEvent
                auditEvent)
            {
                continue;
            }

            if (pendingChange.StateBeforeAudit is EntityState.Modified or
                EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Purchase receipt audit events are append-only.");
            }

            if (pendingChange.StateBeforeAudit != EntityState.Added)
            {
                continue;
            }

            var storeId = CurrentStoreId;
            if (!storeId.HasValue || storeId.Value <= 0)
            {
                throw new InvalidOperationException(
                    "A current store is required to append receipt audit evidence.");
            }

            if (!IsCurrentUserAuthenticated ||
                !CurrentUserId.HasValue ||
                CurrentUserId.Value <= 0)
            {
                throw new InvalidOperationException(
                    "An authenticated actor is required to append receipt audit evidence.");
            }

            if (auditEvent.StockDocument is not null &&
                auditEvent.StockDocument.StoreId > 0 &&
                auditEvent.StockDocument.StoreId != storeId.Value)
            {
                throw new InvalidOperationException(
                    "Receipt audit evidence does not belong to the current store.");
            }

            if (!Enum.IsDefined(auditEvent.EventType))
            {
                throw new InvalidOperationException(
                    "Receipt audit event type is invalid.");
            }

            auditEvent.StoreId = storeId.Value;
            auditEvent.ActorUserId = CurrentUserId.Value;
            var actorUserName = string.IsNullOrWhiteSpace(CurrentUserName)
                ? null
                : CurrentUserName.Trim();
            auditEvent.ActorUserName = actorUserName?.Length > 200
                ? actorUserName[..200]
                : actorUserName;
            auditEvent.OccurredAtUtc = DateTime.UtcNow;
            auditEvent.IsSuccess = auditEvent.EventType is not (
                GaoApp.Domain.Enums.PurchaseReceiptAuditEventType.InputInvoiceOwnerLinkBlocked or
                GaoApp.Domain.Enums.PurchaseReceiptAuditEventType.InputInvoiceOwnerConfirmBlocked);
        }
    }

    private void ProtectInputInvoiceSupplierResolutionEvents(
        IReadOnlyList<PendingChange> pendingChanges)
    {
        foreach (var pendingChange in pendingChanges)
        {
            if (pendingChange.Entry.Entity is not InputInvoiceSupplierResolutionEvent
                resolutionEvent)
            {
                continue;
            }

            if (pendingChange.StateBeforeAudit is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Input-invoice Supplier resolution events are append-only.");
            }

            if (pendingChange.StateBeforeAudit != EntityState.Added)
                continue;

            var storeId = CurrentStoreId;
            if (!storeId.HasValue || storeId.Value <= 0)
            {
                throw new InvalidOperationException(
                    "A current store is required to append Supplier resolution evidence.");
            }

            if (!IsCurrentUserAuthenticated ||
                !CurrentUserId.HasValue ||
                CurrentUserId.Value <= 0)
            {
                throw new InvalidOperationException(
                    "An authenticated actor is required to append Supplier resolution evidence.");
            }

            if (resolutionEvent.StoreId > 0 && resolutionEvent.StoreId != storeId.Value)
            {
                throw new InvalidOperationException(
                    "Supplier resolution evidence does not belong to the current store.");
            }

            if (!Enum.IsDefined(resolutionEvent.EventType) ||
                !Enum.IsDefined(resolutionEvent.PreviousStatus) ||
                !Enum.IsDefined(resolutionEvent.NewStatus))
            {
                throw new InvalidOperationException(
                    "Supplier resolution evidence contains an invalid status or action.");
            }

            if (resolutionEvent.EventType is
                    GaoApp.Domain.Enums.InputInvoiceSupplierResolutionEventType.ManualCandidateSelected or
                    GaoApp.Domain.Enums.InputInvoiceSupplierResolutionEventType.ReceiptAligned or
                    GaoApp.Domain.Enums.InputInvoiceSupplierResolutionEventType.CanonicalCorrected &&
                string.IsNullOrWhiteSpace(resolutionEvent.Reason))
            {
                throw new InvalidOperationException(
                    "A reason is required for manual Supplier resolution evidence.");
            }

            resolutionEvent.StoreId = storeId.Value;
            resolutionEvent.ActorUserId = CurrentUserId.Value;
            resolutionEvent.CreatedAtUtc = DateTime.UtcNow;
            resolutionEvent.Reason = string.IsNullOrWhiteSpace(resolutionEvent.Reason)
                ? null
                : resolutionEvent.Reason.Trim();
        }
    }

    /// <summary>
    /// Phase A đồng bộ: xác minh quyền sở hữu tenant và lấy database values
    /// cần thiết để soft delete detached entity mà không ghi đè dữ liệu nghiệp vụ.
    /// Không thay đổi entity, audit field hoặc EntityState trong phase này.
    /// </summary>
    private void ValidateTenantOwnership(
        IReadOnlyList<PendingChange> pendingChanges)
    {
        var currentStoreId = CurrentStoreId;

        foreach (var pendingChange in pendingChanges)
        {
            ValidateAddedEntry(
                pendingChange,
                currentStoreId);

            if (!RequiresDatabaseValues(
                pendingChange,
                currentStoreId))
            {
                continue;
            }

            // CaptureDatabaseValues vừa kiểm tra null, vừa trả về
            // PropertyValues non-null cho các bước xác minh tiếp theo.
            var databaseValues = CaptureDatabaseValues(
                pendingChange,
                pendingChange.Entry.GetDatabaseValues());

            ValidateDatabaseOwnershipIfRequired(
                pendingChange,
                currentStoreId);

            ValidateSafeSoftDeleteConcurrency(
                pendingChange,
                databaseValues);
        }
    }

    /// <summary>
    /// Phase A bất đồng bộ: xác minh quyền sở hữu tenant và lấy database values
    /// cần thiết để soft delete detached entity mà không ghi đè dữ liệu nghiệp vụ.
    /// Không thay đổi entity, audit field hoặc EntityState trong phase này.
    /// </summary>
    private async Task ValidateTenantOwnershipAsync(
        IReadOnlyList<PendingChange> pendingChanges,
        CancellationToken cancellationToken)
    {
        var currentStoreId = CurrentStoreId;

        foreach (var pendingChange in pendingChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ValidateAddedEntry(
                pendingChange,
                currentStoreId);

            if (!RequiresDatabaseValues(
                pendingChange,
                currentStoreId))
            {
                continue;
            }

            // GetDatabaseValuesAsync có thể trả về null nếu row đã mất.
            // CaptureDatabaseValues kiểm tra và trả về giá trị non-null.
            var databaseValues = CaptureDatabaseValues(
                pendingChange,
                await pendingChange.Entry.GetDatabaseValuesAsync(
                    cancellationToken));

            ValidateDatabaseOwnershipIfRequired(
                pendingChange,
                currentStoreId);

            ValidateSafeSoftDeleteConcurrency(
                pendingChange,
                databaseValues);
        }
    }

    private static void ValidateAddedEntry(
        PendingChange pendingChange,
        int? currentStoreId)
    {
        if (pendingChange.StateBeforeAudit != EntityState.Added ||
            !currentStoreId.HasValue ||
            pendingChange.Entry.Entity is not BaseStoreEntity storeEntity)
        {
            return;
        }

        ValidateAddedStore(
            storeEntity,
            currentStoreId.Value);

        pendingChange.VerifiedStoreId = currentStoreId.Value;
    }

    private static bool RequiresDatabaseValues(
        PendingChange pendingChange,
        int? currentStoreId)
    {
        if (pendingChange.StateBeforeAudit is not (
            EntityState.Modified or EntityState.Deleted))
        {
            return false;
        }

        // Tenant context cần database StoreId thật để authorize
        // Modified và Deleted, kể cả entity detached.
        var requiresTenantOwnershipCheck =
            currentStoreId.HasValue &&
            pendingChange.Entry.Entity is BaseStoreEntity;

        // Mọi BaseEntity soft delete phải lấy lại database values trước khi
        // đổi Deleted -> Modified. Nếu không, Remove(detachedEntity) có thể
        // ghi đè Code/Name/các field nghiệp vụ bằng dữ liệu client gửi.
        var requiresSafeSoftDeleteValues =
            pendingChange.StateBeforeAudit == EntityState.Deleted &&
            pendingChange.Entry.Entity is BaseEntity &&
            pendingChange.Entry.Entity is not ProductVariantAttributeValue;

        return requiresTenantOwnershipCheck ||
            requiresSafeSoftDeleteValues;
    }

    private static void ValidateAddedStore(
        BaseStoreEntity storeEntity,
        int currentStoreId)
    {
        if (storeEntity.StoreId > 0 &&
            storeEntity.StoreId != currentStoreId)
        {
            throw new InvalidOperationException(
                $"StoreId={storeEntity.StoreId} không khớp TenantContext={currentStoreId}");
        }
    }

    private static PropertyValues CaptureDatabaseValues(
      PendingChange pendingChange,
      PropertyValues? databaseValues)
    {
        if (databaseValues is null)
        {
            throw new DbUpdateConcurrencyException(
                $"Tenant guard không thể xác minh " +
                $"{pendingChange.Entry.Metadata.ClrType.Name}: " +
                "dữ liệu không còn tồn tại trong database.");
        }

        pendingChange.DatabaseValues = databaseValues;

        return databaseValues;
    }

    private static void ValidateSafeSoftDeleteConcurrency(
        PendingChange pendingChange,
        PropertyValues databaseValues)
    {
        // InMemory không luôn mô phỏng rowversion giống SQL Server.
        // So sánh token gốc với database trong phase xác minh giúp
        // stale/missing token fail closed. Token gốc vẫn được giữ lại để
        // provider relational tiếp tục đưa vào điều kiện optimistic concurrency.
        if (pendingChange.StateBeforeAudit != EntityState.Deleted ||
            pendingChange.Entry.Entity is not BaseEntity ||
            pendingChange.Entry.Entity is ProductVariantAttributeValue)
        {
            return;
        }

        if (pendingChange.OriginalConcurrencyTokens.Count == 0)
        {
            throw new DbUpdateConcurrencyException(
                $"Không thể soft delete " +
                $"{pendingChange.Entry.Metadata.ClrType.Name}: " +
                "không có concurrency token hợp lệ để xác minh.");
        }

        foreach (var concurrencyToken in
            pendingChange.OriginalConcurrencyTokens)
        {
            var databaseValue = databaseValues[
                concurrencyToken.PropertyName];

            if (ConcurrencyValuesEqual(
                concurrencyToken.OriginalValue,
                databaseValue))
            {
                continue;
            }

            throw new DbUpdateConcurrencyException(
                $"Không thể soft delete " +
                $"{pendingChange.Entry.Metadata.ClrType.Name}: " +
                $"concurrency token '{concurrencyToken.PropertyName}' " +
                "đã thay đổi hoặc không hợp lệ.");
        }
    }

    private static IReadOnlyList<ConcurrencyTokenSnapshot>
        CaptureOriginalConcurrencyTokens(EntityEntry entry)
    {
        var snapshots = new List<ConcurrencyTokenSnapshot>();
        var capturedPropertyNames = new HashSet<string>(
            StringComparer.Ordinal);

        // Thu thập toàn bộ concurrency token theo metadata của EF Core.
        foreach (var property in entry.Metadata.GetProperties())
        {
            if (!property.IsConcurrencyToken ||
                !capturedPropertyNames.Add(property.Name))
            {
                continue;
            }

            var originalValue = entry.Property(property.Name)
                .OriginalValue;

            snapshots.Add(
                new ConcurrencyTokenSnapshot(
                    property.Name,
                    SnapshotConcurrencyValue(originalValue)));
        }

        // Defense-in-depth cho contract chung BaseEntity.RowVersion.
        // Một số test provider có thể làm mất cờ IsConcurrencyToken dù
        // production SQL Server vẫn dùng RowVersion. Không được vì vậy mà
        // soft delete tự động chấp nhận token mới nhất từ database.
        if (entry.Entity is BaseEntity)
        {
            var rowVersionProperty = entry.Metadata.FindProperty(
                nameof(BaseEntity.RowVersion));

            if (rowVersionProperty is not null &&
                capturedPropertyNames.Add(rowVersionProperty.Name))
            {
                var originalValue = entry.Property(
                    rowVersionProperty.Name).OriginalValue;

                snapshots.Add(
                    new ConcurrencyTokenSnapshot(
                        rowVersionProperty.Name,
                        SnapshotConcurrencyValue(originalValue)));
            }
        }

        return snapshots;
    }

    private static void RestoreOriginalConcurrencyTokens(
        EntityEntry entry,
        IReadOnlyList<ConcurrencyTokenSnapshot> concurrencyTokens)
    {
        foreach (var concurrencyToken in concurrencyTokens)
        {
            var property = entry.Property(
                concurrencyToken.PropertyName);

            var originalValue = SnapshotConcurrencyValue(
                concurrencyToken.OriginalValue);

            // Giữ cả CurrentValue và OriginalValue ở token request/context
            // ban đầu. Nhờ đó DetectChanges không đánh dấu token là cột UPDATE,
            // còn provider relational vẫn dùng OriginalValue trong WHERE.
            property.CurrentValue = originalValue;
            property.OriginalValue = SnapshotConcurrencyValue(
                concurrencyToken.OriginalValue);
            property.IsModified = false;
        }
    }

    private static object? SnapshotConcurrencyValue(object? value)
    {
        return value is Array array
            ? array.Clone()
            : value;
    }

    private static bool ConcurrencyValuesEqual(
        object? originalValue,
        object? databaseValue)
    {
        if (ReferenceEquals(originalValue, databaseValue))
        {
            return true;
        }

        if (originalValue is null || databaseValue is null)
        {
            return false;
        }

        if (originalValue is Array || databaseValue is Array)
        {
            return System.Collections.StructuralComparisons
                .StructuralEqualityComparer
                .Equals(originalValue, databaseValue);
        }

        return originalValue.Equals(databaseValue);
    }

    private static void ValidateDatabaseOwnershipIfRequired(
        PendingChange pendingChange,
        int? currentStoreId)
    {
        if (!currentStoreId.HasValue ||
            pendingChange.Entry.Entity is not BaseStoreEntity)
        {
            return;
        }

        var databaseValues = pendingChange.DatabaseValues
            ?? throw new InvalidOperationException(
                "Tenant guard chưa lấy được database values.");

        var databaseStoreId = databaseValues.GetValue<int>(
            nameof(BaseStoreEntity.StoreId));

        var operation = pendingChange.StateBeforeAudit == EntityState.Deleted
            ? "DELETE"
            : "UPDATE";

        if (databaseStoreId != currentStoreId.Value)
        {
            throw new InvalidOperationException(
                $"Tenant mismatch {operation}");
        }

        pendingChange.VerifiedStoreId = databaseStoreId;
    }

    /// <summary>
    /// Phase B: chỉ chạy sau khi toàn bộ entry đã qua tenant verification.
    /// Thực hiện audit, soft delete và khóa StoreId.
    /// </summary>
    private void ApplyAuditAndTenantRules(
        IReadOnlyList<PendingChange> pendingChanges)
    {
        var now = DateTime.UtcNow;
        var userId = GetCurrentUserId();

        foreach (var pendingChange in pendingChanges)
        {
            var entry = pendingChange.Entry;

            if (entry.Entity is BaseEntity baseEntity)
            {
                switch (pendingChange.StateBeforeAudit)
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
                        // Mapping này phải hard delete như nghiệp vụ cũ.
                        if (entry.Entity is ProductVariantAttributeValue)
                        {
                            break;
                        }

                        ApplySafeSoftDelete(
                            pendingChange,
                            baseEntity,
                            now,
                            userId);
                        break;
                }
            }

            ProtectVerifiedStoreId(pendingChange);
        }
    }

    private static void ApplySafeSoftDelete(
        PendingChange pendingChange,
        BaseEntity baseEntity,
        DateTime now,
        int? userId)
    {
        var databaseValues = pendingChange.DatabaseValues
            ?? throw new InvalidOperationException(
                "Soft delete chưa lấy được database values.");

        var entry = pendingChange.Entry;

        // Khôi phục toàn bộ business fields từ database để entity detached
        // không thể ghi đè Code/Name/IsActive hoặc dữ liệu nghiệp vụ khác.
        entry.CurrentValues.SetValues(databaseValues);
        entry.OriginalValues.SetValues(databaseValues);
        entry.State = EntityState.Unchanged;

        // Không chấp nhận token mới nhất từ database thay cho token mà
        // request/context ban đầu mang theo. Việc khôi phục generic theo
        // metadata bảo vệ mọi concurrency token, không hard-code RowVersion.
        RestoreOriginalConcurrencyTokens(
            entry,
            pendingChange.OriginalConcurrencyTokens);

        baseEntity.IsDeleted = true;
        baseEntity.DeletedAtUtc = now;
        baseEntity.DeletedBy = userId;

        // Chỉ ba cột soft-delete được phép UPDATE.
        entry.Property(nameof(BaseEntity.IsDeleted))
            .IsModified = true;
        entry.Property(nameof(BaseEntity.DeletedAtUtc))
            .IsModified = true;
        entry.Property(nameof(BaseEntity.DeletedBy))
            .IsModified = true;
    }

    private static void ProtectVerifiedStoreId(
        PendingChange pendingChange)
    {
        if (pendingChange.Entry.Entity is not BaseStoreEntity storeEntity ||
            !pendingChange.VerifiedStoreId.HasValue)
        {
            return;
        }

        var verifiedStoreId = pendingChange.VerifiedStoreId.Value;

        // Added: gán tenant hiện tại sau khi toàn bộ batch đã được xác minh.
        if (pendingChange.StateBeforeAudit == EntityState.Added)
        {
            storeEntity.StoreId = verifiedStoreId;
            return;
        }

        // Modified/Deleted: StoreId phải lấy từ database, không lấy từ client.
        storeEntity.StoreId = verifiedStoreId;

        var storeIdProperty = pendingChange.Entry.Property(
            nameof(BaseStoreEntity.StoreId));

        storeIdProperty.CurrentValue = verifiedStoreId;

        // Không ghi đè OriginalValue nếu StoreId được cấu hình là
        // concurrency token trong tương lai.
        if (!storeIdProperty.Metadata.IsConcurrencyToken)
        {
            storeIdProperty.OriginalValue = verifiedStoreId;
        }

        if (pendingChange.Entry.State == EntityState.Modified)
        {
            storeIdProperty.IsModified = false;
        }
    }

    private sealed class PendingChange
    {
        public PendingChange(EntityEntry entry)
        {
            Entry = entry;
            StateBeforeAudit = entry.State;
            OriginalConcurrencyTokens =
                CaptureOriginalConcurrencyTokens(entry);
        }

        public EntityEntry Entry { get; }

        public EntityState StateBeforeAudit { get; }

        public IReadOnlyList<ConcurrencyTokenSnapshot>
            OriginalConcurrencyTokens
        { get; }

        public int? VerifiedStoreId { get; set; }

        public PropertyValues? DatabaseValues { get; set; }
    }

    private sealed class ConcurrencyTokenSnapshot
    {
        public ConcurrencyTokenSnapshot(
            string propertyName,
            object? originalValue)
        {
            PropertyName = propertyName;
            OriginalValue = originalValue;
        }

        public string PropertyName { get; }

        public object? OriginalValue { get; }
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

                // So sánh nullable trực tiếp để host-admin (StoreId = null)
                // không phải ép Nullable<int>.Value trong lúc EF tham số hóa query.
                var nullableStoreProp = Expression.Convert(storeProp, typeof(int?));
                var storeMatch = Expression.Equal(nullableStoreProp, currentStoreId);

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
