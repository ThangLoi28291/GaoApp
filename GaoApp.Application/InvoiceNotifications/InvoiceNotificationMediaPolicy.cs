using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;


namespace GaoApp.Application.InvoiceNotifications;

/// <summary>Shared upload and transport policy; caller supplied MIME types are never trusted.</summary>
public static class InvoiceNotificationMediaPolicy
{
    private static readonly byte[] Png = [137,80,78,71,13,10,26,10];
    private static readonly byte[] Ole = [208,207,17,224,161,177,26,225];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public const int MaximumZipEntries = 1000;
    public const long MaximumExpandedZipBytes = 100L * 1024 * 1024;
    public const long MaximumExpandedEntryBytes = 50L * 1024 * 1024;

    public static string NormalizeFileName(string fileName)
    {
        var submitted = fileName?.Trim();
        var safe = submitted?.Normalize(NormalizationForm.FormC);
        if (string.IsNullOrWhiteSpace(safe) || safe.Length > InvoiceNotificationMediaLimits.MaximumFileNameLength ||
            !string.Equals(safe, Path.GetFileName(safe), StringComparison.Ordinal) ||
            safe.IndexOfAny(['/', '\\', ':', '<', '>', '"', '|', '?', '*']) >= 0 ||
            safe.Any(char.IsControl) || safe.EndsWith('.') || safe.EndsWith(' ') ||
            IsReserved(Path.GetFileNameWithoutExtension(safe).Split('.')[0]))
            throw new InvalidDataException("Asset filename is invalid.");
        return safe;
    }

    private static bool IsReserved(string value) => value.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" or
        "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or
        "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9";

