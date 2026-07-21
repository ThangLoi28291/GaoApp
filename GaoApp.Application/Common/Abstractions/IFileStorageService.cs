namespace GaoApp.Application.Common.Abstractions;

public interface IFileStorageService
{
    /// <summary>
    /// Save bytes into storage. Return relative path (no wwwroot), e.g. "uploads/products/2025/12/22/a.txt"
    /// </summary>
    Task<string> SaveAsync(
        Stream content,
        string relativePath,
        CancellationToken ct = default);

    /// <summary>
    /// Delete by relative path.
    /// </summary>
    Task DeleteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Move from old relative path to new relative path.
    /// </summary>
    Task MoveAsync(string fromRelativePath, string toRelativePath, CancellationToken ct = default);

    /// <summary>
    /// Map relative path to public url path (e.g. "/uploads/...").
    /// </summary>
    string ToPublicUrl(string relativePath);
}
