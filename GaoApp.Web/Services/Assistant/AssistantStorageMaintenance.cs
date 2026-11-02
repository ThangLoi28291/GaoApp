using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Web.Services.CustomerService;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.Assistant;

public sealed record AssistantStorageRequest(string Operation, string DatabasePath = "", string SettingsDirectory = "",
    string SnapshotDirectory = "", string DestinationDirectory = "", string SourceDatabase = "", string SourceSettingsDirectory = "",
    bool WritersQuiesced = false, string StorageId = "");
public sealed record AssistantTableEvidence(long Rows, string Sha256);
public sealed record AssistantStorageEvidence(Dictionary<string, AssistantTableEvidence> Tables, Dictionary<string, string> Files);

// Offline-only entry point. It runs before host/DI/SQL Server startup and never dispatches channel jobs.
public static class AssistantStorageMaintenance
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static int Run(TextReader input, TextWriter output, TextWriter error)
    {
        try
        {
            var buffer = new char[32769]; var length = 0; int count;
            while (length < buffer.Length && (count = input.Read(buffer, length, buffer.Length - length)) > 0) length += count;
            if (length == buffer.Length) throw new ArgumentException();
            using var json = JsonDocument.Parse(new string(buffer, 0, length)); AssistantJwt.Unique(json.RootElement);
            var request = json.RootElement.Deserialize<AssistantStorageRequest>(Json) ?? throw new ArgumentException();
            object result = request.Operation switch
            {
                "initialize" or "migrate" or "migrate-settings" => Prepare(request),
                "snapshot" => Snapshot(request),
                "verify" => Verify(request),
                "check" => Check(request),
                _ => throw new ArgumentException()
            };
            output.WriteLine(JsonSerializer.Serialize(result, Json)); return 0;
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // Never print SQLite contents, credentials, paths or raw provider exception text.
            error.WriteLine("ASSISTANT_STORAGE_FAILED: " + e.GetType().Name); return 2;
        }
    }

    private static void Quiesced(AssistantStorageRequest request)
    { if (!request.WritersQuiesced) throw new InvalidOperationException("Quiesce all writers before maintenance."); }
    public static object Check(AssistantStorageRequest request)
    {
        var database = AssistantStorageSafety.Absolute(request.DatabasePath, "DatabasePath");
        var directory = AssistantStorageSafety.Absolute(request.SettingsDirectory, "SettingsDirectory");
        if (!Guid.TryParseExact(request.StorageId, "D", out var id) || id == Guid.Empty || AssistantStorageSafety.ReadIdentity(database) != id) throw new InvalidOperationException();
        var marker = Path.Combine(directory, AssistantStorageSafety.Marker); AssistantStorageSafety.NoLinks(marker);
        if (!File.Exists(marker) || new FileInfo(marker).Length > 4096) throw new InvalidOperationException();
        using var json = JsonDocument.Parse(File.ReadAllText(marker));
        if (json.RootElement.GetProperty("version").GetInt32() != 1 || json.RootElement.GetProperty("storageId").GetGuid() != id) throw new InvalidOperationException();
        return new { status = "storage_verified" };
    }
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    private static string HashFile(string path) { using var file=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static bool Same<T>(T a, T b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);

    public static Dictionary<string, AssistantTableEvidence> Tables(string path)
    {
        using var db = AssistantStorageSafety.OpenReadOnly(path);
        using var transaction = db.BeginTransaction(deferred: true);
        using var check = db.CreateCommand(); check.Transaction = transaction;
        check.CommandText = "PRAGMA integrity_check";
        if (!Equals(check.ExecuteScalar(), "ok")) throw new InvalidOperationException("SQLite integrity check failed.");
        check.CommandText = "PRAGMA foreign_key_check"; using (var reader = check.ExecuteReader()) if (reader.Read()) throw new InvalidOperationException("SQLite foreign key check failed.");
        check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name<>$identity ORDER BY name";
        check.Parameters.AddWithValue("$identity", AssistantStorageSafety.IdentityTable);
        var names = new List<string>(); using (var reader = check.ExecuteReader()) while (reader.Read()) names.Add(reader.GetString(0));
        if (!new[] { "CskhSettings", "CskhConversations", "CskhMessages" }.All(names.Contains))
            throw new InvalidOperationException("Not an initialized CSKH database.");
        var tables = new Dictionary<string, AssistantTableEvidence>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            using var rows = db.CreateCommand(); rows.Transaction = transaction;
            rows.CommandText = "SELECT * FROM " + Quote(name) + " ORDER BY rowid";
            using var reader = rows.ExecuteReader();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long total = 0;
            while (reader.Read())
            {
                total++;
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    var value = reader.GetValue(column);
                    var bytes = value is byte[] blob ? blob : Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                    hash.AppendData(Encoding.UTF8.GetBytes(value.GetType().FullName!));
                    hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
                }
            }
            tables.Add(name, new(total, Convert.ToHexString(hash.GetHashAndReset())));
        }
        return tables;
    }

    public static Dictionary<string, string> Files(string directory, string database)
    {
        AssistantStorageSafety.Absolute(directory, "SettingsDirectory");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        // Reject links before descending; EnumerationOptions must not skip hidden/system files.
        void Visit(string current)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(current).Order(StringComparer.Ordinal))
            {
                AssistantStorageSafety.NoLinks(entry);
                if (Directory.Exists(entry)) { Visit(entry); continue; }
                var path = Path.GetFullPath(entry);
                if (new[] { database, database + "-wal", database + "-shm" }.Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)) ||
                    Path.GetFileName(path) is AssistantStorageSafety.Marker or AssistantStorageSafety.Hold) continue;
                result.Add(Path.GetRelativePath(directory, path), HashFile(path));
            }
        }
        Visit(directory); return result;
    }

    private static void Backup(string source, string target)
    {
        if (File.Exists(target)) throw new IOException("Target already exists.");
        using var from = AssistantStorageSafety.OpenReadOnly(source);
        using var to = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
        to.Open(); from.BackupDatabase(to);
        using var checkpoint = to.CreateCommand(); checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)"; checkpoint.ExecuteNonQuery();
    }

    public static AssistantStorageEvidence Capture(string database, string directory) => new(Tables(database), Files(directory, database));
    private static void Equal(AssistantStorageEvidence expected, AssistantStorageEvidence actual)
    {
        if (!Same(expected.Files, actual.Files) || expected.Tables.Any(t => !actual.Tables.TryGetValue(t.Key, out var found) || found != t.Value) ||
            actual.Tables.Any(t => !expected.Tables.ContainsKey(t.Key) && t.Value.Rows != 0))
            throw new InvalidOperationException("Storage continuity verification failed.");
    }
    private static void CopyFiles(string source, string target, Dictionary<string, string> files)
    {
        foreach (var entry in files)
        {
            var destination = Path.Combine(target, entry.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(source, entry.Key), destination, overwrite: false);
            if (HashFile(destination) != entry.Value) throw new IOException("Settings changed during copy.");
        }
    }

    public static object Snapshot(AssistantStorageRequest request)
    {
        Quiesced(request);
        var database = AssistantStorageSafety.Absolute(request.DatabasePath, "DatabasePath");
        var directory = AssistantStorageSafety.Absolute(request.SettingsDirectory, "SettingsDirectory");
        var destination = AssistantStorageSafety.Absolute(request.SnapshotDirectory, "SnapshotDirectory");
        if (request.StorageId != "") Check(request);
        if (Directory.Exists(destination) || File.Exists(destination) || AssistantStorageSafety.Inside(destination, directory) || AssistantStorageSafety.Inside(database, destination))
            throw new InvalidOperationException("Snapshot must be a new, separate directory.");
        var before = Capture(database, directory);
        Directory.CreateDirectory(destination); var files = Path.Combine(destination, "settings"); Directory.CreateDirectory(files);
        Backup(database, Path.Combine(destination, "customer-service.db")); CopyFiles(directory, files, before.Files);
        Equal(before, Capture(Path.Combine(destination, "customer-service.db"), files));
        Equal(before, Capture(database, directory));
        File.WriteAllText(Path.Combine(destination, "evidence.json"), JsonSerializer.Serialize(before, Json));
        return new { status = "snapshot_verified", tableCount = before.Tables.Count };
    }

    public static object Verify(AssistantStorageRequest request)
    {
        Quiesced(request);
        var database = AssistantStorageSafety.Absolute(request.DatabasePath, "DatabasePath");
        var directory = AssistantStorageSafety.Absolute(request.SettingsDirectory, "SettingsDirectory");
        var snapshot = AssistantStorageSafety.Absolute(request.SnapshotDirectory, "SnapshotDirectory");
        if (request.StorageId != "") Check(request);
        var evidencePath = Path.Combine(snapshot, "evidence.json"); AssistantStorageSafety.NoLinks(evidencePath);
        var expected = JsonSerializer.Deserialize<AssistantStorageEvidence>(File.ReadAllText(evidencePath), Json) ?? throw new JsonException();
        Equal(expected, Capture(Path.Combine(snapshot, "customer-service.db"), Path.Combine(snapshot, "settings")));
        Equal(expected, Capture(database, directory));
        return new { status = "continuity_verified", tableCount = expected.Tables.Count };
    }

    public static object Prepare(AssistantStorageRequest request)
    {
        Quiesced(request);
        var destination = AssistantStorageSafety.Absolute(request.DestinationDirectory, "DestinationDirectory");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Destination must not exist.");
        var settings = Path.Combine(destination, "settings"); var database = Path.Combine(destination, "customer-service.db");
        var id = Guid.NewGuid();
        if (request.Operation == "migrate")
        {
            var source = AssistantStorageSafety.Absolute(request.SourceDatabase, "SourceDatabase");
            var sourceSettings = AssistantStorageSafety.Absolute(request.SourceSettingsDirectory, "SourceSettingsDirectory");
            if (AssistantStorageSafety.Inside(destination, sourceSettings) || AssistantStorageSafety.Inside(sourceSettings, destination) || AssistantStorageSafety.Inside(source, destination)) throw new InvalidOperationException();
            var sourceIdentity = "";
            using (var sourceConnection = AssistantStorageSafety.OpenReadOnly(source))
            {
                using var identityQuery = sourceConnection.CreateCommand(); identityQuery.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='GaoAssistantStorageIdentity'";
                if ((long)identityQuery.ExecuteScalar()! != 0)
                {
                    id = AssistantStorageSafety.ReadIdentity(source); sourceIdentity = id.ToString("D");
                    Check(request with { DatabasePath = source, SettingsDirectory = sourceSettings, StorageId = sourceIdentity });
                }
            }
            // Source is never changed, renamed, deleted or checkpointed by migration.
            Directory.CreateDirectory(destination);
            var backup = Path.Combine(destination, "source-backup");
            Snapshot(request with { Operation = "snapshot", DatabasePath = source, SettingsDirectory = sourceSettings, SnapshotDirectory = backup, StorageId = sourceIdentity });
            Directory.CreateDirectory(settings);
            Backup(Path.Combine(backup, "customer-service.db"), database);
            var original = Capture(source, sourceSettings);
            CopyFiles(Path.Combine(backup, "settings"), settings, original.Files);
            Equal(original, Capture(database, settings)); Equal(original, Capture(source, sourceSettings));
        }
        else if (request.Operation is "initialize" or "migrate-settings")
        {
            if (request.Operation == "initialize" && (request.SourceDatabase != "" || request.SourceSettingsDirectory != "")) throw new ArgumentException();
            if (request.Operation == "migrate-settings")
            {
                var sourceDatabase = AssistantStorageSafety.Absolute(request.SourceDatabase, "SourceDatabase");
                var sourceSettings = AssistantStorageSafety.Absolute(request.SourceSettingsDirectory, "SourceSettingsDirectory");
                if (File.Exists(sourceDatabase) || File.Exists(Path.Combine(sourceSettings, AssistantStorageSafety.Marker)) ||
                    AssistantStorageSafety.Inside(destination, sourceSettings) || AssistantStorageSafety.Inside(sourceSettings, destination)) throw new InvalidOperationException();
                var originalFiles = Files(sourceSettings, sourceDatabase);
                if (originalFiles.Keys.Any(p => p.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".db3", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase) || p.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) || p.EndsWith("-shm", StringComparison.OrdinalIgnoreCase) || p.EndsWith("-journal", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException();
                var sourceBackup = Path.Combine(destination, "source-settings-backup"); Directory.CreateDirectory(sourceBackup);
                CopyFiles(sourceSettings, sourceBackup, originalFiles);
                if (!Same(originalFiles, Files(sourceSettings, sourceDatabase))) throw new InvalidOperationException();
                Directory.CreateDirectory(settings); CopyFiles(sourceBackup, settings, originalFiles);
                if (!Same(originalFiles, Files(settings, database))) throw new InvalidOperationException();
                File.WriteAllText(Path.Combine(destination, "source-settings-evidence.json"), JsonSerializer.Serialize(originalFiles, Json));
            }
            Directory.CreateDirectory(settings);
            var store = new CustomerServiceStore(new MaintenanceEnvironment(destination), new FixedOptions<CustomerServiceOptions>(new() { DatabasePath = database }), TimeProvider.System);
            store.InitializeForMaintenance();
        }
        else throw new ArgumentException();
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            db.Open(); using var command = db.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS GaoAssistantStorageIdentity(Id INTEGER PRIMARY KEY CHECK(Id=1),Value TEXT NOT NULL); INSERT INTO GaoAssistantStorageIdentity(Id,Value) VALUES(1,$id) ON CONFLICT(Id) DO NOTHING";
            command.Parameters.AddWithValue("$id", id.ToString("D")); command.ExecuteNonQuery();
        }
        File.WriteAllText(Path.Combine(settings, AssistantStorageSafety.Marker), JsonSerializer.Serialize(new { version = 1, storageId = id }, Json));
        if (AssistantStorageSafety.ReadIdentity(database) != id) throw new InvalidOperationException();
        return new { status = "storage_prepared", storageId = id, databasePath = database, settingsDirectory = settings };
    }

    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    { public T CurrentValue => value; public T Get(string? name) => value; public IDisposable? OnChange(Action<T, string?> listener) => null; }
    private sealed class MaintenanceEnvironment(string root) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "GaoApp.Web";
        public string ContentRootPath { get; set; } = root;
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
