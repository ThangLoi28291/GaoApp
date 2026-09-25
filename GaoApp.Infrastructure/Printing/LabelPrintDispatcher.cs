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
        try
        {
            var payload = LabelJson.Read<LabelPrintPayload>(job.PayloadJson);
            ProductLabelRenderer.ValidatePayload(payload);
            int spoolId = transport.Send(payload.Printer.WindowsPrinterName, $"GaoApp tem #{job.Id}", ProductLabelRenderer.Commands(payload));
            job.SpoolJobId = spoolId; job.SentAtUtc = DateTime.UtcNow;
            job.Status = ProductLabelJobStatus.AwaitingConfirmation;
        }
        catch (Exception ex)
        {
            job.Status = ProductLabelJobStatus.NeedsAttention;
            var message = "Cần kiểm tra máy in trước khi xác nhận hoặc in bù: " + ex.Message;
            job.Error = message[..Math.Min(500, message.Length)];
        }
        // A lost acknowledgement leaves Sending/NeedsAttention; never replay an uncertain job.
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }
}
