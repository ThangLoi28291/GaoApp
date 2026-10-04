using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory.InputInvoices;

namespace GaoApp.Infrastructure.Services.Invoices;

public sealed partial class FileSystemInputInvoiceDocumentLibrary
{
    // Unlike the receipt picker, indexing must never silently stop at 200 candidates.
    public async IAsyncEnumerable<InputInvoicePickerCandidateDto> EnumerateCatalogAsync(
        int? year, int? month, [EnumeratorCancellation] CancellationToken ct)
    {
        EnsureEnabled();
        if (year is < 2000 or > 9999 || month is < 1 or > 12)
            throw new BusinessRuleException("Năm hoặc tháng không hợp lệ.");
        foreach (var yearPath in Directory.EnumerateDirectories(RootFullPath()).OrderByDescending(x => x))
        {
            if (!int.TryParse(Path.GetFileName(yearPath), out var y) || y < 2000 || y > 9999 || (year.HasValue && year != y)) continue;
            EnsureSafeExistingPath(yearPath, true);
            foreach (var supplierPath in Directory.EnumerateDirectories(yearPath))
            {
                ct.ThrowIfCancellationRequested();
                EnsureSafeExistingPath(supplierPath, true);
                var tax = Path.GetFileName(supplierPath);
                if (tax != ValidateTaxCode(tax)) continue;
                for (var m = 1; m <= 12; m++)
                {
                    if (month.HasValue && month != m) continue;
                    var folder = ResolvePartition(y, tax, m);
                    if (!Directory.Exists(folder)) continue;
                    EnsureSafeExistingPath(folder, true);
                    foreach (var group in Directory.EnumerateFiles(folder).Where(IsSupportedFile).GroupBy(GroupStem, StringComparer.OrdinalIgnoreCase))
                    {
                        ct.ThrowIfCancellationRequested();
                        InputInvoicePickerCandidateDto dto;
                        try { dto = (await BuildCandidateAsync(y, m, tax, group.Key, group, ct)).Dto; }
                        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                        { dto = new InputInvoicePickerCandidateDto { XmlValid = false, SourceSupplierTaxCode = tax }; }
                        yield return dto;
                    }
                }
            }
        }
    }

    // Caller validates tenant ownership before writing. Generated names cannot escape the library.
    public async Task<InputInvoicePickerCandidateDto> StoreCatalogAsync(byte[] xml, byte[]? pdf, CancellationToken ct)
    {
        EnsureEnabled();
        var invoice = _parser.Parse(xml);
        var tax = ValidateTaxCode(invoice.SellerTaxCode!);
        var date = invoice.InvoiceDate!.Value;
        var folder = ResolvePartition(date.Year, tax, date.Month);
        if (Directory.Exists(folder)) EnsureSafeExistingPath(folder, true);
        Directory.CreateDirectory(folder);
        var stem = "upload_" + Convert.ToHexString(SHA256.HashData(xml)).ToLowerInvariant();
        var xmlPath = Path.Combine(folder, stem + ".xml");
        await WriteNewAsync(xmlPath, xml, ct);
        if (pdf is { Length: > 0 })
        {
            if (pdf.Length > MaximumPdfBytes || pdf.Length < 5 || System.Text.Encoding.ASCII.GetString(pdf, 0, 5) != "%PDF-")
                throw new BusinessRuleException("File đính kèm không phải PDF hợp lệ hoặc quá 50 MB.");
            await WriteNewAsync(Path.Combine(folder, stem + ".pdf"), pdf, ct);
        }
        return await ResolveAsync(tax, $"{date.Year:0000}:{date.Month:00}:{stem}", ct);
    }

    private static async Task WriteNewAsync(string path, byte[] content, CancellationToken ct)
    {
        if (File.Exists(path))
        {
            EnsureSafeExistingPath(path, false);
            if (!SHA256.HashData(await File.ReadAllBytesAsync(path, ct)).SequenceEqual(SHA256.HashData(content)))
                throw new BusinessRuleException("File cùng tên đã có nội dung khác. Không ghi đè hóa đơn.");
            return;
        }
        // Atomic publication prevents readers seeing a partially uploaded XML/PDF.
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, content, ct);
            File.Move(temporary, path, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
