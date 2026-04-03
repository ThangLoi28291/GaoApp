using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Product_ProductVariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariant_Product_ProductId",
                table: "ProductVariant");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantAttributeValue_AttributeValue_AttributeValueId",
                table: "ProductVariantAttributeValue");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantAttributeValue_Attribute_AttributeId",
                table: "ProductVariantAttributeValue");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantAttributeValue_ProductVariant_VariantId",
                table: "ProductVariantAttributeValue");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantAttributeValue_StoreId",
                table: "ProductVariantAttributeValue");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantAttributeValue_VariantId_AttributeId",
                table: "ProductVariantAttributeValue");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantAttributeValue_VariantId_AttributeValueId",
                table: "ProductVariantAttributeValue");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_StoreId_Barcode",
                table: "ProductVariant");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_StoreId_Code",
                table: "ProductVariant");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "ProductVariant");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "ProductVariant");

            migrationBuilder.AlterColumn<string>(
                name: "Barcode",
                table: "ProductVariant",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                table: "ProductVariant",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Alias",
                table: "Product",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(250)",
                oldMaxLength: 250);

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
                name: "IX_ProductVariant_StoreId_Barcode",
                table: "ProductVariant",
                columns: new[] { "StoreId", "Barcode" },
                unique: true,
                filter: "[Barcode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_StoreId_Sku",
                table: "ProductVariant",
                columns: new[] { "StoreId", "Sku" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariant_Product_ProductId",
                table: "ProductVariant",
                column: "ProductId",
                principalTable: "Product",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantAttributeValue_AttributeValue_AttributeValueId",
                table: "ProductVariantAttributeValue",
                column: "AttributeValueId",
                principalTable: "AttributeValue",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantAttributeValue_Attribute_AttributeId",
                table: "ProductVariantAttributeValue",
                column: "AttributeId",
                principalTable: "Attribute",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantAttributeValue_ProductVariant_VariantId",
                table: "ProductVariantAttributeValue",
                column: "VariantId",
                principalTable: "ProductVariant",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariant_Product_ProductId",
                table: "ProductVariant");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantAttributeValue_AttributeValue_AttributeValueId",
                table: "ProductVariantAttributeValue");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantAttributeValue_Attribute_AttributeId",
                table: "ProductVariantAttributeValue");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantAttributeValue_ProductVariant_VariantId",
                table: "ProductVariantAttributeValue");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantAttributeValue_StoreId_VariantId_AttributeId",
                table: "ProductVariantAttributeValue");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantAttributeValue_VariantId",
                table: "ProductVariantAttributeValue");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_StoreId_Barcode",
                table: "ProductVariant");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_StoreId_Sku",
                table: "ProductVariant");

            migrationBuilder.DropColumn(
                name: "Sku",
                table: "ProductVariant");

            migrationBuilder.AlterColumn<string>(
                name: "Barcode",
                table: "ProductVariant",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "ProductVariant",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "ProductVariant",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Alias",
                table: "Product",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributeValue_StoreId",
                table: "ProductVariantAttributeValue",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributeValue_VariantId_AttributeId",
                table: "ProductVariantAttributeValue",
                columns: new[] { "VariantId", "AttributeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributeValue_VariantId_AttributeValueId",
                table: "ProductVariantAttributeValue",
                columns: new[] { "VariantId", "AttributeValueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_StoreId_Barcode",
                table: "ProductVariant",
                columns: new[] { "StoreId", "Barcode" },
                unique: true,
                filter: "[Barcode] IS NOT NULL AND [Barcode] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_StoreId_Code",
                table: "ProductVariant",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariant_Product_ProductId",
                table: "ProductVariant",
                column: "ProductId",
                principalTable: "Product",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantAttributeValue_AttributeValue_AttributeValueId",
                table: "ProductVariantAttributeValue",
                column: "AttributeValueId",
                principalTable: "AttributeValue",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantAttributeValue_Attribute_AttributeId",
                table: "ProductVariantAttributeValue",
                column: "AttributeId",
                principalTable: "Attribute",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantAttributeValue_ProductVariant_VariantId",
                table: "ProductVariantAttributeValue",
                column: "VariantId",
                principalTable: "ProductVariant",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
