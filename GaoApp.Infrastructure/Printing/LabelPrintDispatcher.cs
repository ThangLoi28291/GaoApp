using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Printing;

public sealed class LabelPrintDispatcher(AppDbContext db, ILabelPrintTransport transport)
{
    public async Task<bool> DispatchAsync(int printerId, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows printing service required.");
        int store = db.CurrentStoreId ?? throw new InvalidOperationException("Printing service requires StoreId.");
        ProductLabelJob? job;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            string resource = $"gao-label:{store}:printer:{printerId}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @r int;
                EXEC @r=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
                IF @r<0 THROW 51001, 'Label printer is busy.', 1;
                """, ct);
            var printer = await db.Set<ProductLabelPrinter>().SingleOrDefaultAsync(x => x.StoreId == store && x.Id == printerId && x.Enabled, ct);
            if (printer is null) return false;
            printer.LastSeenAtUtc = DateTime.UtcNow;
            bool sending = await db.Set<ProductLabelJob>().AnyAsync(x => x.StoreId == store && x.PrinterId == printerId && x.Status == ProductLabelJobStatus.Sending, ct);
            job = sending ? null : await db.Set<ProductLabelJob>().Where(x => x.StoreId == store && x.PrinterId == printerId && x.Status == ProductLabelJobStatus.Queued)
                .OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (job is not null) job.Status = ProductLabelJobStatus.Sending;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        if (job is null) return false;
        // Commit the sending state BEFORE interacting with the spooler. A crash cannot cause an automatic resend.
        var payload = LabelJson.Read<LabelPrintPayload>(job.PayloadJson);
        int? sentSpoolId = null;
        string? sendError = null;
        try
        {
            ProductLabelRenderer.ValidatePayload(payload);
            sentSpoolId = transport.Send(payload.Printer.WindowsPrinterName, $"GaoApp tem #{job.Id}", ProductLabelRenderer.Commands(payload));
        }
        catch (Exception ex)
        {
            var message = "Cần kiểm tra máy in trước khi xác nhận hoặc in bù: " + ex.Message;
            sendError = message[..Math.Min(500, message.Length)];
        }
        // A lost acknowledgement leaves Sending/NeedsAttention; never replay an uncertain job.
        // Finish the job and product progress in one transaction. Lock order matches the web service.
        await using var resultTx = await db.Database.BeginTransactionAsync(CancellationToken.None);
        foreach (var key in new[] { job.TaskId.HasValue ? $"task:{job.TaskId}" : $"request:{job.RequestId}", $"printer:{printerId}" })
        {
            string resource = $"gao-label:{store}:{key}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @r int;
                EXEC @r=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
                IF @r<0 THROW 51001, 'Label printer is busy.', 1;
                """, CancellationToken.None);
        }
        await db.Entry(job).ReloadAsync(CancellationToken.None);
        if (job.Status != ProductLabelJobStatus.Sending) return true; // Already reconciled by a human; never apply twice.
        job.Error = sendError;
        job.Status = sendError is not null ? ProductLabelJobStatus.NeedsAttention
            : payload.ProductProgress ? ProductLabelJobStatus.Sent : ProductLabelJobStatus.AwaitingConfirmation;
        if (sendError is null)
        {
            job.SpoolJobId = sentSpoolId; job.SentAtUtc = DateTime.UtcNow;
            if (payload.ProductProgress && job.TaskId.HasValue)
            {
                var task = await db.Set<ProductLabelTask>().SingleAsync(x => x.StoreId == store && x.Id == job.TaskId.Value, CancellationToken.None);
                LabelTaskProgress.Apply(task, payload, payload.Items.Select(x => new LabelQuantity(x.Product.VariantId, x.Quantity)).ToList(), job.CreatedBy);
            }
        }
        await db.SaveChangesAsync(CancellationToken.None);
        await resultTx.CommitAsync(CancellationToken.None);
        return true;
    }
}
