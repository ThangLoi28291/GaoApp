using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260923140000_AddInvoiceStockLegacyDocumentReferences")]
public sealed class AddInvoiceStockLegacyDocumentReferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "LegacyOrderId", table: "InvoiceInputStockSupplementalMovements", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<string>(name: "LegacyInvoiceNumber", table: "InvoiceInputStockSupplementalMovements", type: "nvarchar(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>(name: "LegacyInvoiceSymbol", table: "InvoiceInputStockSupplementalMovements", type: "nvarchar(200)", maxLength: 200, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "LegacyOrderId", table: "InvoiceInputStockSupplementalMovements");
        migrationBuilder.DropColumn(name: "LegacyInvoiceNumber", table: "InvoiceInputStockSupplementalMovements");
        migrationBuilder.DropColumn(name: "LegacyInvoiceSymbol", table: "InvoiceInputStockSupplementalMovements");
    }
}
