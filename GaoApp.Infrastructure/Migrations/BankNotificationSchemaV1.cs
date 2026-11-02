using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Infrastructure.Migrations;

// Frozen metadata for this migration/snapshot only. Future changes must use a new schema version.
internal static class BankNotificationSchemaV1
{
    private sealed record Field(string Name, Type Type, string Sql, bool Required = true, int? Length = null);
    private sealed record Key(string[] Fields, bool Unique = false);
    private sealed record Link(string Field, string Entity, string Table);
    private sealed record Table(string Entity, string Name, Field[] Fields, Key[] Keys, Link[] Links);
    private static Field Int(string name, bool optional = false) => new(name, optional ? typeof(int?) : typeof(int), "int", !optional);
    private static Field Text(string name, int length, bool optional = false) => new(name, typeof(string), length > 4000 ? "nvarchar(max)" : $"nvarchar({length})", !optional, length);
    private static Field Date(string name, bool optional = false) => new(name, optional ? typeof(DateTime?) : typeof(DateTime), "datetime2", !optional);
    private static Field Money() => new("Amount", typeof(decimal), "decimal(18,2)");
    private static readonly Table[] Tables = [
        new("BankNotificationDevice", "BankNotificationDevices", [Int("BankAccountId"), Text("BankAccountFingerprint",64), Text("Name",120), Text("TokenHash",64), new("Enabled",typeof(bool),"bit"), Date("LastSeenAtUtc",true)],
            [new(["StoreId","BankAccountId","Enabled"]),new(["BankAccountId"])], [new("BankAccountId","StoreBankAccount","StoreBankAccounts")]),
        new("BankNotificationWait", "BankNotificationWaits", [Int("QrId"),Int("OrderId"),Int("BankAccountId"),Text("BankAccountFingerprint",64),Int("TerminalId"),Int("ShiftId"),Money(),Text("CartFingerprint",64),Date("ExpiresAtUtc")],
            [new(["StoreId","QrId"],true),new(["StoreId","BankAccountId","ExpiresAtUtc"]),new(["QrId"]),new(["OrderId"]),new(["BankAccountId"]),new(["TerminalId"]),new(["ShiftId"])],
            [new("QrId","PosPaymentQrRequest","PosPaymentQrRequests"),new("OrderId","Order","Orders"),new("BankAccountId","StoreBankAccount","StoreBankAccounts"),new("TerminalId","POSTerminal","POSTerminals"),new("ShiftId","POSShift","POSShifts")]),
        new("BankNotificationReceipt", "BankNotificationReceipts", [Int("DeviceId"),Int("BankAccountId"),Text("EvidenceKey",64),Text("PayloadHash",64),Text("Content",12000),Money(),Int("OrderId",true),Text("QrCode",80,true),Text("SenderName",240,true),Text("TransactionCode",80),Date("PostedAtUtc"),Int("AppliedWaitId",true),Int("PaymentId",true),Date("ConfirmedAtUtc",true),Int("ConfirmedByUserId",true),Text("ConfirmationSource",40,true)],
            [new(["StoreId","EvidenceKey"],true),new(["StoreId","BankAccountId","PostedAtUtc"]),new(["DeviceId"]),new(["BankAccountId"]),new(["AppliedWaitId"]),new(["PaymentId"])],
            [new("DeviceId","BankNotificationDevice","BankNotificationDevices"),new("BankAccountId","StoreBankAccount","StoreBankAccounts"),new("AppliedWaitId","BankNotificationWait","BankNotificationWaits"),new("PaymentId","OrderPayment","OrderPayments")]),
        new("BankNotificationDismissal", "BankNotificationDismissals", [Int("ReceiptId"),Int("WaitId")],
            [new(["StoreId","ReceiptId","WaitId"],true),new(["ReceiptId"]),new(["WaitId"])], [new("ReceiptId","BankNotificationReceipt","BankNotificationReceipts"),new("WaitId","BankNotificationWait","BankNotificationWaits")])
    ];
    private static readonly Field[] Common = [Int("Id"),Int("StoreId"),Date("CreatedAtUtc"),Int("CreatedBy",true),Date("UpdatedAtUtc",true),Int("UpdatedBy",true),new("IsDeleted",typeof(bool),"bit"),Date("DeletedAtUtc",true),Int("DeletedBy",true),new("RowVersion",typeof(byte[]),"rowversion")];
    public static IEnumerable<string> CreateSql()
    {
        foreach (var t in Tables)
        {
            var columns = Common.Concat(t.Fields).Select(f => $"[{f.Name}] {f.Sql}{(f.Name == "Id" ? " IDENTITY(1,1)" : "")} {(f.Required ? "NOT NULL" : "NULL")}");
            var links = t.Links.Append(new("StoreId","Store","Stores")).Select(f => $"CONSTRAINT [FK_{t.Name}_{f.Table}_{f.Field}] FOREIGN KEY ([{f.Field}]) REFERENCES [{f.Table}] ([Id]) ON DELETE NO ACTION");
            yield return $"CREATE TABLE [{t.Name}] ({string.Join(",", columns)},CONSTRAINT [PK_{t.Name}] PRIMARY KEY ([Id]),{string.Join(",", links)});";
            foreach (var k in t.Keys) yield return $"CREATE {(k.Unique ? "UNIQUE " : "")}INDEX [IX_{t.Name}_{string.Join("_", k.Fields)}] ON [{t.Name}] ({string.Join(",", k.Fields.Select(f => "["+f+"]"))});";
        }
    }

