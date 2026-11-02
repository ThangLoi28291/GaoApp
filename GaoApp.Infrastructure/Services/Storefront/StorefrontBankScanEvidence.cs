using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

internal sealed record StorefrontBankScanRange(DateTime From, DateTime To);
internal sealed record StorefrontBankScanWindow(StorefrontBankScanRange Range, int TotalPages, IReadOnlyList<string> PageFingerprints);
internal sealed record StorefrontBankScanResult(int ProtocolVersion, int AttemptId, int PaymentIntentId, string Reference,
    string AccountIdentity, string BindingFingerprint, IReadOnlyList<StorefrontBankScanWindow> Windows,
    IReadOnlyList<StorefrontBankScanRange> Coverage, int? PreviousCompletionId, string? PreviousFingerprint);
internal sealed record StorefrontUnpaidScanResult(bool Unpaid, DateTime? RescanFrom = null);

internal static class StorefrontBankScanEvidence
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static async Task<StorefrontBankScanAttempt> StartAsync(AppDbContext db, StorefrontPaymentIntent intent,
        StorefrontBankBinding binding, IReadOnlyList<StorefrontBankScanRange> windows, TimeProvider clock, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Start bank scan outside the business transaction.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await GlobalBankReceiptLedger.LockAsync(db, "GaoApp:BankAccountEvidence:" + binding.AccountIdentity, ct);
        var attempt = new StorefrontBankScanAttempt { StoreId = intent.StoreId, StorefrontOrderId = intent.StorefrontOrderId,
            PaymentIntentId = intent.Id, AccountIdentity = binding.AccountIdentity, BindingFingerprint = GlobalBankReceiptLedger.Hash(intent.BindingSnapshotJson),
            StartedAtUtc = clock.GetUtcNow().UtcDateTime, RequestedWindowsJson = JsonSerializer.Serialize(windows, Json) };
        db.Add(attempt); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return attempt;
    }
    internal static async Task CompleteAsync(AppDbContext db, AcbProtocolCore protocol, StorefrontPaymentIntent intent,
        StorefrontBankScanAttempt attempt, IReadOnlyList<StorefrontBankScanWindow> windows, TimeProvider clock, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Complete bank scan independently of allocation.");
        var requested = JsonSerializer.Deserialize<List<StorefrontBankScanRange>>(attempt.RequestedWindowsJson, Json) ?? [];
        if (requested.Count != windows.Count || !requested.SequenceEqual(windows.Select(x => x.Range)) || windows.Any(x =>
            x.TotalPages < 0 || x.PageFingerprints.Count != Math.Max(1, x.TotalPages) || x.PageFingerprints.Any(p => p.Length != 64)))
            throw new InvalidOperationException("Incomplete bank scan cannot produce completion evidence.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await GlobalBankReceiptLedger.LockAsync(db, "GaoApp:BankAccountEvidence:" + attempt.AccountIdentity, ct);
        var latest = await db.Set<StorefrontBankScanAttempt>().AsNoTracking().Where(x => x.StoreId == intent.StoreId && x.PaymentIntentId == intent.Id)
            .OrderByDescending(x => x.Id).Select(x => x.Id).FirstAsync(ct);
        if (latest != attempt.Id) throw new InvalidOperationException("A newer bank scan has started.");
        var previous = await db.Set<StorefrontBankScanCompletion>().AsNoTracking().Where(x => x.StoreId == intent.StoreId && x.PaymentIntentId == intent.Id)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        var coverage = windows.Select(x => x.Range).ToList();
        if (previous is not null)
        {
            var old = Decode(protocol, intent, previous);
            if (old.AccountIdentity != attempt.AccountIdentity || old.BindingFingerprint != attempt.BindingFingerprint)
                throw new InvalidOperationException("Bank scan identity changed; do not merge different sources.");
            coverage.AddRange(old.Coverage);
        }
        var result = new StorefrontBankScanResult(1, attempt.Id, intent.Id, intent.ProviderReference, attempt.AccountIdentity,
            attempt.BindingFingerprint, windows, Merge(coverage), previous?.Id, previous?.Fingerprint);
        var raw = JsonSerializer.Serialize(result, Json);
        db.Add(new StorefrontBankScanCompletion { StoreId = intent.StoreId, StorefrontOrderId = intent.StorefrontOrderId,
            PaymentIntentId = intent.Id, ScanAttemptId = attempt.Id, CompletedAtUtc = clock.GetUtcNow().UtcDateTime,
            Fingerprint = GlobalBankReceiptLedger.Hash(raw), ProtectedEvidence = protocol.Protect(intent.StoreId, raw) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    internal static async Task<string?> BlockedReasonAsync(AppDbContext db, AcbProtocolCore protocol,
        StorefrontPaymentIntent intent, bool requireProof, CancellationToken ct)
    {
        var latest = await db.Set<StorefrontBankScanAttempt>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == intent.StoreId && x.PaymentIntentId == intent.Id).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (latest is null) return requireProof ? "Chưa có chứng cứ truy xuất đầy đủ các trang và khoảng ngày ngân hàng." : null;
        var completion = await db.Set<StorefrontBankScanCompletion>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.StoreId == intent.StoreId && x.PaymentIntentId == intent.Id && x.ScanAttemptId == latest.Id, ct);
        if (latest.IsDeleted || completion is null || completion.IsDeleted) return "Lần kiểm tra ngân hàng mới nhất chưa hoàn tất; khoản cần hoàn vẫn được giữ.";
        try
        {
            var result = Decode(protocol, intent, completion);
            var requested = JsonSerializer.Deserialize<List<StorefrontBankScanRange>>(latest.RequestedWindowsJson, Json) ?? [];
            if (result.AttemptId != latest.Id || result.BindingFingerprint != latest.BindingFingerprint || result.AccountIdentity != latest.AccountIdentity ||
                requested.Count != result.Windows.Count || !requested.SequenceEqual(result.Windows.Select(x => x.Range)) ||
                result.Windows.Any(x => x.PageFingerprints.Count != Math.Max(1, x.TotalPages)))
                return "Chứng cứ hoàn tất truy xuất không khớp lần kiểm tra mới nhất.";
            var from = DateTime.SpecifyKind(intent.CreatedAtUtc, DateTimeKind.Utc).AddHours(7).Date.AddDays(-1);
            var through = DateTime.SpecifyKind(latest.StartedAtUtc, DateTimeKind.Utc).AddHours(7).Date.AddDays(1);
            if (!result.Coverage.Any(x => x.From <= from && x.To >= through)) return "Lịch sử ngân hàng chưa được truy xuất liên tục đủ khoảng cần kiểm tra.";
            return null;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or InvalidOperationException or FormatException)
        { return "Không xác minh được chứng cứ hoàn tất truy xuất ngân hàng."; }
    }
    internal static async Task<bool> ContainsOnlyUnpaidOrdersAsync(AppDbContext db, AcbProtocolCore protocol,
        StorefrontPaymentIntent intent, StorefrontBankScanCompletion completion, CancellationToken ct)
        => (await EvaluateUnpaidOrdersAsync(db, protocol, intent, completion, ct)).Unpaid;
    internal static async Task<StorefrontUnpaidScanResult> EvaluateUnpaidOrdersAsync(AppDbContext db, AcbProtocolCore protocol,
        StorefrontPaymentIntent intent, StorefrontBankScanCompletion completion, CancellationToken ct)
    {
        try
        {
            var binding = JsonSerializer.Deserialize<StorefrontBankBinding>(intent.BindingSnapshotJson, Json);
            if (binding is null) return new(false);
            var remaining = new List<StorefrontBankScanRange>();
            var visited = new HashSet<int>();
            var pages = new Dictionary<string, (bool Validated, bool Unpaid, int TotalPages)>(StringComparer.Ordinal);
            // Query coverage is not absence coverage. Inspect only actual windows from
            // signed completions; never promote their merged Coverage field to no-money proof.
            for (var depth = 0; depth < 1000; depth++)
            {
                if (!visited.Add(completion.Id) || completion.IsDeleted || completion.StoreId != intent.StoreId || completion.PaymentIntentId != intent.Id)
                    return new(false);
                var result = Decode(protocol, intent, completion);
                var attempt = await db.Set<StorefrontBankScanAttempt>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                    x.StoreId == intent.StoreId && x.PaymentIntentId == intent.Id && x.StorefrontOrderId == intent.StorefrontOrderId && x.Id == result.AttemptId, ct);
                if (attempt is null || attempt.IsDeleted || attempt.AccountIdentity != binding.AccountIdentity || result.AccountIdentity != binding.AccountIdentity ||
                    attempt.BindingFingerprint != result.BindingFingerprint || completion.CompletedAtUtc < attempt.StartedAtUtc) return new(false);
                var requested = JsonSerializer.Deserialize<List<StorefrontBankScanRange>>(attempt.RequestedWindowsJson, Json) ?? [];
                if (requested.Count != result.Windows.Count || !requested.SequenceEqual(result.Windows.Select(x => x.Range)) ||
                    result.Windows.Any(x => !ValidUnpaidRange(x.Range))) return new(false);
                if (depth == 0)
                {
                    var from = DateTime.SpecifyKind(intent.CreatedAtUtc, DateTimeKind.Utc).AddHours(7).Date.AddDays(-1);
                    var through = DateTime.SpecifyKind(attempt.StartedAtUtc, DateTimeKind.Utc).AddHours(7).Date.AddDays(1);
                    if (from > through) return new(false);
                    remaining.Add(new(from, through));
                }
                var safe = new List<StorefrontBankScanRange>();
                var uncertain = false;
                foreach (var window in result.Windows.Where(w => remaining.Any(r => Intersects(r, w.Range))))
                {
                    // Validate every intersecting window in the same attempt BEFORE
                    // subtracting any safe one. An overlapping INIT result cannot hide
                    // an unknown/paid result from that attempt, nor an older INIT clear it.
                    foreach (var fingerprint in window.PageFingerprints)
                    {
                        if (!pages.TryGetValue(fingerprint, out var page))
                        {
                            page = await ReadUnpaidPage(db, protocol, intent, fingerprint, ct);
                            pages.Add(fingerprint, page);
                        }
                        if (!page.Validated || page.TotalPages != window.TotalPages) return new(false);
                        if (!page.Unpaid) uncertain = true;
                    }
                    safe.Add(window.Range);
                }
                if (uncertain) return new(false, remaining.Min(x => x.From));
                foreach (var range in safe) remaining = SubtractUnpaidRange(remaining, range);
                if (remaining.Count == 0) return new(true); // A genuine newer rescan can supersede older uncertainty for these exact days.
                if (remaining.Count > 1000) return new(false, remaining.Min(x => x.From));
                if (result.PreviousCompletionId is null && result.PreviousFingerprint is null) return new(false, remaining.Min(x => x.From));
                if (result.PreviousCompletionId is not > 0 || result.PreviousFingerprint is not { Length: 64 }) return new(false);
                var previous = await db.Set<StorefrontBankScanCompletion>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                    x.StoreId == intent.StoreId && x.PaymentIntentId == intent.Id && x.StorefrontOrderId == intent.StorefrontOrderId && x.Id == result.PreviousCompletionId, ct);
                if (previous is null || previous.Id >= completion.Id || previous.ScanAttemptId >= completion.ScanAttemptId ||
                    previous.Fingerprint != result.PreviousFingerprint) return new(false);
                completion = previous;
            }
            // A traversal budget is incomplete proof, never acceptance or permanent denial.
            // The existing worker can genuinely rescan the still-unproved historical dates.
            return new(false, remaining.Min(x => x.From));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or InvalidOperationException or FormatException or ArgumentOutOfRangeException)
        { return new(false); }
    }
    private static bool ValidUnpaidRange(StorefrontBankScanRange range) => range.From.Date == range.From && range.To.Date == range.To &&
        range.From > DateTime.MinValue.Date && range.To < DateTime.MaxValue.Date && range.From <= range.To && (range.To - range.From).TotalDays <= 31;
    private static bool Intersects(StorefrontBankScanRange a, StorefrontBankScanRange b) => a.From <= b.To && b.From <= a.To;
    private static List<StorefrontBankScanRange> SubtractUnpaidRange(IEnumerable<StorefrontBankScanRange> ranges, StorefrontBankScanRange safe)
    {
        var result = new List<StorefrontBankScanRange>();
        foreach (var range in ranges)
        {
            if (!Intersects(range, safe)) { result.Add(range); continue; }
            if (range.From < safe.From) result.Add(new(range.From, safe.From.AddDays(-1)));
            if (range.To > safe.To) result.Add(new(safe.To.AddDays(1), range.To));
        }
        return result;
    }
    private static async Task<(bool Validated, bool Unpaid, int TotalPages)> ReadUnpaidPage(AppDbContext db, AcbProtocolCore protocol,
        StorefrontPaymentIntent intent, string fingerprint, CancellationToken ct)
    {
        var page = await db.Set<StorefrontBankObservation>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.StoreId == intent.StoreId && x.PaymentIntentId == intent.Id && x.Kind == "Retrieve" && x.Fingerprint == fingerprint, ct);
        if (page is null || page.IsDeleted) return (false, false, 0);
        var raw = protocol.Unprotect(intent.StoreId, page.ProtectedEvidence);
        if (GlobalBankReceiptLedger.Hash(raw) != fingerprint) return (false, false, 0);
        using var json = JsonDocument.Parse(raw);
        var orders = AcbProtocolCore.Path(json.RootElement, "responseBody", "orders");
        var pagination = AcbProtocolCore.Path(json.RootElement, "responseBody", "pagination", "totalPages");
        if (orders.ValueKind != JsonValueKind.Array || orders.GetArrayLength() > 100 || pagination.ValueKind != JsonValueKind.Number ||
            !pagination.TryGetInt32(out var count) || count is < 0 or > 100 || count == 0 && orders.GetArrayLength() != 0) return (false, false, 0);
        var unpaid = true;
        foreach (var order in orders.EnumerateArray())
        {
            var transactions = AcbProtocolCore.Path(order, "transactionDetail");
            if (AcbProtocolCore.Text(order, "orderId") != intent.ProviderReference || transactions.ValueKind != JsonValueKind.Array) return (false, false, count);
            if (AcbProtocolCore.Text(order, "status") != "INIT" || transactions.GetArrayLength() != 0) unpaid = false;
        }
        return (true, unpaid, count);
    }
    private static StorefrontBankScanResult Decode(AcbProtocolCore protocol, StorefrontPaymentIntent intent, StorefrontBankScanCompletion completion)
    {
        var raw = protocol.Unprotect(intent.StoreId, completion.ProtectedEvidence);
        var result = JsonSerializer.Deserialize<StorefrontBankScanResult>(raw, Json) ?? throw new InvalidOperationException("Missing bank scan evidence.");
        if (GlobalBankReceiptLedger.Hash(raw) != completion.Fingerprint || result.ProtocolVersion != 1 || result.PaymentIntentId != intent.Id ||
            result.Reference != intent.ProviderReference || result.BindingFingerprint != GlobalBankReceiptLedger.Hash(intent.BindingSnapshotJson) ||
            completion.StorefrontOrderId != intent.StorefrontOrderId || completion.ScanAttemptId != result.AttemptId ||
            result.Windows is null || result.Coverage is null || result.Windows.Count is <1 or >3 || result.Coverage.Count is <1 or >1000 ||
            result.Windows.Any(x=>x is null||x.Range is null||x.TotalPages is <0 or >100||x.PageFingerprints is null||
                x.PageFingerprints.Count!=Math.Max(1,x.TotalPages)||x.PageFingerprints.Any(p=>p is null||p.Length!=64||p.Any(c=>!Uri.IsHexDigit(c)))))
            throw new InvalidOperationException("Bank scan proof scope does not match.");
        return result;
    }
    private static IReadOnlyList<StorefrontBankScanRange> Merge(IEnumerable<StorefrontBankScanRange> ranges)
    {
        var merged = new List<StorefrontBankScanRange>();
        foreach (var range in ranges.OrderBy(x => x.From).ThenBy(x => x.To))
        {
            if (range.From.Date != range.From || range.To.Date != range.To || range.From > range.To)
                throw new InvalidOperationException("Invalid bank scan date interval.");
            if (merged.Count == 0 || range.From > merged[^1].To.AddDays(1)) merged.Add(range);
            else if (range.To > merged[^1].To) merged[^1] = merged[^1] with { To = range.To };
        }
        return merged;
    }
}
