using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Fix_ProductImages_Primary_FilteredIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop index primary per product nếu đã tồn tại (dù đúng hay sai)
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_ProductImages_Primary_PerProduct'
      AND object_id = OBJECT_ID('dbo.ProductImages')
)
BEGIN
    DROP INDEX [UX_ProductImages_Primary_PerProduct] ON [dbo].[ProductImages];
END
");

            // Create lại UNIQUE FILTERED: chỉ unique khi IsPrimary = 1
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX [UX_ProductImages_Primary_PerProduct]
ON [dbo].[ProductImages]([StoreId],[ProductId])
WHERE [IsPrimary] = 1;
");
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_ProductImages_Primary_PerProduct'
      AND object_id = OBJECT_ID('dbo.ProductImages')
)
BEGIN
    DROP INDEX [UX_ProductImages_Primary_PerProduct] ON [dbo].[ProductImages];
END
");

            // (Tuỳ bạn) Nếu muốn rollback về unique trần (mình KHÔNG khuyến nghị),
            // bạn có thể tạo lại index unique trần ở đây. Thường thì bỏ trống là ok.
        }

    }
}
