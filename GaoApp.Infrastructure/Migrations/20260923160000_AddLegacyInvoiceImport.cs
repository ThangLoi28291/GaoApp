using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace GaoApp.Infrastructure.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20260923160000_AddLegacyInvoiceImport")]
public sealed class AddLegacyInvoiceImport : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        foreach (var table in new[] { "InvoiceHeads", "InvoiceDetails" })
        {
            m.AddColumn<long>("LegacySourceId", table, type: "bigint", nullable: true);
            m.AddColumn<string>("LegacySnapshotJson", table, type: "nvarchar(max)", nullable: true);
            m.AddColumn<byte[]>("LegacyImportedHash", table, type: "binary(32)", nullable: true);
            m.CreateIndex($"IX_{table}_StoreId_LegacySourceId", table, new[] { "StoreId", "LegacySourceId" }, unique: true, filter: "[LegacySourceId] IS NOT NULL");
        }
        m.AddColumn<long>("LegacyOrderCategoryId", "InvoiceHeads", type: "bigint", nullable: true);
        m.AddColumn<string>("LegacyMergeId", "InvoiceHeads", type: "nvarchar(15)", maxLength: 15, nullable: true);
        m.AddColumn<bool>("LegacyReadOnly", "InvoiceHeads", type: "bit", nullable: false, defaultValue: false);
        m.AddColumn<decimal>("LegacyUnitFactor", "InvoiceDetails", type: "decimal(18,4)", precision: 18, scale: 4, nullable: true);
        ChangeOrderIndexes(m, true);
        m.AddCheckConstraint("CK_InvoiceHeads_LegacyOrder", "InvoiceHeads", "[OrderId] IS NOT NULL OR ([LegacySourceId] IS NOT NULL AND [LegacyReadOnly] = 1)");
    }
    protected override void Down(MigrationBuilder m)
    {
        // Downgrade intentionally fails while archived invoices without an Order remain.
        m.DropCheckConstraint("CK_InvoiceHeads_LegacyOrder", "InvoiceHeads");
        ChangeOrderIndexes(m, false);
        foreach (var table in new[] { "InvoiceHeads", "InvoiceDetails" })
        {
            m.DropIndex($"IX_{table}_StoreId_LegacySourceId", table);
            foreach (var column in new[] { "LegacySourceId", "LegacySnapshotJson", "LegacyImportedHash" }) m.DropColumn(column, table);
        }
        foreach (var column in new[] { "LegacyOrderCategoryId", "LegacyMergeId", "LegacyReadOnly" }) m.DropColumn(column, "InvoiceHeads");
        m.DropColumn("LegacyUnitFactor", "InvoiceDetails");
    }
    private static void ChangeOrderIndexes(MigrationBuilder m, bool nullable)
    {
        m.DropIndex("IX_InvoiceHeads_OrderId", "InvoiceHeads");
        m.DropIndex("IX_InvoiceHeads_StoreId_OrderId", "InvoiceHeads");
        m.DropIndex("IX_InvoiceHeads_StoreId_OrderId_LegalEntityId", "InvoiceHeads");
        m.AlterColumn<int>("OrderId", "InvoiceHeads", type: "int", nullable: nullable, oldClrType: typeof(int), oldType: "int", oldNullable: !nullable);
        m.CreateIndex("IX_InvoiceHeads_OrderId", "InvoiceHeads", "OrderId");
        var prefix = nullable ? "[OrderId] IS NOT NULL AND " : "";
        m.CreateIndex("IX_InvoiceHeads_StoreId_OrderId", "InvoiceHeads", new[] { "StoreId", "OrderId" }, unique: true,
            filter: prefix + "[IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL AND [LegalEntityId] IS NULL");
        m.CreateIndex("IX_InvoiceHeads_StoreId_OrderId_LegalEntityId", "InvoiceHeads", new[] { "StoreId", "OrderId", "LegalEntityId" }, unique: true,
            filter: prefix + "[IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL AND [LegalEntityId] IS NOT NULL");
    }
}
