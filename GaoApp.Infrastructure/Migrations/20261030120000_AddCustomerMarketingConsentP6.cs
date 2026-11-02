using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerMarketingConsentP6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerMarketingConsentEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsGranted = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_CustomerMarketingConsentEvents", x => x.Id);
                    table.CheckConstraint("CK_CustomerMarketingConsentEvents_Values", "[StoreId] > 0 AND [CustomerId] > 0 AND [Channel] = N'Zalo' AND ([RecordedByUserId] IS NULL OR [RecordedByUserId] > 0) AND LEN([Source]) > 0");
                    table.ForeignKey(
                        name: "FK_CustomerMarketingConsentEvents_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CustomerMarketingConsents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsGranted = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedByUserId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_CustomerMarketingConsents", x => x.Id);
                    table.CheckConstraint("CK_CustomerMarketingConsents_Values", "[StoreId] > 0 AND [CustomerId] > 0 AND [Channel] = N'Zalo' AND ([ChangedByUserId] IS NULL OR [ChangedByUserId] > 0) AND LEN([Source]) > 0");
                    table.ForeignKey(
                        name: "FK_CustomerMarketingConsents_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerMarketingConsents_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMarketingConsentEvents_CustomerId",
                table: "CustomerMarketingConsentEvents",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMarketingConsentEvents_StoreId_CustomerId_Channel_RecordedAtUtc",
                table: "CustomerMarketingConsentEvents",
                columns: new[] { "StoreId", "CustomerId", "Channel", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMarketingConsentEvents_StoreId_Channel_EvidenceReference",
                table: "CustomerMarketingConsentEvents",
                columns: new[] { "StoreId", "Channel", "EvidenceReference" },
                unique: true,
                filter: "[Source] = N'ZaloKeyword' AND [EvidenceReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMarketingConsents_CustomerId",
                table: "CustomerMarketingConsents",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMarketingConsents_StoreId_CustomerId_Channel",
                table: "CustomerMarketingConsents",
                columns: new[] { "StoreId", "CustomerId", "Channel" },
                unique: true);

            migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'customer.service.marketing-consent')
    INSERT INTO dbo.Permissions (Code, Name, GroupName)
    VALUES (N'customer.service.marketing-consent', N'Quản lý đồng ý nhận quảng cáo Zalo', N'Customers');
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
IF EXISTS (SELECT 1 FROM dbo.CustomerMarketingConsents)
   OR EXISTS (SELECT 1 FROM dbo.CustomerMarketingConsentEvents)
    THROW 51000, 'Cannot remove marketing consent while consent or opt-out evidence exists.', 1;
""");
            migrationBuilder.Sql("""
DELETE permission
FROM dbo.Permissions AS permission
WHERE permission.Code = N'customer.service.marketing-consent'
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions AS rolePermission WHERE rolePermission.PermissionId = permission.Id);
""");
            migrationBuilder.DropTable(
                name: "CustomerMarketingConsentEvents");

            migrationBuilder.DropTable(
                name: "CustomerMarketingConsents");
        }
    }
}
