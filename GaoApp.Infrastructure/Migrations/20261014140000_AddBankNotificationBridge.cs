using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261014140000_AddBankNotificationBridge")]
public sealed class AddBankNotificationBridge : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var sql in BankNotificationSchemaV1.CreateSql()) migrationBuilder.Sql(sql);
        migrationBuilder.AddColumn<int>("NotificationReceiptId", "PosPaymentQrRequests", type: "int", nullable: true);
        migrationBuilder.AddColumn<string>("NotificationConfirmationSource", "PosPaymentQrRequests", type: "nvarchar(40)", maxLength: 40, nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("NotificationReceiptId", "PosPaymentQrRequests");
        migrationBuilder.DropColumn("NotificationConfirmationSource", "PosPaymentQrRequests");
        foreach (var table in new[] { "BankNotificationDismissals", "BankNotificationReceipts", "BankNotificationWaits", "BankNotificationDevices" }) migrationBuilder.DropTable(table);
    }
}
