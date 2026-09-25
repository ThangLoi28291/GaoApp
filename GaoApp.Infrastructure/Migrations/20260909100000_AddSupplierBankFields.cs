using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

public partial class AddSupplierBankFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "BankAccountNumber", table: "Suppliers",
            type: "nvarchar(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>(name: "BankAccountName", table: "Suppliers",
            type: "nvarchar(250)", maxLength: 250, nullable: true);
        migrationBuilder.AddColumn<string>(name: "BankName", table: "Suppliers",
            type: "nvarchar(250)", maxLength: 250, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "BankAccountNumber", table: "Suppliers");
        migrationBuilder.DropColumn(name: "BankAccountName", table: "Suppliers");
        migrationBuilder.DropColumn(name: "BankName", table: "Suppliers");
    }
}