    public static string Validate(string kind, string fileName, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var name = NormalizeFileName(fileName);
        if (kind is not ("Image" or "File")) throw new ArgumentOutOfRangeException(nameof(kind));
        if (bytes.Length == 0 || bytes.LongLength > (kind == "Image" ? InvoiceNotificationMediaLimits.MaximumImageBytes : InvoiceNotificationMediaLimits.MaximumFileBytes))
            throw new InvalidDataException("Asset is empty or exceeds the allowed size.");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (kind == "Image")
            return extension switch
            {
                ".png" when bytes.AsSpan().StartsWith(Png) => "image/png",
                ".jpg" or ".jpeg" when bytes.AsSpan().StartsWith(new byte[] {255,216,255}) => "image/jpeg",
                _ => throw new InvalidDataException("Only valid JPG, JPEG or PNG images are allowed.")
            };
        return extension switch
        {
            ".pdf" when bytes.AsSpan().StartsWith("%PDF-"u8) => "application/pdf",
            ".doc" when bytes.AsSpan().StartsWith(Ole) && bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes("WordDocument")) >= 0 => "application/msword",
            ".xls" when bytes.AsSpan().StartsWith(Ole) && (bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes("Workbook")) >= 0 || bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes("Book")) >= 0) => "application/vnd.ms-excel",
            ".docx" when ValidateZip(bytes, "word/") => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" when ValidateZip(bytes, "xl/") => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xml" when ValidateXml(bytes) => "application/xml",
            ".zip" when ValidateZip(bytes, null) => "application/zip",
            _ => throw new InvalidDataException("Only valid PDF, DOC, DOCX, XLS, XLSX, XML or ZIP files are allowed.")
        };
    }

    private static bool ValidateXml(byte[] bytes)
    {
        try
        {
            var text = StrictUtf8.GetString(bytes.AsSpan().StartsWith(new byte[] {239,187,191}) ? bytes.AsSpan(3) : bytes);
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = InvoiceNotificationMediaLimits.MaximumFileBytes,
                MaxCharactersFromEntities = 1024, ConformanceLevel = ConformanceLevel.Document
            });
            var count = 0;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.XmlDeclaration && reader.GetAttribute("encoding") is { } encoding &&
                    !encoding.Equals("utf-8", StringComparison.OrdinalIgnoreCase) && !encoding.Equals("utf8", StringComparison.OrdinalIgnoreCase)) return false;
                if (reader.Depth >= 64 || (reader.NodeType == XmlNodeType.Element && ++count > 100000)) return false;
            }
            return count > 0;
        }
        catch (Exception error) when (error is XmlException or DecoderFallbackException) { return false; }
    }

    private static bool ValidateZip(byte[] bytes, string? officePrefix)
    {
        try
        {
            // Inspect the actual central directory to reject encrypted and ZIP64 packages,
            // even when ZipArchive could otherwise open their directory without reading data.
            var end = -1;
            for (var i = bytes.Length - 22; i >= Math.Max(0, bytes.Length - 65557); i--)
                if (U32(bytes, i) == 0x06054b50 && i + 22 + U16(bytes, i + 20) == bytes.Length) { end = i; break; }
            if (end < 0 || U16(bytes, end + 4) != 0 || U16(bytes, end + 6) != 0) return false;
            var count = U16(bytes, end + 10);
            var size = U32(bytes, end + 12);
            var offset = U32(bytes, end + 16);
            if (count is 0 or > MaximumZipEntries || U16(bytes, end + 8) != count || size == uint.MaxValue || offset == uint.MaxValue || (long)offset + size != end) return false;
            long expanded = 0;
            var position = checked((int)offset);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < count; index++)
            {
                if (position + 46 > end || U32(bytes, position) != 0x02014b50) return false;
                var flags = U16(bytes, position + 8);
                var method = U16(bytes, position + 10);
                var compressed = U32(bytes, position + 20);
                var uncompressed = U32(bytes, position + 24);
                var nameLength = U16(bytes, position + 28);
                var extraLength = U16(bytes, position + 30);
                var commentLength = U16(bytes, position + 32);
                var attributes = U32(bytes, position + 38);
                var local = U32(bytes, position + 42);
                var next = checked(position + 46 + nameLength + extraLength + commentLength);
                if (next > end || nameLength == 0 || (flags & 0x41) != 0 || method is not (0 or 8) ||
                    compressed == uint.MaxValue || uncompressed > MaximumExpandedEntryBytes || local == uint.MaxValue ||
                    (uncompressed > 0 && (compressed == 0 || (long)uncompressed > (long)compressed * 1000)) ||
                    (expanded += uncompressed) > MaximumExpandedZipBytes || (attributes & 0x400) != 0 || ((attributes >> 16) & 0xF000) == 0xA000) return false;
                var name = StrictUtf8.GetString(bytes, position + 46, nameLength);
                if (!SafeZipName(name) || !names.Add(name)) return false;
                var extraEnd = position + 46 + nameLength + extraLength;
                for (var e = position + 46 + nameLength; e < extraEnd;)
                {
                    if (e + 4 > extraEnd || U16(bytes, e) == 1) return false;
                    e += 4 + U16(bytes, e + 2);
                    if (e > extraEnd) return false;
                }
                if ((long)local + 30 > offset || U32(bytes, (int)local) != 0x04034b50 ||
                    U16(bytes, (int)local + 6) != flags || U16(bytes, (int)local + 8) != method) return false;
                var localNameLength = U16(bytes, (int)local + 26);
                var localExtraLength = U16(bytes, (int)local + 28);
                if (localNameLength != nameLength || (long)local + 30 + localNameLength + localExtraLength + compressed > offset ||
                    !bytes.AsSpan((int)local + 30, localNameLength).SequenceEqual(bytes.AsSpan(position + 46, nameLength))) return false;
                position = next;
            }
            if (position != end) return false;
            using var input = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);
            if (archive.Entries.Count != count || (officePrefix is not null && (archive.GetEntry("[Content_Types].xml") is null || !archive.Entries.Any(x => x.FullName.StartsWith(officePrefix, StringComparison.OrdinalIgnoreCase))))) return false;
            var buffer = new byte[81920];
            long actualTotal = 0;
            foreach (var entry in archive.Entries)
            {
                using var content = entry.Open();
                long actual = 0;
                int read;
                while ((read = content.Read(buffer)) != 0)
                    if ((actual += read) > MaximumExpandedEntryBytes || (actualTotal += read) > MaximumExpandedZipBytes || actual > entry.Length) return false;
                if (actual != entry.Length) return false;
            }
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or OverflowException or DecoderFallbackException) { return false; }
    }

    private static bool SafeZipName(string name)
    {
        if (name.StartsWith('/') || name.Contains('\\') || name.Contains(':') || name.Any(char.IsControl)) return false;
        var path = name.EndsWith('/') ? name[..^1] : name;
        return path.Split('/').All(x => x.Length > 0 && x is not ("." or "..") && !x.EndsWith('.') && !x.EndsWith(' '));
    }
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
}

internal static class InvoiceNotificationMediaLimits { public const int MaximumFileNameLength=120; public const long MaximumImageBytes=10L*1024*1024; public const long MaximumFileBytes=20L*1024*1024; }
