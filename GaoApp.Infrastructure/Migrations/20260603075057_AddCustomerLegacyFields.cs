using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerLegacyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Customers",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerGroup",
                table: "Customers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "MEMBER");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Customers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HaveDebt",
                table: "Customers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ImportedRewardAmount",
                table: "Customers",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsImportedFromOldSystem",
                table: "Customers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "OldCustomerId",
                table: "Customers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxCode",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_StoreId_Code",
                table: "Customers",
                columns: new[] { "StoreId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_StoreId_OldCustomerId",
                table: "Customers",
                columns: new[] { "StoreId", "OldCustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_StoreId_TaxCode",
                table: "Customers",
                columns: new[] { "StoreId", "TaxCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_StoreId_Code",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_StoreId_OldCustomerId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_StoreId_TaxCode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CustomerGroup",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "HaveDebt",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "ImportedRewardAmount",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IsImportedFromOldSystem",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OldCustomerId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TaxCode",
                table: "Customers");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Customers",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);
        }
    }
}
