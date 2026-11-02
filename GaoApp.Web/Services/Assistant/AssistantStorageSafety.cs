using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace GaoApp.Web.Services.Assistant;

// A prepared storage identity binds the private settings directory to its SQLite file.
// Production never creates a replacement file merely because an existing path disappeared.
public static class AssistantStorageSafety
{
    public const string Marker = ".gao-assistant-storage.json";
    public const string Hold = ".gao-assistant-storage-hold";
    public const string IdentityTable = "GaoAssistantStorageIdentity";

    public static bool Inside(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.GetFullPath(path);
        return path.Equals(root, comparison) || path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    public static void NoLinks(string path)
    {
        FileSystemInfo? item = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        while (item is not null)
        {
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0 || item.LinkTarget is not null)
                throw new InvalidOperationException("Assistant storage link/reparse paths are not allowed.");
            item = item is DirectoryInfo directory ? directory.Parent : ((FileInfo)item).Directory;
        }
    }

    public static string Absolute(string path, string key)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException(key + " must be an absolute persistent path.");
        var result = Path.GetFullPath(path);
        NoLinks(result);
        return result;
    }

    public static void Private(string path, IWebHostEnvironment environment, string? uploads = null)
    {
        if (Inside(path, environment.ContentRootPath) ||
            !string.IsNullOrWhiteSpace(environment.WebRootPath) && Inside(path, environment.WebRootPath) ||
            !string.IsNullOrWhiteSpace(uploads) && Inside(path, Path.GetFullPath(uploads, environment.ContentRootPath)))
            throw new InvalidOperationException("Assistant persistent storage must be outside release and public/upload paths.");
    }

    public static Guid ReadIdentity(string database)
    {
        using var db = OpenReadOnly(database);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT Value FROM GaoAssistantStorageIdentity WHERE Id=1";
        if (!Guid.TryParseExact(command.ExecuteScalar() as string, "D", out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Assistant:StorageId does not match prepared storage.");
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN('CskhSettings','CskhConversations','CskhMessages')";
        if ((long)command.ExecuteScalar()! != 3) throw new InvalidOperationException("CustomerService:DatabasePath is not initialized storage.");
        return id;
    }

    public static SqliteConnection OpenReadOnly(string database)
    {
        if (!File.Exists(database)) throw new InvalidOperationException("CustomerService:DatabasePath is missing; initialize or migrate storage explicitly.");
        NoLinks(database);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 10 }.ToString());
        try { db.Open(); return db; } catch { db.Dispose(); throw; }
    }

    public static void Database(string path, string identity, IWebHostEnvironment environment)
    {
        Absolute(path, "CustomerService:DatabasePath");
        Private(path, environment);
        if (!Guid.TryParseExact(identity, "D", out var expected) || expected == Guid.Empty || ReadIdentity(path) != expected)
            throw new InvalidOperationException("Assistant:StorageId does not match prepared storage.");
    }

    public static void Settings(string directory, string identity, IWebHostEnvironment environment)
    {
        Absolute(directory, "Assistant:StoreSettingsDirectory");
        Private(directory, environment);
        var marker = Path.Combine(directory, Marker);
        NoLinks(marker);
        if (!File.Exists(marker) || new FileInfo(marker).Length > 4096 ||
            !Guid.TryParseExact(identity, "D", out var expected) || expected == Guid.Empty)
            throw new InvalidOperationException("Assistant:StorageId requires a prepared settings directory.");
        using var json = JsonDocument.Parse(File.ReadAllText(marker));
        if (json.RootElement.GetProperty("version").GetInt32() != 1 || json.RootElement.GetProperty("storageId").GetGuid() != expected)
            throw new InvalidOperationException("Assistant:StorageId does not match the settings directory.");
    }

    public static void Available(string directory)
    {
        if (File.Exists(Path.Combine(directory, Hold)))
            throw new AssistantException(503, "assistant_storage_maintenance", "Dữ liệu CSKH đang được kiểm tra khi triển khai. Vui lòng thử lại sau.");
    }

    public static string SettingsFile(IWebHostEnvironment environment, AssistantOptions options, string name)
    {
        var directory = Path.GetFullPath(options.StoreSettingsDirectory, environment.ContentRootPath);
        Available(directory);
        try
        {
            if (!environment.IsDevelopment()) Settings(options.StoreSettingsDirectory, options.StorageId, environment);
            var file = Path.Combine(directory, name); NoLinks(file); return file;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or FormatException)
        { throw new AssistantException(503, "assistant_persistent_storage_unavailable", "Chưa xác minh được dữ liệu AI đã cấu hình. Vui lòng kiểm tra triển khai."); }
    }

    public static void Validate(IWebHostEnvironment environment, IConfiguration configuration)
    {
        if (environment.IsDevelopment()) return;
        try
        {
            var directory = configuration["Assistant:StoreSettingsDirectory"] ?? "";
            var database = configuration["CustomerService:DatabasePath"] ?? "";
            var identity = configuration["Assistant:StorageId"] ?? "";
            Settings(directory, identity, environment);
            Database(database, identity, environment);
            var uploads = configuration["Storage:UploadRoot"];
            Private(directory, environment, uploads);
            Private(database, environment, uploads);
            if (Inside(environment.ContentRootPath, directory) ||
                !string.IsNullOrWhiteSpace(uploads) && Inside(Path.GetFullPath(uploads, environment.ContentRootPath), directory))
                throw new InvalidOperationException("Assistant settings must not contain release or upload storage.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException or JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidOperationException("Assistant:StorageId / CustomerService:DatabasePath persistent storage could not be verified.");
        }
    }
}
