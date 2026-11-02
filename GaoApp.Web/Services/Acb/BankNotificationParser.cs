using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GaoApp.Web.Services.Acb;

public sealed record BankNotificationData(decimal Amount, int? OrderId, string? QrCode,
    string? SenderName, string TransactionCode)
{
    public string[] BankReferences { get; init; } = [];
    public DateTime? BankOccurredAtUtc { get; init; }
}

public static class BankNotificationParser
{
    private static readonly Regex Money = new(@"([+-])\s*([0-9][0-9., ]*)\s*VND\b", RegexOptions.CultureInvariant);
    private static readonly Regex Pos = new(@"(?<![A-Z0-9])POS\s*-?\s*([0-9]{1,10})(?![A-Z0-9]|\s*(?:\.\.\.|…))");
    private static readonly Regex PosQr = new(@"(?<![A-Z0-9])POS\s*-?\s*([0-9]{1,10})\s*-?\s*QR\s*-?\s*([0-9]{1,10})\s*-?\s*([0-9A-F]{32})(?![A-Z0-9]|\s*(?:\.\.\.|…))");
    private static readonly Regex Qr = new(@"\bQR-([0-9]{1,10})-([0-9A-F]{32})(?![A-Z0-9]|\s*(?:\.\.\.|…))");
    private static readonly Regex WebReference = new(@"(?<![A-Z0-9])W[0-9]{12}(?![A-Z0-9]|\s*(?:\.\.\.|…))");
    private static readonly Regex Reference = new(@"\bFT[0-9A-Z]{6,40}\b|\bGD\s*[-:]?\s*([0-9A-Z]{8,40})\b|\bMGD\s*[:.]?\s*([0-9A-Z]{3,40})\b");
    private static readonly Regex BankTime = new(@"(?<![0-9])([0-9]{6}-[0-9]{2}:[0-9]{2}:[0-9]{2})(?![0-9])");
    private static string FoldBase(string value) => Regex.Replace(Regex.Replace(value.Normalize(NormalizationForm.FormKD), @"\p{M}|\p{Cf}", ""), @"[\p{Z}\s]+", " ")
        .Replace('đ', 'd').Replace('Đ', 'D').Replace('−', '-').ToUpperInvariant();
    public static string Fold(string value) => Regex.Replace(FoldBase(value), @"\p{Pd}", "-");
    public static bool Sensitive(string text) => Regex.IsMatch(Fold(text), @"\b(?:OTP|SAFEKEY|PASSCODE)\b|MA XAC THUC|MA BAO MAT|MA DANG NHAP|VERIFICATION CODE|ONE.?TIME PASSWORD");
    public static BankNotificationData? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 12000 || Sensitive(content)) return null;
        var text = Fold(content);
        if (text.Contains("DA AN NOI DUNG THONG BAO NHAY CAM") || text.Contains("CONTENT HIDDEN")) return null;
        // Restore separators only for a complete POS/QR pair. Keep the original
        // content as evidence; malformed or truncated notes must not gain an ID.
        var joined = PosQr.Matches(text);
        if (joined.Any(m => m.Groups[1].Value != m.Groups[2].Value)) return null;
        text = PosQr.Replace(text, m => $"POS-{m.Groups[1].Value} QR-{m.Groups[2].Value}-{m.Groups[3].Value}");
        var values = new HashSet<decimal>();
        foreach (Match m in Money.Matches(text))
        {
            if (m.Index > 0 && char.IsLetterOrDigit(text[m.Index - 1])) continue;
            var raw = m.Groups[2].Value.Trim();
            if (m.Groups[1].Value != "+" || !TryWholeVnd(raw, out var amount)) return null;
            values.Add(amount);
        }
        if (values.Count != 1) return null;
        var ids = Pos.Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();
        if (ids.Count > 1 || ids.Count == 1 && (!int.TryParse(ids[0], out var id) || id <= 0)) return null;
        int? order = ids.Count == 1 ? int.Parse(ids[0], CultureInfo.InvariantCulture) : null;
        var qrMatches = Qr.Matches(text).Select(m => m.Value).Distinct().ToList();
        if (qrMatches.Count > 1) return null;
        var qr = qrMatches.SingleOrDefault();
        var webRefs=WebReference.Matches(text).Select(m=>m.Value).Distinct().ToList();
        if(webRefs.Count>1||webRefs.Count>0&&(qr is not null||order.HasValue))return null;
        if(webRefs.Count==1)qr=webRefs[0];
        if (qr != null && order.HasValue && Qr.Match(qr).Groups[1].Value != order.Value.ToString(CultureInfo.InvariantCulture)) return null;
        // Prefer a globally stronger bank reference; MGD is scoped to account and business date below.
        var refs = Reference.Matches(text).ToList();
        var preferred = refs.Where(m => m.Value.StartsWith("FT", StringComparison.Ordinal)).Select(m => m.Value).Distinct().ToList();
        if (preferred.Count == 0) preferred = refs.Where(m => m.Value.StartsWith("GD", StringComparison.Ordinal)).Select(m => "GD:" + m.Groups[1].Value).Distinct().ToList();
        if (preferred.Count > 1) return null;
        var transaction = preferred.SingleOrDefault();
        if (transaction == null)
        {
            var mgd = refs.Select(m => m.Groups[2].Value).Where(x => x.Length > 0).Distinct().ToList();
            if (mgd.Count != 1) return null;
            transaction = "MGD:" + mgd[0];
        }
        var times = BankTime.Matches(text).Select(m => m.Groups[1].Value).Distinct().ToArray();
        DateTime? occurred = null;
        if (times.Length > 1) return null;
        if (times.Length == 1)
        {
            // The bank's business timestamp is in Vietnam time. A repost on Android
            // must not move old money into the waiting window of a later order.
            var value = times[0];
            var fullYear = value[..4] + "20" + value[4..];
            if (!DateTime.TryParseExact(fullYear, "ddMMyyyy-HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var localTime)) return null;
            occurred = DateTime.SpecifyKind(localTime.AddHours(-7), DateTimeKind.Utc);
        }
        return new(values.Single(), order, qr, ReadSender(content), transaction)
        {
            BankReferences = refs.Select(m => m.Value.StartsWith("FT", StringComparison.Ordinal) ? m.Value :
                m.Groups[1].Success ? "GD:" + m.Groups[1].Value : "MGD:" + m.Groups[2].Value).Distinct().ToArray(),
            BankOccurredAtUtc = occurred
        };
    }
    private static bool TryWholeVnd(string raw, out decimal amount)
    {
        // Android already accepts explicit zero fractions. Remove only those;
        // values with nonzero fractions or inconsistent grouping stay invalid.
        if (Regex.IsMatch(raw, @"^[0-9]{1,3}(?:\.[0-9]{3})+,00$|^[0-9]{1,3}(?:,[0-9]{3})+\.00$|^[0-9]+[.,]00$|^[0-9]{1,3}(?: [0-9]{3})+[.,]00$"))
            raw = raw[..^3];
        amount = 0;
        return Regex.IsMatch(raw, @"^(?:[0-9]+|[0-9]{1,3}(?:\.[0-9]{3})+|[0-9]{1,3}(?:,[0-9]{3})+|[0-9]{1,3}(?: [0-9]{3})+)$") &&
            decimal.TryParse(Regex.Replace(raw, @"[., ]", ""), NumberStyles.None, CultureInfo.InvariantCulture, out amount) &&
            amount > 0 && amount < 10000000000000000m;
    }
    private static string? ReadSender(string content)
    {
        var normalized = Regex.Replace(Regex.Replace(content.Normalize(NormalizationForm.FormKC), @"\p{Cf}", ""), @"[\p{Z}\s]+", " ");
        // In the bank template, accented 'tại' marks the bank, while TAI can be
        // part of the person's name. Preserve that name instead of cutting it.
        var accented = Regex.Matches(normalized, @"\b(?:từ|tu)\s+(.+?)\s+tại\b", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (accented.Length > 0) return accented.Length == 1 && accented[0].Length <= 240 ? accented[0] : null;
        var from = Regex.Match(normalized, @"\b(?:từ|tu)\s+", RegexOptions.IgnoreCase);
        if (!from.Success) return null;
        var tail = normalized[(from.Index + from.Length)..];
        var delimiters = Regex.Matches(tail, @"\btai\b", RegexOptions.IgnoreCase);
        if (delimiters.Count != 1) return null; // Fully unaccented names must not be guessed.
        var sender = tail[..delimiters[0].Index].Trim();
        return sender.Length is > 0 and <= 240 ? sender : null;
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    // Existing configured devices/waits retain their exact account identity.
    public static string AccountFingerprint(GaoApp.Domain.Entities.StoreBankAccount bank) => Hash(FoldBase(bank.BankCode + "|" + bank.AccountNumber).Replace(" ", ""));
}
