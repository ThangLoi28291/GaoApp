using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefrontFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(GaoApp.Infrastructure.Data.Migrations.StorefrontFoundationSchemaSqlCatalog.PermissionSql);
            migrationBuilder.AddUniqueConstraint(
                name: "AK_StoreBankAccounts_StoreId_Id",
                table: "StoreBankAccounts",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProductVariant_StoreId_Id_ProductId",
                table: "ProductVariant",
                columns: new[] { "StoreId", "Id", "ProductId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Products_StoreId_Id",
                table: "Products",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_POSShifts_StoreId_Id_OpenedByUserId",
                table: "POSShifts",
                columns: new[] { "StoreId", "Id", "OpenedByUserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Orders_StoreId_Id_POSShiftId",
                table: "Orders",
                columns: new[] { "StoreId", "Id", "POSShiftId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_MediaAssets_StoreId_Id",
                table: "MediaAssets",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InventoryReservations_StoreId_Id",
                table: "InventoryReservations",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.CreateTable(
                name: "StorefrontBankCollectionAuthorities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    ReceivingLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    SourceLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontBankCollectionAuthorities", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontBankCollectionAuthorities_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontBankCollectionAuthorities_Live", "[IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_StorefrontBankCollectionAuthorities_LegalEntities_StoreId_ReceivingLegalEntityId",
                        columns: x => new { x.StoreId, x.ReceivingLegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontBankCollectionAuthorities_LegalEntities_StoreId_SourceLegalEntityId",
                        columns: x => new { x.StoreId, x.SourceLegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontBankCollectionAuthorities_StoreBankAccounts_StoreId_BankAccountId",
                        columns: x => new { x.StoreId, x.BankAccountId },
                        principalTable: "StoreBankAccounts",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontBankCollectionAuthorities_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontBankReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    ReceivingLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    ExternalTransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EvidenceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProtectedEvidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false),
                    NeedsReview = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontBankReceipts", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontBankReceipts_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontBankReceipts_StoreId_Id_BankAccountId_ReceivingLegalEntityId_Amount_IsVerified", x => new { x.StoreId, x.Id, x.BankAccountId, x.ReceivingLegalEntityId, x.Amount, x.IsVerified });
                    table.CheckConstraint("CK_StorefrontBankReceipts_Amount", "[Amount] > 0 AND [Amount] = ROUND([Amount], 0)");
                    table.CheckConstraint("CK_StorefrontBankReceipts_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontBankReceipts_Verified", "[IsVerified] = 1");
                    table.ForeignKey(
                        name: "FK_StorefrontBankReceipts_LegalEntities_StoreId_ReceivingLegalEntityId",
                        columns: x => new { x.StoreId, x.ReceivingLegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontBankReceipts_StoreBankAccounts_StoreId_BankAccountId",
                        columns: x => new { x.StoreId, x.BankAccountId },
                        principalTable: "StoreBankAccounts",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontBankReceipts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DisplayName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 6000, nullable: true),
                    DisplayGroup = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontProducts", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontProducts_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontProducts_StoreId_Id_ProductId", x => new { x.StoreId, x.Id, x.ProductId });
                    table.CheckConstraint("CK_StorefrontProducts_Live", "[IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_StorefrontProducts_Products_StoreId_ProductId",
                        columns: x => new { x.StoreId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontProducts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LogoPath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceWarehouseId = table.Column<int>(type: "int", nullable: true),
                    SourceLegalEntityId = table.Column<int>(type: "int", nullable: true),
                    CodEnabled = table.Column<bool>(type: "bit", nullable: false),
                    QrEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AllowNegativeInventory = table.Column<bool>(type: "bit", nullable: false),
                    ShippingByWeight = table.Column<bool>(type: "bit", nullable: false),
                    QrHoldMinutes = table.Column<int>(type: "int", nullable: true),
                    ShippingFeeTreatment = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    BankAccountId = table.Column<int>(type: "int", nullable: true),
                    ReceivingLegalEntityId = table.Column<int>(type: "int", nullable: true),
                    BankTerminalCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BankUserCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontSettings", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontSettings_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontSettings_BankPair", "([BankAccountId] IS NULL AND [ReceivingLegalEntityId] IS NULL) OR ([BankAccountId] IS NOT NULL AND [ReceivingLegalEntityId] IS NOT NULL)");
                    table.CheckConstraint("CK_StorefrontSettings_FeeTreatment", "[ShippingFeeTreatment] = 1");
                    table.CheckConstraint("CK_StorefrontSettings_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontSettings_QrConfig", "[QrEnabled] = 0 OR ([BankAccountId] IS NOT NULL AND [ReceivingLegalEntityId] IS NOT NULL AND [QrHoldMinutes] IS NOT NULL AND LEN(LTRIM(RTRIM([BankTerminalCode]))) > 0 AND LEN(LTRIM(RTRIM([BankUserCode]))) > 0)");
                    table.CheckConstraint("CK_StorefrontSettings_QrHold", "[QrHoldMinutes] IS NULL OR [QrHoldMinutes] BETWEEN 1 AND 10080");
                    table.CheckConstraint("CK_StorefrontSettings_Revision", "[Revision] > 0");
                    table.CheckConstraint("CK_StorefrontSettings_SourcePair", "([SourceWarehouseId] IS NULL AND [SourceLegalEntityId] IS NULL) OR ([SourceWarehouseId] IS NOT NULL AND [SourceLegalEntityId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_StorefrontSettings_LegalEntities_StoreId_ReceivingLegalEntityId",
                        columns: x => new { x.StoreId, x.ReceivingLegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontSettings_StoreBankAccounts_StoreId_BankAccountId",
                        columns: x => new { x.StoreId, x.BankAccountId },
                        principalTable: "StoreBankAccounts",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontSettings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontSettings_Warehouses_StoreId_SourceWarehouseId_SourceLegalEntityId",
                        columns: x => new { x.StoreId, x.SourceWarehouseId, x.SourceLegalEntityId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "StoreId", "Id", "LegalEntityId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontShippingZones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    ProvinceCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WardCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DeliveryMethod = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    MinDeliveryDays = table.Column<int>(type: "int", nullable: false),
                    MaxDeliveryDays = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontShippingZones", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontShippingZones_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontShippingZones_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontShippingZones_Scope", "[CountryCode] = N'VN' AND ([WardCode] = N'' OR [ProvinceCode] <> N'') AND ([DeliveryMethod] = 1 OR [ProvinceCode] <> N'')");
                    table.CheckConstraint("CK_StorefrontShippingZones_State", "[Revision] > 0 AND [DeliveryMethod] IN (0, 1) AND [MinDeliveryDays] >= 0 AND [MaxDeliveryDays] >= [MinDeliveryDays] AND [MaxDeliveryDays] <= 365");
                    table.ForeignKey(
                        name: "FK_StorefrontShippingZones_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontMedia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontProductId = table.Column<int>(type: "int", nullable: false),
                    MediaAssetId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontMedia", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontMedia_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontMedia_Kind", "[Kind] IN (0, 1)");
                    table.CheckConstraint("CK_StorefrontMedia_Live", "[IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_StorefrontMedia_MediaAssets_StoreId_MediaAssetId",
                        columns: x => new { x.StoreId, x.MediaAssetId },
                        principalTable: "MediaAssets",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontMedia_StorefrontProducts_StoreId_StorefrontProductId",
                        columns: x => new { x.StoreId, x.StorefrontProductId },
                        principalTable: "StorefrontProducts",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontMedia_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontVariants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontProductId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    BaseUnitWeightKg = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontVariants", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontVariants_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontVariants_StoreId_Id_VariantId", x => new { x.StoreId, x.Id, x.VariantId });
                    table.CheckConstraint("CK_StorefrontVariants_BaseUnitWeightKg", "[BaseUnitWeightKg] IS NULL OR [BaseUnitWeightKg] > 0");
                    table.CheckConstraint("CK_StorefrontVariants_Live", "[IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_StorefrontVariants_ProductVariant_StoreId_VariantId_ProductId",
                        columns: x => new { x.StoreId, x.VariantId, x.ProductId },
                        principalTable: "ProductVariant",
                        principalColumns: new[] { "StoreId", "Id", "ProductId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontVariants_StorefrontProducts_StoreId_StorefrontProductId_ProductId",
                        columns: x => new { x.StoreId, x.StorefrontProductId, x.ProductId },
                        principalTable: "StorefrontProducts",
                        principalColumns: new[] { "StoreId", "Id", "ProductId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontVariants_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontShippingRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShippingZoneId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    ByWeight = table.Column<bool>(type: "bit", nullable: false),
                    BaseFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AdditionalFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FirstWeightKg = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AdditionalStepKg = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    FreeFromGoodsAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontShippingRates", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontShippingRates_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontShippingRates_AdditionalFee", "[AdditionalFee] >= 0 AND [AdditionalFee] = ROUND([AdditionalFee], 0)");
                    table.CheckConstraint("CK_StorefrontShippingRates_AdditionalStepKg", "[AdditionalStepKg] > 0");
                    table.CheckConstraint("CK_StorefrontShippingRates_BaseFee", "[BaseFee] >= 0 AND [BaseFee] = ROUND([BaseFee], 0)");
                    table.CheckConstraint("CK_StorefrontShippingRates_FirstWeightKg", "[FirstWeightKg] > 0");
                    table.CheckConstraint("CK_StorefrontShippingRates_FreeThreshold", "[FreeFromGoodsAmount] IS NULL OR ([FreeFromGoodsAmount] > 0 AND [FreeFromGoodsAmount] = ROUND([FreeFromGoodsAmount], 0))");
                    table.CheckConstraint("CK_StorefrontShippingRates_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontShippingRates_Revision", "[Revision] > 0");
                    table.ForeignKey(
                        name: "FK_StorefrontShippingRates_StorefrontShippingZones_StoreId_ShippingZoneId",
                        columns: x => new { x.StoreId, x.ShippingZoneId },
                        principalTable: "StorefrontShippingZones",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontShippingRates_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontSellingUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontVariantId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    WeightKg = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontSellingUnits", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontSellingUnits_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontSellingUnits_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontSellingUnits_WeightKg", "[WeightKg] IS NULL OR [WeightKg] > 0");
                    table.ForeignKey(
                        name: "FK_StorefrontSellingUnits_ProductUnitConversion_StoreId_VariantId_ProductUnitConversionId_UnitId",
                        columns: x => new { x.StoreId, x.VariantId, x.ProductUnitConversionId, x.UnitId },
                        principalTable: "ProductUnitConversion",
                        principalColumns: new[] { "StoreId", "ProductVariantId", "Id", "UnitId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontSellingUnits_StorefrontVariants_StoreId_StorefrontVariantId_VariantId",
                        columns: x => new { x.StoreId, x.StorefrontVariantId, x.VariantId },
                        principalTable: "StorefrontVariants",
                        principalColumns: new[] { "StoreId", "Id", "VariantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontSellingUnits_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontQuotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    SettingsRevision = table.Column<int>(type: "int", nullable: false),
                    ShippingRateId = table.Column<int>(type: "int", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GoodsAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingCharge = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontQuotes", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontQuotes_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontQuotes_StoreId_Id_GoodsAmount_ShippingCharge_TotalDue", x => new { x.StoreId, x.Id, x.GoodsAmount, x.ShippingCharge, x.TotalDue });
                    table.CheckConstraint("CK_StorefrontQuotes_GoodsAmount", "[GoodsAmount] > 0 AND [GoodsAmount] = ROUND([GoodsAmount], 0)");
                    table.CheckConstraint("CK_StorefrontQuotes_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontQuotes_Quote", "[Revision] > 0 AND [SettingsRevision] > 0 AND [TotalDue] = [GoodsAmount] + [ShippingCharge] AND [ExpiresAtUtc] > [CreatedAtUtc]");
                    table.CheckConstraint("CK_StorefrontQuotes_ShippingCharge", "[ShippingCharge] >= 0 AND [ShippingCharge] = ROUND([ShippingCharge], 0)");
                    table.CheckConstraint("CK_StorefrontQuotes_SnapshotJson", "ISJSON([SnapshotJson]) = 1");
                    table.CheckConstraint("CK_StorefrontQuotes_TotalDue", "[TotalDue] > 0 AND [TotalDue] = ROUND([TotalDue], 0)");
                    table.ForeignKey(
                        name: "FK_StorefrontQuotes_StorefrontShippingRates_StoreId_ShippingRateId",
                        columns: x => new { x.StoreId, x.ShippingRateId },
                        principalTable: "StorefrontShippingRates",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontQuotes_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    GuestTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    QuoteId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<int>(type: "int", nullable: false),
                    PaymentState = table.Column<int>(type: "int", nullable: false),
                    DeliveryState = table.Column<int>(type: "int", nullable: false),
                    RefundState = table.Column<int>(type: "int", nullable: false),
                    DeliveryMethod = table.Column<int>(type: "int", nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecipientPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RecipientAddress = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AddressSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GoodsAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingCharge = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingFeeTreatment = table.Column<int>(type: "int", nullable: false),
                    PaymentExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontOrders", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontOrders_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontOrders_StoreId_Id_TotalDue", x => new { x.StoreId, x.Id, x.TotalDue });
                    table.CheckConstraint("CK_StorefrontOrders_AddressSnapshotJson", "ISJSON([AddressSnapshotJson]) = 1");
                    table.CheckConstraint("CK_StorefrontOrders_GoodsAmount", "[GoodsAmount] > 0 AND [GoodsAmount] = ROUND([GoodsAmount], 0)");
                    table.CheckConstraint("CK_StorefrontOrders_GuestHash", "LEN([GuestTokenHash]) = 64 AND [GuestTokenHash] NOT LIKE '%[^0-9A-Fa-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_StorefrontOrders_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontOrders_ShippingCharge", "[ShippingCharge] >= 0 AND [ShippingCharge] = ROUND([ShippingCharge], 0)");
                    table.CheckConstraint("CK_StorefrontOrders_State", "[Revision] > 0 AND [Phase] BETWEEN 0 AND 6 AND [PaymentMethod] IN (0, 1) AND [PaymentState] BETWEEN 0 AND 3 AND [DeliveryState] BETWEEN 0 AND 6 AND [RefundState] BETWEEN 0 AND 4 AND [DeliveryMethod] IN (0, 1)");
                    table.CheckConstraint("CK_StorefrontOrders_Total", "[TotalDue] = [GoodsAmount] + [ShippingCharge] AND [ShippingFeeTreatment] = 1");
                    table.CheckConstraint("CK_StorefrontOrders_TotalDue", "[TotalDue] > 0 AND [TotalDue] = ROUND([TotalDue], 0)");
                    table.ForeignKey(
                        name: "FK_StorefrontOrders_StorefrontQuotes_StoreId_QuoteId_GoodsAmount_ShippingCharge_TotalDue",
                        columns: x => new { x.StoreId, x.QuoteId, x.GoodsAmount, x.ShippingCharge, x.TotalDue },
                        principalTable: "StorefrontQuotes",
                        principalColumns: new[] { "StoreId", "Id", "GoodsAmount", "ShippingCharge", "TotalDue" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontOrders_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontCommandReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    OutcomeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontCommandReceipts", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontCommandReceipts_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontCommandReceipts_Key", "[ClientRequestId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_StorefrontCommandReceipts_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontCommandReceipts_OutcomeJson", "ISJSON([OutcomeJson]) = 1");
                    table.ForeignKey(
                        name: "FK_StorefrontCommandReceipts_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCommandReceipts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontCommandReceipts_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CausationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontEvents", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontEvents_StoreId_EventId_StorefrontOrderId_Revision", x => new { x.StoreId, x.EventId, x.StorefrontOrderId, x.Revision });
                    table.UniqueConstraint("AK_StorefrontEvents_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontEvents_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontEvents_PayloadJson", "ISJSON([PayloadJson]) = 1");
                    table.CheckConstraint("CK_StorefrontEvents_Revision", "[Revision] > 0");
                    table.ForeignKey(
                        name: "FK_StorefrontEvents_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontEvents_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontEvents_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontFulfillmentSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    SourceWarehouseId = table.Column<int>(type: "int", nullable: false),
                    SourceLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: true),
                    DispatchPostingKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontFulfillmentSources", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontFulfillmentSources_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontFulfillmentSources_StoreId_StorefrontOrderId_Id", x => new { x.StoreId, x.StorefrontOrderId, x.Id });
                    table.CheckConstraint("CK_StorefrontFulfillmentSources_Live", "[IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentSources_DeliveryOrders_StoreId_DeliveryOrderId_SourceWarehouseId_SourceLegalEntityId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId, x.SourceWarehouseId, x.SourceLegalEntityId },
                        principalTable: "DeliveryOrders",
                        principalColumns: new[] { "StoreId", "Id", "SourceWarehouseId", "SourceLegalEntityId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentSources_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentSources_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentSources_Warehouses_StoreId_SourceWarehouseId_SourceLegalEntityId",
                        columns: x => new { x.StoreId, x.SourceWarehouseId, x.SourceLegalEntityId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "StoreId", "Id", "LegalEntityId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    SellingUnitId = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    UnitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseMultiplier = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WeightKgPerSellingUnit = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    IsGift = table.Column<bool>(type: "bit", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontOrderLines", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontOrderLines_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontOrderLines_StoreId_StorefrontOrderId_Id", x => new { x.StoreId, x.StorefrontOrderId, x.Id });
                    table.CheckConstraint("CK_StorefrontOrderLines_Discount", "[Discount] >= 0 AND [Discount] = ROUND([Discount], 0)");
                    table.CheckConstraint("CK_StorefrontOrderLines_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontOrderLines_NetAmount", "[NetAmount] >= 0 AND [NetAmount] = ROUND([NetAmount], 0)");
                    table.CheckConstraint("CK_StorefrontOrderLines_Price", "[Discount] <= ROUND([Quantity] * [UnitPrice], 0) AND [NetAmount] = ROUND([Quantity] * [UnitPrice], 0) - [Discount]");
                    table.CheckConstraint("CK_StorefrontOrderLines_Quantities", "[Quantity] > 0 AND [BaseMultiplier] > 0 AND [BaseQuantity] > 0 AND [BaseQuantity] = ROUND([Quantity] * [BaseMultiplier], 4) AND [UnitPrice] >= 0 AND [WeightKgPerSellingUnit] >= 0");
                    table.CheckConstraint("CK_StorefrontOrderLines_SnapshotJson", "ISJSON([SnapshotJson]) = 1");
                    table.ForeignKey(
                        name: "FK_StorefrontOrderLines_ProductUnitConversion_StoreId_VariantId_ProductUnitConversionId_SellingUnitId",
                        columns: x => new { x.StoreId, x.VariantId, x.ProductUnitConversionId, x.SellingUnitId },
                        principalTable: "ProductUnitConversion",
                        principalColumns: new[] { "StoreId", "ProductVariantId", "Id", "UnitId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontOrderLines_ProductVariant_StoreId_VariantId_ProductId",
                        columns: x => new { x.StoreId, x.VariantId, x.ProductId },
                        principalTable: "ProductVariant",
                        principalColumns: new[] { "StoreId", "Id", "ProductId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontOrderLines_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontOrderLines_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontOrderLines_Unit_StoreId_SellingUnitId",
                        columns: x => new { x.StoreId, x.SellingUnitId },
                        principalTable: "Unit",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontPaymentIntents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderReference = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    ReceivingLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    AmountDue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BankTerminalCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BankUserCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BindingSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontPaymentIntents", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontPaymentIntents_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontPaymentIntents_StoreId_Id_StorefrontOrderId_BankAccountId_ReceivingLegalEntityId_AmountDue", x => new { x.StoreId, x.Id, x.StorefrontOrderId, x.BankAccountId, x.ReceivingLegalEntityId, x.AmountDue });
                    table.CheckConstraint("CK_StorefrontPaymentIntents_AcbBounds", "[AmountDue] <= 2147483647 AND LEN([ProviderReference]) > 0 AND LEN([BankTerminalCode]) > 0 AND LEN([BankUserCode]) > 0 AND [ExpiresAtUtc] > [CreatedAtUtc]");
                    table.CheckConstraint("CK_StorefrontPaymentIntents_AmountDue", "[AmountDue] > 0 AND [AmountDue] = ROUND([AmountDue], 0)");
                    table.CheckConstraint("CK_StorefrontPaymentIntents_BindingSnapshotJson", "ISJSON([BindingSnapshotJson]) = 1");
                    table.CheckConstraint("CK_StorefrontPaymentIntents_Live", "[IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_StorefrontPaymentIntents_LegalEntities_StoreId_ReceivingLegalEntityId",
                        columns: x => new { x.StoreId, x.ReceivingLegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontPaymentIntents_StoreBankAccounts_StoreId_BankAccountId",
                        columns: x => new { x.StoreId, x.BankAccountId },
                        principalTable: "StoreBankAccounts",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontPaymentIntents_StorefrontOrders_StoreId_StorefrontOrderId_AmountDue",
                        columns: x => new { x.StoreId, x.StorefrontOrderId, x.AmountDue },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id", "TotalDue" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontPaymentIntents_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    GoodsAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnallocatedReceiptAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontRefunds", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontRefunds_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontRefunds_StoreId_Id_StorefrontOrderId", x => new { x.StoreId, x.Id, x.StorefrontOrderId });
                    table.CheckConstraint("CK_StorefrontRefunds_Amount", "[Amount] > 0 AND [Amount] = ROUND([Amount], 0)");
                    table.CheckConstraint("CK_StorefrontRefunds_GoodsAmount", "[GoodsAmount] >= 0 AND [GoodsAmount] = ROUND([GoodsAmount], 0)");
                    table.CheckConstraint("CK_StorefrontRefunds_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontRefunds_ShippingAmount", "[ShippingAmount] >= 0 AND [ShippingAmount] = ROUND([ShippingAmount], 0)");
                    table.CheckConstraint("CK_StorefrontRefunds_Total", "[Amount] = [GoodsAmount] + [ShippingAmount] + [UnallocatedReceiptAmount] AND [State] BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_StorefrontRefunds_UnallocatedReceiptAmount", "[UnallocatedReceiptAmount] >= 0 AND [UnallocatedReceiptAmount] = ROUND([UnallocatedReceiptAmount], 0)");
                    table.ForeignKey(
                        name: "FK_StorefrontRefunds_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontRefunds_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontRefunds_UserInStores_StoreId_ApprovedByUserId",
                        columns: x => new { x.StoreId, x.ApprovedByUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontOutboxMessages", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontOutboxMessages_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontOutboxMessages_Attempts", "[Attempts] >= 0 AND [Revision] > 0");
                    table.CheckConstraint("CK_StorefrontOutboxMessages_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontOutboxMessages_PayloadJson", "ISJSON([PayloadJson]) = 1");
                    table.ForeignKey(
                        name: "FK_StorefrontOutboxMessages_StorefrontEvents_StoreId_EventId_StorefrontOrderId_Revision",
                        columns: x => new { x.StoreId, x.EventId, x.StorefrontOrderId, x.Revision },
                        principalTable: "StorefrontEvents",
                        principalColumns: new[] { "StoreId", "EventId", "StorefrontOrderId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontOutboxMessages_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontSettlementLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    FulfillmentSourceId = table.Column<int>(type: "int", nullable: false),
                    SaleOrderId = table.Column<int>(type: "int", nullable: false),
                    SettlingShiftId = table.Column<int>(type: "int", nullable: false),
                    SettledByUserId = table.Column<int>(type: "int", nullable: false),
                    GoodsAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingServiceAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DeliveryExpense = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AllocationSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontSettlementLinks", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontSettlementLinks_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontSettlementLinks_AllocationSnapshotJson", "ISJSON([AllocationSnapshotJson]) = 1");
                    table.CheckConstraint("CK_StorefrontSettlementLinks_DeliveryExpense", "[DeliveryExpense] >= 0 AND [DeliveryExpense] = ROUND([DeliveryExpense], 0)");
                    table.CheckConstraint("CK_StorefrontSettlementLinks_GoodsAmount", "[GoodsAmount] > 0 AND [GoodsAmount] = ROUND([GoodsAmount], 0)");
                    table.CheckConstraint("CK_StorefrontSettlementLinks_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontSettlementLinks_ShippingServiceAmount", "[ShippingServiceAmount] >= 0 AND [ShippingServiceAmount] = ROUND([ShippingServiceAmount], 0)");
                    table.ForeignKey(
                        name: "FK_StorefrontSettlementLinks_Orders_StoreId_SaleOrderId_SettlingShiftId",
                        columns: x => new { x.StoreId, x.SaleOrderId, x.SettlingShiftId },
                        principalTable: "Orders",
                        principalColumns: new[] { "StoreId", "Id", "POSShiftId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontSettlementLinks_POSShifts_StoreId_SettlingShiftId_SettledByUserId",
                        columns: x => new { x.StoreId, x.SettlingShiftId, x.SettledByUserId },
                        principalTable: "POSShifts",
                        principalColumns: new[] { "StoreId", "Id", "OpenedByUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontSettlementLinks_StorefrontFulfillmentSources_StoreId_StorefrontOrderId_FulfillmentSourceId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId, x.FulfillmentSourceId },
                        principalTable: "StorefrontFulfillmentSources",
                        principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontSettlementLinks_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontSettlementLinks_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontSettlementLinks_UserInStores_StoreId_SettledByUserId",
                        columns: x => new { x.StoreId, x.SettledByUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontLineSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderLineId = table.Column<int>(type: "int", nullable: false),
                    FulfillmentSourceId = table.Column<int>(type: "int", nullable: false),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: true),
                    RecordedCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsProvisionalCost = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontLineSources", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontLineSources_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontLineSources_Budget", "[BaseQuantity] > 0 AND [RecordedCost] >= 0");
                    table.CheckConstraint("CK_StorefrontLineSources_Live", "[IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_StorefrontLineSources_InventoryTransactions_StoreId_InventoryTransactionId",
                        columns: x => new { x.StoreId, x.InventoryTransactionId },
                        principalTable: "InventoryTransactions",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontLineSources_StorefrontFulfillmentSources_StoreId_StorefrontOrderId_FulfillmentSourceId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId, x.FulfillmentSourceId },
                        principalTable: "StorefrontFulfillmentSources",
                        principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontLineSources_StorefrontOrderLines_StoreId_StorefrontOrderId_OrderLineId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId, x.OrderLineId },
                        principalTable: "StorefrontOrderLines",
                        principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontLineSources_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontLineSources_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontReservationLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    OrderLineId = table.Column<int>(type: "int", nullable: false),
                    InventoryReservationId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontReservationLinks", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontReservationLinks_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontReservationLinks_Live", "[IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_StorefrontReservationLinks_InventoryReservations_StoreId_InventoryReservationId",
                        columns: x => new { x.StoreId, x.InventoryReservationId },
                        principalTable: "InventoryReservations",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontReservationLinks_StorefrontOrderLines_StoreId_StorefrontOrderId_OrderLineId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId, x.OrderLineId },
                        principalTable: "StorefrontOrderLines",
                        principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontReservationLinks_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontReservationLinks_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontPaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptVerified = table.Column<bool>(type: "bit", nullable: false),
                    BankReceiptId = table.Column<int>(type: "int", nullable: false),
                    PaymentIntentId = table.Column<int>(type: "int", nullable: false),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    ReceivingLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontPaymentAllocations", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontPaymentAllocations_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontPaymentAllocations_Amount", "[Amount] > 0 AND [Amount] = ROUND([Amount], 0)");
                    table.CheckConstraint("CK_StorefrontPaymentAllocations_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_StorefrontPaymentAllocations_Verified", "[ReceiptVerified] = 1");
                    table.ForeignKey(
                        name: "FK_StorefrontPaymentAllocations_StorefrontBankReceipts_StoreId_BankReceiptId_BankAccountId_ReceivingLegalEntityId_Amount_Receip~",
                        columns: x => new { x.StoreId, x.BankReceiptId, x.BankAccountId, x.ReceivingLegalEntityId, x.Amount, x.ReceiptVerified },
                        principalTable: "StorefrontBankReceipts",
                        principalColumns: new[] { "StoreId", "Id", "BankAccountId", "ReceivingLegalEntityId", "Amount", "IsVerified" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontPaymentAllocations_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontPaymentAllocations_StorefrontPaymentIntents_StoreId_PaymentIntentId_StorefrontOrderId_BankAccountId_ReceivingLegal~",
                        columns: x => new { x.StoreId, x.PaymentIntentId, x.StorefrontOrderId, x.BankAccountId, x.ReceivingLegalEntityId, x.Amount },
                        principalTable: "StorefrontPaymentIntents",
                        principalColumns: new[] { "StoreId", "Id", "StorefrontOrderId", "BankAccountId", "ReceivingLegalEntityId", "AmountDue" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontPaymentAllocations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontRefundEvidence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RefundId = table.Column<int>(type: "int", nullable: false),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    BankReceiptId = table.Column<int>(type: "int", nullable: false),
                    ExternalPayoutKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EvidenceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontRefundEvidence", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontRefundEvidence_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontRefundEvidence_Amount", "[Amount] > 0 AND [Amount] = ROUND([Amount], 0)");
                    table.CheckConstraint("CK_StorefrontRefundEvidence_Live", "[IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_StorefrontRefundEvidence_StorefrontBankReceipts_StoreId_BankReceiptId",
                        columns: x => new { x.StoreId, x.BankReceiptId },
                        principalTable: "StorefrontBankReceipts",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontRefundEvidence_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontRefundEvidence_StorefrontRefunds_StoreId_RefundId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.RefundId, x.StorefrontOrderId },
                        principalTable: "StorefrontRefunds",
                        principalColumns: new[] { "StoreId", "Id", "StorefrontOrderId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontRefundEvidence_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontRefundEvidence_UserInStores_StoreId_ConfirmedByUserId",
                        columns: x => new { x.StoreId, x.ConfirmedByUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankCollectionAuthorities_StoreId_BankAccountId_ReceivingLegalEntityId_SourceLegalEntityId",
                table: "StorefrontBankCollectionAuthorities",
                columns: new[] { "StoreId", "BankAccountId", "ReceivingLegalEntityId", "SourceLegalEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankCollectionAuthorities_StoreId_ReceivingLegalEntityId",
                table: "StorefrontBankCollectionAuthorities",
                columns: new[] { "StoreId", "ReceivingLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankCollectionAuthorities_StoreId_SourceLegalEntityId",
                table: "StorefrontBankCollectionAuthorities",
                columns: new[] { "StoreId", "SourceLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankReceipts_Provider_BankAccountId_ExternalTransactionId",
                table: "StorefrontBankReceipts",
                columns: new[] { "Provider", "BankAccountId", "ExternalTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankReceipts_StoreId_BankAccountId",
                table: "StorefrontBankReceipts",
                columns: new[] { "StoreId", "BankAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankReceipts_StoreId_ReceivingLegalEntityId",
                table: "StorefrontBankReceipts",
                columns: new[] { "StoreId", "ReceivingLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCommandReceipts_StoreId_ActorUserId",
                table: "StorefrontCommandReceipts",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCommandReceipts_StoreId_Operation_ClientRequestId",
                table: "StorefrontCommandReceipts",
                columns: new[] { "StoreId", "Operation", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCommandReceipts_StoreId_StorefrontOrderId",
                table: "StorefrontCommandReceipts",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontEvents_StoreId_ActorUserId",
                table: "StorefrontEvents",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontEvents_StoreId_StorefrontOrderId_Revision",
                table: "StorefrontEvents",
                columns: new[] { "StoreId", "StorefrontOrderId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFulfillmentSources_StoreId_DeliveryOrderId",
                table: "StorefrontFulfillmentSources",
                columns: new[] { "StoreId", "DeliveryOrderId" },
                unique: true,
                filter: "[DeliveryOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFulfillmentSources_StoreId_DeliveryOrderId_SourceWarehouseId_SourceLegalEntityId",
                table: "StorefrontFulfillmentSources",
                columns: new[] { "StoreId", "DeliveryOrderId", "SourceWarehouseId", "SourceLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFulfillmentSources_StoreId_DispatchPostingKey",
                table: "StorefrontFulfillmentSources",
                columns: new[] { "StoreId", "DispatchPostingKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFulfillmentSources_StoreId_SourceWarehouseId_SourceLegalEntityId",
                table: "StorefrontFulfillmentSources",
                columns: new[] { "StoreId", "SourceWarehouseId", "SourceLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFulfillmentSources_StoreId_StorefrontOrderId_SourceWarehouseId",
                table: "StorefrontFulfillmentSources",
                columns: new[] { "StoreId", "StorefrontOrderId", "SourceWarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontLineSources_StoreId_InventoryTransactionId",
                table: "StorefrontLineSources",
                columns: new[] { "StoreId", "InventoryTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontLineSources_StoreId_OrderLineId_FulfillmentSourceId",
                table: "StorefrontLineSources",
                columns: new[] { "StoreId", "OrderLineId", "FulfillmentSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontLineSources_StoreId_StorefrontOrderId_FulfillmentSourceId",
                table: "StorefrontLineSources",
                columns: new[] { "StoreId", "StorefrontOrderId", "FulfillmentSourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontLineSources_StoreId_StorefrontOrderId_OrderLineId",
                table: "StorefrontLineSources",
                columns: new[] { "StoreId", "StorefrontOrderId", "OrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontMedia_StoreId_MediaAssetId",
                table: "StorefrontMedia",
                columns: new[] { "StoreId", "MediaAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontMedia_StoreId_StorefrontProductId_MediaAssetId_Kind",
                table: "StorefrontMedia",
                columns: new[] { "StoreId", "StorefrontProductId", "MediaAssetId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOrderLines_StoreId_SellingUnitId",
                table: "StorefrontOrderLines",
                columns: new[] { "StoreId", "SellingUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOrderLines_StoreId_VariantId_ProductId",
                table: "StorefrontOrderLines",
                columns: new[] { "StoreId", "VariantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOrderLines_StoreId_VariantId_ProductUnitConversionId_SellingUnitId",
                table: "StorefrontOrderLines",
                columns: new[] { "StoreId", "VariantId", "ProductUnitConversionId", "SellingUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOrders_StoreId_Code",
                table: "StorefrontOrders",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOrders_StoreId_GuestTokenHash",
                table: "StorefrontOrders",
                columns: new[] { "StoreId", "GuestTokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOrders_StoreId_Phase_CreatedAtUtc",
                table: "StorefrontOrders",
                columns: new[] { "StoreId", "Phase", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOrders_StoreId_QuoteId",
                table: "StorefrontOrders",
                columns: new[] { "StoreId", "QuoteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOrders_StoreId_QuoteId_GoodsAmount_ShippingCharge_TotalDue",
                table: "StorefrontOrders",
                columns: new[] { "StoreId", "QuoteId", "GoodsAmount", "ShippingCharge", "TotalDue" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOutboxMessages_ProcessedAtUtc_NextAttemptAtUtc",
                table: "StorefrontOutboxMessages",
                columns: new[] { "ProcessedAtUtc", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOutboxMessages_StoreId_EventId",
                table: "StorefrontOutboxMessages",
                columns: new[] { "StoreId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOutboxMessages_StoreId_EventId_StorefrontOrderId_Revision",
                table: "StorefrontOutboxMessages",
                columns: new[] { "StoreId", "EventId", "StorefrontOrderId", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontPaymentAllocations_StoreId_BankReceiptId",
                table: "StorefrontPaymentAllocations",
                columns: new[] { "StoreId", "BankReceiptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontPaymentAllocations_StoreId_BankReceiptId_BankAccountId_ReceivingLegalEntityId_Amount_ReceiptVerified",
                table: "StorefrontPaymentAllocations",
                columns: new[] { "StoreId", "BankReceiptId", "BankAccountId", "ReceivingLegalEntityId", "Amount", "ReceiptVerified" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontPaymentAllocations_StoreId_PaymentIntentId",
                table: "StorefrontPaymentAllocations",
                columns: new[] { "StoreId", "PaymentIntentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontPaymentAllocations_StoreId_PaymentIntentId_StorefrontOrderId_BankAccountId_ReceivingLegalEntityId_Amount",
                table: "StorefrontPaymentAllocations",
                columns: new[] { "StoreId", "PaymentIntentId", "StorefrontOrderId", "BankAccountId", "ReceivingLegalEntityId", "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontPaymentAllocations_StoreId_StorefrontOrderId",
                table: "StorefrontPaymentAllocations",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontPaymentIntents_Provider_ProviderReference",
                table: "StorefrontPaymentIntents",
                columns: new[] { "Provider", "ProviderReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontPaymentIntents_StoreId_BankAccountId",
                table: "StorefrontPaymentIntents",
                columns: new[] { "StoreId", "BankAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontPaymentIntents_StoreId_ReceivingLegalEntityId",
                table: "StorefrontPaymentIntents",
                columns: new[] { "StoreId", "ReceivingLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontPaymentIntents_StoreId_StorefrontOrderId",
                table: "StorefrontPaymentIntents",
                columns: new[] { "StoreId", "StorefrontOrderId" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontPaymentIntents_StoreId_StorefrontOrderId_AmountDue",
                table: "StorefrontPaymentIntents",
                columns: new[] { "StoreId", "StorefrontOrderId", "AmountDue" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontProducts_StoreId_ProductId",
                table: "StorefrontProducts",
                columns: new[] { "StoreId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontQuotes_StoreId_ShippingRateId",
                table: "StorefrontQuotes",
                columns: new[] { "StoreId", "ShippingRateId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontQuotes_StoreId_Token",
                table: "StorefrontQuotes",
                columns: new[] { "StoreId", "Token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundEvidence_ExternalPayoutKey",
                table: "StorefrontRefundEvidence",
                column: "ExternalPayoutKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_BankReceiptId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "BankReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_ConfirmedByUserId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "ConfirmedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_RefundId_StorefrontOrderId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "RefundId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_StorefrontOrderId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefunds_StoreId_ApprovedByUserId",
                table: "StorefrontRefunds",
                columns: new[] { "StoreId", "ApprovedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefunds_StoreId_StorefrontOrderId",
                table: "StorefrontRefunds",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontReservationLinks_StoreId_InventoryReservationId",
                table: "StorefrontReservationLinks",
                columns: new[] { "StoreId", "InventoryReservationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontReservationLinks_StoreId_StorefrontOrderId_OrderLineId",
                table: "StorefrontReservationLinks",
                columns: new[] { "StoreId", "StorefrontOrderId", "OrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSellingUnits_StoreId_ProductUnitConversionId",
                table: "StorefrontSellingUnits",
                columns: new[] { "StoreId", "ProductUnitConversionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSellingUnits_StoreId_StorefrontVariantId_VariantId",
                table: "StorefrontSellingUnits",
                columns: new[] { "StoreId", "StorefrontVariantId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSellingUnits_StoreId_VariantId_ProductUnitConversionId_UnitId",
                table: "StorefrontSellingUnits",
                columns: new[] { "StoreId", "VariantId", "ProductUnitConversionId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSettings_StoreId",
                table: "StorefrontSettings",
                column: "StoreId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSettings_StoreId_BankAccountId",
                table: "StorefrontSettings",
                columns: new[] { "StoreId", "BankAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSettings_StoreId_ReceivingLegalEntityId",
                table: "StorefrontSettings",
                columns: new[] { "StoreId", "ReceivingLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSettings_StoreId_SourceWarehouseId_SourceLegalEntityId",
                table: "StorefrontSettings",
                columns: new[] { "StoreId", "SourceWarehouseId", "SourceLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSettlementLinks_StoreId_FulfillmentSourceId",
                table: "StorefrontSettlementLinks",
                columns: new[] { "StoreId", "FulfillmentSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSettlementLinks_StoreId_SaleOrderId",
                table: "StorefrontSettlementLinks",
                columns: new[] { "StoreId", "SaleOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSettlementLinks_StoreId_SaleOrderId_SettlingShiftId",
                table: "StorefrontSettlementLinks",
                columns: new[] { "StoreId", "SaleOrderId", "SettlingShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSettlementLinks_StoreId_SettledByUserId",
                table: "StorefrontSettlementLinks",
                columns: new[] { "StoreId", "SettledByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSettlementLinks_StoreId_SettlingShiftId_SettledByUserId",
                table: "StorefrontSettlementLinks",
                columns: new[] { "StoreId", "SettlingShiftId", "SettledByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSettlementLinks_StoreId_StorefrontOrderId_FulfillmentSourceId",
                table: "StorefrontSettlementLinks",
                columns: new[] { "StoreId", "StorefrontOrderId", "FulfillmentSourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontShippingRates_StoreId_ShippingZoneId",
                table: "StorefrontShippingRates",
                columns: new[] { "StoreId", "ShippingZoneId" },
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontShippingRates_StoreId_ShippingZoneId_Revision",
                table: "StorefrontShippingRates",
                columns: new[] { "StoreId", "ShippingZoneId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontShippingZones_StoreId_Code",
                table: "StorefrontShippingZones",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontShippingZones_StoreId_DeliveryMethod_CountryCode_ProvinceCode_WardCode",
                table: "StorefrontShippingZones",
                columns: new[] { "StoreId", "DeliveryMethod", "CountryCode", "ProvinceCode", "WardCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontVariants_StoreId_StorefrontProductId_ProductId",
                table: "StorefrontVariants",
                columns: new[] { "StoreId", "StorefrontProductId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontVariants_StoreId_VariantId",
                table: "StorefrontVariants",
                columns: new[] { "StoreId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontVariants_StoreId_VariantId_ProductId",
                table: "StorefrontVariants",
                columns: new[] { "StoreId", "VariantId", "ProductId" });

            foreach (var table in GaoApp.Infrastructure.Data.Migrations.StorefrontFoundationSchemaSqlCatalog.ProtectedTables.Keys)
                migrationBuilder.Sql(GaoApp.Infrastructure.Data.Migrations.StorefrontFoundationSchemaSqlCatalog.Definition(table));
            migrationBuilder.Sql(GaoApp.Infrastructure.Data.Migrations.StorefrontFoundationSchemaSqlCatalog.LineSourceGuardDefinition);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(GaoApp.Infrastructure.Data.Migrations.StorefrontFoundationSchemaSqlCatalog.DowngradeGuardSql);
            migrationBuilder.DropTable(
                name: "StorefrontBankCollectionAuthorities");

            migrationBuilder.DropTable(
                name: "StorefrontCommandReceipts");

            migrationBuilder.DropTable(
                name: "StorefrontLineSources");

            migrationBuilder.DropTable(
                name: "StorefrontMedia");

            migrationBuilder.DropTable(
                name: "StorefrontOutboxMessages");

            migrationBuilder.DropTable(
                name: "StorefrontPaymentAllocations");

            migrationBuilder.DropTable(
                name: "StorefrontRefundEvidence");

            migrationBuilder.DropTable(
                name: "StorefrontReservationLinks");

            migrationBuilder.DropTable(
                name: "StorefrontSellingUnits");

            migrationBuilder.DropTable(
                name: "StorefrontSettings");

            migrationBuilder.DropTable(
                name: "StorefrontSettlementLinks");

            migrationBuilder.DropTable(
                name: "StorefrontEvents");

            migrationBuilder.DropTable(
                name: "StorefrontPaymentIntents");

            migrationBuilder.DropTable(
                name: "StorefrontBankReceipts");

            migrationBuilder.DropTable(
                name: "StorefrontRefunds");

            migrationBuilder.DropTable(
                name: "StorefrontOrderLines");

            migrationBuilder.DropTable(
                name: "StorefrontVariants");

            migrationBuilder.DropTable(
                name: "StorefrontFulfillmentSources");

            migrationBuilder.DropTable(
                name: "StorefrontProducts");

            migrationBuilder.DropTable(
                name: "StorefrontOrders");

            migrationBuilder.DropTable(
                name: "StorefrontQuotes");

            migrationBuilder.DropTable(
                name: "StorefrontShippingRates");

            migrationBuilder.DropTable(
                name: "StorefrontShippingZones");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_StoreBankAccounts_StoreId_Id",
                table: "StoreBankAccounts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProductVariant_StoreId_Id_ProductId",
                table: "ProductVariant");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Products_StoreId_Id",
                table: "Products");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_POSShifts_StoreId_Id_OpenedByUserId",
                table: "POSShifts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Orders_StoreId_Id_POSShiftId",
                table: "Orders");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MediaAssets_StoreId_Id",
                table: "MediaAssets");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InventoryReservations_StoreId_Id",
                table: "InventoryReservations");
        }
    }
}
