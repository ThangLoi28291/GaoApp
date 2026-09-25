using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260822090000_AddInputInvoiceSupplierResolution")]
public sealed class AddInputInvoiceSupplierResolution : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NormalizedTaxCode",
            table: "Suppliers",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true,
            computedColumnSql: "CONVERT(nvarchar(50), NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([TaxCode])), N' ', N''), N'.', N''), N'-', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N''))",
            stored: true);

        migrationBuilder.AddColumn<int>(
            name: "ResolvedSupplierId",
            table: "InputInvoiceHead",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "SupplierResolutionStatus",
            table: "InputInvoiceHead",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "SupplierResolutionUpdatedAtUtc",
            table: "InputInvoiceHead",
            type: "datetime2",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "InputInvoiceSupplierResolutionEvent",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                StoreId = table.Column<int>(type: "int", nullable: false),
                InputInvoiceHeadId = table.Column<int>(type: "int", nullable: false),
                StockDocumentId = table.Column<int>(type: "int", nullable: true),
                EventType = table.Column<int>(type: "int", nullable: false),
                PreviousStatus = table.Column<int>(type: "int", nullable: false),
                NewStatus = table.Column<int>(type: "int", nullable: false),
                OldSupplierId = table.Column<int>(type: "int", nullable: true),
                NewSupplierId = table.Column<int>(type: "int", nullable: true),
                CandidateCount = table.Column<int>(type: "int", nullable: true),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                ActorUserId = table.Column<int>(type: "int", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InputInvoiceSupplierResolutionEvent", x => x.Id);
                table.ForeignKey(
                    name: "FK_InputInvoiceSupplierResolutionEvent_InputInvoiceHead_InputInvoiceHeadId",
                    column: x => x.InputInvoiceHeadId,
                    principalTable: "InputInvoiceHead",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InputInvoiceSupplierResolutionEvent_StockDocument_StockDocumentId",
                    column: x => x.StockDocumentId,
                    principalTable: "StockDocument",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_InputInvoiceSupplierResolutionEvent_Stores_StoreId",
                    column: x => x.StoreId,
                    principalTable: "Stores",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_InputInvoiceSupplierResolutionEvent_Suppliers_NewSupplierId",
                    column: x => x.NewSupplierId,
                    principalTable: "Suppliers",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_InputInvoiceSupplierResolutionEvent_Suppliers_OldSupplierId",
                    column: x => x.OldSupplierId,
                    principalTable: "Suppliers",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_Suppliers_StoreId_NormalizedTaxCode_State",
            table: "Suppliers",
            columns: new[] { "StoreId", "NormalizedTaxCode", "IsDeleted", "IsActive" });

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceHead_StoreId_ResolvedSupplierId",
            table: "InputInvoiceHead",
            columns: new[] { "StoreId", "ResolvedSupplierId" });

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceSupplierResolutionEvent_InputInvoiceHeadId",
            table: "InputInvoiceSupplierResolutionEvent",
            column: "InputInvoiceHeadId");

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceSupplierResolutionEvent_NewSupplierId",
            table: "InputInvoiceSupplierResolutionEvent",
            column: "NewSupplierId");

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceSupplierResolutionEvent_OldSupplierId",
            table: "InputInvoiceSupplierResolutionEvent",
            column: "OldSupplierId");

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceSupplierResolutionEvent_StockDocumentId",
            table: "InputInvoiceSupplierResolutionEvent",
            column: "StockDocumentId");

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceSupplierResolutionEvent_Store_Invoice_Time",
            table: "InputInvoiceSupplierResolutionEvent",
            columns: new[] { "StoreId", "InputInvoiceHeadId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceSupplierResolutionEvent_Store_Receipt_Time",
            table: "InputInvoiceSupplierResolutionEvent",
            columns: new[] { "StoreId", "StockDocumentId", "CreatedAtUtc" });

        migrationBuilder.AddForeignKey(
            name: "FK_InputInvoiceHead_Suppliers_ResolvedSupplierId",
            table: "InputInvoiceHead",
            column: "ResolvedSupplierId",
            principalTable: "Suppliers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "InputInvoiceSupplierResolutionEvent");
        migrationBuilder.DropForeignKey(
            name: "FK_InputInvoiceHead_Suppliers_ResolvedSupplierId",
            table: "InputInvoiceHead");
        migrationBuilder.DropIndex(
            name: "IX_InputInvoiceHead_StoreId_ResolvedSupplierId",
            table: "InputInvoiceHead");
        migrationBuilder.DropIndex(
            name: "IX_Suppliers_StoreId_NormalizedTaxCode_State",
            table: "Suppliers");
        migrationBuilder.DropColumn(
            name: "ResolvedSupplierId",
            table: "InputInvoiceHead");
        migrationBuilder.DropColumn(
            name: "SupplierResolutionStatus",
            table: "InputInvoiceHead");
        migrationBuilder.DropColumn(
            name: "SupplierResolutionUpdatedAtUtc",
            table: "InputInvoiceHead");
        migrationBuilder.DropColumn(
            name: "NormalizedTaxCode",
            table: "Suppliers");
    }
}
