using System.Text;
using GaoApp.Application.Common.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Controllers;

[Route("storage-test")]
public class StorageTestController : Controller
{
    private readonly IFileStorageService _storage;

    public StorageTestController(IFileStorageService storage)
    {
        _storage = storage;
    }

    // GET /storage-test/write
    [HttpGet("write")]
    public async Task<IActionResult> Write(CancellationToken ct)
    {
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

    // GET /storage-test/delete?path=uploads/tests/...
    [HttpGet("delete")]
    public async Task<IActionResult> Delete([FromQuery] string path, CancellationToken ct)
    {
        await _storage.DeleteAsync(path, ct);
        return Ok(new { deleted = path });
    }
}
