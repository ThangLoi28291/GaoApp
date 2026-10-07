using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PreserveHeldOrderShiftChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM DeliveryOrders g WITH (UPDLOCK, HOLDLOCK, INDEX([IX_DeliveryOrders_StoreId_SourceCartId])) LEFT JOIN Orders o WITH (UPDLOCK, HOLDLOCK, INDEX([AK_Orders_StoreId_Id])) ON o.StoreId=g.StoreId AND o.Id=g.SourceCartId WHERE o.Id IS NULL OR o.POSShiftId<>g.CreatedShiftId) THROW 51005, 'Existing delivery source cart must belong to its created shift.', 1;");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrders_Orders_StoreId_SourceCartId_CreatedShiftId",
                table: "DeliveryOrders");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Orders_StoreId_Id_POSShiftId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_StoreId_SourceCartId_CreatedShiftId",
                table: "DeliveryOrders");

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrders_Orders_StoreId_SourceCartId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "SourceCartId" },
                principalTable: "Orders",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryOrders_SourceShift] ON [DeliveryOrders] AFTER INSERT, UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN Orders o WITH (UPDLOCK, HOLDLOCK, INDEX([AK_Orders_StoreId_Id])) ON o.StoreId=i.StoreId AND o.Id=i.SourceCartId WHERE o.Id IS NULL OR o.POSShiftId<>i.CreatedShiftId) THROW 51005, 'Delivery source cart must belong to its created shift.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_Orders_DeliverySourceShift] ON [Orders] AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id JOIN DeliveryOrders g WITH (UPDLOCK, HOLDLOCK, INDEX([IX_DeliveryOrders_StoreId_SourceCartId])) ON g.StoreId=d.StoreId AND g.SourceCartId=d.Id WHERE i.POSShiftId<>d.POSShiftId) THROW 51006, 'Delivery source cart shift is immutable.', 1; END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryOrders_SourceShift];");

            migrationBuilder.Sql("DROP TRIGGER [TR_Orders_DeliverySourceShift];");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrders_Orders_StoreId_SourceCartId",
                table: "DeliveryOrders");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Orders_StoreId_Id_POSShiftId",
                table: "Orders",
                columns: new[] { "StoreId", "Id", "POSShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_SourceCartId_CreatedShiftId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "SourceCartId", "CreatedShiftId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrders_Orders_StoreId_SourceCartId_CreatedShiftId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "SourceCartId", "CreatedShiftId" },
                principalTable: "Orders",
                principalColumns: new[] { "StoreId", "Id", "POSShiftId" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
