using GaoApp.Application.Interfaces.Services.Invoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace GaoApp.Infrastructure.Services.Invoices;

public class InvoiceFileStorage : IInvoiceFileStorage
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public InvoiceFileStorage(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
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

        var fullFolder = Path.Combine(
            uploadRoot,
            relativeFolder.Replace("/", Path.DirectorySeparatorChar.ToString()));

        Directory.CreateDirectory(fullFolder);

        var fullPath = Path.Combine(fullFolder, fileName);

        await File.WriteAllBytesAsync(fullPath, bytes, ct);

        // Lưu path tương đối để dùng web.
        return $"uploads/{relativeFolder}/{fileName}".Replace("\\", "/");
    }

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

        var fullPath = Path.Combine(
            GetUploadRootFullPath(),
            normalized.Replace("/", Path.DirectorySeparatorChar.ToString()));

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
            return configured;

        return Path.Combine(_environment.ContentRootPath, configured);
    }

    private static string NormalizeRelative(string value)
    {
        value = (value ?? string.Empty)
            .Replace("\\", "/")
            .Trim()
            .Trim('/');

        if (value.Contains(".."))
            throw new InvalidOperationException("Đường dẫn lưu file không hợp lệ.");

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

        return fileName;
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