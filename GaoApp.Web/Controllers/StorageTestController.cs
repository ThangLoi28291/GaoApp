using System.Text;
using GaoApp.Application.Common.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Controllers;

[Route("storage-test")]
[Authorize]
[ApiExplorerSettings(IgnoreApi = true)]
public class StorageTestController : Controller
{
    private const string AllowedTestPrefix = "uploads/tests/";

    private readonly IFileStorageService _storage;
    private readonly IWebHostEnvironment _environment;

    public StorageTestController(
        IFileStorageService storage,
        IWebHostEnvironment environment)
    {
        _storage = storage;
        _environment = environment;
    }

    // Endpoint chẩn đoán chỉ tồn tại trong Development.
    [HttpPost("write")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Write(CancellationToken ct)
    {
        if (!_environment.IsDevelopment())
            return NotFound();

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        var relativePath = $"uploads/tests/{DateTime.UtcNow:yyyy/MM/dd}/hello_{now}.txt";

        var text = $"Hello GaoApp - {DateTime.UtcNow:O}";
        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var savedPath = await _storage.SaveAsync(ms, relativePath, ct);
        var url = _storage.ToPublicUrl(savedPath);

        return Ok(new
        {
            savedPath,
            publicUrl = url
        });
    }

    [HttpPost("delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromQuery] string path, CancellationToken ct)
    {
        if (!_environment.IsDevelopment())
            return NotFound();

        var normalizedPath = (path ?? string.Empty)
            .Trim()
            .Replace('\\', '/')
            .TrimStart('/');

        if (!normalizedPath.StartsWith(AllowedTestPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                error = "Chỉ được xóa file trong uploads/tests/."
            });
        }

        await _storage.DeleteAsync(normalizedPath, ct);
        return Ok(new { deleted = normalizedPath });
    }
}
