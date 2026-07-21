namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IInvoiceFileStorage
{
    Task<string> SaveAsync(
        string relativeFolder,
        string fileName,
        byte[] bytes,
        CancellationToken ct = default);

    Task<(byte[] Bytes, string ContentType, string FileName)?> ReadAsync(
        string storedPath,
        CancellationToken ct = default);
}