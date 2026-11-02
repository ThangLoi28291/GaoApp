namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IBoundedInvoiceFileStorage
{
    Task<(byte[] Bytes, string ContentType, string FileName)?> ReadBoundedAsync(string storedPath, long maximumBytes, CancellationToken ct);
}
