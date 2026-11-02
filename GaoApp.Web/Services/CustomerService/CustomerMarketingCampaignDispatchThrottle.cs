using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Web.Services.CustomerService;

/// <summary>SQL-backed, cross-process send rhythm keyed to the Zalo sender profile.</summary>
public sealed class CustomerMarketingCampaignDispatchThrottle(AppDbContext db)
{
    public async Task<DateTime> ReserveAsync(Guid profileId, DateTime now, TimeSpan interval, CancellationToken ct)
    {
        if (profileId == Guid.Empty || interval <= TimeSpan.Zero || db.Database.CurrentTransaction is not null)
            throw new ArgumentException("Invalid dispatch throttle request.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connection = db.Database.GetDbConnection();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx.GetDbTransaction();
            command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000; SELECT @result;";
            var resource = command.CreateParameter(); resource.ParameterName = "@resource"; resource.Value = $"GaoApp:MarketingDispatch:{profileId:D}"; command.Parameters.Add(resource);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) < 0)
                throw new InvalidOperationException("Marketing dispatch throttle lock was not acquired.");
        }
        DateTime? next;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx.GetDbTransaction();
            command.CommandText = "SELECT NextAllowedAtUtc FROM dbo.CustomerMarketingCampaignDispatchGates WITH (UPDLOCK, HOLDLOCK) WHERE SenderProfileId=@profile;";
            var profile = command.CreateParameter(); profile.ParameterName = "@profile"; profile.Value = profileId; command.Parameters.Add(profile);
            var value = await command.ExecuteScalarAsync(ct);
            next = value is null or DBNull ? null : Convert.ToDateTime(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        if (next > now) { await tx.CommitAsync(ct); return next.Value; }
        var reserved = now.Add(interval);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx.GetDbTransaction();
            command.CommandText = next is null
                ? "INSERT dbo.CustomerMarketingCampaignDispatchGates(SenderProfileId,NextAllowedAtUtc) VALUES(@profile,@next);"
                : "UPDATE dbo.CustomerMarketingCampaignDispatchGates SET NextAllowedAtUtc=@next WHERE SenderProfileId=@profile;";
            var profile = command.CreateParameter(); profile.ParameterName = "@profile"; profile.Value = profileId; command.Parameters.Add(profile);
            var nextParam = command.CreateParameter(); nextParam.ParameterName = "@next"; nextParam.Value = reserved; command.Parameters.Add(nextParam);
            await command.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return now;
    }
}
