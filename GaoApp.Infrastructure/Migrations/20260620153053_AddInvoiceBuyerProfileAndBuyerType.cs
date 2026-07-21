using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceBuyerProfileAndBuyerType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "BuyerName",
                table: "InvoiceHeads",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(250)",
                oldMaxLength: 250,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerAddress",
                table: "InvoiceHeads",
                type: "nvarchar(1200)",
                maxLength: 1200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerEmail",
                table: "InvoiceHeads",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerLegalName",
                table: "InvoiceHeads",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerPhone",
                table: "InvoiceHeads",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerType",
                table: "InvoiceHeads",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NoInvoice");

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

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_BuyerTaxCode_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "BuyerTaxCode", "IsDeleted" });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceBuyerProfiles");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_BuyerTaxCode_IsDeleted",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "BuyerEmail",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "BuyerLegalName",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "BuyerPhone",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "BuyerType",
                table: "InvoiceHeads");

            migrationBuilder.AlterColumn<string>(
                name: "BuyerName",
                table: "InvoiceHeads",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerAddress",
                table: "InvoiceHeads",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1200)",
                oldMaxLength: 1200,
                oldNullable: true);
        }
    }
}
