using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class editbarcodehistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantBarcodeHistory_ProductVariant_ProductVariantId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId",
                table: "ProductVariantUnitBarcode");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantUnitBarcode_ProductUnitConversionId",
                table: "ProductVariantUnitBarcode");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantUnitBarcode_StoreId_Barcode",
                table: "ProductVariantUnitBarcode");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId_IsPrimary",
                table: "ProductVariantUnitBarcode");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantBarcodeHistory_OldBarcode",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantBarcodeHistory_ProductVariantId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantBarcodeHistory_StoreId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.RenameIndex(
                name: "IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId",
                table: "ProductVariantUnitBarcode",
                newName: "IX_ProductVariantUnitBarcode_Store_Conversion");

            migrationBuilder.RenameColumn(
                name: "ChangedBy",
                table: "ProductVariantBarcodeHistory",
                newName: "OldBarcodeId");

            migrationBuilder.AlterColumn<string>(
                name: "OldBarcode",
                table: "ProductVariantBarcodeHistory",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "NewBarcode",
                table: "ProductVariantBarcodeHistory",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<int>(
                name: "ActionType",
                table: "ProductVariantBarcodeHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ChangedByUserId",
                table: "ProductVariantBarcodeHistory",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangedByUserName",
                table: "ProductVariantBarcodeHistory",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NewBarcodeId",
                table: "ProductVariantBarcodeHistory",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductUnitConversionId",
                table: "ProductVariantBarcodeHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "ProductVariantBarcodeHistory",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantUnitBarcode_Conversion_Active_Primary",
                table: "ProductVariantUnitBarcode",
                columns: new[] { "ProductUnitConversionId", "IsActive", "IsPrimary" });

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

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantBarcodeHistory_ProductUnitConversion_ProductUnitConversionId",
                table: "ProductVariantBarcodeHistory",
                column: "ProductUnitConversionId",
                principalTable: "ProductUnitConversion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantBarcodeHistory_ProductVariantUnitBarcode_NewBarcodeId",
                table: "ProductVariantBarcodeHistory",
                column: "NewBarcodeId",
                principalTable: "ProductVariantUnitBarcode",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantBarcodeHistory_ProductVariantUnitBarcode_OldBarcodeId",
                table: "ProductVariantBarcodeHistory",
                column: "OldBarcodeId",
                principalTable: "ProductVariantUnitBarcode",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantBarcodeHistory_ProductVariant_ProductVariantId",
                table: "ProductVariantBarcodeHistory",
                column: "ProductVariantId",
                principalTable: "ProductVariant",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId",
                table: "ProductVariantUnitBarcode",
                column: "ProductUnitConversionId",
                principalTable: "ProductUnitConversion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantBarcodeHistory_ProductUnitConversion_ProductUnitConversionId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantBarcodeHistory_ProductVariantUnitBarcode_NewBarcodeId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantBarcodeHistory_ProductVariantUnitBarcode_OldBarcodeId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantBarcodeHistory_ProductVariant_ProductVariantId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId",
                table: "ProductVariantUnitBarcode");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantUnitBarcode_Conversion_Active_Primary",
                table: "ProductVariantUnitBarcode");

            migrationBuilder.DropIndex(
                name: "UX_ProductVariantUnitBarcode_Conversion_Primary_Active",
                table: "ProductVariantUnitBarcode");

            migrationBuilder.DropIndex(
                name: "UX_ProductVariantUnitBarcode_Store_Barcode_Active",
                table: "ProductVariantUnitBarcode");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantBarcodeHistory_Conversion_ChangedAtUtc",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantBarcodeHistory_NewBarcodeId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantBarcodeHistory_OldBarcodeId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantBarcodeHistory_Store_NewBarcode",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantBarcodeHistory_Store_OldBarcode",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantBarcodeHistory_Variant_ChangedAtUtc",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropColumn(
                name: "ActionType",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropColumn(
                name: "ChangedByUserId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropColumn(
                name: "ChangedByUserName",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropColumn(
                name: "NewBarcodeId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropColumn(
                name: "ProductUnitConversionId",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "ProductVariantBarcodeHistory");

            migrationBuilder.RenameIndex(
                name: "IX_ProductVariantUnitBarcode_Store_Conversion",
                table: "ProductVariantUnitBarcode",
                newName: "IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId");

            migrationBuilder.RenameColumn(
                name: "OldBarcodeId",
                table: "ProductVariantBarcodeHistory",
                newName: "ChangedBy");

            migrationBuilder.AlterColumn<string>(
                name: "OldBarcode",
                table: "ProductVariantBarcodeHistory",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NewBarcode",
                table: "ProductVariantBarcodeHistory",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantUnitBarcode_ProductUnitConversionId",
                table: "ProductVariantUnitBarcode",
                column: "ProductUnitConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantUnitBarcode_StoreId_Barcode",
                table: "ProductVariantUnitBarcode",
                columns: new[] { "StoreId", "Barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantUnitBarcode_StoreId_ProductUnitConversionId_IsPrimary",
                table: "ProductVariantUnitBarcode",
                columns: new[] { "StoreId", "ProductUnitConversionId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantBarcodeHistory_OldBarcode",
                table: "ProductVariantBarcodeHistory",
                column: "OldBarcode");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantBarcodeHistory_ProductVariantId",
                table: "ProductVariantBarcodeHistory",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantBarcodeHistory_StoreId",
                table: "ProductVariantBarcodeHistory",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantBarcodeHistory_ProductVariant_ProductVariantId",
                table: "ProductVariantBarcodeHistory",
                column: "ProductVariantId",
                principalTable: "ProductVariant",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantUnitBarcode_ProductUnitConversion_ProductUnitConversionId",
                table: "ProductVariantUnitBarcode",
                column: "ProductUnitConversionId",
                principalTable: "ProductUnitConversion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
