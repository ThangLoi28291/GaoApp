using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Data.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class StorefrontFoundationConfiguration :
    IEntityTypeConfiguration<StorefrontSettings>, IEntityTypeConfiguration<StorefrontBankCollectionAuthority>,
    IEntityTypeConfiguration<StorefrontProduct>, IEntityTypeConfiguration<StorefrontVariant>,
    IEntityTypeConfiguration<StorefrontSellingUnit>, IEntityTypeConfiguration<StorefrontMedia>,
    IEntityTypeConfiguration<StorefrontShippingZone>, IEntityTypeConfiguration<StorefrontShippingRate>,
    IEntityTypeConfiguration<StorefrontQuote>, IEntityTypeConfiguration<StorefrontOrder>,
    IEntityTypeConfiguration<StorefrontOrderLine>, IEntityTypeConfiguration<StorefrontPaymentIntent>,
    IEntityTypeConfiguration<StorefrontBankReceipt>, IEntityTypeConfiguration<StorefrontPaymentAllocation>,
    IEntityTypeConfiguration<StorefrontFulfillmentSource>, IEntityTypeConfiguration<StorefrontLineSource>,
    IEntityTypeConfiguration<StorefrontReservationLink>,
    IEntityTypeConfiguration<StorefrontSettlementLink>, IEntityTypeConfiguration<StorefrontRefund>,
    IEntityTypeConfiguration<StorefrontRefundEvidence>, IEntityTypeConfiguration<StorefrontCommandReceipt>,
    IEntityTypeConfiguration<StorefrontEvent>, IEntityTypeConfiguration<StorefrontOutboxMessage>
{
    private static void Base<T>(EntityTypeBuilder<T> b, string table) where T : BaseStoreEntity
    {
        b.ToTable(table, t => t.HasCheckConstraint("CK_" + table + "_Live", "[IsDeleted] = 0"));
        if (StorefrontFoundationSchemaSqlCatalog.ProtectedTables.ContainsKey(table))
            b.ToTable(table, t => t.HasTrigger(StorefrontFoundationSchemaSqlCatalog.TriggerName(table)));
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.StoreId, x.Id });
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
    private static void Link<T, P>(EntityTypeBuilder<T> b, string[] fk, string[] pk) where T : class where P : class
        => b.HasOne<P>().WithMany().HasForeignKey(fk).HasPrincipalKey(pk).OnDelete(DeleteBehavior.Restrict);
    private static void StoreLink<T, P>(EntityTypeBuilder<T> b, string id) where T : class where P : class
        => Link<T, P>(b, ["StoreId", id], ["StoreId", "Id"]);
    private static void Text<T>(EntityTypeBuilder<T> b, string name, int length, bool required = true) where T : class
        => b.Property<string>(name).HasMaxLength(length).IsRequired(required);
    private static void Check<T>(EntityTypeBuilder<T> b, string suffix, string sql) where T : class
        => b.ToTable(t => t.HasCheckConstraint("CK_" + b.Metadata.GetTableName() + "_" + suffix, sql));
    private static void Json<T>(EntityTypeBuilder<T> b, string name) where T : class
    { b.Property<string>(name).IsRequired(); Check(b, name, "ISJSON([" + name + "]) = 1"); }
    private static void Money<T>(EntityTypeBuilder<T> b, string name, bool positive = false) where T : class
    { b.Property<decimal>(name).HasPrecision(18, 2); Check(b, name, $"[{name}] {(positive ? ">" : ">=")} 0 AND [{name}] = ROUND([{name}], 0)"); }
    private static void KeyMoney<T>(EntityTypeBuilder<T> b, string name, bool positive = false) where T : class
    {
        Money(b, name, positive);
        b.Property<decimal>(name).HasConversion<WholeVndKeyConverter>().HasColumnType("decimal(18,2)");
    }
    private static void Kg<T>(EntityTypeBuilder<T> b, string name, bool nullable = true) where T : class
    { b.Property(name).HasPrecision(18, 6); Check(b, name, (nullable ? $"[{name}] IS NULL OR " : "") + $"[{name}] > 0"); }
    private static void Order<T>(EntityTypeBuilder<T> b) where T : class => StoreLink<T, StorefrontOrder>(b, "StorefrontOrderId");
    private static void User<T>(EntityTypeBuilder<T> b, string name) where T : class
        => Link<T, UserInStore>(b, ["StoreId", name], ["StoreId", "UserId"]);

    public void Configure(EntityTypeBuilder<StorefrontSettings> b)
    {
        Base(b, "StorefrontSettings"); b.HasIndex(x => x.StoreId).IsUnique();
        b.Property(x => x.IsEnabled).HasDefaultValue(false);
        b.Property(x => x.ShippingFeeTreatment)
            .HasDefaultValue(StorefrontShippingFeeTreatment.StoreServiceFee)
            .HasSentinel(StorefrontShippingFeeTreatment.StoreServiceFee);
        Text(b, "DisplayName", 200); Text(b, "Description", 2000, false); Text(b, "LogoPath", 260, false);
        Text(b, "ContactPhone", 30); Text(b, "BankTerminalCode", 100); Text(b, "BankUserCode", 100);
        Check(b, "Revision", "[Revision] > 0");
        Check(b, "FeeTreatment", "[ShippingFeeTreatment] = 1");
        Check(b, "QrHold", "[QrHoldMinutes] IS NULL OR [QrHoldMinutes] BETWEEN 1 AND 10080");
        Check(b, "SourcePair", "([SourceWarehouseId] IS NULL AND [SourceLegalEntityId] IS NULL) OR ([SourceWarehouseId] IS NOT NULL AND [SourceLegalEntityId] IS NOT NULL)");
        Check(b, "BankPair", "([BankAccountId] IS NULL AND [ReceivingLegalEntityId] IS NULL) OR ([BankAccountId] IS NOT NULL AND [ReceivingLegalEntityId] IS NOT NULL)");
        Check(b, "QrConfig", "[QrEnabled] = 0 OR ([BankAccountId] IS NOT NULL AND [ReceivingLegalEntityId] IS NOT NULL AND [QrHoldMinutes] IS NOT NULL AND LEN(LTRIM(RTRIM([BankTerminalCode]))) > 0 AND LEN(LTRIM(RTRIM([BankUserCode]))) > 0)");
        Link<StorefrontSettings, Warehouse>(b, ["StoreId", "SourceWarehouseId", "SourceLegalEntityId"], ["StoreId", "Id", "LegalEntityId"]);
        StoreLink<StorefrontSettings, StoreBankAccount>(b, "BankAccountId");
        StoreLink<StorefrontSettings, LegalEntity>(b, "ReceivingLegalEntityId");
    }
    public void Configure(EntityTypeBuilder<StorefrontBankCollectionAuthority> b)
    {
        Base(b, "StorefrontBankCollectionAuthorities");
        b.HasIndex(x => new { x.StoreId, x.BankAccountId, x.ReceivingLegalEntityId, x.SourceLegalEntityId }).IsUnique();
        StoreLink<StorefrontBankCollectionAuthority, StoreBankAccount>(b, "BankAccountId");
        StoreLink<StorefrontBankCollectionAuthority, LegalEntity>(b, "ReceivingLegalEntityId");
        StoreLink<StorefrontBankCollectionAuthority, LegalEntity>(b, "SourceLegalEntityId");
    }
    public void Configure(EntityTypeBuilder<StorefrontProduct> b)
    {
        Base(b, "StorefrontProducts"); b.HasIndex(x => new { x.StoreId, x.ProductId }).IsUnique();
        b.HasAlternateKey(x => new { x.StoreId, x.Id, x.ProductId });
        b.Property(x => x.IsPublished).HasDefaultValue(false);
        Text(b, "DisplayName", 255, false); Text(b, "Description", 6000, false); Text(b, "DisplayGroup", 100, false);
        StoreLink<StorefrontProduct, Product>(b, "ProductId");
    }
    public void Configure(EntityTypeBuilder<StorefrontVariant> b)
    {
        Base(b, "StorefrontVariants"); b.HasIndex(x => new { x.StoreId, x.VariantId }).IsUnique();
        b.HasAlternateKey(x => new { x.StoreId, x.Id, x.VariantId });
        b.Property(x => x.IsPublished).HasDefaultValue(false); Kg(b, "BaseUnitWeightKg");
        b.Property(x => x.BaseUnitIsPublished).HasDefaultValue(false);
        Link<StorefrontVariant, StorefrontProduct>(b, ["StoreId", "StorefrontProductId", "ProductId"], ["StoreId", "Id", "ProductId"]);
        Link<StorefrontVariant, ProductVariant>(b, ["StoreId", "VariantId", "ProductId"], ["StoreId", "Id", "ProductId"]);
    }
    public void Configure(EntityTypeBuilder<StorefrontSellingUnit> b)
    {
        Base(b, "StorefrontSellingUnits"); b.HasIndex(x => new { x.StoreId, x.ProductUnitConversionId }).IsUnique();
        b.Property(x => x.IsPublished).HasDefaultValue(false); Kg(b, "WeightKg");
        Link<StorefrontSellingUnit, StorefrontVariant>(b, ["StoreId", "StorefrontVariantId", "VariantId"], ["StoreId", "Id", "VariantId"]);
        Link<StorefrontSellingUnit, ProductUnitConversion>(b, ["StoreId", "VariantId", "ProductUnitConversionId", "UnitId"], ["StoreId", "ProductVariantId", "Id", "UnitId"]);
    }
    public void Configure(EntityTypeBuilder<StorefrontMedia> b)
    {
        Base(b, "StorefrontMedia"); Text(b, "AltText", 300); Check(b, "Kind", "[Kind] IN (0, 1)");
        b.HasIndex(x => new { x.StoreId, x.StorefrontProductId, x.MediaAssetId, x.Kind }).IsUnique();
        StoreLink<StorefrontMedia, StorefrontProduct>(b, "StorefrontProductId"); StoreLink<StorefrontMedia, MediaAsset>(b, "MediaAssetId");
    }
    public void Configure(EntityTypeBuilder<StorefrontShippingZone> b)
    {
        Base(b, "StorefrontShippingZones"); Text(b, "Code", 40); Text(b, "Name", 200);
        Text(b, "CountryCode", 2); Text(b, "ProvinceCode", 20); Text(b, "WardCode", 20);
        b.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.DeliveryMethod, x.CountryCode, x.ProvinceCode, x.WardCode }).IsUnique();
        Check(b, "Scope", "[CountryCode] = N'VN' AND ([WardCode] = N'' OR [ProvinceCode] <> N'') AND ([DeliveryMethod] = 1 OR [ProvinceCode] <> N'')");
        Check(b, "State", "[Revision] > 0 AND [DeliveryMethod] IN (0, 1) AND [MinDeliveryDays] >= 0 AND [MaxDeliveryDays] >= [MinDeliveryDays] AND [MaxDeliveryDays] <= 365");
    }
    public void Configure(EntityTypeBuilder<StorefrontShippingRate> b)
    {
        Base(b, "StorefrontShippingRates"); StoreLink<StorefrontShippingRate, StorefrontShippingZone>(b, "ShippingZoneId");
        b.HasIndex(x => new { x.StoreId, x.ShippingZoneId, x.Revision }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.ShippingZoneId }).IsUnique().HasFilter("[IsCurrent] = 1");
        Money(b, "BaseFee"); Money(b, "AdditionalFee"); Kg(b, "FirstWeightKg", false); Kg(b, "AdditionalStepKg", false);
        b.Property(x => x.FreeFromGoodsAmount).HasPrecision(18, 2);
        Check(b, "FreeThreshold", "[FreeFromGoodsAmount] IS NULL OR ([FreeFromGoodsAmount] > 0 AND [FreeFromGoodsAmount] = ROUND([FreeFromGoodsAmount], 0))");
        Check(b, "Revision", "[Revision] > 0");
    }
    public void Configure(EntityTypeBuilder<StorefrontQuote> b)
    {
        Base(b, "StorefrontQuotes"); b.HasIndex(x => new { x.StoreId, x.Token }).IsUnique();
        b.HasAlternateKey(x => new { x.StoreId, x.Id, x.GoodsAmount, x.ShippingCharge, x.TotalDue });
        StoreLink<StorefrontQuote, StorefrontShippingRate>(b, "ShippingRateId");
        Text(b, "Fingerprint", 64); Json(b, "SnapshotJson");
        KeyMoney(b, "GoodsAmount", true); KeyMoney(b, "ShippingCharge"); KeyMoney(b, "TotalDue", true);
        Check(b, "Quote", "[Revision] > 0 AND [SettingsRevision] > 0 AND [TotalDue] = [GoodsAmount] + [ShippingCharge] AND [ExpiresAtUtc] > [CreatedAtUtc]");
    }
    public void Configure(EntityTypeBuilder<StorefrontOrder> b)
    {
        Base(b, "StorefrontOrders"); Text(b, "Code", 40); Text(b, "GuestTokenHash", 64);
        b.ToTable(table => table.HasTrigger(StorefrontCheckoutSchemaSqlCatalog.OrderSealTriggerName));
        Text(b, "RecipientName", 200); Text(b, "RecipientPhone", 30); Text(b, "RecipientAddress", 1200); Text(b, "Note", 1000, false);
        b.HasIndex(x => new { x.StoreId, x.Code }).IsUnique(); b.HasIndex(x => new { x.StoreId, x.GuestTokenHash }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.QuoteId }).IsUnique(); b.HasIndex(x => new { x.StoreId, x.Phase, x.CreatedAtUtc });
        Link<StorefrontOrder, StorefrontQuote>(b, ["StoreId", "QuoteId", "GoodsAmount", "ShippingCharge", "TotalDue"], ["StoreId", "Id", "GoodsAmount", "ShippingCharge", "TotalDue"]); Json(b, "AddressSnapshotJson");
        b.HasAlternateKey(x => new { x.StoreId, x.Id, x.TotalDue });
        KeyMoney(b, "GoodsAmount", true); KeyMoney(b, "ShippingCharge"); KeyMoney(b, "TotalDue", true);
        Check(b, "Total", "[TotalDue] = [GoodsAmount] + [ShippingCharge] AND [ShippingFeeTreatment] = 1");
        Check(b, "State", "[Revision] > 0 AND [Phase] BETWEEN 0 AND 6 AND [PaymentMethod] IN (0, 1) AND [PaymentState] BETWEEN 0 AND 3 AND [DeliveryState] BETWEEN 0 AND 6 AND [RefundState] BETWEEN 0 AND 4 AND [DeliveryMethod] IN (0, 1)");
        Check(b, "GuestHash", "LEN([GuestTokenHash]) = 64 AND [GuestTokenHash] NOT LIKE '%[^0-9A-Fa-f]%' COLLATE Latin1_General_100_BIN2");
    }
    public void Configure(EntityTypeBuilder<StorefrontOrderLine> b)
    {
        Base(b, "StorefrontOrderLines"); Order(b); b.HasAlternateKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id });
        b.HasAlternateKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id, x.VariantId });
        Link<StorefrontOrderLine, ProductVariant>(b, ["StoreId", "VariantId", "ProductId"], ["StoreId", "Id", "ProductId"]);
        Link<StorefrontOrderLine, ProductUnitConversion>(b, ["StoreId", "VariantId", "ProductUnitConversionId", "SellingUnitId"], ["StoreId", "ProductVariantId", "Id", "UnitId"]);
        StoreLink<StorefrontOrderLine, Unit>(b, "SellingUnitId");
        Text(b, "ItemName", 255); Text(b, "UnitName", 200); Json(b, "SnapshotJson");
        b.Property(x => x.Quantity).HasPrecision(18, 4); b.Property(x => x.BaseMultiplier).HasPrecision(18, 4); b.Property(x => x.BaseQuantity).HasPrecision(18, 4);
        b.Property(x => x.UnitPrice).HasPrecision(18, 2); Money(b, "Discount"); Money(b, "NetAmount");
        b.Property(x => x.RoundingAdjustment).HasPrecision(18, 2).HasDefaultValue(0m);
        Check(b, "RoundingAdjustment", "[RoundingAdjustment] BETWEEN -201 AND 201 AND [RoundingAdjustment] = ROUND([RoundingAdjustment],0)");
        b.Property(x => x.WeightKgPerSellingUnit).HasPrecision(18, 6);
        Check(b, "Quantities", "[Quantity] > 0 AND [BaseMultiplier] > 0 AND [BaseQuantity] > 0 AND [BaseQuantity] = ROUND([Quantity] * [BaseMultiplier], 4) AND [UnitPrice] >= 0 AND [WeightKgPerSellingUnit] >= 0");
        Check(b, "Price", StorefrontCheckoutSchemaSqlCatalog.LinePriceConstraint);
    }
    public void Configure(EntityTypeBuilder<StorefrontPaymentIntent> b)
    {
        Base(b, "StorefrontPaymentIntents");
        Link<StorefrontPaymentIntent, StorefrontOrder>(b, ["StoreId", "StorefrontOrderId", "AmountDue"], ["StoreId", "Id", "TotalDue"]);
        Text(b, "Provider", 30); Text(b, "ProviderReference", 13);
        Text(b, "BankTerminalCode", 100); Text(b, "BankUserCode", 100); Json(b, "BindingSnapshotJson"); KeyMoney(b, "AmountDue", true);
        b.HasAlternateKey(x => new { x.StoreId, x.Id, x.StorefrontOrderId, x.BankAccountId, x.ReceivingLegalEntityId, x.AmountDue });
        b.HasIndex(x => new { x.StoreId, x.StorefrontOrderId }).IsUnique().HasFilter("[IsActive] = 1");
        b.HasIndex(x => new { x.Provider, x.ProviderReference }).IsUnique();
        StoreLink<StorefrontPaymentIntent, StoreBankAccount>(b, "BankAccountId"); StoreLink<StorefrontPaymentIntent, LegalEntity>(b, "ReceivingLegalEntityId");
        Check(b, "AcbBounds", "[AmountDue] <= 2147483647 AND LEN([ProviderReference]) > 0 AND LEN([BankTerminalCode]) > 0 AND LEN([BankUserCode]) > 0 AND [ExpiresAtUtc] > [CreatedAtUtc]");
    }
    public void Configure(EntityTypeBuilder<StorefrontBankReceipt> b)
    {
        b.HasOne<BankVerifiedReceipt>().WithMany().HasForeignKey(x=>new{x.StoreId,x.GlobalBankReceiptId}).HasPrincipalKey(x=>new{x.OriginStoreId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>x.GlobalBankReceiptId).IsUnique().HasFilter("[GlobalBankReceiptId] IS NOT NULL");
        Base(b, "StorefrontBankReceipts"); Text(b, "Provider", 30); Text(b, "ExternalTransactionId", 100); Text(b, "EvidenceFingerprint", 64);
        Check(b, "Verified", "[IsVerified] = 1");
        b.Property(x => x.ProtectedEvidence).IsRequired(); KeyMoney(b, "Amount", true);
        b.HasIndex(x => new { x.Provider, x.BankAccountId, x.ExternalTransactionId }).IsUnique();
        b.HasAlternateKey(x => new { x.StoreId, x.Id, x.BankAccountId, x.ReceivingLegalEntityId, x.Amount, x.IsVerified });
        StoreLink<StorefrontBankReceipt, StoreBankAccount>(b, "BankAccountId"); StoreLink<StorefrontBankReceipt, LegalEntity>(b, "ReceivingLegalEntityId");
    }
    public void Configure(EntityTypeBuilder<StorefrontPaymentAllocation> b)
    {
        Base(b, "StorefrontPaymentAllocations"); Order(b); KeyMoney(b, "Amount", true);
        Check(b, "Verified", "[ReceiptVerified] = 1");
        b.HasIndex(x => new { x.StoreId, x.BankReceiptId }).IsUnique(); b.HasIndex(x => new { x.StoreId, x.PaymentIntentId }).IsUnique();
        Link<StorefrontPaymentAllocation, StorefrontBankReceipt>(b, ["StoreId", "BankReceiptId", "BankAccountId", "ReceivingLegalEntityId", "Amount", "ReceiptVerified"], ["StoreId", "Id", "BankAccountId", "ReceivingLegalEntityId", "Amount", "IsVerified"]);
        Link<StorefrontPaymentAllocation, StorefrontPaymentIntent>(b, ["StoreId", "PaymentIntentId", "StorefrontOrderId", "BankAccountId", "ReceivingLegalEntityId", "Amount"], ["StoreId", "Id", "StorefrontOrderId", "BankAccountId", "ReceivingLegalEntityId", "AmountDue"]);
    }
    public void Configure(EntityTypeBuilder<StorefrontFulfillmentSource> b)
    {
        Base(b, "StorefrontFulfillmentSources"); Order(b); Text(b, "DispatchPostingKey", 100);
        b.HasAlternateKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id });
        b.HasAlternateKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id, x.SourceWarehouseId, x.SourceLegalEntityId });
        b.HasIndex(x => new { x.StoreId, x.StorefrontOrderId, x.SourceWarehouseId }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.DispatchPostingKey }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.DeliveryOrderId }).IsUnique().HasFilter("[DeliveryOrderId] IS NOT NULL");
        Link<StorefrontFulfillmentSource, Warehouse>(b, ["StoreId", "SourceWarehouseId", "SourceLegalEntityId"], ["StoreId", "Id", "LegalEntityId"]);
        Link<StorefrontFulfillmentSource, GaoApp.Domain.Delivery.DeliveryOrder>(b,
            ["StoreId", "DeliveryOrderId", "SourceWarehouseId", "SourceLegalEntityId"],
            ["StoreId", "Id", "SourceWarehouseId", "SourceLegalEntityId"]);
    }
    public void Configure(EntityTypeBuilder<StorefrontLineSource> b)
    {
        Base(b, "StorefrontLineSources"); Order(b);
        b.ToTable(table => { table.HasTrigger(StorefrontFoundationSchemaSqlCatalog.LineSourceGuardName); table.HasTrigger("TR_StorefrontLineSources_NativeBudget"); });
        Link<StorefrontLineSource, StorefrontOrderLine>(b, ["StoreId", "StorefrontOrderId", "OrderLineId"], ["StoreId", "StorefrontOrderId", "Id"]);
        Link<StorefrontLineSource, StorefrontFulfillmentSource>(b, ["StoreId", "StorefrontOrderId", "FulfillmentSourceId"], ["StoreId", "StorefrontOrderId", "Id"]);
        StoreLink<StorefrontLineSource, InventoryTransaction>(b, "InventoryTransactionId");
        b.HasIndex(x => new { x.StoreId, x.OrderLineId, x.FulfillmentSourceId }).IsUnique();
        b.Property(x => x.BaseQuantity).HasPrecision(18, 4); b.Property(x => x.RecordedCost).HasPrecision(18, 4);
        Check(b, "Budget", "[BaseQuantity] > 0 AND [RecordedCost] >= 0");
    }
    public void Configure(EntityTypeBuilder<StorefrontSettlementLink> b)
    {
        Base(b, "StorefrontSettlementLinks"); Order(b);
        Link<StorefrontSettlementLink, StorefrontFulfillmentSource>(b, ["StoreId", "StorefrontOrderId", "FulfillmentSourceId"], ["StoreId", "StorefrontOrderId", "Id"]);
        Link<StorefrontSettlementLink, Order>(b, ["StoreId", "SaleOrderId", "SettlingShiftId"], ["StoreId", "Id", "POSShiftId"]);
        Link<StorefrontSettlementLink, POSShift>(b, ["StoreId", "SettlingShiftId", "SettledByUserId"], ["StoreId", "Id", "OpenedByUserId"]);
        User(b, "SettledByUserId");
        b.HasIndex(x => new { x.StoreId, x.FulfillmentSourceId }).IsUnique(); b.HasIndex(x => new { x.StoreId, x.SaleOrderId });
        b.HasOne<StorefrontSaleBinding>().WithMany().HasForeignKey(x => new { x.StoreId, x.StorefrontOrderId, x.SaleOrderId, x.SettlingShiftId, x.SettledByUserId })
            .HasPrincipalKey(x => new { x.StoreId, x.StorefrontOrderId, x.SaleOrderId, x.SettlingShiftId, x.ActorUserId }).OnDelete(DeleteBehavior.Restrict);
        Money(b, "GoodsAmount"); Money(b, "ShippingServiceAmount"); Money(b, "DeliveryExpense"); Json(b, "AllocationSnapshotJson");
    }
    public void Configure(EntityTypeBuilder<StorefrontReservationLink> b)
    {
        Base(b, "StorefrontReservationLinks"); Order(b);
        b.ToTable(table => table.HasTrigger(StorefrontCheckoutSchemaSqlCatalog.LinkTriggerName));
        Link<StorefrontReservationLink, StorefrontOrderLine>(b, ["StoreId", "StorefrontOrderId", "OrderLineId"], ["StoreId", "StorefrontOrderId", "Id"]);
        StoreLink<StorefrontReservationLink, InventoryReservation>(b, "InventoryReservationId");
        b.HasIndex(x => new { x.StoreId, x.InventoryReservationId }).IsUnique();
    }
    public void Configure(EntityTypeBuilder<StorefrontRefund> b)
    {
        Base(b, "StorefrontRefunds"); Order(b); Text(b, "Reason", 1000);
        b.HasAlternateKey(x => new { x.StoreId, x.Id, x.StorefrontOrderId });
        Money(b, "GoodsAmount"); Money(b, "ShippingAmount"); Money(b, "UnallocatedReceiptAmount"); Money(b, "Amount", true);
        Check(b, "Total", "[Amount] = [GoodsAmount] + [ShippingAmount] + [UnallocatedReceiptAmount] AND [State] BETWEEN 1 AND 4"); User(b, "ApprovedByUserId");
    }
    public void Configure(EntityTypeBuilder<StorefrontRefundEvidence> b)
    {
        Base(b, "StorefrontRefundEvidence"); Order(b); Text(b, "ExternalPayoutKey", 100); Text(b, "EvidenceFingerprint", 64); Money(b, "Amount", true);
        Link<StorefrontRefundEvidence, StorefrontRefund>(b, ["StoreId", "RefundId", "StorefrontOrderId"], ["StoreId", "Id", "StorefrontOrderId"]);
        StoreLink<StorefrontRefundEvidence, StorefrontBankReceipt>(b, "BankReceiptId"); User(b, "ConfirmedByUserId");
        b.HasIndex(x => x.ExternalPayoutKey).IsUnique();
    }
    public void Configure(EntityTypeBuilder<StorefrontCommandReceipt> b)
    {
        Base(b, "StorefrontCommandReceipts"); Text(b, "Operation", 60); Text(b, "RequestHash", 64); Json(b, "OutcomeJson");
        Order(b); User(b, "ActorUserId"); b.HasIndex(x => new { x.StoreId, x.Operation, x.ClientRequestId }).IsUnique();
        Check(b, "Key", "[ClientRequestId] <> '00000000-0000-0000-0000-000000000000'");
    }
    public void Configure(EntityTypeBuilder<StorefrontEvent> b)
    {
        Base(b, "StorefrontEvents"); Order(b); User(b, "ActorUserId"); Text(b, "Kind", 60); Json(b, "PayloadJson");
        b.ToTable(table => table.HasTrigger(StorefrontCheckoutSchemaSqlCatalog.EventTriggerName));
        b.HasAlternateKey(x => new { x.StoreId, x.EventId, x.StorefrontOrderId, x.Revision });
        b.HasIndex(x => new { x.StoreId, x.StorefrontOrderId, x.Revision }).IsUnique(); Check(b, "Revision", "[Revision] > 0");
    }
    public void Configure(EntityTypeBuilder<StorefrontOutboxMessage> b)
    {
        Base(b, "StorefrontOutboxMessages"); Text(b, "Kind", 60); Json(b, "PayloadJson");
        Link<StorefrontOutboxMessage, StorefrontEvent>(b, ["StoreId", "EventId", "StorefrontOrderId", "Revision"], ["StoreId", "EventId", "StorefrontOrderId", "Revision"]);
        b.HasIndex(x => new { x.StoreId, x.EventId }).IsUnique(); b.HasIndex(x => new { x.ProcessedAtUtc, x.NextAttemptAtUtc });
        Check(b, "Attempts", "[Attempts] >= 0 AND [Revision] > 0");
    }
}
