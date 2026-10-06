using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProtectDeliverySourceCarts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE TRIGGER [TR_Orders_DeliverySource] ON [Orders] AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted d JOIN DeliveryOrders g ON g.StoreId=d.StoreId AND g.SourceCartId=d.Id WHERE d.Status=3) THROW 51004, 'Delivery source cart is immutable.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_OrderLines_DeliverySource] ON [OrderLines] AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM (SELECT StoreId,OrderId FROM inserted UNION SELECT StoreId,OrderId FROM deleted) x JOIN Orders o ON o.StoreId=x.StoreId AND o.Id=x.OrderId JOIN DeliveryOrders g ON g.StoreId=o.StoreId AND g.SourceCartId=o.Id WHERE o.Status=3) THROW 51004, 'Delivery source cart is immutable.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_OrderPayments_DeliverySource] ON [OrderPayments] AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM (SELECT StoreId,OrderId FROM inserted UNION SELECT StoreId,OrderId FROM deleted) x JOIN Orders o ON o.StoreId=x.StoreId AND o.Id=x.OrderId JOIN DeliveryOrders g ON g.StoreId=o.StoreId AND g.SourceCartId=o.Id WHERE o.Status=3) THROW 51004, 'Delivery source cart is immutable.', 1; END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_Orders_DeliverySource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_OrderLines_DeliverySource];");

            migrationBuilder.Sql("DROP TRIGGER [TR_OrderPayments_DeliverySource];");
        }
    }
}
