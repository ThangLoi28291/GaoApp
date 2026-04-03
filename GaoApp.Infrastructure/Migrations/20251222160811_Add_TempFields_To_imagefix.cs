using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_TempFields_To_imagefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Drop index unique sai (nếu có)
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductImages_StoreId_ProductId' AND object_id = OBJECT_ID('dbo.ProductImages'))
BEGIN
    DROP INDEX [IX_ProductImages_StoreId_ProductId] ON [dbo].[ProductImages];
END
");

            // 2) Create index sortOrder (nếu chưa có)
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductImages_StoreId_ProductId_SortOrder' AND object_id = OBJECT_ID('dbo.ProductImages'))
BEGIN
    CREATE INDEX [IX_ProductImages_StoreId_ProductId_SortOrder]
    ON [dbo].[ProductImages]([StoreId],[ProductId],[SortOrder]);
END
");

            // 3) Create index primary query (nếu chưa có)
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductImages_StoreId_ProductId_IsPrimary' AND object_id = OBJECT_ID('dbo.ProductImages'))
BEGIN
    CREATE INDEX [IX_ProductImages_StoreId_ProductId_IsPrimary]
    ON [dbo].[ProductImages]([StoreId],[ProductId],[IsPrimary]);
END
");

            // 4) Create unique filtered (1 primary / product) (nếu chưa có)
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ProductImages_Primary_PerProduct' AND object_id = OBJECT_ID('dbo.ProductImages'))
BEGIN
    CREATE UNIQUE INDEX [UX_ProductImages_Primary_PerProduct]
    ON [dbo].[ProductImages]([StoreId],[ProductId])
    WHERE [IsPrimary] = 1;
END
");
        }



        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ProductImages_Primary_PerProduct' AND object_id = OBJECT_ID('dbo.ProductImages'))
BEGIN
    DROP INDEX [UX_ProductImages_Primary_PerProduct] ON [dbo].[ProductImages];
END
");
        }

    }
}
