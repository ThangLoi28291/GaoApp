using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

public partial class AddStoreReceiptIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "ReceiptName", table: "Stores", type: "nvarchar(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ReceiptAddress", table: "Stores", type: "nvarchar(300)", maxLength: 300, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ReceiptPhone", table: "Stores", type: "nvarchar(50)", maxLength: 50, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ReceiptName", table: "Stores");
        migrationBuilder.DropColumn(name: "ReceiptAddress", table: "Stores");
        migrationBuilder.DropColumn(name: "ReceiptPhone", table: "Stores");
    }
}
