using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Common.Options;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using Microsoft.Extensions.Options;

namespace GaoApp.Infrastructure.Services.Invoices;

public sealed class FileSystemInputInvoiceDocumentLibrary
    : IInputInvoiceDocumentLibrary
{
    private const int MaximumPdfBytes = 50 * 1024 * 1024;
    private readonly InputInvoiceLibraryOptions _options;
    private readonly IInputInvoiceXmlDocumentParser _parser;

    public FileSystemInputInvoiceDocumentLibrary(
        IOptions<InputInvoiceLibraryOptions> options,
        IInputInvoiceXmlDocumentParser parser)
    {
        _options = options.Value;
        _parser = parser;
    }

    public async Task<IReadOnlyList<InputInvoicePickerCandidateDto>> BrowseAsync(
        string normalizedSupplierTaxCode,
        InputInvoicePickerBrowseRequest request,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        var taxCode = ValidateTaxCode(normalizedSupplierTaxCode);
        var partitions = ResolvePartitions(request);
        var physical = new List<PhysicalCandidate>();
        var remaining = EffectiveMaximumCandidates();

        foreach (var partition in partitions)
        {
            ct.ThrowIfCancellationRequested();
            if (remaining <= 0) break;
            var folder = ResolvePartition(partition.Year, taxCode, partition.Month);
            if (!Directory.Exists(folder)) continue;
            EnsureSafeExistingPath(folder, expectDirectory: true);

            var maximumFiles = Math.Max(remaining * 4, 4);
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(IsSupportedFile)
                .Take(maximumFiles)
                .ToList();
            foreach (var group in files.GroupBy(GroupStem, StringComparer.OrdinalIgnoreCase)
                         .OrderByDescending(x => x.Key, StringComparer.OrdinalIgnoreCase)
                         .Take(remaining))
            {
                var candidate = await BuildCandidateAsync(
                    partition.Year, partition.Month, taxCode, group.Key, group, ct);
                if (MatchesFilters(candidate.Dto, request))
                    physical.Add(candidate);
            }
            remaining = EffectiveMaximumCandidates() - physical.Count;
        }

        return physical
            .GroupBy(BusinessDedupeKey, StringComparer.Ordinal)
            .Select(ChooseBest)
            .OrderByDescending(x => x.Dto.InvoiceDate)
            .ThenByDescending(x => x.Dto.DocumentKey, StringComparer.OrdinalIgnoreCase)
            .Take(EffectiveMaximumCandidates())
            .Select(x => x.Dto)
            .ToList();
    }

    public async Task<InputInvoicePickerCandidateDto> ResolveAsync(
        string normalizedSupplierTaxCode,
        string documentKey,
        CancellationToken ct = default)
        => (await ResolvePhysicalAsync(normalizedSupplierTaxCode, documentKey, ct)).Dto;

    public async Task<InputInvoicePdfPreviewDto> GetPdfAsync(
        string normalizedSupplierTaxCode,
        string documentKey,
        CancellationToken ct = default)
    {
        var candidate = await ResolvePhysicalAsync(normalizedSupplierTaxCode, documentKey, ct);
        if (candidate.PdfPath is null)
            throw new BusinessRuleException("Hóa đơn không có file PDF để xem trước.");
        EnsureSafeExistingPath(candidate.PdfPath, expectDirectory: false);
        var info = new FileInfo(candidate.PdfPath);
        if (info.Length > MaximumPdfBytes)
            throw new BusinessRuleException("File PDF vượt quá giới hạn xem trước.");
        return new InputInvoicePdfPreviewDto
        {
            Content = await File.ReadAllBytesAsync(candidate.PdfPath, ct),
            FileName = info.Name
        };
    }

    public async Task<byte[]> GetXmlBytesAsync(
        string normalizedSupplierTaxCode,
        string documentKey,
        CancellationToken ct = default)
    {
        var candidate = await ResolvePhysicalAsync(normalizedSupplierTaxCode, documentKey, ct);
        if (!candidate.Dto.XmlValid || candidate.SelectedXmlPath is null)
            throw new BusinessRuleException(
                candidate.Dto.SelectionBlockMessage ?? "Hóa đơn không có XML hợp lệ.");
        EnsureSafeExistingPath(candidate.SelectedXmlPath, expectDirectory: false);
        return await File.ReadAllBytesAsync(candidate.SelectedXmlPath, ct);
    }

    private async Task<PhysicalCandidate> ResolvePhysicalAsync(
        string normalizedSupplierTaxCode,
        string documentKey,
        CancellationToken ct)
    {
        EnsureEnabled();
        var taxCode = ValidateTaxCode(normalizedSupplierTaxCode);
        var (year, month, stem) = ParseDocumentKey(documentKey);
        var folder = ResolvePartition(year, taxCode, month);
        if (!Directory.Exists(folder))
            throw new BusinessRuleException("Không tìm thấy hóa đơn trong thư viện.");
        EnsureSafeExistingPath(folder, expectDirectory: true);
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedFile)
            .Where(path => string.Equals(GroupStem(path), stem, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();
        if (files.Count == 0)
            throw new BusinessRuleException("Không tìm thấy hóa đơn trong thư viện.");
        return await BuildCandidateAsync(year, month, taxCode, stem, files, ct);
    }

    private async Task<PhysicalCandidate> BuildCandidateAsync(
        int year,
        int month,
        string taxCode,
        string stem,
        IEnumerable<string> paths,
        CancellationToken ct)
    {
        var files = paths.ToList();
        foreach (var path in files) EnsureSafeExistingPath(path, expectDirectory: false);
        var pdf = files.FirstOrDefault(x => Path.GetExtension(x)
            .Equals(".pdf", StringComparison.OrdinalIgnoreCase));
        var normalXml = files.FirstOrDefault(x =>
            Path.GetExtension(x).Equals(".xml", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetFileNameWithoutExtension(x).EndsWith(
                "_misa_original", StringComparison.OrdinalIgnoreCase));
        var originalXml = files.FirstOrDefault(x =>
            Path.GetExtension(x).Equals(".xml", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileNameWithoutExtension(x).EndsWith(
                "_misa_original", StringComparison.OrdinalIgnoreCase));

        var normal = await TryParseAsync(normalXml, ct);
        var original = await TryParseAsync(originalXml, ct);
        var conflict = normal.Invoice is not null && original.Invoice is not null &&
                       !SameIdentity(normal.Invoice, original.Invoice);
        var selected = conflict ? null : normal.Invoice is not null ? normal : original;
        var invoice = selected?.Invoice;
        var folderMatches = invoice is null || string.Equals(
            TaxCodeIdentityNormalizer.Normalize(invoice.SellerTaxCode),
            taxCode,
            StringComparison.Ordinal);
        var xmlValid = invoice is not null && !conflict;
        var allowed = xmlValid && folderMatches;
        var reasonCode = allowed ? null : conflict ? "ConflictingXmlVariants"
            : invoice is null ? "MissingOrInvalidXml" : "SellerTaxCodeMismatch";
        var reason = allowed ? null : conflict
            ? "Các biến thể XML có định danh hóa đơn mâu thuẫn."
            : invoice is null
                ? "Thiếu XML hợp lệ — chỉ có thể xem PDF."
                : "MST người bán trên XML không khớp thư mục nhà cung cấp.";

        return new PhysicalCandidate
        {
            PdfPath = pdf,
            SelectedXmlPath = selected?.Path,
            HasNormalXml = normal.Invoice is not null,
            HasOriginalXml = original.Invoice is not null,
            Dto = new InputInvoicePickerCandidateDto
            {
                DocumentKey = $"{year:0000}:{month:00}:{stem}",
                InvoiceDate = invoice?.InvoiceDate,
                InvoiceTemplateCode = invoice?.InvoiceTemplateCode,
                InvoiceSeries = invoice?.InvoiceSeries,
                InvoiceNumber = invoice?.InvoiceNumber,
                SellerTaxCode = invoice?.SellerTaxCode,
                SellerName = invoice?.SellerName,
                BuyerTaxCode = invoice?.BuyerTaxCode,
                TotalPaymentAmount = invoice?.TotalPaymentAmount ?? 0,
                DetailCount = invoice?.Details.Count ?? 0,
                HasPdf = pdf is not null,
                HasXml = normalXml is not null || originalXml is not null,
                XmlValid = xmlValid,
                FolderTaxCodeMatches = folderMatches,
                SelectionAllowed = allowed,
                SelectionBlockReasonCode = reasonCode,
                SelectionBlockMessage = reason
            }
        };
    }

    private async Task<ParsedFile> TryParseAsync(string? path, CancellationToken ct)
    {
        if (path is null) return new ParsedFile(null, null);
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            return new ParsedFile(path, _parser.Parse(bytes));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return new ParsedFile(path, null); }
    }

    private IReadOnlyList<(int Year, int Month)> ResolvePartitions(
        InputInvoicePickerBrowseRequest request)
    {
        var maximum = Math.Clamp(_options.MaxMonthPartitions, 1, 12);
        if (request.FromDate.HasValue || request.ToDate.HasValue)
        {
            var start = new DateTime((request.FromDate ?? request.ToDate)!.Value.Year,
                (request.FromDate ?? request.ToDate)!.Value.Month, 1);
            var end = new DateTime((request.ToDate ?? request.FromDate)!.Value.Year,
                (request.ToDate ?? request.FromDate)!.Value.Month, 1);
            if (start > end) throw new BusinessRuleException("Khoảng ngày tìm kiếm không hợp lệ.");
            var result = new List<(int, int)>();
            for (var current = start; current <= end && result.Count <= maximum;
                 current = current.AddMonths(1))
                result.Add((current.Year, current.Month));
            if (result.Count > maximum)
                throw new BusinessRuleException($"Chỉ được tìm tối đa {maximum} tháng mỗi lần.");
            result.Reverse();
            return result;
        }

        var year = request.Year ?? DateTime.UtcNow.Year;
        if (year is < 2000 or > 9999)
            throw new BusinessRuleException("Năm tìm kiếm không hợp lệ.");
        if (request.Month.HasValue)
        {
            if (request.Month is < 1 or > 12)
                throw new BusinessRuleException("Tháng tìm kiếm không hợp lệ.");
            return [(year, request.Month.Value)];
        }
        return Enumerable.Range(1, 12).Reverse().Take(maximum)
            .Select(month => (year, month)).ToList();
    }

    private string ResolvePartition(int year, string taxCode, int month)
    {
        var root = RootFullPath();
        var yearFolder = Path.GetFullPath(Path.Combine(root, year.ToString("0000")));
        var supplierFolder = Path.GetFullPath(Path.Combine(yearFolder, taxCode));
        var candidate = Path.GetFullPath(Path.Combine(supplierFolder, month.ToString("00")));
        EnsureInsideRoot(root, candidate);
        if (Directory.Exists(yearFolder))
            EnsureSafeExistingPath(yearFolder, expectDirectory: true);
        if (Directory.Exists(supplierFolder))
            EnsureSafeExistingPath(supplierFolder, expectDirectory: true);
        return candidate;
    }

    private string RootFullPath()
    {
        if (string.IsNullOrWhiteSpace(_options.RootPath) ||
            !Path.IsPathFullyQualified(_options.RootPath))
            throw new BusinessRuleException("InvoiceRoot chưa được cấu hình hợp lệ.");
        var root = Path.GetFullPath(_options.RootPath);
        if (!Directory.Exists(root))
            throw new BusinessRuleException("InvoiceRoot không tồn tại.");
        EnsureSafeExistingPath(root, expectDirectory: true);
        return root;
    }

    private static void EnsureInsideRoot(string root, string candidate)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(prefix, comparison))
            throw new BusinessRuleException("Đường dẫn hóa đơn nằm ngoài InvoiceRoot.");
    }

    private static void EnsureSafeExistingPath(string path, bool expectDirectory)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new BusinessRuleException("Thư viện hóa đơn chứa đường dẫn reparse không an toàn.");
        if (expectDirectory && !attributes.HasFlag(FileAttributes.Directory))
            throw new BusinessRuleException("Đường dẫn thư viện hóa đơn không hợp lệ.");
    }

    private static (int Year, int Month, string Stem) ParseDocumentKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Path.IsPathRooted(key) ||
            key.Contains('/') || key.Contains('\\') || key.Contains("..", StringComparison.Ordinal))
            throw new BusinessRuleException("Mã tài liệu hóa đơn không hợp lệ.");
        var parts = key.Split(':', StringSplitOptions.None);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var month) || year is < 2000 or > 9999 ||
            month is < 1 or > 12 || string.IsNullOrWhiteSpace(parts[2]) ||
            parts[2].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new BusinessRuleException("Mã tài liệu hóa đơn không hợp lệ.");
        return (year, month, parts[2]);
    }

    private static string ValidateTaxCode(string value)
    {
        var normalized = TaxCodeIdentityNormalizer.Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.All(char.IsLetterOrDigit))
            throw new BusinessRuleException("MST nhà cung cấp không phù hợp để mở thư viện.");
        return normalized;
    }

    private void EnsureEnabled()
    {
        if (!_options.Enabled)
            throw new BusinessRuleException("Thư viện hóa đơn chưa được bật cho môi trường này.");
    }

    private int EffectiveMaximumCandidates() => Math.Clamp(_options.MaxCandidates, 1, 200);
    private static bool IsSupportedFile(string path) =>
        Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase);
    private static string GroupStem(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        return stem.EndsWith("_misa_original", StringComparison.OrdinalIgnoreCase)
            ? stem[..^"_misa_original".Length] : stem;
    }

    private static bool SameIdentity(InputInvoiceHead left, InputInvoiceHead right) =>
        left.NormalizedSellerTaxCode == right.NormalizedSellerTaxCode &&
        left.NormalizedInvoiceSeries == right.NormalizedInvoiceSeries &&
        left.NormalizedInvoiceNumber == right.NormalizedInvoiceNumber &&
        left.InvoiceIdentityDate == right.InvoiceIdentityDate;

    private static string BusinessDedupeKey(PhysicalCandidate value)
    {
        var dto = value.Dto;
        if (!dto.XmlValid)
            return $"physical:{dto.DocumentKey.ToUpperInvariant()}";
        return $"business:{TaxCodeIdentityNormalizer.Normalize(dto.SellerTaxCode)}|" +
               $"{InputInvoiceIdentityPolicy.NormalizeIdentityToken(dto.InvoiceSeries)}|" +
               $"{InputInvoiceIdentityPolicy.NormalizeIdentityToken(dto.InvoiceNumber)}|" +
               $"{dto.InvoiceDate:yyyy-MM-dd}";
    }

    private static PhysicalCandidate ChooseBest(IGrouping<string, PhysicalCandidate> group) =>
        group.OrderByDescending(x => x.HasNormalXml)
            .ThenByDescending(x => x.Dto.HasPdf)
            .ThenByDescending(x => x.HasOriginalXml)
            .ThenBy(x => x.Dto.DocumentKey, StringComparer.OrdinalIgnoreCase)
            .First();

    private static bool MatchesFilters(
        InputInvoicePickerCandidateDto candidate,
        InputInvoicePickerBrowseRequest request)
    {
        if (request.FromDate.HasValue && candidate.InvoiceDate < request.FromDate.Value.Date) return false;
        if (request.ToDate.HasValue && candidate.InvoiceDate >= request.ToDate.Value.Date.AddDays(1)) return false;
        if (string.IsNullOrWhiteSpace(request.Search)) return true;
        var search = request.Search.Trim();
        return (candidate.InvoiceSeries?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (candidate.InvoiceNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
               candidate.DocumentKey.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PhysicalCandidate
    {
        public InputInvoicePickerCandidateDto Dto { get; init; } = new();
        public string? PdfPath { get; init; }
        public string? SelectedXmlPath { get; init; }
        public bool HasNormalXml { get; init; }
        public bool HasOriginalXml { get; init; }
    }

    private sealed record ParsedFile(string? Path, InputInvoiceHead? Invoice);
}
