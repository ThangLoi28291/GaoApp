using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261030150000_AddCustomerServicePermissionCatalog")]
public sealed class AddCustomerServicePermissionCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DECLARE @Catalog TABLE (Code nvarchar(150) PRIMARY KEY, Name nvarchar(200), GroupName nvarchar(100));
INSERT @Catalog (Code, Name, GroupName) VALUES
    (N'customer.service.view', N'Xem hộp thư chăm sóc khách hàng', N'Customer Service'),
    (N'customer.service.reply', N'Tiếp nhận và trả lời khách', N'Customer Service'),
    (N'customer.service.manage', N'Cấu hình và điều phối chăm sóc khách hàng', N'Customer Service'),
    (N'customer.service.verify', N'Xác minh khách và cấp quyền tra cứu CSKH', N'Customer Service'),
    (N'customer.service.invoice-notifications', N'Quản lý thông báo hóa đơn CSKH', N'Customers'),
    (N'customer.service.marketing-consent', N'Quản lý đồng ý nhận quảng cáo Zalo', N'Customers'),
    (N'assistant.use', N'Sử dụng trợ lý', N'Assistant'),
    (N'assistant.chat', N'Hội thoại với trợ lý', N'Assistant'),
    (N'assistant.receiving.resolve', N'Đề xuất nhập hàng bằng trợ lý', N'Assistant'),
    (N'assistant.products.normalize_name', N'Đề xuất tên sản phẩm', N'Assistant'),
    (N'quotes.retail.read', N'Tra giá lẻ qua trợ lý', N'Assistant'),
    (N'quotes.wholesale.read', N'Tra giá sỉ qua trợ lý', N'Assistant');
INSERT dbo.Permissions (Code, Name, GroupName)
SELECT source.Code, source.Name, source.GroupName
FROM @Catalog AS source
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions AS target WITH (UPDLOCK, HOLDLOCK) WHERE target.Code = source.Code);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Catalog entries and any user-assigned role grants survive a downgrade.
    }
}

