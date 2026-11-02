using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

// Adds only the role-selectable permission catalog entry. It grants no role.
public sealed partial class AddCustomerInvoiceNotificationsPermission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF NOT EXISTS (
    SELECT 1
    FROM dbo.Permissions
    WHERE Code = N'customer.service.invoice-notifications'
)
BEGIN
    INSERT INTO dbo.Permissions (Code, Name, GroupName)
    VALUES (
        N'customer.service.invoice-notifications',
        N'Quản lý thông báo hóa đơn CSKH',
        N'Customers'
    );
END;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DELETE permission
FROM dbo.Permissions AS permission
WHERE permission.Code = N'customer.service.invoice-notifications'
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.RolePermissions AS rolePermission
      WHERE rolePermission.PermissionId = permission.Id
  );
""");
    }
}