using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialProductionBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActorUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Module = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EntityDisplay = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OldValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedColumnsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TraceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSuccess = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SubDomain = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SubDomainNormalized = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsMultiLegalEntityEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    MultiLegalEntityActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsHostAdmin = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminMenuItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Area = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Controller = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    PermissionCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_AdminMenuItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminMenuItems_AdminMenuItems_ParentId",
                        column: x => x.ParentId,
                        principalTable: "AdminMenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdminMenuItems_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Attribute",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_Attribute", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attribute_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Brands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Brands_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Category",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    IsRewardEligible = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Category", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Category_Category_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Category_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OldCustomerId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerGroup = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "MEMBER"),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TaxCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HaveDebt = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsImportedFromOldSystem = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ImportedRewardAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    PriceTier = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DisplayPromotions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MediaType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MediaUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ButtonText = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BackgroundColor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TextColor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DurationSeconds = table.Column<int>(type: "int", nullable: false),
                    StartAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsFlashSale = table.Column<bool>(type: "bit", nullable: false),
                    CountdownToUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsFullscreen = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_DisplayPromotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisplayPromotions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DocumentNumberSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SequenceType = table.Column<int>(type: "int", nullable: false),
                    SequenceDate = table.Column<DateTime>(type: "date", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_DocumentNumberSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentNumberSequences_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InputInvoiceHead",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceTemplateCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InvoiceSeries = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InvoiceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TaxAuthorityCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SellerTaxCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SellerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SellerAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BuyerTaxCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BuyerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BuyerAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TotalBeforeTax = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalTaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalPaymentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    XmlFilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    XmlHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InputInvoiceHead", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InputInvoiceHead_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceProviderSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsProduction = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Password = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SupplierTaxCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InvoiceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InvoiceSeries = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 1m),
                    PaymentMethodName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CusGetInvoiceRight = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DefaultPaymentStatus = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AuthMode = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)2),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InvoiceProviderSettings", x => x.Id);
                    table.UniqueConstraint("AK_InvoiceProviderSettings_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.ForeignKey(
                        name: "FK_InvoiceProviderSettings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LegalEntityActivationEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    PreviousIsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    NewIsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActivationAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChangedByUserId = table.Column<int>(type: "int", nullable: true),
                    ChangedByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PreflightPassed = table.Column<bool>(type: "bit", nullable: false),
                    PreflightSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_LegalEntityActivationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalEntityActivationEvents_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoragePath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsTemp = table.Column<bool>(type: "bit", nullable: false),
                    TempToken = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ExpireAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaAssets_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrderNumberSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DateKey = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_OrderNumberSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderNumberSequences_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "POSAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_POSAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_POSAuditLogs_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "POSTerminals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    LocalIp = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeviceName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AutoResolveByIp = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_POSTerminals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_POSTerminals_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    DiscountType = table.Column<byte>(type: "tinyint", nullable: false),
                    DiscountValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    StartAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Priority = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CustomerPriceTier = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ComboFixedPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ComboNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BuyQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    GetQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RequireGiftQuantityInCart = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promotions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    RequestDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NeedByDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReturnedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConvertedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConvertedByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<int>(type: "int", nullable: true),
                    WorkflowNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RewardSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MoneyPerPoint = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PointsPerVoucher = table.Column<int>(type: "int", nullable: false),
                    VoucherValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_RewardSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RewardSettings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsSystemRole = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_Roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Roles_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoreBankAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BankCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccountNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    QrRenderMode = table.Column<int>(type: "int", nullable: false),
                    ConfirmMode = table.Column<int>(type: "int", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "LOCAL"),
                    NoteTemplate = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VietQrBankBin = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApiClientId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApiSecretEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CallbackSecret = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_StoreBankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreBankAccounts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    TaxCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Suppliers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Taxes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Rate = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Taxes", x => x.Id);
                    table.CheckConstraint("CK_Taxes_Rate_0_100", "[Rate] >= 0 AND [Rate] <= 100");
                    table.ForeignKey(
                        name: "FK_Taxes_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Unit",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsBase = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_Unit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Unit_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AttributeValue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttributeId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_AttributeValue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttributeValue_Attribute_AttributeId",
                        column: x => x.AttributeId,
                        principalTable: "Attribute",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttributeValue_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceBuyerProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    BuyerType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Business"),
                    TaxCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BuyerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BuyerLegalName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BuyerAddress = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true),
                    BuyerEmail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    BuyerPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "manual"),
                    IsVerifiedByUser = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastLookupAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastUsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UseCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InvoiceBuyerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceBuyerProfiles_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InvoiceBuyerProfiles_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InputInvoiceDetail",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InputInvoiceHeadId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UnitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatRate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InputInvoiceDetail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InputInvoiceDetail_InputInvoiceHead_InputInvoiceHeadId",
                        column: x => x.InputInvoiceHeadId,
                        principalTable: "InputInvoiceHead",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "POSTerminalDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TerminalId = table.Column<int>(type: "int", nullable: false),
                    DeviceKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastIp = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_POSTerminalDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_POSTerminalDevices_POSTerminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "POSTerminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSTerminalDevices_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PromotionComboRule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PromotionId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: true),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    RequiredQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 1m),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_PromotionComboRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionComboRule_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionComboRule_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PromotionItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PromotionId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: true),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    MinQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 1m),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_PromotionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionItems_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionItems_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    PermissionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserInStores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PositionName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    JoinedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_UserInStores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserInStores_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserInStores_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserInStores_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Alias = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    BrandId = table.Column<int>(type: "int", nullable: true),
                    TaxId = table.Column<int>(type: "int", nullable: true),
                    BaseUnitId = table.Column<int>(type: "int", nullable: false),
                    BasePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsSellable = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsRewardEligibleOverride = table.Column<bool>(type: "bit", nullable: true),
                    RewardBulkExcludeQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Category_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Products_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Taxes_TaxId",
                        column: x => x.TaxId,
                        principalTable: "Taxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Unit_BaseUnitId",
                        column: x => x.BaseUnitId,
                        principalTable: "Unit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    MediaAssetId = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_ProductImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductImages_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductImages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductImages_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProductVariant",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ProductVariantName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ProductVariantNameNormalized = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CostPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    PrimaryProductImageId = table.Column<int>(type: "int", nullable: true),
                    HasInputInvoice = table.Column<bool>(type: "bit", nullable: false),
                    WholesalePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_ProductVariant", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariant_ProductImages_PrimaryProductImageId",
                        column: x => x.PrimaryProductImageId,
                        principalTable: "ProductImages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductVariant_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductVariant_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProductUnitConversion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    Factor = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsBaseUnit = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsDefaultForSale = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    WholesalePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_ProductUnitConversion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductUnitConversion_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductUnitConversion_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductUnitConversion_Unit_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Unit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariantAttributeValue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    AttributeId = table.Column<int>(type: "int", nullable: false),
                    AttributeValueId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_ProductVariantAttributeValue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariantAttributeValue_AttributeValue_AttributeValueId",
                        column: x => x.AttributeValueId,
                        principalTable: "AttributeValue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantAttributeValue_Attribute_AttributeId",
                        column: x => x.AttributeId,
                        principalTable: "Attribute",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantAttributeValue_ProductVariant_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantAttributeValue_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProductVariantUnitBarcode",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BarcodeType = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Note = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_ProductVariantUnitBarcode", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId",
                        column: x => x.ProductUnitConversionId,
                        principalTable: "ProductUnitConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantUnitBarcode_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequestLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemKind = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    ProductVariantId = table.Column<int>(type: "int", nullable: true),
                    UnitId = table.Column<int>(type: "int", nullable: true),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    ProductNameSnapshot = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    SkuSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ConversionFactor = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ApprovedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    ConvertedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseRequestLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRequestLines_ProductUnitConversion_ProductUnitConversionId",
                        column: x => x.ProductUnitConversionId,
                        principalTable: "ProductUnitConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequestLines_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequestLines_PurchaseRequests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "PurchaseRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequestLines_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseRequestLines_Unit_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Unit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariantBarcodeHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: false),
                    OldBarcodeId = table.Column<int>(type: "int", nullable: true),
                    NewBarcodeId = table.Column<int>(type: "int", nullable: true),
                    OldBarcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    NewBarcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ChangedByUserId = table.Column<int>(type: "int", nullable: true),
                    ChangedByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_ProductVariantBarcodeHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariantBarcodeHistory_ProductUnitConversion_ProductUnitConversionId",
                        column: x => x.ProductUnitConversionId,
                        principalTable: "ProductUnitConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantBarcodeHistory_ProductVariantUnitBarcode_NewBarcodeId",
                        column: x => x.NewBarcodeId,
                        principalTable: "ProductVariantUnitBarcode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantBarcodeHistory_ProductVariantUnitBarcode_OldBarcodeId",
                        column: x => x.OldBarcodeId,
                        principalTable: "ProductVariantUnitBarcode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantBarcodeHistory_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantBarcodeHistory_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CustomerRewardLedgers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    SalesReturnId = table.Column<int>(type: "int", nullable: true),
                    VoucherId = table.Column<int>(type: "int", nullable: true),
                    ReferenceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_CustomerRewardLedgers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerRewardLedgers_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerRewardLedgers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CustomerRewardVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    VoucherCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RequiredAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedOrderId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReferenceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_CustomerRewardVouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerRewardVouchers_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerRewardVouchers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InventoryAdjustmentDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocumentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    AdjustmentType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReasonType = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ApprovalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InventoryAdjustmentDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustmentDocuments_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InventoryAdjustmentLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventoryAdjustmentDocumentId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: true),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Factor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ProvisionalUnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InventoryAdjustmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustmentLines_InventoryAdjustmentDocuments_InventoryAdjustmentDocumentId",
                        column: x => x.InventoryAdjustmentDocumentId,
                        principalTable: "InventoryAdjustmentDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustmentLines_ProductUnitConversion_ProductUnitConversionId",
                        column: x => x.ProductUnitConversionId,
                        principalTable: "ProductUnitConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustmentLines_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustmentLines_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InventoryAdjustmentLines_Unit_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Unit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    OnHandQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    ReservedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    InventoryValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    AverageUnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m),
                    LastInboundUnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    LastInboundAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastValuationAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InventoryBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InventoryCostLayerAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventoryValuationEntryId = table.Column<int>(type: "int", nullable: false),
                    InventoryCostLayerId = table.Column<int>(type: "int", nullable: true),
                    ReverseOfAllocationId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsProvisional = table.Column<bool>(type: "bit", nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    ResolvedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ResolvedAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByInventoryCostLayerId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InventoryCostLayerAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryCostLayerAllocations_InventoryCostLayerAllocations_ReverseOfAllocationId",
                        column: x => x.ReverseOfAllocationId,
                        principalTable: "InventoryCostLayerAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostLayerAllocations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InventoryCostLayers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: false),
                    InventoryValuationEntryId = table.Column<int>(type: "int", nullable: false),
                    ReferenceType = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReferenceLineId = table.Column<int>(type: "int", nullable: true),
                    ReferenceSubKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OriginalQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RemainingQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ResolvedProvisionalQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RemainingOpenProvisionalQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    IsProvisionalSource = table.Column<bool>(type: "bit", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InventoryCostLayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryCostLayers_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCostLayers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InventoryReservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    ReferenceType = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceLineId = table.Column<int>(type: "int", nullable: true),
                    ReservedQty = table.Column<decimal>(type: "decimal(18,3)", nullable: false, defaultValue: 0m),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleaseNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InventoryReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryReservations_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InventoryTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    TransactionType = table.Column<int>(type: "int", nullable: false),
                    ReferenceType = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReferenceLineId = table.Column<int>(type: "int", nullable: true),
                    QuantityChange = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BeforeQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AfterQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCostSnapshot = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    BeforeInventoryValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    AfterInventoryValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    RunningAverageUnitCostAfter = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m),
                    CostSourceType = table.Column<int>(type: "int", nullable: false),
                    IsProvisionalCost = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CostFinalizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReferenceSubKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InventoryTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InventoryValuationEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    EntryType = table.Column<int>(type: "int", nullable: false),
                    ReferenceType = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReferenceLineId = table.Column<int>(type: "int", nullable: true),
                    ReferenceSubKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    RunningQtyAfter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    RunningValueAfter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    RunningAverageUnitCostAfter = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m),
                    CostSourceType = table.Column<int>(type: "int", nullable: false),
                    IsProvisional = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CostFinalizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevaluationOfEntryId = table.Column<int>(type: "int", nullable: true),
                    SourceValuationEntryId = table.Column<int>(type: "int", nullable: true),
                    SourceReferenceSubKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InventoryCostLayerId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InventoryValuationEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryValuationEntries_InventoryCostLayers_InventoryCostLayerId",
                        column: x => x.InventoryCostLayerId,
                        principalTable: "InventoryCostLayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryValuationEntries_InventoryTransactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryValuationEntries_InventoryValuationEntries_RevaluationOfEntryId",
                        column: x => x.RevaluationOfEntryId,
                        principalTable: "InventoryValuationEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryValuationEntries_InventoryValuationEntries_SourceValuationEntryId",
                        column: x => x.SourceValuationEntryId,
                        principalTable: "InventoryValuationEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryValuationEntries_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryValuationEntries_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceCorrectionCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OriginalInvoiceHeadId = table.Column<int>(type: "int", nullable: false),
                    NewInvoiceHeadId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                    Reason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    AgreementDocumentNo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    AgreementDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InvoiceCorrectionCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceCorrectionCases_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceHeadId = table.Column<int>(type: "int", nullable: false),
                    OrderLineId = table.Column<int>(type: "int", nullable: true),
                    OrderLegalEntityAllocationId = table.Column<int>(type: "int", nullable: true),
                    ProductVariantId = table.Column<int>(type: "int", nullable: true),
                    SourceType = table.Column<byte>(type: "tinyint", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    UnitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InvoiceDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceDetails_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvoiceDetails_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceHeads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegalEntityId = table.Column<int>(type: "int", nullable: true),
                    InvoiceProviderSettingId = table.Column<int>(type: "int", nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InvoiceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BuyerType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "NoInvoice"),
                    BuyerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BuyerLegalName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BuyerTaxCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BuyerAddress = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true),
                    BuyerEmail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    BuyerPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TotalQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    SubTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockedByUserId = table.Column<int>(type: "int", nullable: true),
                    LockReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProviderStatus = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                    TransactionUuid = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SupplierTaxCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    InvoiceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TemplateCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    InvoiceSeries = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    ProviderInvoiceNo = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReservationCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CodeOfTax = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PdfFilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ZipFilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OfficialPdfStatus = table.Column<int>(type: "int", nullable: false),
                    OfficialPdfDownloadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OfficialPdfFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    OfficialZipXmlStatus = table.Column<int>(type: "int", nullable: false),
                    OfficialZipXmlDownloadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OfficialZipXmlFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    EmailStatus = table.Column<int>(type: "int", nullable: false),
                    EmailSentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastEmailTo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EmailSendCount = table.Column<int>(type: "int", nullable: false),
                    LastEmailErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OriginalInvoiceHeadId = table.Column<int>(type: "int", nullable: true),
                    CorrectionType = table.Column<byte>(type: "tinyint", nullable: true),
                    OriginalInvoiceNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OriginalInvoiceIssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AdjustedNote = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    AdditionalReferenceDesc = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    AdditionalReferenceDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InvoiceHeads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceHeads_InvoiceHeads_OriginalInvoiceHeadId",
                        column: x => x.OriginalInvoiceHeadId,
                        principalTable: "InvoiceHeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceHeads_InvoiceProviderSettings_StoreId_InvoiceProviderSettingId",
                        columns: x => new { x.StoreId, x.InvoiceProviderSettingId },
                        principalTable: "InvoiceProviderSettings",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceHeads_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceIntegrationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceHeadId = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<byte>(type: "tinyint", nullable: false),
                    RequestUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSuccess = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InvoiceIntegrationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceIntegrationLogs_InvoiceHeads_InvoiceHeadId",
                        column: x => x.InvoiceHeadId,
                        principalTable: "InvoiceHeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceIntegrationLogs_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LegalEntities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TaxCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    DefaultWarehouseId = table.Column<int>(type: "int", nullable: true),
                    InvoiceProviderSettingId = table.Column<int>(type: "int", nullable: true),
                    SalePriority = table.Column<int>(type: "int", nullable: false),
                    IsDefaultForPurchase = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_LegalEntities", x => x.Id);
                    table.UniqueConstraint("AK_LegalEntities_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_LegalEntities_SalePriority_Positive", "[SalePriority] > 0");
                    table.ForeignKey(
                        name: "FK_LegalEntities_InvoiceProviderSettings_StoreId_InvoiceProviderSettingId",
                        columns: x => new { x.StoreId, x.InvoiceProviderSettingId },
                        principalTable: "InvoiceProviderSettings",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegalEntities_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AllowNegativeInventory = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                    table.UniqueConstraint("AK_Warehouses_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.ForeignKey(
                        name: "FK_Warehouses_LegalEntities_StoreId_LegalEntityId",
                        columns: x => new { x.StoreId, x.LegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Warehouses_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "NegativeInventoryLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    BeforeQty = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    QuantityChange = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    AfterQty = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    TransactionType = table.Column<int>(type: "int", nullable: false),
                    ReferenceType = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReferenceLineId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_NegativeInventoryLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NegativeInventoryLog_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NegativeInventoryLog_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NegativeInventoryLog_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourcePurchaseRequestId = table.Column<int>(type: "int", nullable: true),
                    SourceConversionKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    ExpectedWarehouseId = table.Column<int>(type: "int", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpectedDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OutsideRequestReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HasVat = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubtotalBeforeVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VatTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAfterVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReturnedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnedByUserId = table.Column<int>(type: "int", nullable: true),
                    SentToSupplierAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentToSupplierByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<int>(type: "int", nullable: true),
                    WorkflowNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_PurchaseRequests_SourcePurchaseRequestId",
                        column: x => x.SourcePurchaseRequestId,
                        principalTable: "PurchaseRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Warehouses_ExpectedWarehouseId",
                        column: x => x.ExpectedWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockCountDocument",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocumentName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    DocumentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_StockCountDocument", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCountDocument_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StockCountDocument_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferDocument",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocumentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromWarehouseId = table.Column<int>(type: "int", nullable: false),
                    ToWarehouseId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_StockTransferDocument", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransferDocument_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StockTransferDocument_Warehouses_FromWarehouseId",
                        column: x => x.FromWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferDocument_Warehouses_ToWarehouseId",
                        column: x => x.ToWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    SourcePurchaseRequestLineId = table.Column<int>(type: "int", nullable: true),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemKind = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    ProductVariantId = table.Column<int>(type: "int", nullable: true),
                    UnitId = table.Column<int>(type: "int", nullable: true),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    TaxId = table.Column<int>(type: "int", nullable: true),
                    ProductNameSnapshot = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    SkuSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TaxNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConversionFactor = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPriceBeforeVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitPriceAfterVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotalBeforeVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotalAfterVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ShortClosedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ReceiptStatus = table.Column<int>(type: "int", nullable: false),
                    ShortCloseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ShortClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ShortClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_ProductUnitConversion_ProductUnitConversionId",
                        column: x => x.ProductUnitConversionId,
                        principalTable: "ProductUnitConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseRequestLines_SourcePurchaseRequestLineId",
                        column: x => x.SourcePurchaseRequestLineId,
                        principalTable: "PurchaseRequestLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_Taxes_TaxId",
                        column: x => x.TaxId,
                        principalTable: "Taxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_Unit_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Unit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequestActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: false),
                    ToStatus = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseRequestActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRequestActions_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequestActions_PurchaseRequests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "PurchaseRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequestActions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StockDocument",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocumentTitle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DocumentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    ReceiptSource = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    DirectReceiptReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HasVat = table.Column<bool>(type: "bit", nullable: false),
                    SubtotalBeforeVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    HasFreight = table.Column<bool>(type: "bit", nullable: false),
                    FreightTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FreightPayeeName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    FreightNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsFreightPaid = table.Column<bool>(type: "bit", nullable: false),
                    IsMerchandisePaid = table.Column<bool>(type: "bit", nullable: false),
                    MerchandisePayeeName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    HasRevisionRequest = table.Column<bool>(type: "bit", nullable: false),
                    RevisionRequestNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RevisionRequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevisionRequestedByUserId = table.Column<int>(type: "int", nullable: true),
                    RevisionResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevisionResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_StockDocument", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockDocument_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocument_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StockDocument_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocument_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockCountLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockCountDocumentId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    UnitNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Factor = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    SystemQtyBase = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    CountedQty = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    CountedQtyBase = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    DifferenceQtyBase = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    SkuSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BarcodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UnitCostSnapshot = table.Column<decimal>(type: "decimal(18,6)", nullable: false, defaultValue: 0m),
                    LineCostTotal = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    IsProvisionalCost = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_StockCountLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCountLine_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCountLine_StockCountDocument_StockCountDocumentId",
                        column: x => x.StockCountDocumentId,
                        principalTable: "StockCountDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockCountLine_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StockCountLine_Unit_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Unit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockTransferDocumentId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    UnitNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Factor = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    SkuSnapshot = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    BarcodeSnapshot = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UnitCostSnapshot = table.Column<decimal>(type: "decimal(18,6)", nullable: false, defaultValue: 0m),
                    LineCostTotal = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    IsProvisionalCost = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_StockTransferLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransferLine_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferLine_StockTransferDocument_StockTransferDocumentId",
                        column: x => x.StockTransferDocumentId,
                        principalTable: "StockTransferDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockTransferLine_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProductBarcodeVerificationRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: false),
                    StockDocumentId = table.Column<int>(type: "int", nullable: true),
                    ProductNameSnapshot = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UnitNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FactorSnapshot = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    SuggestedBarcode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequestType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EmployeeNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ManagerNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBarcodeId = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBarcodeVerificationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductBarcodeVerificationRequests_ProductUnitConversion_ProductUnitConversionId",
                        column: x => x.ProductUnitConversionId,
                        principalTable: "ProductUnitConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBarcodeVerificationRequests_ProductVariantUnitBarcode_CreatedBarcodeId",
                        column: x => x.CreatedBarcodeId,
                        principalTable: "ProductVariantUnitBarcode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBarcodeVerificationRequests_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBarcodeVerificationRequests_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBarcodeVerificationRequests_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: false),
                    ToStatus = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StockDocumentId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseOrderActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderActions_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderActions_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderActions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchasePayables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockDocumentId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    PayeeName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RecognizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_PurchasePayables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchasePayables_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchasePayables_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchasePayables_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchasePayables_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockDocumentInputInvoiceMap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockDocumentId = table.Column<int>(type: "int", nullable: false),
                    InputInvoiceHeadId = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_StockDocumentInputInvoiceMap", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockDocumentInputInvoiceMap_InputInvoiceHead_InputInvoiceHeadId",
                        column: x => x.InputInvoiceHeadId,
                        principalTable: "InputInvoiceHead",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentInputInvoiceMap_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentInputInvoiceMap_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StockDocumentLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockDocumentId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderLineId = table.Column<int>(type: "int", nullable: true),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    TaxId = table.Column<int>(type: "int", nullable: true),
                    TaxNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitId = table.Column<int>(type: "int", nullable: true),
                    UnitNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Factor = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnitPriceBeforeVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitPriceAfterVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FreightAllocation = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShortageDisposition = table.Column<int>(type: "int", nullable: false),
                    ShortageReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProductNameSnapshot = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    SkuSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BarcodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockDocumentLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockDocumentLine_ProductUnitConversion_ProductUnitConversionId",
                        column: x => x.ProductUnitConversionId,
                        principalTable: "ProductUnitConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentLine_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentLine_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentLine_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockDocumentLine_Taxes_TaxId",
                        column: x => x.TaxId,
                        principalTable: "Taxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentLine_Unit_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Unit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockDocumentLineInputInvoiceMap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockDocumentId = table.Column<int>(type: "int", nullable: false),
                    StockDocumentLineId = table.Column<int>(type: "int", nullable: false),
                    InputInvoiceDetailId = table.Column<int>(type: "int", nullable: true),
                    UseInputInvoice = table.Column<bool>(type: "bit", nullable: false),
                    MatchStatus = table.Column<int>(type: "int", nullable: false),
                    QuantityDifference = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    AmountDifference = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_StockDocumentLineInputInvoiceMap", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockDocumentLineInputInvoiceMap_InputInvoiceDetail_InputInvoiceDetailId",
                        column: x => x.InputInvoiceDetailId,
                        principalTable: "InputInvoiceDetail",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentLineInputInvoiceMap_StockDocumentLine_StockDocumentLineId",
                        column: x => x.StockDocumentLineId,
                        principalTable: "StockDocumentLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentLineInputInvoiceMap_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentLineInputInvoiceMap_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrderInventoryIssueActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderInventoryIssueId = table.Column<int>(type: "int", nullable: false),
                    OrderInventoryIssueLineId = table.Column<int>(type: "int", nullable: true),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActionAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReferenceType = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_OrderInventoryIssueActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueActions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueActions_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderInventoryIssueLineAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderInventoryIssueId = table.Column<int>(type: "int", nullable: false),
                    OrderInventoryIssueLineId = table.Column<int>(type: "int", nullable: false),
                    SourceReferenceType = table.Column<int>(type: "int", nullable: false),
                    SourceReferenceId = table.Column<int>(type: "int", nullable: false),
                    SourceReferenceLineId = table.Column<int>(type: "int", nullable: true),
                    InventoryCostLayerId = table.Column<int>(type: "int", nullable: true),
                    InventoryCostLayerAllocationId = table.Column<int>(type: "int", nullable: true),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: true),
                    AllocatedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_OrderInventoryIssueLineAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLineAllocations_InventoryCostLayerAllocations_InventoryCostLayerAllocationId",
                        column: x => x.InventoryCostLayerAllocationId,
                        principalTable: "InventoryCostLayerAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLineAllocations_InventoryCostLayers_InventoryCostLayerId",
                        column: x => x.InventoryCostLayerId,
                        principalTable: "InventoryCostLayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLineAllocations_InventoryTransactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLineAllocations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrderInventoryIssueLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderInventoryIssueId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    OrderLineId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    BarcodeId = table.Column<int>(type: "int", nullable: true),
                    OrderedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    StockBefore = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    StockAfter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    NegativeQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ProvisionalUnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ProvisionalCostAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RevaluationAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AutoDetectedInboundQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    AutoDetectedDocumentResolved = table.Column<bool>(type: "bit", nullable: false),
                    AutoDetectedCostResolved = table.Column<bool>(type: "bit", nullable: false),
                    AutoDetectedRevaluationAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AutoResolveNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastAutoResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_OrderInventoryIssueLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLines_ProductUnitConversion_ProductUnitConversionId",
                        column: x => x.ProductUnitConversionId,
                        principalTable: "ProductUnitConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLines_ProductVariantUnitBarcode_BarcodeId",
                        column: x => x.BarcodeId,
                        principalTable: "ProductVariantUnitBarcode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLines_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLines_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrderInventoryIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadyForApprovalAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReasonType = table.Column<int>(type: "int", nullable: false),
                    InternalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsOverdue = table.Column<bool>(type: "bit", nullable: false),
                    OverdueSinceUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastOverdueNotifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastAutoResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AutoResolvedLineCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_OrderInventoryIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssues_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssues_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssues_Users_RejectedByUserId",
                        column: x => x.RejectedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderLegalEntityAllocationReversals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    OrderLineId = table.Column<int>(type: "int", nullable: false),
                    OrderLegalEntityAllocationId = table.Column<int>(type: "int", nullable: false),
                    SalesReturnId = table.Column<int>(type: "int", nullable: true),
                    SalesReturnLineId = table.Column<int>(type: "int", nullable: true),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    SourceValuationEntryId = table.Column<int>(type: "int", nullable: false),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: true),
                    ReversalType = table.Column<byte>(type: "tinyint", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    FinancialAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_OrderLegalEntityAllocationReversals", x => x.Id);
                    table.CheckConstraint("CK_OrderLegalEntityAllocationReversals_BaseQuantity_Positive", "[BaseQuantity] > 0");
                    table.CheckConstraint("CK_OrderLegalEntityAllocationReversals_FinancialAmount_NonNegative", "[FinancialAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_InventoryTransactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_InventoryValuationEntries_SourceValuationEntryId",
                        column: x => x.SourceValuationEntryId,
                        principalTable: "InventoryValuationEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_LegalEntities_StoreId_LegalEntityId",
                        columns: x => new { x.StoreId, x.LegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocationReversals_Warehouses_StoreId_WarehouseId",
                        columns: x => new { x.StoreId, x.WarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderLegalEntityAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    OrderLineId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: true),
                    SalePriority = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAllocated = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PromotionDiscountAllocated = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ComboDiscountAllocated = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OrderDiscountAllocated = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VoucherDiscountAllocated = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AllocationSource = table.Column<byte>(type: "tinyint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_OrderLegalEntityAllocations", x => x.Id);
                    table.UniqueConstraint("AK_OrderLegalEntityAllocations_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_OrderLegalEntityAllocations_Amounts_NonNegative", "[LineTotal] >= 0 AND [DiscountAllocated] >= 0 AND [PromotionDiscountAllocated] >= 0 AND [ComboDiscountAllocated] >= 0 AND [OrderDiscountAllocated] >= 0 AND [VoucherDiscountAllocated] >= 0 AND [NetAmount] >= 0");
                    table.CheckConstraint("CK_OrderLegalEntityAllocations_Quantity_Positive", "[Quantity] > 0 AND [BaseQuantity] > 0");
                    table.CheckConstraint("CK_OrderLegalEntityAllocations_SalePriority_Positive", "[SalePriority] > 0");
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_InventoryTransactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_LegalEntities_StoreId_LegalEntityId",
                        columns: x => new { x.StoreId, x.LegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_ProductUnitConversion_ProductUnitConversionId",
                        column: x => x.ProductUnitConversionId,
                        principalTable: "ProductUnitConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_Warehouses_StoreId_WarehouseId",
                        columns: x => new { x.StoreId, x.WarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnitName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Sku = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Barcode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCostSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 6, nullable: true),
                    LineCostTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 4, nullable: true),
                    GrossProfit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 4, nullable: true),
                    IsProvisionalCost = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ProvisionalUnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 6, nullable: true),
                    CostSnapshotNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SellingUnitId = table.Column<int>(type: "int", nullable: true),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    SellingUnitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BaseUnitId = table.Column<int>(type: "int", nullable: true),
                    BaseUnitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Multiplier = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 1m),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ScannedBarcode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BarcodeSource = table.Column<int>(type: "int", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OriginalUnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PromotionDiscount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PromotionId = table.Column<int>(type: "int", nullable: true),
                    PromotionName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ComboPromotionId = table.Column<int>(type: "int", nullable: true),
                    ComboPromotionName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ComboPromotionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ComboAllocatedDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PromotionType = table.Column<byte>(type: "tinyint", nullable: true),
                    PromotionBuyQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    PromotionGiftQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    IsPromotionGift = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    GiftPromotionId = table.Column<int>(type: "int", nullable: true),
                    GiftSourceLineId = table.Column<int>(type: "int", nullable: true),
                    GiftPromotionName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    GiftPromotionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_OrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderLines_ProductVariant_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderLines_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrderPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReferenceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_OrderPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderPayments_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrderRewardVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: false),
                    VoucherValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_OrderRewardVouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderRewardVouchers_CustomerRewardVouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "CustomerRewardVouchers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderRewardVouchers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    PaymentStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OrderDiscount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceDue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ChangeDue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    POSShiftId = table.Column<int>(type: "int", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HeldAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HoldNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HoldCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HasReservation = table.Column<bool>(type: "bit", nullable: false),
                    ReservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LegalEntityCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    HasMultipleLegalEntities = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LegalEntityAllocatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UseMultiLegalEntity = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LegalEntityModeCapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LegalEntityActivationAtUtcSnapshot = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HasInventoryIssue = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    InventoryResolutionStatus = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    InventoryIssueOpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InventoryIssueApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoucherDiscountTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PromotionDiscountTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ComboDiscountTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ComboPromotionId = table.Column<int>(type: "int", nullable: true),
                    ComboPromotionName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ComboPromotionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Orders_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PosPaymentQrRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    QrRenderMode = table.Column<int>(type: "int", nullable: false),
                    ConfirmMode = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QrDataUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QrRawText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProviderRawResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CallbackRawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpireAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ManualConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    ManualConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_PosPaymentQrRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosPaymentQrRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosPaymentQrRequests_StoreBankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "StoreBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosPaymentQrRequests_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "POSShifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TerminalId = table.Column<int>(type: "int", nullable: false),
                    OpenedByUserId = table.Column<int>(type: "int", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ShiftCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    OpeningCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    OpenNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CashSalesTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    NonCashSalesTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    CashRefundTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    NonCashRefundTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    RefundCount = table.Column<int>(type: "int", nullable: false),
                    VoidCount = table.Column<int>(type: "int", nullable: false),
                    CashInTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    CashOutTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    ClosingCashExpected = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    ClosingCashActual = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CloseNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CurrentOrderId = table.Column<int>(type: "int", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_POSShifts", x => x.Id);
                    table.CheckConstraint("CK_POSShifts_CashInTotal_NonNegative", "[CashInTotal] >= 0");
                    table.CheckConstraint("CK_POSShifts_CashOutTotal_NonNegative", "[CashOutTotal] >= 0");
                    table.CheckConstraint("CK_POSShifts_CashSalesTotal_NonNegative", "[CashSalesTotal] >= 0");
                    table.CheckConstraint("CK_POSShifts_ClosingCashActual_NonNegative", "[ClosingCashActual] IS NULL OR [ClosingCashActual] >= 0");
                    table.CheckConstraint("CK_POSShifts_ClosingCashExpected_NonNegative", "[ClosingCashExpected] >= 0");
                    table.CheckConstraint("CK_POSShifts_NonCashSalesTotal_NonNegative", "[NonCashSalesTotal] >= 0");
                    table.CheckConstraint("CK_POSShifts_OpeningCash_NonNegative", "[OpeningCash] >= 0");
                    table.ForeignKey(
                        name: "FK_POSShifts_Orders_CurrentOrderId",
                        column: x => x.CurrentOrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShifts_POSTerminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "POSTerminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShifts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_POSShifts_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "POSShiftCashDenominations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    POSShiftId = table.Column<int>(type: "int", nullable: false),
                    EntryType = table.Column<byte>(type: "tinyint", nullable: false),
                    DenominationValue = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_POSShiftCashDenominations", x => x.Id);
                    table.CheckConstraint("CK_POSShiftCashDenominations_Amount_NonNegative", "[Amount] >= 0");
                    table.CheckConstraint("CK_POSShiftCashDenominations_DenominationValue_NonNegative", "[DenominationValue] >= 0");
                    table.CheckConstraint("CK_POSShiftCashDenominations_Quantity_NonNegative", "[Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_POSShiftCashDenominations_POSShifts_POSShiftId",
                        column: x => x.POSShiftId,
                        principalTable: "POSShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShiftCashDenominations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "POSShiftCashTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    POSShiftId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_POSShiftCashTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_POSShiftCashTransactions_POSShifts_POSShiftId",
                        column: x => x.POSShiftId,
                        principalTable: "POSShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShiftCashTransactions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "POSShiftClosingSlips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    POSShiftId = table.Column<int>(type: "int", nullable: false),
                    SlipCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BarcodeValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    OpenedByUserId = table.Column<int>(type: "int", nullable: false),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OpeningCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CashSalesTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NonCashSalesTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CashRefundTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NonCashRefundTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundCount = table.Column<int>(type: "int", nullable: false),
                    VoidCount = table.Column<int>(type: "int", nullable: false),
                    CashInTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CashOutTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingCashExpected = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingCashActual = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CashDifference = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CloseNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PrintedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_POSShiftClosingSlips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_POSShiftClosingSlips_POSShifts_POSShiftId",
                        column: x => x.POSShiftId,
                        principalTable: "POSShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShiftClosingSlips_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "POSShiftHandoverSlips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SlipCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BarcodeValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    TerminalId = table.Column<int>(type: "int", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    AssignedToUserId = table.Column<int>(type: "int", nullable: true),
                    OpeningCashTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    UsedPOSShiftId = table.Column<int>(type: "int", nullable: true),
                    PrintedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_POSShiftHandoverSlips", x => x.Id);
                    table.CheckConstraint("CK_POSShiftHandoverSlips_OpeningCashTotal_NonNegative", "[OpeningCashTotal] >= 0");
                    table.ForeignKey(
                        name: "FK_POSShiftHandoverSlips_POSShifts_UsedPOSShiftId",
                        column: x => x.UsedPOSShiftId,
                        principalTable: "POSShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShiftHandoverSlips_POSTerminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "POSTerminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShiftHandoverSlips_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_POSShiftHandoverSlips_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesReturns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    POSShiftId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReturnSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CompletedByUserId = table.Column<int>(type: "int", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_SalesReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesReturns_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesReturns_POSShifts_POSShiftId",
                        column: x => x.POSShiftId,
                        principalTable: "POSShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesReturns_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "POSShiftClosingSlipDenominations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    POSShiftClosingSlipId = table.Column<int>(type: "int", nullable: false),
                    DenominationValue = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_POSShiftClosingSlipDenominations", x => x.Id);
                    table.CheckConstraint("CK_POSShiftClosingSlipDenominations_Amount_NonNegative", "[Amount] >= 0");
                    table.CheckConstraint("CK_POSShiftClosingSlipDenominations_DenominationValue_NonNegative", "[DenominationValue] >= 0");
                    table.CheckConstraint("CK_POSShiftClosingSlipDenominations_Quantity_NonNegative", "[Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_POSShiftClosingSlipDenominations_POSShiftClosingSlips_POSShiftClosingSlipId",
                        column: x => x.POSShiftClosingSlipId,
                        principalTable: "POSShiftClosingSlips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShiftClosingSlipDenominations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "POSShiftHandoverSlipDenominations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    POSShiftHandoverSlipId = table.Column<int>(type: "int", nullable: false),
                    DenominationValue = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_POSShiftHandoverSlipDenominations", x => x.Id);
                    table.CheckConstraint("CK_POSShiftHandoverSlipDenominations_Amount_NonNegative", "[Amount] >= 0");
                    table.CheckConstraint("CK_POSShiftHandoverSlipDenominations_DenominationValue_NonNegative", "[DenominationValue] >= 0");
                    table.CheckConstraint("CK_POSShiftHandoverSlipDenominations_Quantity_NonNegative", "[Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_POSShiftHandoverSlipDenominations_POSShiftHandoverSlips_POSShiftHandoverSlipId",
                        column: x => x.POSShiftHandoverSlipId,
                        principalTable: "POSShiftHandoverSlips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShiftHandoverSlipDenominations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SalesReturnLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesReturnId = table.Column<int>(type: "int", nullable: false),
                    OrderLineId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    UnitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReturnQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ReturnBaseQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    RefundUnitAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    RefundLineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    Action = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UnitCostSnapshot = table.Column<decimal>(type: "decimal(18,6)", nullable: false, defaultValue: 0m),
                    LineCostTotal = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    IsProvisionalCost = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_SalesReturnLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesReturnLines_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnLines_SalesReturns_SalesReturnId",
                        column: x => x.SalesReturnId,
                        principalTable: "SalesReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesReturnLines_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SalesReturnPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesReturnId = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReferenceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_SalesReturnPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesReturnPayments_SalesReturns_SalesReturnId",
                        column: x => x.SalesReturnId,
                        principalTable: "SalesReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnPayments_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminMenuItems_ParentId",
                table: "AdminMenuItems",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminMenuItems_StoreId_ParentId_SortOrder",
                table: "AdminMenuItems",
                columns: new[] { "StoreId", "ParentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Attribute_StoreId_Code",
                table: "Attribute",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attribute_StoreId_Name",
                table: "Attribute",
                columns: new[] { "StoreId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttributeValue_AttributeId",
                table: "AttributeValue",
                column: "AttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttributeValue_StoreId_AttributeId_Code",
                table: "AttributeValue",
                columns: new[] { "StoreId", "AttributeId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttributeValue_StoreId_AttributeId_Name",
                table: "AttributeValue",
                columns: new[] { "StoreId", "AttributeId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityName_EntityId_CreatedAtUtc",
                table: "AuditLogs",
                columns: new[] { "EntityName", "EntityId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_StoreId",
                table: "AuditLogs",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_StoreId_ActorUserId_CreatedAtUtc",
                table: "AuditLogs",
                columns: new[] { "StoreId", "ActorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_StoreId_CreatedAtUtc",
                table: "AuditLogs",
                columns: new[] { "StoreId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_StoreId_Module_ActionType_CreatedAtUtc",
                table: "AuditLogs",
                columns: new[] { "StoreId", "Module", "ActionType", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TraceId",
                table: "AuditLogs",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_Brands_StoreId_Code",
                table: "Brands",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Brands_StoreId_Name",
                table: "Brands",
                columns: new[] { "StoreId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Category_ParentId",
                table: "Category",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Category_StoreId_Code",
                table: "Category",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Category_StoreId_Name",
                table: "Category",
                columns: new[] { "StoreId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_CustomerId",
                table: "CustomerRewardLedgers",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_OrderId",
                table: "CustomerRewardLedgers",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_SalesReturnId",
                table: "CustomerRewardLedgers",
                column: "SalesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_StoreId_CustomerId_CreatedAtUtc",
                table: "CustomerRewardLedgers",
                columns: new[] { "StoreId", "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_StoreId_OrderId",
                table: "CustomerRewardLedgers",
                columns: new[] { "StoreId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_StoreId_ReferenceCode",
                table: "CustomerRewardLedgers",
                columns: new[] { "StoreId", "ReferenceCode" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_StoreId_SalesReturnId",
                table: "CustomerRewardLedgers",
                columns: new[] { "StoreId", "SalesReturnId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_StoreId_VoucherId",
                table: "CustomerRewardLedgers",
                columns: new[] { "StoreId", "VoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_VoucherId",
                table: "CustomerRewardLedgers",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardVouchers_CustomerId",
                table: "CustomerRewardVouchers",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardVouchers_StoreId_CustomerId_Status",
                table: "CustomerRewardVouchers",
                columns: new[] { "StoreId", "CustomerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardVouchers_StoreId_ReferenceCode",
                table: "CustomerRewardVouchers",
                columns: new[] { "StoreId", "ReferenceCode" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardVouchers_StoreId_VoucherCode",
                table: "CustomerRewardVouchers",
                columns: new[] { "StoreId", "VoucherCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardVouchers_UsedOrderId",
                table: "CustomerRewardVouchers",
                column: "UsedOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_StoreId_Code",
                table: "Customers",
                columns: new[] { "StoreId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_StoreId_OldCustomerId",
                table: "Customers",
                columns: new[] { "StoreId", "OldCustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_StoreId_Phone",
                table: "Customers",
                columns: new[] { "StoreId", "Phone" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_StoreId_TaxCode",
                table: "Customers",
                columns: new[] { "StoreId", "TaxCode" });

            migrationBuilder.CreateIndex(
                name: "IX_DisplayPromotions_StoreId_IsActive_SortOrder",
                table: "DisplayPromotions",
                columns: new[] { "StoreId", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberSequences_StoreId_SequenceType_SequenceDate",
                table: "DocumentNumberSequences",
                columns: new[] { "StoreId", "SequenceType", "SequenceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InputInvoiceDetail_InputInvoiceHeadId_LineNo",
                table: "InputInvoiceDetail",
                columns: new[] { "InputInvoiceHeadId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_InputInvoiceHead_StoreId_SellerTaxCode_InvoiceTemplateCode_InvoiceSeries_InvoiceNumber",
                table: "InputInvoiceHead",
                columns: new[] { "StoreId", "SellerTaxCode", "InvoiceTemplateCode", "InvoiceSeries", "InvoiceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_InputInvoiceHead_StoreId_XmlHash",
                table: "InputInvoiceHead",
                columns: new[] { "StoreId", "XmlHash" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustmentDocument_Store_Status_Date",
                table: "InventoryAdjustmentDocuments",
                columns: new[] { "StoreId", "Status", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustmentDocument_Store_Warehouse_Date",
                table: "InventoryAdjustmentDocuments",
                columns: new[] { "StoreId", "WarehouseId", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustmentDocuments_WarehouseId",
                table: "InventoryAdjustmentDocuments",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryAdjustmentDocument_Store_DocumentNo",
                table: "InventoryAdjustmentDocuments",
                columns: new[] { "StoreId", "DocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustmentLine_Store_Document",
                table: "InventoryAdjustmentLines",
                columns: new[] { "StoreId", "InventoryAdjustmentDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustmentLine_Store_ProductVariant",
                table: "InventoryAdjustmentLines",
                columns: new[] { "StoreId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustmentLines_InventoryAdjustmentDocumentId",
                table: "InventoryAdjustmentLines",
                column: "InventoryAdjustmentDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustmentLines_ProductUnitConversionId",
                table: "InventoryAdjustmentLines",
                column: "ProductUnitConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustmentLines_ProductVariantId",
                table: "InventoryAdjustmentLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustmentLines_UnitId",
                table: "InventoryAdjustmentLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_ProductVariantId",
                table: "InventoryBalances",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_StoreId_ProductVariantId",
                table: "InventoryBalances",
                columns: new[] { "StoreId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_StoreId_WarehouseId",
                table: "InventoryBalances",
                columns: new[] { "StoreId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_StoreId_WarehouseId_ProductVariantId",
                table: "InventoryBalances",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_WarehouseId",
                table: "InventoryBalances",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayerAllocations_CostLayerId",
                table: "InventoryCostLayerAllocations",
                column: "InventoryCostLayerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayerAllocations_OpenProvisional",
                table: "InventoryCostLayerAllocations",
                columns: new[] { "StoreId", "IsProvisional", "IsResolved", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayerAllocations_ResolvedByLayerId",
                table: "InventoryCostLayerAllocations",
                column: "ResolvedByInventoryCostLayerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayerAllocations_ReverseOfAllocationId",
                table: "InventoryCostLayerAllocations",
                column: "ReverseOfAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayerAllocations_ValuationEntryId",
                table: "InventoryCostLayerAllocations",
                column: "InventoryValuationEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayers_FIFO",
                table: "InventoryCostLayers",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayers_InventoryTransactionId",
                table: "InventoryCostLayers",
                column: "InventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayers_InventoryValuationEntryId",
                table: "InventoryCostLayers",
                column: "InventoryValuationEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayers_Open",
                table: "InventoryCostLayers",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "RemainingQuantity" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayers_ProductVariantId",
                table: "InventoryCostLayers",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostLayers_WarehouseId",
                table: "InventoryCostLayers",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_ProductVariantId",
                table: "InventoryReservations",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_StoreId_ReferenceType_ReferenceId",
                table: "InventoryReservations",
                columns: new[] { "StoreId", "ReferenceType", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_StoreId_ReferenceType_ReferenceId_ReferenceLineId_WarehouseId_ProductVariantId_Status",
                table: "InventoryReservations",
                columns: new[] { "StoreId", "ReferenceType", "ReferenceId", "ReferenceLineId", "WarehouseId", "ProductVariantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_StoreId_WarehouseId_ProductVariantId_Status",
                table: "InventoryReservations",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_WarehouseId",
                table: "InventoryReservations",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ProductVariantId",
                table: "InventoryTransactions",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_StoreId_ReferenceType_ReferenceId_ReferenceLineId_TransactionType",
                table: "InventoryTransactions",
                columns: new[] { "StoreId", "ReferenceType", "ReferenceId", "ReferenceLineId", "TransactionType" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_StoreId_WarehouseId_ProductVariantId_OccurredAtUtc_Id",
                table: "InventoryTransactions",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_WarehouseId",
                table: "InventoryTransactions",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_InventoryCostLayerId",
                table: "InventoryValuationEntries",
                column: "InventoryCostLayerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_InventoryTransactionId",
                table: "InventoryValuationEntries",
                column: "InventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_ProductVariantId",
                table: "InventoryValuationEntries",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_RevaluationOfEntryId",
                table: "InventoryValuationEntries",
                column: "RevaluationOfEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_SourceValuationEntryId",
                table: "InventoryValuationEntries",
                column: "SourceValuationEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_InventoryCostLayerId",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "InventoryCostLayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_InventoryTransactionId",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "InventoryTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_ReferenceType_ReferenceId_ReferenceLineId_ReferenceSubKey_EntryType",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "ReferenceType", "ReferenceId", "ReferenceLineId", "ReferenceSubKey", "EntryType" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_RevaluationOfEntryId",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "RevaluationOfEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_SourceValuationEntryId",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "SourceValuationEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_WarehouseId_ProductVariantId_EntryType_IsProvisional_CostFinalizedAtUtc_OccurredAtUtc_Id",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "EntryType", "IsProvisional", "CostFinalizedAtUtc", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_WarehouseId_ProductVariantId_OccurredAtUtc_Id",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_WarehouseId",
                table: "InventoryValuationEntries",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBuyerProfiles_CustomerId",
                table: "InvoiceBuyerProfiles",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBuyerProfiles_StoreId_BuyerType_IsDeleted",
                table: "InvoiceBuyerProfiles",
                columns: new[] { "StoreId", "BuyerType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBuyerProfiles_StoreId_CustomerId_IsDeleted",
                table: "InvoiceBuyerProfiles",
                columns: new[] { "StoreId", "CustomerId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBuyerProfiles_StoreId_IsActive_IsDeleted",
                table: "InvoiceBuyerProfiles",
                columns: new[] { "StoreId", "IsActive", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBuyerProfiles_StoreId_LastUsedAtUtc_IsDeleted",
                table: "InvoiceBuyerProfiles",
                columns: new[] { "StoreId", "LastUsedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBuyerProfiles_StoreId_TaxCode_IsDeleted",
                table: "InvoiceBuyerProfiles",
                columns: new[] { "StoreId", "TaxCode", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_NewInvoiceHeadId",
                table: "InvoiceCorrectionCases",
                column: "NewInvoiceHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_OriginalInvoiceHeadId",
                table: "InvoiceCorrectionCases",
                column: "OriginalInvoiceHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_StoreId_CreatedAtUtc_IsDeleted",
                table: "InvoiceCorrectionCases",
                columns: new[] { "StoreId", "CreatedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_StoreId_NewInvoiceHeadId_IsDeleted",
                table: "InvoiceCorrectionCases",
                columns: new[] { "StoreId", "NewInvoiceHeadId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_StoreId_OriginalInvoiceHeadId_IsDeleted",
                table: "InvoiceCorrectionCases",
                columns: new[] { "StoreId", "OriginalInvoiceHeadId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_StoreId_Type_Status_IsDeleted",
                table: "InvoiceCorrectionCases",
                columns: new[] { "StoreId", "Type", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_InvoiceHeadId",
                table: "InvoiceDetails",
                column: "InvoiceHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_OrderLineId",
                table: "InvoiceDetails",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_ProductVariantId",
                table: "InvoiceDetails",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_InvoiceHeadId_IsDeleted",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "InvoiceHeadId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLegalEntityAllocationId",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "InvoiceHeadId", "OrderLegalEntityAllocationId" },
                unique: true,
                filter: "[OrderLegalEntityAllocationId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLineId",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "InvoiceHeadId", "OrderLineId" },
                unique: true,
                filter: "[OrderLineId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_OrderLegalEntityAllocationId",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "OrderLegalEntityAllocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_ProductVariantId_IsDeleted",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "ProductVariantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_SourceType_IsDeleted",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "SourceType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_OrderId",
                table: "InvoiceHeads",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceHeads",
                column: "OriginalInvoiceHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_BuyerTaxCode_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "BuyerTaxCode", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_CorrectionType_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "CorrectionType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_InvoiceDate_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "InvoiceDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_InvoiceProviderSettingId_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "InvoiceProviderSettingId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_LegalEntityId_InvoiceDate_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "LegalEntityId", "InvoiceDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "OrderId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL AND [LegalEntityId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId_LegalEntityId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "OrderId", "LegalEntityId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL AND [LegalEntityId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_OriginalInvoiceHeadId_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "OriginalInvoiceHeadId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_ProviderInvoiceNo_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "ProviderInvoiceNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_ProviderStatus_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "ProviderStatus", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_TransactionUuid",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "TransactionUuid" },
                unique: true,
                filter: "[TransactionUuid] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceIntegrationLogs_InvoiceHeadId",
                table: "InvoiceIntegrationLogs",
                column: "InvoiceHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceIntegrationLogs_StoreId_InvoiceHeadId_ActionType",
                table: "InvoiceIntegrationLogs",
                columns: new[] { "StoreId", "InvoiceHeadId", "ActionType" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceIntegrationLogs_StoreId_IsSuccess_ActionType",
                table: "InvoiceIntegrationLogs",
                columns: new[] { "StoreId", "IsSuccess", "ActionType" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceIntegrationLogs_StoreId_StartedAtUtc",
                table: "InvoiceIntegrationLogs",
                columns: new[] { "StoreId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceProviderSettings_StoreId_IsActive_IsDeleted",
                table: "InvoiceProviderSettings",
                columns: new[] { "StoreId", "IsActive", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceProviderSettings_StoreId_ProviderCode_SupplierTaxCode_TemplateCode_InvoiceSeries",
                table: "InvoiceProviderSettings",
                columns: new[] { "StoreId", "ProviderCode", "SupplierTaxCode", "TemplateCode", "InvoiceSeries" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LegalEntities_StoreId",
                table: "LegalEntities",
                column: "StoreId",
                unique: true,
                filter: "[IsDefaultForPurchase] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LegalEntities_StoreId_Code",
                table: "LegalEntities",
                columns: new[] { "StoreId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LegalEntities_StoreId_DefaultWarehouseId",
                table: "LegalEntities",
                columns: new[] { "StoreId", "DefaultWarehouseId" },
                unique: true,
                filter: "[DefaultWarehouseId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LegalEntities_StoreId_InvoiceProviderSettingId",
                table: "LegalEntities",
                columns: new[] { "StoreId", "InvoiceProviderSettingId" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalEntities_StoreId_IsActive_IsDeleted",
                table: "LegalEntities",
                columns: new[] { "StoreId", "IsActive", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalEntities_StoreId_SalePriority",
                table: "LegalEntities",
                columns: new[] { "StoreId", "SalePriority" },
                unique: true,
                filter: "[IsActive] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LegalEntities_StoreId_TaxCode",
                table: "LegalEntities",
                columns: new[] { "StoreId", "TaxCode" },
                unique: true,
                filter: "[TaxCode] IS NOT NULL AND [TaxCode] <> '' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LegalEntityActivationEvents_StoreId_OccurredAtUtc_IsDeleted",
                table: "LegalEntityActivationEvents",
                columns: new[] { "StoreId", "OccurredAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_StoreId_Id",
                table: "MediaAssets",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_StoreId_IsDeleted",
                table: "MediaAssets",
                columns: new[] { "StoreId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_NegativeInventoryLog_ProductVariantId",
                table: "NegativeInventoryLog",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_NegativeInventoryLog_Store_Warehouse_Variant_OccurredAt",
                table: "NegativeInventoryLog",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NegativeInventoryLog_WarehouseId",
                table: "NegativeInventoryLog",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueActions_ActorUserId",
                table: "OrderInventoryIssueActions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueActions_OrderInventoryIssueId_ActionAtUtc_IsDeleted",
                table: "OrderInventoryIssueActions",
                columns: new[] { "OrderInventoryIssueId", "ActionAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueActions_OrderInventoryIssueLineId",
                table: "OrderInventoryIssueActions",
                column: "OrderInventoryIssueLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueActions_StoreId_ActionType_ActionAtUtc_IsDeleted",
                table: "OrderInventoryIssueActions",
                columns: new[] { "StoreId", "ActionType", "ActionAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueActions_StoreId_ReferenceType_ReferenceId_IsDeleted",
                table: "OrderInventoryIssueActions",
                columns: new[] { "StoreId", "ReferenceType", "ReferenceId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLineAllocations_InventoryCostLayerAllocationId",
                table: "OrderInventoryIssueLineAllocations",
                column: "InventoryCostLayerAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLineAllocations_InventoryCostLayerId",
                table: "OrderInventoryIssueLineAllocations",
                column: "InventoryCostLayerId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLineAllocations_InventoryTransactionId",
                table: "OrderInventoryIssueLineAllocations",
                column: "InventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLineAllocations_OrderInventoryIssueId_OrderInventoryIssueLineId_IsDeleted",
                table: "OrderInventoryIssueLineAllocations",
                columns: new[] { "OrderInventoryIssueId", "OrderInventoryIssueLineId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLineAllocations_OrderInventoryIssueLineId",
                table: "OrderInventoryIssueLineAllocations",
                column: "OrderInventoryIssueLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLineAllocations_StoreId",
                table: "OrderInventoryIssueLineAllocations",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_BarcodeId",
                table: "OrderInventoryIssueLines",
                column: "BarcodeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_OrderId_IsDeleted",
                table: "OrderInventoryIssueLines",
                columns: new[] { "OrderId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_OrderInventoryIssueId_IsDeleted",
                table: "OrderInventoryIssueLines",
                columns: new[] { "OrderInventoryIssueId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_OrderLineId_IsDeleted",
                table: "OrderInventoryIssueLines",
                columns: new[] { "OrderLineId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_ProductId",
                table: "OrderInventoryIssueLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_ProductUnitConversionId",
                table: "OrderInventoryIssueLines",
                column: "ProductUnitConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_ProductVariantId",
                table: "OrderInventoryIssueLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_StoreId_ProductUnitConversionId_IsResolved_IsDeleted",
                table: "OrderInventoryIssueLines",
                columns: new[] { "StoreId", "ProductUnitConversionId", "IsResolved", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_StoreId_ProductVariantId_IsResolved_IsDeleted",
                table: "OrderInventoryIssueLines",
                columns: new[] { "StoreId", "ProductVariantId", "IsResolved", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_ApprovedByUserId",
                table: "OrderInventoryIssues",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_OrderId",
                table: "OrderInventoryIssues",
                column: "OrderId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_RejectedByUserId",
                table: "OrderInventoryIssues",
                column: "RejectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_Code",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_DueAtUtc_Status_IsDeleted",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "DueAtUtc", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_IsOverdue_LastOverdueNotifiedAtUtc_IsDeleted",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "IsOverdue", "LastOverdueNotifiedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_IsOverdue_OverdueSinceUtc_IsDeleted",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "IsOverdue", "OverdueSinceUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_IsOverdue_Status_IsDeleted",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "IsOverdue", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_OpenedAtUtc_IsDeleted",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "OpenedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_Status_IsDeleted",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_InventoryTransactionId",
                table: "OrderLegalEntityAllocationReversals",
                column: "InventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_OrderId",
                table: "OrderLegalEntityAllocationReversals",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_OrderLegalEntityAllocationId",
                table: "OrderLegalEntityAllocationReversals",
                column: "OrderLegalEntityAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_OrderLineId",
                table: "OrderLegalEntityAllocationReversals",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_ProductVariantId",
                table: "OrderLegalEntityAllocationReversals",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_SalesReturnId",
                table: "OrderLegalEntityAllocationReversals",
                column: "SalesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_SalesReturnLineId",
                table: "OrderLegalEntityAllocationReversals",
                column: "SalesReturnLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_SourceValuationEntryId",
                table: "OrderLegalEntityAllocationReversals",
                column: "SourceValuationEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_LegalEntityId",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "LegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_OrderId_IsDeleted",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "OrderId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_OrderLegalEntityAllocationId_IsDeleted",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "OrderLegalEntityAllocationId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_ReversalType_SalesReturnLineId_SourceValuationEntryId",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "ReversalType", "SalesReturnLineId", "SourceValuationEntryId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_SalesReturnId_IsDeleted",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "SalesReturnId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_SourceValuationEntryId_IsDeleted",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "SourceValuationEntryId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocationReversals_StoreId_WarehouseId",
                table: "OrderLegalEntityAllocationReversals",
                columns: new[] { "StoreId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_InventoryTransactionId",
                table: "OrderLegalEntityAllocations",
                column: "InventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_OrderId",
                table: "OrderLegalEntityAllocations",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_OrderLineId",
                table: "OrderLegalEntityAllocations",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_ProductUnitConversionId",
                table: "OrderLegalEntityAllocations",
                column: "ProductUnitConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_ProductVariantId",
                table: "OrderLegalEntityAllocations",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_StoreId_InventoryTransactionId",
                table: "OrderLegalEntityAllocations",
                columns: new[] { "StoreId", "InventoryTransactionId" },
                unique: true,
                filter: "[InventoryTransactionId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_StoreId_LegalEntityId_OrderId_IsDeleted",
                table: "OrderLegalEntityAllocations",
                columns: new[] { "StoreId", "LegalEntityId", "OrderId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_StoreId_OrderId_IsDeleted",
                table: "OrderLegalEntityAllocations",
                columns: new[] { "StoreId", "OrderId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_StoreId_OrderId_OrderLineId_LegalEntityId_WarehouseId",
                table: "OrderLegalEntityAllocations",
                columns: new[] { "StoreId", "OrderId", "OrderLineId", "LegalEntityId", "WarehouseId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_StoreId_WarehouseId_ProductVariantId_IsDeleted",
                table: "OrderLegalEntityAllocations",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_OrderId",
                table: "OrderLines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StoreId_GiftPromotionId",
                table: "OrderLines",
                columns: new[] { "StoreId", "GiftPromotionId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StoreId_GiftSourceLineId",
                table: "OrderLines",
                columns: new[] { "StoreId", "GiftSourceLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StoreId_IsPromotionGift",
                table: "OrderLines",
                columns: new[] { "StoreId", "IsPromotionGift" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StoreId_OrderId",
                table: "OrderLines",
                columns: new[] { "StoreId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StoreId_ProductId",
                table: "OrderLines",
                columns: new[] { "StoreId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StoreId_ProductUnitConversionId",
                table: "OrderLines",
                columns: new[] { "StoreId", "ProductUnitConversionId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StoreId_VariantId",
                table: "OrderLines",
                columns: new[] { "StoreId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_VariantId",
                table: "OrderLines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNumberSequences_StoreId_DateKey",
                table: "OrderNumberSequences",
                columns: new[] { "StoreId", "DateKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_OrderId",
                table: "OrderPayments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_StoreId_OrderId",
                table: "OrderPayments",
                columns: new[] { "StoreId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRewardVouchers_OrderId",
                table: "OrderRewardVouchers",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRewardVouchers_StoreId_OrderId",
                table: "OrderRewardVouchers",
                columns: new[] { "StoreId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRewardVouchers_StoreId_VoucherId",
                table: "OrderRewardVouchers",
                columns: new[] { "StoreId", "VoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRewardVouchers_VoucherId",
                table: "OrderRewardVouchers",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId",
                table: "Orders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_POSShiftId",
                table: "Orders",
                column: "POSShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_CompletedAtUtc",
                table: "Orders",
                columns: new[] { "StoreId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_HasInventoryIssue_InventoryResolutionStatus_IsDeleted",
                table: "Orders",
                columns: new[] { "StoreId", "HasInventoryIssue", "InventoryResolutionStatus", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_HasMultipleLegalEntities_LegalEntityAllocatedAtUtc_IsDeleted",
                table: "Orders",
                columns: new[] { "StoreId", "HasMultipleLegalEntities", "LegalEntityAllocatedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_InventoryIssueOpenedAtUtc_IsDeleted",
                table: "Orders",
                columns: new[] { "StoreId", "InventoryIssueOpenedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_OrderNumber",
                table: "Orders",
                columns: new[] { "StoreId", "OrderNumber" },
                unique: true,
                filter: "[OrderNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_POSShiftId_Status",
                table: "Orders",
                columns: new[] { "StoreId", "POSShiftId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_UseMultiLegalEntity_Status_LegalEntityModeCapturedAtUtc_IsDeleted",
                table: "Orders",
                columns: new[] { "StoreId", "UseMultiLegalEntity", "Status", "LegalEntityModeCapturedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Code",
                table: "Permissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_POSAuditLogs_StoreId",
                table: "POSAuditLogs",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_PosPaymentQrRequests_BankAccountId",
                table: "PosPaymentQrRequests",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PosPaymentQrRequests_OrderId",
                table: "PosPaymentQrRequests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PosPaymentQrRequests_StoreId_OrderId_Status",
                table: "PosPaymentQrRequests",
                columns: new[] { "StoreId", "OrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PosPaymentQrRequests_StoreId_RequestCode",
                table: "PosPaymentQrRequests",
                columns: new[] { "StoreId", "RequestCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashDenominations_POSShiftId",
                table: "POSShiftCashDenominations",
                column: "POSShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashDenominations_StoreId_POSShiftId_EntryType",
                table: "POSShiftCashDenominations",
                columns: new[] { "StoreId", "POSShiftId", "EntryType" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashDenominations_StoreId_POSShiftId_EntryType_DenominationValue",
                table: "POSShiftCashDenominations",
                columns: new[] { "StoreId", "POSShiftId", "EntryType", "DenominationValue" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashTransactions_CreatedAtUtc",
                table: "POSShiftCashTransactions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashTransactions_POSShiftId",
                table: "POSShiftCashTransactions",
                column: "POSShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashTransactions_StoreId_POSShiftId",
                table: "POSShiftCashTransactions",
                columns: new[] { "StoreId", "POSShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashTransactions_StoreId_Type",
                table: "POSShiftCashTransactions",
                columns: new[] { "StoreId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftClosingSlipDenominations_POSShiftClosingSlipId",
                table: "POSShiftClosingSlipDenominations",
                column: "POSShiftClosingSlipId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftClosingSlipDenominations_StoreId_POSShiftClosingSlipId_DenominationValue",
                table: "POSShiftClosingSlipDenominations",
                columns: new[] { "StoreId", "POSShiftClosingSlipId", "DenominationValue" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftClosingSlips_POSShiftId",
                table: "POSShiftClosingSlips",
                column: "POSShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftClosingSlips_StoreId_BarcodeValue",
                table: "POSShiftClosingSlips",
                columns: new[] { "StoreId", "BarcodeValue" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftClosingSlips_StoreId_POSShiftId",
                table: "POSShiftClosingSlips",
                columns: new[] { "StoreId", "POSShiftId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftClosingSlips_StoreId_SlipCode",
                table: "POSShiftClosingSlips",
                columns: new[] { "StoreId", "SlipCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlipDenominations_POSShiftHandoverSlipId",
                table: "POSShiftHandoverSlipDenominations",
                column: "POSShiftHandoverSlipId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlipDenominations_StoreId_POSShiftHandoverSlipId",
                table: "POSShiftHandoverSlipDenominations",
                columns: new[] { "StoreId", "POSShiftHandoverSlipId" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlipDenominations_StoreId_POSShiftHandoverSlipId_DenominationValue",
                table: "POSShiftHandoverSlipDenominations",
                columns: new[] { "StoreId", "POSShiftHandoverSlipId", "DenominationValue" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_StoreId_BarcodeValue",
                table: "POSShiftHandoverSlips",
                columns: new[] { "StoreId", "BarcodeValue" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_StoreId_CreatedAtUtc",
                table: "POSShiftHandoverSlips",
                columns: new[] { "StoreId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_StoreId_SlipCode",
                table: "POSShiftHandoverSlips",
                columns: new[] { "StoreId", "SlipCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_StoreId_Status",
                table: "POSShiftHandoverSlips",
                columns: new[] { "StoreId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_StoreId_TerminalId",
                table: "POSShiftHandoverSlips",
                columns: new[] { "StoreId", "TerminalId" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_StoreId_WarehouseId",
                table: "POSShiftHandoverSlips",
                columns: new[] { "StoreId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_TerminalId",
                table: "POSShiftHandoverSlips",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_UsedPOSShiftId",
                table: "POSShiftHandoverSlips",
                column: "UsedPOSShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_WarehouseId",
                table: "POSShiftHandoverSlips",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_CurrentOrderId",
                table: "POSShifts",
                column: "CurrentOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_StoreId_ClosedAtUtc",
                table: "POSShifts",
                columns: new[] { "StoreId", "ClosedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_StoreId_OpenedAtUtc",
                table: "POSShifts",
                columns: new[] { "StoreId", "OpenedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_StoreId_ShiftCode",
                table: "POSShifts",
                columns: new[] { "StoreId", "ShiftCode" },
                unique: true,
                filter: "[ShiftCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_StoreId_Status",
                table: "POSShifts",
                columns: new[] { "StoreId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_StoreId_TerminalId_Status",
                table: "POSShifts",
                columns: new[] { "StoreId", "TerminalId", "Status" },
                unique: true,
                filter: "[Status] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_TerminalId",
                table: "POSShifts",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_WarehouseId",
                table: "POSShifts",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_POSTerminalDevices_StoreId_DeviceKey",
                table: "POSTerminalDevices",
                columns: new[] { "StoreId", "DeviceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_POSTerminalDevices_StoreId_TerminalId_IsActive",
                table: "POSTerminalDevices",
                columns: new[] { "StoreId", "TerminalId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_POSTerminalDevices_TerminalId",
                table: "POSTerminalDevices",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_POSTerminals_StoreId_Code",
                table: "POSTerminals",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_POSTerminals_StoreId_LocalIp",
                table: "POSTerminals",
                columns: new[] { "StoreId", "LocalIp" },
                unique: true,
                filter: "[LocalIp] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodeVerificationRequests_CreatedBarcodeId",
                table: "ProductBarcodeVerificationRequests",
                column: "CreatedBarcodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodeVerificationRequests_ProductUnitConversionId",
                table: "ProductBarcodeVerificationRequests",
                column: "ProductUnitConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodeVerificationRequests_ProductVariantId",
                table: "ProductBarcodeVerificationRequests",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodeVerificationRequests_StockDocumentId_IsDeleted",
                table: "ProductBarcodeVerificationRequests",
                columns: new[] { "StockDocumentId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodeVerificationRequests_StoreId_ProductUnitConversionId_Status_IsDeleted",
                table: "ProductBarcodeVerificationRequests",
                columns: new[] { "StoreId", "ProductUnitConversionId", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodeVerificationRequests_StoreId_SuggestedBarcode_Status_IsDeleted",
                table: "ProductBarcodeVerificationRequests",
                columns: new[] { "StoreId", "SuggestedBarcode", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_MediaAssetId",
                table: "ProductImages",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId",
                table: "ProductImages",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_StoreId_ProductId",
                table: "ProductImages",
                columns: new[] { "StoreId", "ProductId" },
                unique: true,
                filter: "[IsPrimary] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_StoreId_ProductId_IsPrimary",
                table: "ProductImages",
                columns: new[] { "StoreId", "ProductId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_StoreId_ProductId_SortOrder",
                table: "ProductImages",
                columns: new[] { "StoreId", "ProductId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_BaseUnitId",
                table: "Products",
                column: "BaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_BrandId",
                table: "Products",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId_Alias",
                table: "Products",
                columns: new[] { "StoreId", "Alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_SupplierId",
                table: "Products",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TaxId",
                table: "Products",
                column: "TaxId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnitConversion_ProductVariantId",
                table: "ProductUnitConversion",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnitConversion_StoreId_ProductVariantId_IsActive",
                table: "ProductUnitConversion",
                columns: new[] { "StoreId", "ProductVariantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnitConversion_StoreId_ProductVariantId_IsBaseUnit",
                table: "ProductUnitConversion",
                columns: new[] { "StoreId", "ProductVariantId", "IsBaseUnit" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnitConversion_StoreId_ProductVariantId_IsDefaultForSale",
                table: "ProductUnitConversion",
                columns: new[] { "StoreId", "ProductVariantId", "IsDefaultForSale" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnitConversion_StoreId_ProductVariantId_UnitId",
                table: "ProductUnitConversion",
                columns: new[] { "StoreId", "ProductVariantId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnitConversion_UnitId",
                table: "ProductUnitConversion",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_PrimaryProductImageId",
                table: "ProductVariant",
                column: "PrimaryProductImageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_ProductId",
                table: "ProductVariant",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_Store_ProductVariantName",
                table: "ProductVariant",
                columns: new[] { "StoreId", "IsDeleted", "IsActive", "ProductVariantName" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_Store_ProductVariantNameNormalized",
                table: "ProductVariant",
                columns: new[] { "StoreId", "IsDeleted", "IsActive", "ProductVariantNameNormalized" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_Store_Sku",
                table: "ProductVariant",
                columns: new[] { "StoreId", "IsDeleted", "IsActive", "Sku" });

            migrationBuilder.CreateIndex(
                name: "UX_ProductVariant_Store_Product_Sku",
                table: "ProductVariant",
                columns: new[] { "StoreId", "ProductId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributeValue_AttributeId",
                table: "ProductVariantAttributeValue",
                column: "AttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributeValue_AttributeValueId",
                table: "ProductVariantAttributeValue",
                column: "AttributeValueId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributeValue_StoreId_VariantId_AttributeId",
                table: "ProductVariantAttributeValue",
                columns: new[] { "StoreId", "VariantId", "AttributeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributeValue_VariantId",
                table: "ProductVariantAttributeValue",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantBarcodeHistory_Conversion_ChangedAtUtc",
                table: "ProductVariantBarcodeHistory",
                columns: new[] { "ProductUnitConversionId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantBarcodeHistory_NewBarcodeId",
                table: "ProductVariantBarcodeHistory",
                column: "NewBarcodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantBarcodeHistory_OldBarcodeId",
                table: "ProductVariantBarcodeHistory",
                column: "OldBarcodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantBarcodeHistory_Store_NewBarcode",
                table: "ProductVariantBarcodeHistory",
                columns: new[] { "StoreId", "NewBarcode" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantBarcodeHistory_Store_OldBarcode",
                table: "ProductVariantBarcodeHistory",
                columns: new[] { "StoreId", "OldBarcode" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantBarcodeHistory_Variant_ChangedAtUtc",
                table: "ProductVariantBarcodeHistory",
                columns: new[] { "ProductVariantId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantUnitBarcode_Conversion_Active_Primary",
                table: "ProductVariantUnitBarcode",
                columns: new[] { "ProductUnitConversionId", "IsActive", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantUnitBarcode_Store_Conversion",
                table: "ProductVariantUnitBarcode",
                columns: new[] { "StoreId", "ProductUnitConversionId" });

            migrationBuilder.CreateIndex(
                name: "UX_ProductVariantUnitBarcode_Conversion_Primary_Active",
                table: "ProductVariantUnitBarcode",
                column: "ProductUnitConversionId",
                unique: true,
                filter: "[IsDeleted] = 0 AND [IsActive] = 1 AND [IsPrimary] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_ProductVariantUnitBarcode_Store_Barcode_Active",
                table: "ProductVariantUnitBarcode",
                columns: new[] { "StoreId", "Barcode" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionComboRule_PromotionId",
                table: "PromotionComboRule",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionComboRule_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId_IsDeleted",
                table: "PromotionComboRule",
                columns: new[] { "StoreId", "PromotionId", "ProductId", "VariantId", "ProductUnitConversionId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionItems_PromotionId",
                table: "PromotionItems",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionItems_StoreId_ProductId",
                table: "PromotionItems",
                columns: new[] { "StoreId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionItems_StoreId_ProductUnitConversionId",
                table: "PromotionItems",
                columns: new[] { "StoreId", "ProductUnitConversionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionItems_StoreId_PromotionId",
                table: "PromotionItems",
                columns: new[] { "StoreId", "PromotionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionItems_StoreId_PromotionId_ProductId_VariantId_ProductUnitConversionId_IsDeleted",
                table: "PromotionItems",
                columns: new[] { "StoreId", "PromotionId", "ProductId", "VariantId", "ProductUnitConversionId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionItems_StoreId_VariantId",
                table: "PromotionItems",
                columns: new[] { "StoreId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_StoreId_IsActive_StartAtUtc_EndAtUtc",
                table: "Promotions",
                columns: new[] { "StoreId", "IsActive", "StartAtUtc", "EndAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_StoreId_Type_IsActive_StartAtUtc_EndAtUtc_IsDeleted",
                table: "Promotions",
                columns: new[] { "StoreId", "Type", "IsActive", "StartAtUtc", "EndAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_StoreId_Type_IsDeleted",
                table: "Promotions",
                columns: new[] { "StoreId", "Type", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderActions_PurchaseOrderId",
                table: "PurchaseOrderActions",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderActions_StockDocumentId",
                table: "PurchaseOrderActions",
                column: "StockDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderActions_StoreId_PurchaseOrderId_OccurredAtUtc",
                table: "PurchaseOrderActions",
                columns: new[] { "StoreId", "PurchaseOrderId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_ProductUnitConversionId",
                table: "PurchaseOrderLines",
                column: "ProductUnitConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_ProductVariantId",
                table: "PurchaseOrderLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId_LineNo",
                table: "PurchaseOrderLines",
                columns: new[] { "PurchaseOrderId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_SourcePurchaseRequestLineId",
                table: "PurchaseOrderLines",
                column: "SourcePurchaseRequestLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_StoreId",
                table: "PurchaseOrderLines",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_TaxId",
                table: "PurchaseOrderLines",
                column: "TaxId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_UnitId",
                table: "PurchaseOrderLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_ExpectedWarehouseId",
                table: "PurchaseOrders",
                column: "ExpectedWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_LegalEntityId",
                table: "PurchaseOrders",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SourcePurchaseRequestId",
                table: "PurchaseOrders",
                column: "SourcePurchaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_StoreId_LegalEntityId_Status",
                table: "PurchaseOrders",
                columns: new[] { "StoreId", "LegalEntityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_StoreId_OrderNumber",
                table: "PurchaseOrders",
                columns: new[] { "StoreId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_StoreId_SourcePurchaseRequestId_SourceConversionKey",
                table: "PurchaseOrders",
                columns: new[] { "StoreId", "SourcePurchaseRequestId", "SourceConversionKey" },
                unique: true,
                filter: "[SourcePurchaseRequestId] IS NOT NULL AND [SourceConversionKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_StoreId_Status_OrderDate",
                table: "PurchaseOrders",
                columns: new[] { "StoreId", "Status", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_StoreId_SupplierId_OrderDate",
                table: "PurchaseOrders",
                columns: new[] { "StoreId", "SupplierId", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SupplierId",
                table: "PurchaseOrders",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasePayables_PurchaseOrderId",
                table: "PurchasePayables",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasePayables_StockDocumentId",
                table: "PurchasePayables",
                column: "StockDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasePayables_StoreId_SourceKey",
                table: "PurchasePayables",
                columns: new[] { "StoreId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchasePayables_StoreId_Status_RecognizedAtUtc",
                table: "PurchasePayables",
                columns: new[] { "StoreId", "Status", "RecognizedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchasePayables_SupplierId",
                table: "PurchasePayables",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestActions_PurchaseOrderId",
                table: "PurchaseRequestActions",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestActions_PurchaseRequestId",
                table: "PurchaseRequestActions",
                column: "PurchaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestActions_StoreId_PurchaseRequestId_OccurredAtUtc",
                table: "PurchaseRequestActions",
                columns: new[] { "StoreId", "PurchaseRequestId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestLines_ProductUnitConversionId",
                table: "PurchaseRequestLines",
                column: "ProductUnitConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestLines_ProductVariantId",
                table: "PurchaseRequestLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestLines_PurchaseRequestId_LineNo",
                table: "PurchaseRequestLines",
                columns: new[] { "PurchaseRequestId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestLines_StoreId",
                table: "PurchaseRequestLines",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestLines_UnitId",
                table: "PurchaseRequestLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_StoreId_RequestedByUserId_Status",
                table: "PurchaseRequests",
                columns: new[] { "StoreId", "RequestedByUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_StoreId_RequestNumber",
                table: "PurchaseRequests",
                columns: new[] { "StoreId", "RequestNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_StoreId_Status_RequestDate",
                table: "PurchaseRequests",
                columns: new[] { "StoreId", "Status", "RequestDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RewardSettings_StoreId",
                table: "RewardSettings",
                column: "StoreId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_PermissionId",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_StoreId_Code",
                table: "Roles",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_StoreId_Name",
                table: "Roles",
                columns: new[] { "StoreId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_OrderLineId",
                table: "SalesReturnLines",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_SalesReturnId",
                table: "SalesReturnLines",
                column: "SalesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_StoreId_OrderLineId",
                table: "SalesReturnLines",
                columns: new[] { "StoreId", "OrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_StoreId_SalesReturnId",
                table: "SalesReturnLines",
                columns: new[] { "StoreId", "SalesReturnId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_StoreId_VariantId",
                table: "SalesReturnLines",
                columns: new[] { "StoreId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnPayments_SalesReturnId",
                table: "SalesReturnPayments",
                column: "SalesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnPayments_StoreId_PaidAtUtc",
                table: "SalesReturnPayments",
                columns: new[] { "StoreId", "PaidAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnPayments_StoreId_SalesReturnId",
                table: "SalesReturnPayments",
                columns: new[] { "StoreId", "SalesReturnId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_OrderId",
                table: "SalesReturns",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_POSShiftId",
                table: "SalesReturns",
                column: "POSShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_StoreId_OrderId",
                table: "SalesReturns",
                columns: new[] { "StoreId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_StoreId_POSShiftId",
                table: "SalesReturns",
                columns: new[] { "StoreId", "POSShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_StoreId_ReturnNumber",
                table: "SalesReturns",
                columns: new[] { "StoreId", "ReturnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCountDocument_StoreId_DocumentNo",
                table: "StockCountDocument",
                columns: new[] { "StoreId", "DocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCountDocument_StoreId_WarehouseId_Status_DocumentDate",
                table: "StockCountDocument",
                columns: new[] { "StoreId", "WarehouseId", "Status", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockCountDocument_WarehouseId",
                table: "StockCountDocument",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLine_ProductVariantId",
                table: "StockCountLine",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLine_StockCountDocumentId_LineNo",
                table: "StockCountLine",
                columns: new[] { "StockCountDocumentId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLine_StockCountDocumentId_ProductVariantId",
                table: "StockCountLine",
                columns: new[] { "StockCountDocumentId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLine_StoreId",
                table: "StockCountLine",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLine_UnitId",
                table: "StockCountLine",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_HasRevisionRequest",
                table: "StockDocument",
                column: "HasRevisionRequest");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_PurchaseOrderId",
                table: "StockDocument",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_Status",
                table: "StockDocument",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_StoreId_DocumentNo",
                table: "StockDocument",
                columns: new[] { "StoreId", "DocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_StoreId_Type_Status_DocumentDate",
                table: "StockDocument",
                columns: new[] { "StoreId", "Type", "Status", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_StoreId_WarehouseId_DocumentDate",
                table: "StockDocument",
                columns: new[] { "StoreId", "WarehouseId", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_SupplierId",
                table: "StockDocument",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_WarehouseId",
                table: "StockDocument",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentInputInvoiceMap_InputInvoiceHeadId",
                table: "StockDocumentInputInvoiceMap",
                column: "InputInvoiceHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentInputInvoiceMap_StockDocumentId",
                table: "StockDocumentInputInvoiceMap",
                column: "StockDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_InputInvoiceHeadId",
                table: "StockDocumentInputInvoiceMap",
                columns: new[] { "StoreId", "StockDocumentId", "InputInvoiceHeadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLine_ProductUnitConversionId",
                table: "StockDocumentLine",
                column: "ProductUnitConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLine_ProductVariantId",
                table: "StockDocumentLine",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLine_PurchaseOrderLineId",
                table: "StockDocumentLine",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLine_StockDocumentId",
                table: "StockDocumentLine",
                column: "StockDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLine_StockDocumentId_LineNo",
                table: "StockDocumentLine",
                columns: new[] { "StockDocumentId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLine_TaxId",
                table: "StockDocumentLine",
                column: "TaxId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLine_UnitId",
                table: "StockDocumentLine",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLineInputInvoiceMap_InputInvoiceDetailId",
                table: "StockDocumentLineInputInvoiceMap",
                column: "InputInvoiceDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLineInputInvoiceMap_StockDocumentId",
                table: "StockDocumentLineInputInvoiceMap",
                column: "StockDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLineInputInvoiceMap_StockDocumentLineId",
                table: "StockDocumentLineInputInvoiceMap",
                column: "StockDocumentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLineInputInvoiceMap_StoreId_StockDocumentId_UseInputInvoice",
                table: "StockDocumentLineInputInvoiceMap",
                columns: new[] { "StoreId", "StockDocumentId", "UseInputInvoice" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLineInputInvoiceMap_StoreId_StockDocumentLineId",
                table: "StockDocumentLineInputInvoiceMap",
                columns: new[] { "StoreId", "StockDocumentLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferDocument_FromWarehouseId",
                table: "StockTransferDocument",
                column: "FromWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferDocument_StoreId_DocumentDate_Status",
                table: "StockTransferDocument",
                columns: new[] { "StoreId", "DocumentDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferDocument_StoreId_DocumentNo",
                table: "StockTransferDocument",
                columns: new[] { "StoreId", "DocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferDocument_StoreId_FromWarehouseId_DocumentDate",
                table: "StockTransferDocument",
                columns: new[] { "StoreId", "FromWarehouseId", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferDocument_StoreId_ToWarehouseId_DocumentDate",
                table: "StockTransferDocument",
                columns: new[] { "StoreId", "ToWarehouseId", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferDocument_ToWarehouseId",
                table: "StockTransferDocument",
                column: "ToWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLine_ProductVariantId",
                table: "StockTransferLine",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLine_StockTransferDocumentId_LineNo",
                table: "StockTransferLine",
                columns: new[] { "StockTransferDocumentId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLine_StoreId_ProductVariantId",
                table: "StockTransferLine",
                columns: new[] { "StoreId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreBankAccounts_StoreId_AccountNumber",
                table: "StoreBankAccounts",
                columns: new[] { "StoreId", "AccountNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreBankAccounts_StoreId_IsDefault",
                table: "StoreBankAccounts",
                columns: new[] { "StoreId", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_Stores_SubDomainNormalized",
                table: "Stores",
                column: "SubDomainNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_StoreId_Code",
                table: "Suppliers",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_StoreId_Name",
                table: "Suppliers",
                columns: new[] { "StoreId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Taxes_StoreId_Code",
                table: "Taxes",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Taxes_StoreId_Name",
                table: "Taxes",
                columns: new[] { "StoreId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Unit_StoreId_Code",
                table: "Unit",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Unit_StoreId_Name",
                table: "Unit",
                columns: new[] { "StoreId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserInStores_RoleId",
                table: "UserInStores",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserInStores_StoreId_IsActive",
                table: "UserInStores",
                columns: new[] { "StoreId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UserInStores_StoreId_RoleId",
                table: "UserInStores",
                columns: new[] { "StoreId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserInStores_StoreId_UserId",
                table: "UserInStores",
                columns: new[] { "StoreId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserInStores_UserId",
                table: "UserInStores",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_StoreId_Code",
                table: "Warehouses",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_StoreId_LegalEntityId",
                table: "Warehouses",
                columns: new[] { "StoreId", "LegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_StoreId_Name",
                table: "Warehouses",
                columns: new[] { "StoreId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerRewardLedgers_CustomerRewardVouchers_VoucherId",
                table: "CustomerRewardLedgers",
                column: "VoucherId",
                principalTable: "CustomerRewardVouchers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerRewardLedgers_Orders_OrderId",
                table: "CustomerRewardLedgers",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerRewardLedgers_SalesReturns_SalesReturnId",
                table: "CustomerRewardLedgers",
                column: "SalesReturnId",
                principalTable: "SalesReturns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerRewardVouchers_Orders_UsedOrderId",
                table: "CustomerRewardVouchers",
                column: "UsedOrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryAdjustmentDocuments_Warehouses_WarehouseId",
                table: "InventoryAdjustmentDocuments",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryBalances_Warehouses_WarehouseId",
                table: "InventoryBalances",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryCostLayerAllocations_InventoryCostLayers_InventoryCostLayerId",
                table: "InventoryCostLayerAllocations",
                column: "InventoryCostLayerId",
                principalTable: "InventoryCostLayers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryCostLayerAllocations_InventoryCostLayers_ResolvedByInventoryCostLayerId",
                table: "InventoryCostLayerAllocations",
                column: "ResolvedByInventoryCostLayerId",
                principalTable: "InventoryCostLayers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryCostLayerAllocations_InventoryValuationEntries_InventoryValuationEntryId",
                table: "InventoryCostLayerAllocations",
                column: "InventoryValuationEntryId",
                principalTable: "InventoryValuationEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryCostLayers_InventoryTransactions_InventoryTransactionId",
                table: "InventoryCostLayers",
                column: "InventoryTransactionId",
                principalTable: "InventoryTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryCostLayers_InventoryValuationEntries_InventoryValuationEntryId",
                table: "InventoryCostLayers",
                column: "InventoryValuationEntryId",
                principalTable: "InventoryValuationEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryCostLayers_Warehouses_WarehouseId",
                table: "InventoryCostLayers",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryReservations_Warehouses_WarehouseId",
                table: "InventoryReservations",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_Warehouses_WarehouseId",
                table: "InventoryTransactions",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryValuationEntries_Warehouses_WarehouseId",
                table: "InventoryValuationEntries",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceCorrectionCases_InvoiceHeads_NewInvoiceHeadId",
                table: "InvoiceCorrectionCases",
                column: "NewInvoiceHeadId",
                principalTable: "InvoiceHeads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceCorrectionCases_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceCorrectionCases",
                column: "OriginalInvoiceHeadId",
                principalTable: "InvoiceHeads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceDetails_InvoiceHeads_InvoiceHeadId",
                table: "InvoiceDetails",
                column: "InvoiceHeadId",
                principalTable: "InvoiceHeads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceDetails_OrderLegalEntityAllocations_StoreId_OrderLegalEntityAllocationId",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "OrderLegalEntityAllocationId" },
                principalTable: "OrderLegalEntityAllocations",
                principalColumns: new[] { "StoreId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceDetails_OrderLines_OrderLineId",
                table: "InvoiceDetails",
                column: "OrderLineId",
                principalTable: "OrderLines",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceHeads_LegalEntities_StoreId_LegalEntityId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "LegalEntityId" },
                principalTable: "LegalEntities",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceHeads_Orders_OrderId",
                table: "InvoiceHeads",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LegalEntities_Warehouses_StoreId_DefaultWarehouseId",
                table: "LegalEntities",
                columns: new[] { "StoreId", "DefaultWarehouseId" },
                principalTable: "Warehouses",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInventoryIssueActions_OrderInventoryIssueLines_OrderInventoryIssueLineId",
                table: "OrderInventoryIssueActions",
                column: "OrderInventoryIssueLineId",
                principalTable: "OrderInventoryIssueLines",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInventoryIssueActions_OrderInventoryIssues_OrderInventoryIssueId",
                table: "OrderInventoryIssueActions",
                column: "OrderInventoryIssueId",
                principalTable: "OrderInventoryIssues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInventoryIssueLineAllocations_OrderInventoryIssueLines_OrderInventoryIssueLineId",
                table: "OrderInventoryIssueLineAllocations",
                column: "OrderInventoryIssueLineId",
                principalTable: "OrderInventoryIssueLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInventoryIssueLineAllocations_OrderInventoryIssues_OrderInventoryIssueId",
                table: "OrderInventoryIssueLineAllocations",
                column: "OrderInventoryIssueId",
                principalTable: "OrderInventoryIssues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInventoryIssueLines_OrderInventoryIssues_OrderInventoryIssueId",
                table: "OrderInventoryIssueLines",
                column: "OrderInventoryIssueId",
                principalTable: "OrderInventoryIssues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInventoryIssueLines_OrderLines_OrderLineId",
                table: "OrderInventoryIssueLines",
                column: "OrderLineId",
                principalTable: "OrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInventoryIssueLines_Orders_OrderId",
                table: "OrderInventoryIssueLines",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInventoryIssues_Orders_OrderId",
                table: "OrderInventoryIssues",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLegalEntityAllocationReversals_OrderLegalEntityAllocations_OrderLegalEntityAllocationId",
                table: "OrderLegalEntityAllocationReversals",
                column: "OrderLegalEntityAllocationId",
                principalTable: "OrderLegalEntityAllocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLegalEntityAllocationReversals_OrderLines_OrderLineId",
                table: "OrderLegalEntityAllocationReversals",
                column: "OrderLineId",
                principalTable: "OrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLegalEntityAllocationReversals_Orders_OrderId",
                table: "OrderLegalEntityAllocationReversals",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLegalEntityAllocationReversals_SalesReturnLines_SalesReturnLineId",
                table: "OrderLegalEntityAllocationReversals",
                column: "SalesReturnLineId",
                principalTable: "SalesReturnLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLegalEntityAllocationReversals_SalesReturns_SalesReturnId",
                table: "OrderLegalEntityAllocationReversals",
                column: "SalesReturnId",
                principalTable: "SalesReturns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLegalEntityAllocations_OrderLines_OrderLineId",
                table: "OrderLegalEntityAllocations",
                column: "OrderLineId",
                principalTable: "OrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLegalEntityAllocations_Orders_OrderId",
                table: "OrderLegalEntityAllocations",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLines_Orders_OrderId",
                table: "OrderLines",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderPayments_Orders_OrderId",
                table: "OrderPayments",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderRewardVouchers_Orders_OrderId",
                table: "OrderRewardVouchers",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_POSShifts_POSShiftId",
                table: "Orders",
                column: "POSShiftId",
                principalTable: "POSShifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Brands_Stores_StoreId",
                table: "Brands");

            migrationBuilder.DropForeignKey(
                name: "FK_Category_Stores_StoreId",
                table: "Category");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Stores_StoreId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryCostLayers_Stores_StoreId",
                table: "InventoryCostLayers");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_Stores_StoreId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryValuationEntries_Stores_StoreId",
                table: "InventoryValuationEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceProviderSettings_Stores_StoreId",
                table: "InvoiceProviderSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_LegalEntities_Stores_StoreId",
                table: "LegalEntities");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaAssets_Stores_StoreId",
                table: "MediaAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Stores_StoreId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_POSShifts_Stores_StoreId",
                table: "POSShifts");

            migrationBuilder.DropForeignKey(
                name: "FK_POSTerminals_Stores_StoreId",
                table: "POSTerminals");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductImages_Stores_StoreId",
                table: "ProductImages");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Stores_StoreId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariant_Stores_StoreId",
                table: "ProductVariant");

            migrationBuilder.DropForeignKey(
                name: "FK_Suppliers_Stores_StoreId",
                table: "Suppliers");

            migrationBuilder.DropForeignKey(
                name: "FK_Taxes_Stores_StoreId",
                table: "Taxes");

            migrationBuilder.DropForeignKey(
                name: "FK_Unit_Stores_StoreId",
                table: "Unit");

            migrationBuilder.DropForeignKey(
                name: "FK_Warehouses_Stores_StoreId",
                table: "Warehouses");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Customers_CustomerId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_POSShifts_Orders_CurrentOrderId",
                table: "POSShifts");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryCostLayers_Warehouses_WarehouseId",
                table: "InventoryCostLayers");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_Warehouses_WarehouseId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryValuationEntries_Warehouses_WarehouseId",
                table: "InventoryValuationEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_LegalEntities_Warehouses_StoreId_DefaultWarehouseId",
                table: "LegalEntities");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryCostLayers_ProductVariant_ProductVariantId",
                table: "InventoryCostLayers");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_ProductVariant_ProductVariantId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryValuationEntries_ProductVariant_ProductVariantId",
                table: "InventoryValuationEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryValuationEntries_InventoryCostLayers_InventoryCostLayerId",
                table: "InventoryValuationEntries");

            migrationBuilder.DropTable(
                name: "AdminMenuItems");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "CustomerRewardLedgers");

            migrationBuilder.DropTable(
                name: "DisplayPromotions");

            migrationBuilder.DropTable(
                name: "DocumentNumberSequences");

            migrationBuilder.DropTable(
                name: "InventoryAdjustmentLines");

            migrationBuilder.DropTable(
                name: "InventoryBalances");

            migrationBuilder.DropTable(
                name: "InventoryReservations");

            migrationBuilder.DropTable(
                name: "InvoiceBuyerProfiles");

            migrationBuilder.DropTable(
                name: "InvoiceCorrectionCases");

            migrationBuilder.DropTable(
                name: "InvoiceDetails");

            migrationBuilder.DropTable(
                name: "InvoiceIntegrationLogs");

            migrationBuilder.DropTable(
                name: "LegalEntityActivationEvents");

            migrationBuilder.DropTable(
                name: "NegativeInventoryLog");

            migrationBuilder.DropTable(
                name: "OrderInventoryIssueActions");

            migrationBuilder.DropTable(
                name: "OrderInventoryIssueLineAllocations");

            migrationBuilder.DropTable(
                name: "OrderLegalEntityAllocationReversals");

            migrationBuilder.DropTable(
                name: "OrderNumberSequences");

            migrationBuilder.DropTable(
                name: "OrderPayments");

            migrationBuilder.DropTable(
                name: "OrderRewardVouchers");

            migrationBuilder.DropTable(
                name: "POSAuditLogs");

            migrationBuilder.DropTable(
                name: "PosPaymentQrRequests");

            migrationBuilder.DropTable(
                name: "POSShiftCashDenominations");

            migrationBuilder.DropTable(
                name: "POSShiftCashTransactions");

            migrationBuilder.DropTable(
                name: "POSShiftClosingSlipDenominations");

            migrationBuilder.DropTable(
                name: "POSShiftHandoverSlipDenominations");

            migrationBuilder.DropTable(
                name: "POSTerminalDevices");

            migrationBuilder.DropTable(
                name: "ProductBarcodeVerificationRequests");

            migrationBuilder.DropTable(
                name: "ProductVariantAttributeValue");

            migrationBuilder.DropTable(
                name: "ProductVariantBarcodeHistory");

            migrationBuilder.DropTable(
                name: "PromotionComboRule");

            migrationBuilder.DropTable(
                name: "PromotionItems");

            migrationBuilder.DropTable(
                name: "PurchaseOrderActions");

            migrationBuilder.DropTable(
                name: "PurchasePayables");

            migrationBuilder.DropTable(
                name: "PurchaseRequestActions");

            migrationBuilder.DropTable(
                name: "RewardSettings");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "SalesReturnPayments");

            migrationBuilder.DropTable(
                name: "StockCountLine");

            migrationBuilder.DropTable(
                name: "StockDocumentInputInvoiceMap");

            migrationBuilder.DropTable(
                name: "StockDocumentLineInputInvoiceMap");

            migrationBuilder.DropTable(
                name: "StockTransferLine");

            migrationBuilder.DropTable(
                name: "UserInStores");

            migrationBuilder.DropTable(
                name: "InventoryAdjustmentDocuments");

            migrationBuilder.DropTable(
                name: "InvoiceHeads");

            migrationBuilder.DropTable(
                name: "InventoryCostLayerAllocations");

            migrationBuilder.DropTable(
                name: "OrderInventoryIssueLines");

            migrationBuilder.DropTable(
                name: "OrderLegalEntityAllocations");

            migrationBuilder.DropTable(
                name: "SalesReturnLines");

            migrationBuilder.DropTable(
                name: "CustomerRewardVouchers");

            migrationBuilder.DropTable(
                name: "StoreBankAccounts");

            migrationBuilder.DropTable(
                name: "POSShiftClosingSlips");

            migrationBuilder.DropTable(
                name: "POSShiftHandoverSlips");

            migrationBuilder.DropTable(
                name: "AttributeValue");

            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "StockCountDocument");

            migrationBuilder.DropTable(
                name: "InputInvoiceDetail");

            migrationBuilder.DropTable(
                name: "StockDocumentLine");

            migrationBuilder.DropTable(
                name: "StockTransferDocument");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "OrderInventoryIssues");

            migrationBuilder.DropTable(
                name: "ProductVariantUnitBarcode");

            migrationBuilder.DropTable(
                name: "OrderLines");

            migrationBuilder.DropTable(
                name: "SalesReturns");

            migrationBuilder.DropTable(
                name: "Attribute");

            migrationBuilder.DropTable(
                name: "InputInvoiceHead");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "StockDocument");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "PurchaseRequestLines");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "ProductUnitConversion");

            migrationBuilder.DropTable(
                name: "PurchaseRequests");

            migrationBuilder.DropTable(
                name: "Stores");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "POSShifts");

            migrationBuilder.DropTable(
                name: "POSTerminals");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropTable(
                name: "LegalEntities");

            migrationBuilder.DropTable(
                name: "InvoiceProviderSettings");

            migrationBuilder.DropTable(
                name: "ProductVariant");

            migrationBuilder.DropTable(
                name: "ProductImages");

            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Brands");

            migrationBuilder.DropTable(
                name: "Category");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "Taxes");

            migrationBuilder.DropTable(
                name: "Unit");

            migrationBuilder.DropTable(
                name: "InventoryCostLayers");

            migrationBuilder.DropTable(
                name: "InventoryValuationEntries");

            migrationBuilder.DropTable(
                name: "InventoryTransactions");
        }
    }
}
