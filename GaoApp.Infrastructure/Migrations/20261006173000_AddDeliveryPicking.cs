using System;
using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryPicking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrderLines_StoreId_DeliveryOrderId_SourceOrderLineId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrderLines_StoreId_VariantId",
                table: "DeliveryOrderLines");

            migrationBuilder.AlterColumn<int>(
                name: "SourceOrderLineId",
                table: "DeliveryOrderLines",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "BaseUnitId",
                table: "DeliveryOrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalRootLineId",
                table: "DeliveryOrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductUnitConversionId",
                table: "DeliveryOrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SellingUnitId",
                table: "DeliveryOrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Unit_StoreId_Id",
                table: "Unit",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProductUnitConversion_StoreId_ProductVariantId_Id_UnitId",
                table: "ProductUnitConversion",
                columns: new[] { "StoreId", "ProductVariantId", "Id", "UnitId" });

            migrationBuilder.CreateTable(
                name: "DeliveryPickingWorks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: false),
                    PickerUserId = table.Column<int>(type: "int", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovalRequired = table.Column<bool>(type: "bit", nullable: false),
                    ApprovedRevision = table.Column<int>(type: "int", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CustomerConfirmationNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_DeliveryPickingWorks", x => x.Id);
                    table.UniqueConstraint("AK_DeliveryPickingWorks_StoreId_DeliveryOrderId", x => new { x.StoreId, x.DeliveryOrderId });
                    table.CheckConstraint("CK_DeliveryPickingWorks_Approval", "([ApprovedRevision] IS NULL AND [ApprovedByUserId] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovedTotal] IS NULL) OR ([ApprovedRevision] IS NOT NULL AND [ApprovedRevision]>0 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [ApprovedTotal] IS NOT NULL AND [ApprovedTotal]>0 AND [ApprovedTotal]=ROUND([ApprovedTotal],0))");
                    table.CheckConstraint("CK_DeliveryPickingWorks_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_DeliveryPickingWorks_DeliveryOrders_StoreId_DeliveryOrderId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId },
                        principalTable: "DeliveryOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryPickingWorks_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DeliveryPickingWorks_UserInStores_StoreId_ApprovedByUserId",
                        columns: x => new { x.StoreId, x.ApprovedByUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryPickingWorks_UserInStores_StoreId_PickerUserId",
                        columns: x => new { x.StoreId, x.PickerUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryPickingLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: false),
                    DeliveryOrderLineId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PlannedOriginalCoverage = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReportedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ReporterUserId = table.Column<int>(type: "int", nullable: true),
                    ReportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ShortageReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReportFactKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ApprovedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ApprovedOriginalCoverage = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ApprovedNet = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
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
                    table.PrimaryKey("PK_DeliveryPickingLines", x => x.Id);
                    table.CheckConstraint("CK_DeliveryPickingLines_Approval", "([ApprovedQuantity] IS NULL AND [ApprovedOriginalCoverage] IS NULL AND [ApprovedNet] IS NULL) OR ([ApprovedQuantity] IS NOT NULL AND [ApprovedOriginalCoverage] IS NOT NULL AND [ApprovedNet] IS NOT NULL AND [ApprovedNet]>=0 AND [ApprovedNet]=ROUND([ApprovedNet],0) AND ([ApprovedQuantity]>0 OR [ApprovedOriginalCoverage]=0 AND [ApprovedNet]=0))");
                    table.CheckConstraint("CK_DeliveryPickingLines_Inactive", "[IsActive]=1 OR [PlannedQuantity]=0 AND [PlannedOriginalCoverage]=0");
                    table.CheckConstraint("CK_DeliveryPickingLines_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_DeliveryPickingLines_Quantities", "[PlannedQuantity]>=0 AND [PlannedOriginalCoverage]>=0 AND ([ReportedQuantity] IS NULL OR [ReportedQuantity]>=0 AND [ReportedQuantity]<=[PlannedQuantity]) AND ([ApprovedQuantity] IS NULL OR [ReportedQuantity] IS NOT NULL AND [ApprovedQuantity]>=0 AND [ApprovedQuantity]<=[ReportedQuantity]) AND ([ApprovedOriginalCoverage] IS NULL OR [ApprovedOriginalCoverage]>=0 AND [ApprovedOriginalCoverage]<=[PlannedOriginalCoverage])");
                    table.CheckConstraint("CK_DeliveryPickingLines_Report", "([ReportedQuantity] IS NULL AND [ReporterUserId] IS NULL AND [ReportedAtUtc] IS NULL AND [ReportFactKind]='unreported') OR ([ReportedQuantity] IS NOT NULL AND [ReporterUserId] IS NOT NULL AND [ReportedAtUtc] IS NOT NULL AND ([ReportFactKind]='picker-report' OR [ReportFactKind]='plan-removal' AND [ReportedQuantity]=0) AND ([ReportedQuantity]>0 AND [ReportedQuantity]=[PlannedQuantity] OR NULLIF(LTRIM(RTRIM([ShortageReason])), '') IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_DeliveryPickingLines_DeliveryOrderLines_StoreId_DeliveryOrderId_DeliveryOrderLineId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId },
                        principalTable: "DeliveryOrderLines",
                        principalColumns: new[] { "StoreId", "DeliveryOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryPickingLines_DeliveryOrders_StoreId_DeliveryOrderId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId },
                        principalTable: "DeliveryOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryPickingLines_DeliveryPickingWorks_StoreId_DeliveryOrderId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId },
                        principalTable: "DeliveryPickingWorks",
                        principalColumns: new[] { "StoreId", "DeliveryOrderId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryPickingLines_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DeliveryPickingLines_UserInStores_StoreId_ReporterUserId",
                        columns: x => new { x.StoreId, x.ReporterUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_StoreId_BaseUnitId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "BaseUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_StoreId_DeliveryOrderId_OriginalRootLineId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "DeliveryOrderId", "OriginalRootLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_StoreId_DeliveryOrderId_SourceOrderLineId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "DeliveryOrderId", "SourceOrderLineId" },
                unique: true,
                filter: "[SourceOrderLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_StoreId_SellingUnitId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "SellingUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_StoreId_VariantId_ProductUnitConversionId_SellingUnitId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "VariantId", "ProductUnitConversionId", "SellingUnitId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryOrderLines_Identity",
                table: "DeliveryOrderLines",
                sql: "([SourceOrderLineId] IS NOT NULL AND [OriginalRootLineId] IS NULL AND [ProductUnitConversionId] IS NULL AND [SellingUnitId] IS NULL AND [BaseUnitId] IS NULL) OR ([SourceOrderLineId] IS NULL AND [OriginalRootLineId] IS NOT NULL AND [SellingUnitId] IS NOT NULL AND [BaseUnitId] IS NOT NULL AND [LineDiscount]=0 AND [AllocatedOrderDiscount]=0)");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryPickingLines_StoreId_DeliveryOrderId_DeliveryOrderLineId",
                table: "DeliveryPickingLines",
                columns: new[] { "StoreId", "DeliveryOrderId", "DeliveryOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryPickingLines_StoreId_ReporterUserId",
                table: "DeliveryPickingLines",
                columns: new[] { "StoreId", "ReporterUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryPickingWorks_StoreId_ApprovedByUserId",
                table: "DeliveryPickingWorks",
                columns: new[] { "StoreId", "ApprovedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryPickingWorks_StoreId_PickerUserId",
                table: "DeliveryPickingWorks",
                columns: new[] { "StoreId", "PickerUserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrderLines_DeliveryOrderLines_StoreId_DeliveryOrderId_OriginalRootLineId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "DeliveryOrderId", "OriginalRootLineId" },
                principalTable: "DeliveryOrderLines",
                principalColumns: new[] { "StoreId", "DeliveryOrderId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrderLines_ProductUnitConversion_StoreId_VariantId_ProductUnitConversionId_SellingUnitId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "VariantId", "ProductUnitConversionId", "SellingUnitId" },
                principalTable: "ProductUnitConversion",
                principalColumns: new[] { "StoreId", "ProductVariantId", "Id", "UnitId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrderLines_Unit_StoreId_BaseUnitId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "BaseUnitId" },
                principalTable: "Unit",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrderLines_Unit_StoreId_SellingUnitId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "SellingUnitId" },
                principalTable: "Unit",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(DeliverySchemaSqlCatalog.PickingQuoteImmutableSql);
            migrationBuilder.Sql(DeliverySchemaSqlCatalog.PickingRootSql);
            migrationBuilder.Sql(DeliverySchemaSqlCatalog.PickingBoundsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM DeliveryPickingWorks) OR EXISTS (SELECT 1 FROM DeliveryPickingLines) OR EXISTS (SELECT 1 FROM DeliveryOrderLines WHERE SourceOrderLineId IS NULL) OR EXISTS (SELECT 1 FROM DeliveryRevisions WHERE Action LIKE 'picking-%') THROW 51009, 'Picking history requires a forward fix; downgrade refused.', 1;");
            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryPickingLines_Bounds];");
            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryOrderLines_Root];");
            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryOrderLines_Immutable];");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrderLines_DeliveryOrderLines_StoreId_DeliveryOrderId_OriginalRootLineId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrderLines_ProductUnitConversion_StoreId_VariantId_ProductUnitConversionId_SellingUnitId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrderLines_Unit_StoreId_BaseUnitId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrderLines_Unit_StoreId_SellingUnitId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropTable(
                name: "DeliveryPickingLines");

            migrationBuilder.DropTable(
                name: "DeliveryPickingWorks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Unit_StoreId_Id",
                table: "Unit");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProductUnitConversion_StoreId_ProductVariantId_Id_UnitId",
                table: "ProductUnitConversion");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrderLines_StoreId_BaseUnitId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrderLines_StoreId_DeliveryOrderId_OriginalRootLineId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrderLines_StoreId_DeliveryOrderId_SourceOrderLineId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrderLines_StoreId_SellingUnitId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrderLines_StoreId_VariantId_ProductUnitConversionId_SellingUnitId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryOrderLines_Identity",
                table: "DeliveryOrderLines");

            migrationBuilder.DropColumn(
                name: "BaseUnitId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropColumn(
                name: "OriginalRootLineId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropColumn(
                name: "ProductUnitConversionId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropColumn(
                name: "SellingUnitId",
                table: "DeliveryOrderLines");

            migrationBuilder.AlterColumn<int>(
                name: "SourceOrderLineId",
                table: "DeliveryOrderLines",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_StoreId_DeliveryOrderId_SourceOrderLineId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "DeliveryOrderId", "SourceOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_StoreId_VariantId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "VariantId" });
        }
    }
}