    // Describes exactly the frozen SQL above to the Migrator's schema verifier.
    // This is metadata only; the already-deployed SQL is left unchanged.
    public static IEnumerable<MigrationOperation> ManifestOperations()
    {
        foreach (var t in Tables)
        {
            var create = new CreateTableOperation
            {
                Name = t.Name,
                PrimaryKey = new AddPrimaryKeyOperation { Table = t.Name, Name = "PK_" + t.Name, Columns = ["Id"] }
            };
            foreach (var f in Common.Concat(t.Fields))
            {
                var column = new AddColumnOperation
                {
                    Table = t.Name, Name = f.Name, ClrType = f.Type, ColumnType = f.Sql,
                    IsNullable = !f.Required, MaxLength = f.Length, IsRowVersion = f.Name == "RowVersion"
                };
                if (f.Name == "Id") column.AddAnnotation("SqlServer:Identity", "1, 1");
                create.Columns.Add(column);
            }
            foreach (var f in t.Links.Append(new("StoreId", "Store", "Stores")))
                create.ForeignKeys.Add(new AddForeignKeyOperation
                {
                    Table = t.Name, Name = $"FK_{t.Name}_{f.Table}_{f.Field}", Columns = [f.Field],
                    PrincipalTable = f.Table, PrincipalColumns = ["Id"], OnDelete = ReferentialAction.NoAction
                });
            yield return create;
            foreach (var k in t.Keys)
                yield return new CreateIndexOperation
                {
                    Table = t.Name, Name = $"IX_{t.Name}_{string.Join("_", k.Fields)}",
                    Columns = k.Fields, IsUnique = k.Unique
                };
        }
    }

    public static void Snapshot(ModelBuilder model)
    {
        foreach (var t in Tables)
            model.Entity("GaoApp.Domain.Entities." + t.Entity, b => {
                foreach (var f in Common.Concat(t.Fields))
                {
                    var p = b.Property(f.Type, f.Name).HasColumnType(f.Sql).IsRequired(f.Required);
                    if (f.Length.HasValue) p.HasMaxLength(f.Length.Value);
                    if (f.Name == "Id") p.ValueGeneratedOnAdd();
                    if (f.Name == "RowVersion") p.IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
                    if (f.Sql == "decimal(18,2)") p.HasPrecision(18,2);
                }
                b.HasKey("Id"); b.ToTable(t.Name);
                foreach (var k in t.Keys) b.HasIndex(k.Fields).IsUnique(k.Unique);
            });
        foreach (var t in Tables)
            model.Entity("GaoApp.Domain.Entities." + t.Entity, b => {
                foreach (var f in t.Links.Append(new("StoreId","Store","Stores")))
                    b.HasOne("GaoApp.Domain.Entities."+f.Entity, f.Field == "StoreId" ? "Store" : null).WithMany().HasForeignKey(f.Field)
                        .OnDelete(f.Field == "StoreId" ? DeleteBehavior.NoAction : DeleteBehavior.Restrict);
            });
        model.Entity("GaoApp.Domain.Entities.PosPaymentQrRequest", b => {
            b.Property<int?>("NotificationReceiptId").HasColumnType("int");
            b.Property<string>("NotificationConfirmationSource").HasMaxLength(40).HasColumnType("nvarchar(40)");
        });
    }
}
