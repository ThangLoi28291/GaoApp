using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260930100000_AddReceiptInvoiceFollowUp")]
public sealed class AddReceiptInvoiceFollowUp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<bool>(name: "WaitForInputInvoice", table: "StockDocument", type: "bit", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "WaitForInputInvoice", table: "StockDocument");
}
