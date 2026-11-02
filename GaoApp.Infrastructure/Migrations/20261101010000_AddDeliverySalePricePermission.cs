using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261101010000_AddDeliverySalePricePermission")]
public sealed class AddDeliverySalePricePermission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
INSERT dbo.Permissions (Code, Name, GroupName)
SELECT N'delivery.order.edit-sale-price', N'Sửa giá bán đơn giao chưa chốt tiền', N'Delivery'
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Permissions WITH (UPDLOCK, HOLDLOCK)
    WHERE Code = N'delivery.order.edit-sale-price'
);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Preserve the catalog entry and any role grants chosen by the administrator.
    }
}
