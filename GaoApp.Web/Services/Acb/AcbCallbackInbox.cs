using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

public sealed class AcbCallbackInbox(AppDbContext db, IAcbOrderLockProvider locks,
    AcbPaymentService payments, AcbCallbackSignal signal, AcbCallbackDiagnostics diagnostics)
{
    private int StoreId => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new InvalidOperationException("Chưa xác định cửa hàng.");

    public async Task<int> AcceptAsync(JsonElement payload, CancellationToken ct)
    {
        var envelope = AcbCallbackEnvelope.Parse(payload);
        // Order ids are positive; zero serializes incoming receipts for this store only.
        await using var gate = await locks.AcquireAsync(db, 0, ct);
        var receipt = await db.Set<AcbCallbackReceipt>().SingleOrDefaultAsync(x => x.StoreId == StoreId &&
            x.RequestCode == envelope.RequestCode && x.ClientRequestId == envelope.ClientRequestId && x.Page == envelope.Page, ct);
        if (receipt != null && receipt.PayloadHash != envelope.PayloadHash)
        {
            // Older receipts used the full transport payload hash. Keep their original evidence.
            using var original = JsonDocument.Parse(receipt.PayloadJson);
            if (AcbCallbackEnvelope.Parse(original.RootElement).PayloadHash != envelope.PayloadHash)
                throw new AcbCallbackConflictException();
        }
        if (receipt == null)
        {
            if (await db.Set<AcbCallbackReceipt>().AnyAsync(x => x.StoreId == StoreId &&
                x.RequestCode == envelope.RequestCode && x.ClientRequestId == envelope.ClientRequestId &&
                x.TotalPages != envelope.TotalPages, ct))
                throw new AcbCallbackValidationException("INCONSISTENT_PAGINATION");
            receipt = new AcbCallbackReceipt { StoreId = StoreId, ClientRequestId = envelope.ClientRequestId,
                RequestCode = envelope.RequestCode, TotalPages = envelope.TotalPages,
                Page = envelope.Page, PayloadHash = envelope.PayloadHash, PayloadJson = payload.GetRawText(), NextAttemptAtUtc = DateTime.UtcNow };
            foreach (var item in envelope.Items) { item.StoreId = StoreId; receipt.Items.Add(item); }
            db.Add(receipt);
            await db.SaveChangesAsync(ct);
        }
        signal.Wake();
        return receipt.Id;
    }

    public async Task ProcessAsync(int id, CancellationToken ct)
    {
        // Negative ids serialize receipt processing across server instances.
        await using var gate = await locks.AcquireAsync(db, -id, ct);
        var receipt = await db.Set<AcbCallbackReceipt>().SingleAsync(x => x.StoreId == StoreId && x.Id == id, ct);
        if (receipt.ProcessedAtUtc.HasValue || receipt.NextAttemptAtUtc > DateTime.UtcNow) return;
        var attempt = receipt.Attempts + 1;
        receipt.Attempts = attempt;
        diagnostics.Process(receipt, "PROCESSING_STARTED");
        Exception? failure = null;
        try
        {
            using var json = JsonDocument.Parse(receipt.PayloadJson);
            var result = await payments.CallbackAsync(json.RootElement, ct, receipt.Id);
            receipt.NeedsReview = result.Unmatched || result.NeedsReview;
            receipt.LastErrorCode = result.Unmatched ? "UNMATCHED_QR" : result.NeedsReview ? "QR_REVIEW_REQUIRED" : result.AwaitingBank ? "AWAITING_BANK_EVIDENCE" : null;
            if (!result.AwaitingBank) receipt.ProcessedAtUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            failure = ex;
            receipt.LastErrorCode = AcbCallbackDiagnostics.ErrorCode(ex);
            // Discard uncommitted bank/entity changes before persisting the retry marker.
            // Already committed transaction evidence is retained for the next attempt.
            db.ChangeTracker.Clear();
            receipt = await db.Set<AcbCallbackReceipt>().SingleAsync(x => x.StoreId == StoreId && x.Id == id, ct);
            receipt.Attempts = attempt;
            receipt.LastErrorCode = AcbCallbackDiagnostics.ErrorCode(ex);
        }
        receipt.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(60, Math.Pow(2, Math.Min(receipt.Attempts, 6))));
        await db.SaveChangesAsync(ct);
        diagnostics.Process(receipt, receipt.LastErrorCode ?? "PROCESSED", failure);
    }

    public async Task RetryAsync(int id, CancellationToken ct)
    {
        await using var gate = await locks.AcquireAsync(db, -id, ct);
        var receipt = await db.Set<AcbCallbackReceipt>().SingleAsync(x => x.StoreId == StoreId && x.Id == id, ct);
        receipt.ProcessedAtUtc = null;
        receipt.NextAttemptAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        diagnostics.Process(receipt, "MANUAL_RETRY_QUEUED");
        signal.Wake();
    }
}

public sealed class AcbCallbackConflictException : Exception;

public sealed class AcbCallbackSignal : IDisposable
{
    private readonly SemaphoreSlim available = new(0, 1);
    public void Wake()
    {
        try { available.Release(); }
        catch (SemaphoreFullException)
        {
            // A wake is already pending; coalesce this notification into that wake.
            return;
        }
    }
    public Task<bool> WaitAsync(CancellationToken ct) => available.WaitAsync(TimeSpan.FromSeconds(5), ct);
    public void Dispose() => available.Dispose();
}
