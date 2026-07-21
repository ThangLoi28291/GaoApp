using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase227LegalEntityInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId",
                table: "InvoiceHeads");

            migrationBuilder.AddColumn<int>(
                name: "InvoiceProviderSettingId",
                table: "InvoiceHeads",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegalEntityId",
                table: "InvoiceHeads",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderLegalEntityAllocationId",
                table: "InvoiceDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrderLegalEntityAllocations_StoreId_Id",
                table: "OrderLegalEntityAllocations",
                columns: new[] { "StoreId", "Id" });

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
                name: "IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLegalEntityAllocationId",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "InvoiceHeadId", "OrderLegalEntityAllocationId" },
                unique: true,
                filter: "[OrderLegalEntityAllocationId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_OrderLegalEntityAllocationId",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "OrderLegalEntityAllocationId" });

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceDetails_OrderLegalEntityAllocations_StoreId_OrderLegalEntityAllocationId",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "OrderLegalEntityAllocationId" },
                principalTable: "OrderLegalEntityAllocations",
                principalColumns: new[] { "StoreId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceHeads_InvoiceProviderSettings_StoreId_InvoiceProviderSettingId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "InvoiceProviderSettingId" },
                principalTable: "InvoiceProviderSettings",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceHeads_LegalEntities_StoreId_LegalEntityId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "LegalEntityId" },
                principalTable: "LegalEntities",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceDetails_OrderLegalEntityAllocations_StoreId_OrderLegalEntityAllocationId",
                table: "InvoiceDetails");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceHeads_InvoiceProviderSettings_StoreId_InvoiceProviderSettingId",
                table: "InvoiceHeads");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceHeads_LegalEntities_StoreId_LegalEntityId",
                table: "InvoiceHeads");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrderLegalEntityAllocations_StoreId_Id",
                table: "OrderLegalEntityAllocations");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_InvoiceProviderSettingId_IsDeleted",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_LegalEntityId_InvoiceDate_IsDeleted",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId_LegalEntityId",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLegalEntityAllocationId",
                table: "InvoiceDetails");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceDetails_StoreId_OrderLegalEntityAllocationId",
                table: "InvoiceDetails");

            migrationBuilder.DropColumn(
                name: "InvoiceProviderSettingId",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "OrderLegalEntityAllocationId",
                table: "InvoiceDetails");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "OrderId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL");
        }
    }
}
