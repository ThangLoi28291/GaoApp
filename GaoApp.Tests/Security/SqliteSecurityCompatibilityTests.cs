using System.Text.Json;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace GaoApp.Tests.Security;

public sealed class SqliteSecurityCompatibilityTests(ITestOutputHelper output) : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaoSqliteSecurityTests", Guid.NewGuid().ToString("N"));
    private readonly AssistantIntegrationTests.FixedClock clock = new();

    [Fact]
    public void Loaded_native_engine_meets_CVE_2025_6965_security_baseline()
    {
        using var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT sqlite_version(), sqlite_source_id()";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        var version = Version.Parse(reader.GetString(0));
        output.WriteLine($"Loaded SQLite {version}; source {reader.GetString(1)}");
        Assert.True(version >= new Version(3, 50, 2), $"SQLite {version} is below the CVE-2025-6965 fixed version.");
    }

    [Fact]
    public void Online_backup_preserves_committed_WAL_identity_and_delivery_state_and_restores_independently()
    {
        var store = Store("source.db");
        store.Settings(1);
        // Keep a connection open so committed writes remain in WAL during the backup.
        using var source = Open("source.db");
        Execute(source, "PRAGMA wal_autocheckpoint=0");
        // Opening a connection alone does not attach it to the WAL pager.
        _ = Snapshot(source);
        store.Configure(1, true, 0);
        var visitor = Guid.NewGuid();
        var conversation = store.Start(1, visitor, new(Guid.NewGuid(), "TEST Khách Việt ' 漢字", null, true));
        var agent = new CustomerStaff(1, 1, "TEST nhân viên", true, true, true);
        conversation = store.StaffCommand(agent, conversation.Id, new(Guid.NewGuid(), "claim", conversation.Version));
        var protector = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "keys"))).CreateProtector("B9-test");
        var code = store.IssueIdentity(agent, conversation.Id,
            new(Guid.NewGuid(), conversation.Version, 42, "in_person", "TEST verified at counter", true), protector);
        Assert.True(store.RedeemIdentity(1, visitor, conversation.Id, new(Guid.NewGuid(), code.Code!)));

        var channel = new CustomerChannelConnection
        {
            Id = "facebook-b9-test", Provider = "facebook", AccountId = "123456", StoreId = 1, Enabled = true, AutoReply = false
        };
        var incoming = new CustomerChannelIncoming("b9-mid-1", "999111", "TEST hỏi giá", clock.Now, "b9-reference");
        var channelId = store.ImportChannel(channel, incoming)!.Value;
        var channelChat = store.StaffGet(agent, channelId);
        channelChat = store.StaffCommand(agent, channelId, new(Guid.NewGuid(), "claim", channelChat.Version));
        store.StaffCommand(agent, channelId, new(Guid.NewGuid(), "reply", channelChat.Version, "TEST đã nhận tin"));

        Assert.True(new FileInfo(Path.Combine(directory, "source.db-wal")).Length > 0);
        var expected = Snapshot(source);
        using (var backup = Open("backup.db"))
        {
            source.BackupDatabase(backup);
            Assert.Equal(expected, Snapshot(backup));
            using var integrity = backup.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check";
            Assert.Equal("ok", integrity.ExecuteScalar());
            integrity.CommandText = "PRAGMA foreign_key_check";
            using var violations = integrity.ExecuteReader();
            Assert.False(violations.Read());
        }

        store.Start(1, visitor, new(Guid.NewGuid(), "TEST after backup", null, true));
        Assert.NotEqual(expected, Snapshot(source));
        var restored = Store("backup.db");
        Assert.Equal(42, restored.RequireIdentity(1, visitor, conversation.Id).CustomerId);
        Assert.Null(restored.ImportChannel(channel, incoming));
        Assert.Equal("pending", restored.StaffGet(agent, channelId).Messages.Single(m => m.Role == "agent").Delivery!.State);
        using var restoredDb = Open("backup.db");
        Assert.Equal(expected, Snapshot(restoredDb));
    }

    [Fact]
    public void Backup_excludes_uncommitted_writes_and_rollback_keeps_original_rows()
    {
        var store = Store("transaction.db");
        store.Configure(1, true, 0);
        using var source = Open("transaction.db");
        var expected = Snapshot(source);
        using (var writer = Open("transaction.db"))
        using (var transaction = writer.BeginTransaction())
        {
            using var command = writer.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE CskhSettings SET Version=777 WHERE Store=1";
            Assert.Equal(1, command.ExecuteNonQuery());
            using var backup = Open("transaction-backup.db");
            source.BackupDatabase(backup);
            Assert.Equal(expected, Snapshot(backup));
            transaction.Rollback();
        }
        Assert.Equal(expected, Snapshot(source));
    }

    private CustomerServiceStore Store(string file) => new(
        new AssistantIntegrationTests.EnvironmentStub(directory),
        new AssistantIntegrationTests.Monitor<CustomerServiceOptions>(new() { DatabasePath = file }), clock);

    private SqliteConnection Open(string file)
    {
        Directory.CreateDirectory(directory);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, file), Pooling = false, ForeignKeys = true, DefaultTimeout = 5
        }.ToString());
        db.Open();
        return db;
    }

    private static void Execute(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Snapshot(SqliteConnection db)
    {
        using var tables = db.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name LIKE 'Cskh%' ORDER BY name";
        var names = new List<string>();
        using (var reader = tables.ExecuteReader()) while (reader.Read()) names.Add(reader.GetString(0));
        Assert.NotEmpty(names);
        var rows = new SortedDictionary<string, List<object?[]>>();
        foreach (var name in names)
        {
            using var command = db.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{name.Replace("\"", "\"\"")}\" ORDER BY rowid";
            using var reader = command.ExecuteReader();
            var values = new List<object?[]>();
            while (reader.Read())
            {
                var row = new object[reader.FieldCount];
                reader.GetValues(row);
                values.Add(row.Select(x => x is DBNull ? null : x).ToArray());
            }
            rows.Add(name, values);
        }
        return JsonSerializer.Serialize(rows);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
