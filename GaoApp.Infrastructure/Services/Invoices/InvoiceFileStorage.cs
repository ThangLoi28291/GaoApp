using GaoApp.Application.Interfaces.Services.Invoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Infrastructure.Services.Invoices;

public class InvoiceFileStorage : IInvoiceFileStorage
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InvoiceFileStorage> _logger;

    public InvoiceFileStorage(
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<InvoiceFileStorage>? logger = null)
    {
        _environment = environment;
        _configuration = configuration;
        _logger = logger ?? NullLogger<InvoiceFileStorage>.Instance;
    }

    public async Task<string> SaveAsync(
        string relativeFolder,
        string fileName,
        byte[] bytes,
        CancellationToken ct = default)
    {
        if (bytes == null || bytes.Length == 0)
            throw new InvalidOperationException("File rỗng, không thể lưu.");

        relativeFolder = NormalizeRelative(relativeFolder);
        fileName = SanitizeFileName(fileName);

        var uploadRoot = GetUploadRootFullPath();
        var fullFolder = ResolveInsideUploadRoot(uploadRoot, relativeFolder);

        Directory.CreateDirectory(fullFolder);

        var fullPath = ResolveInsideUploadRoot(
            uploadRoot,
            $"{relativeFolder}/{fileName}");

        var temporaryPath = ResolveInsideUploadRoot(
            uploadRoot,
            $"{relativeFolder}/.{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await WriteTemporaryFileAsync(temporaryPath, bytes, ct);
            ct.ThrowIfCancellationRequested();

            // File tạm và file đích nằm cùng thư mục để publish bằng một thao tác đổi tên hoàn chỉnh.
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    DeleteTemporaryFile(temporaryPath);
            }
            catch (Exception cleanupEx)
            {
                // Cleanup is secondary and must not replace the write/cancel failure.
                _logger.LogWarning(
                    "Invoice temporary-file cleanup failed. ExceptionType={ExceptionType}",
                    cleanupEx.GetType().Name);
            }
        }

        // Lưu path tương đối để dùng web.
        return $"uploads/{relativeFolder}/{fileName}".Replace("\\", "/");
    }

    protected virtual Task WriteTemporaryFileAsync(
        string temporaryPath,
        byte[] bytes,
        CancellationToken ct) =>
        File.WriteAllBytesAsync(temporaryPath, bytes, ct);

    protected virtual void DeleteTemporaryFile(string temporaryPath) =>
        File.Delete(temporaryPath);

    public async Task<(byte[] Bytes, string ContentType, string FileName)?> ReadAsync(
        string storedPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return null;

        var normalized = storedPath.Trim()
            .Replace("\\", "/")
            .TrimStart('/');

        if (normalized.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["uploads/".Length..];
        }

        normalized = NormalizeRelative(normalized);

        var uploadRoot = GetUploadRootFullPath();
        var fullPath = ResolveInsideUploadRoot(uploadRoot, normalized);

        if (!File.Exists(fullPath))
            return null;

        var bytes = await File.ReadAllBytesAsync(fullPath, ct);
        var fileName = Path.GetFileName(fullPath);
        var contentType = ResolveContentType(fileName);

        return (bytes, contentType, fileName);
    }

    private string GetUploadRootFullPath()
    {
        var configured = _configuration["Storage:UploadRoot"];

        if (string.IsNullOrWhiteSpace(configured))
            configured = "wwwroot/uploads";

        if (Path.IsPathRooted(configured))
            return Path.GetFullPath(configured);

        return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configured));
    }

    private static string NormalizeRelative(string value)
    {
        value = (value ?? string.Empty)
            .Replace("\\", "/")
            .Trim()
            .Trim('/');

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            Path.IsPathRooted(value) ||
            segments.Any(x => x is "." or ".."))
        {
            throw new InvalidOperationException("Đường dẫn lưu file không hợp lệ.");
        }

        return value;
    }

    private static string SanitizeFileName(string fileName)
    {
        fileName = string.IsNullOrWhiteSpace(fileName)
            ? $"invoice-file-{DateTime.UtcNow:yyyyMMddHHmmss}.bin"
            : fileName.Trim();

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '-');
        }

        if (fileName is "." or "..")
            throw new InvalidOperationException("Tên file không hợp lệ.");

        return fileName;
    }

    private static string ResolveInsideUploadRoot(string uploadRoot, string relativePath)
    {
        var root = Path.GetFullPath(uploadRoot);
        var candidate = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.StartsWith(rootWithSeparator, comparison))
            throw new InvalidOperationException("Đường dẫn file nằm ngoài thư mục upload.");

        return candidate;
    }

    private static string ResolveContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch
        {
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }
}
