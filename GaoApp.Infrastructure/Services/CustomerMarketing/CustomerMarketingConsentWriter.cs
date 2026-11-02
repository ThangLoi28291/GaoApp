using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Services.CustomerMarketing;

/// <summary>Serializes staff consent edits and inbound opt-outs per store/customer.</summary>
public sealed class CustomerMarketingConsentWriter(AppDbContext db, TimeProvider clock)
{
    public async Task<bool> SetAsync(int storeId, int customerId, bool granted, string source,
        string? evidenceReference, int? actorUserId, CancellationToken ct)
    {
        if (storeId <= 0 || customerId <= 0 || source is not ("CustomerRequest" or "PaperForm" or "PhoneCall" or "ZaloKeyword") ||
            (source == "ZaloKeyword" ? actorUserId is not null : actorUserId is not > 0) ||
            (granted && string.IsNullOrWhiteSpace(evidenceReference)))
            throw new ArgumentException("Invalid marketing consent transition.");
        var evidence = Clean(evidenceReference);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AcquireLockAsync(transaction, storeId, customerId, ct);
        if (!await db.Customers.IgnoreQueryFilters().AnyAsync(x => x.Id == customerId && x.StoreId == storeId && x.IsActive && !x.IsDeleted && x.Phone != null && x.Phone != "", ct))
            throw new KeyNotFoundException("Marketing recipient is not active in this store.");

        if (source == "ZaloKeyword" && evidence is not null && await db.CustomerMarketingConsentEvents.IgnoreQueryFilters()
            .AnyAsync(x => x.StoreId == storeId && x.Channel == "Zalo" && x.Source == source && x.EvidenceReference == evidence, ct))
        {
            await transaction.CommitAsync(ct);
            return false;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var current = await db.CustomerMarketingConsents.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.StoreId == storeId && x.CustomerId == customerId && x.Channel == "Zalo", ct);
        if (current is null)
        {
            current = new CustomerMarketingConsent { StoreId = storeId, CustomerId = customerId, Channel = "Zalo" };
            db.CustomerMarketingConsents.Add(current);
        }
        current.IsDeleted = false;
        current.DeletedAtUtc = null;
        current.DeletedBy = null;
        current.IsGranted = granted;
        current.Source = source;
        current.EvidenceReference = evidence;
        current.ChangedAtUtc = now;
        current.ChangedByUserId = actorUserId;
        db.CustomerMarketingConsentEvents.Add(new CustomerMarketingConsentEvent
        {
            StoreId = storeId, CustomerId = customerId, Channel = "Zalo", IsGranted = granted,
            Source = source, EvidenceReference = evidence, RecordedAtUtc = now, RecordedByUserId = actorUserId
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private async Task AcquireLockAsync(IDbContextTransaction transaction, int storeId, int customerId, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000; SELECT @result;";
        var resource = command.CreateParameter(); resource.ParameterName = "@resource"; resource.Value = $"GaoApp:MarketingConsent:{storeId}:{customerId}"; command.Parameters.Add(resource);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0) throw new InvalidOperationException("Marketing consent update lock was not acquired.");
    }

    private static string? Clean(string? value)
    {
        var result = new string((value ?? "").Where(c => !char.IsControl(c)).Take(250).ToArray()).Trim();
        return result.Length == 0 ? null : result;
    }
}
