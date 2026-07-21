using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase221LegalEntityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LegalEntityId",
                table: "Warehouses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMultiLegalEntityEnabled",
                table: "Stores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MultiLegalEntityActivatedAtUtc",
                table: "Stores",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Warehouses_StoreId_Id",
                table: "Warehouses",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InvoiceProviderSettings_StoreId_Id",
                table: "InvoiceProviderSettings",
                columns: new[] { "StoreId", "Id" });

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
                    table.ForeignKey(
                        name: "FK_LegalEntities_Warehouses_StoreId_DefaultWarehouseId",
                        columns: x => new { x.StoreId, x.DefaultWarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            // =========================================================
            // PHASE 22.1 - BACKFILL TƯƠNG THÍCH
            // =========================================================
            // Mỗi Store hiện hữu nhận đúng một LegalEntity PRIMARY.
            // Không bật feature đa HKD và không tạo allocation lịch sử.
            // Cấu hình Viettel chỉ được tự gắn khi Store có đúng một cấu hình
            // VIETTEL active; trường hợp mơ hồ được để null để admin xử lý.
            migrationBuilder.Sql(
                """
                INSERT INTO [LegalEntities]
                (
                    [Code],
                    [Name],
                    [LegalName],
                    [TaxCode],
                    [Address],
                    [Phone],
                    [Email],
                    [DefaultWarehouseId],
                    [InvoiceProviderSettingId],
                    [SalePriority],
                    [IsDefaultForPurchase],
                    [IsActive],
                    [Note],
                    [CreatedAtUtc],
                    [CreatedBy],
                    [UpdatedAtUtc],
                    [UpdatedBy],
                    [IsDeleted],
                    [DeletedAtUtc],
                    [DeletedBy],
                    [StoreId]
                )
                SELECT
                    N'PRIMARY',
                    s.[Name],
                    s.[Name],
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    providerSetting.[Id],
                    1,
                    CASE WHEN s.[IsDeleted] = 0 AND s.[IsActive] = 1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
                    CASE WHEN s.[IsDeleted] = 0 AND s.[IsActive] = 1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
                    N'Bootstrap tự động Phase 22.1 từ Store hiện hữu. Multi LegalEntity vẫn tắt.',
                    SYSUTCDATETIME(),
                    NULL,
                    NULL,
                    NULL,
                    s.[IsDeleted],
                    s.[DeletedAtUtc],
                    s.[DeletedBy],
                    s.[Id]
                FROM [Stores] s
                OUTER APPLY
                (
                    SELECT
                        CASE WHEN COUNT_BIG(*) = 1 THEN MAX(ips.[Id]) ELSE NULL END AS [Id]
                    FROM [InvoiceProviderSettings] ips
                    WHERE ips.[StoreId] = s.[Id]
                      AND ips.[IsDeleted] = 0
                      AND ips.[IsActive] = 1
                      AND UPPER(ips.[ProviderCode]) = N'VIETTEL'
                ) providerSetting
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM [LegalEntities] existing
                    WHERE existing.[StoreId] = s.[Id]
                );
                """);

            // Gắn toàn bộ kho cũ vào LegalEntity bootstrap cùng Store.
            migrationBuilder.Sql(
                """
                UPDATE w
                SET w.[LegalEntityId] = le.[Id]
                FROM [Warehouses] w
                INNER JOIN [LegalEntities] le
                    ON le.[StoreId] = w.[StoreId]
                   AND le.[Code] = N'PRIMARY';
                """);

            // Chọn kho mặc định hiện tại; nếu Store chưa đánh dấu kho mặc định
            // thì lấy kho active đầu tiên, cuối cùng mới fallback theo Id.
            migrationBuilder.Sql(
                """
                UPDATE le
                SET le.[DefaultWarehouseId] = selectedWarehouse.[Id]
                FROM [LegalEntities] le
                OUTER APPLY
                (
                    SELECT TOP (1) w.[Id]
                    FROM [Warehouses] w
                    WHERE w.[StoreId] = le.[StoreId]
                      AND w.[LegalEntityId] = le.[Id]
                    ORDER BY
                        w.[IsDeleted] ASC,
                        w.[IsDefault] DESC,
                        w.[IsActive] DESC,
                        w.[Id] ASC
                ) selectedWarehouse
                WHERE le.[Code] = N'PRIMARY';
                """);

            // Sau backfill, ownership kho là bắt buộc và không để default 0.
            migrationBuilder.AlterColumn<int>(
                name: "LegalEntityId",
                table: "Warehouses",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_StoreId_LegalEntityId",
                table: "Warehouses",
                columns: new[] { "StoreId", "LegalEntityId" });

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

            migrationBuilder.AddForeignKey(
                name: "FK_Warehouses_LegalEntities_StoreId_LegalEntityId",
                table: "Warehouses",
                columns: new[] { "StoreId", "LegalEntityId" },
                principalTable: "LegalEntities",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Warehouses_LegalEntities_StoreId_LegalEntityId",
                table: "Warehouses");

            migrationBuilder.DropTable(
                name: "LegalEntities");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Warehouses_StoreId_Id",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_StoreId_LegalEntityId",
                table: "Warehouses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InvoiceProviderSettings_StoreId_Id",
                table: "InvoiceProviderSettings");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "IsMultiLegalEntityEnabled",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "MultiLegalEntityActivatedAtUtc",
                table: "Stores");
        }
    }
}
