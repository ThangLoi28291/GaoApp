using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;

namespace GaoApp.Web.Services.CustomerService;

public sealed record CustomerMarketingCampaignRecipientDraft(int CustomerId, string CustomerName, Guid BindingId, int BindingVersion,
    string ConnectionId, string ConnectionBinding, Guid SenderProfileId, Guid ConversationId, string NativePeer, string Phone);

public static class CustomerMarketingCampaignFingerprint
{
    public static string Compute(string name, string text, string? customerGroup, string connectionId, string connectionBinding,
        Guid senderProfileId, DateTime scheduledAtUtc, string snapshotSha256, CustomerOutgoingAttachment? asset, string? assetSha256)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { name, text, customerGroup, connectionId, connectionBinding, senderProfileId, scheduledAtUtc, snapshotSha256,
            assetKind = asset?.Kind, assetFileName = asset?.FileName, assetContentType = asset?.ContentType, assetLength = asset?.Length, assetSha256 });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

public sealed class CustomerMarketingCampaignPreviewStore(AppDbContext db)
{
    public async Task<CustomerMarketingCampaign?> FindReplayAsync(int storeId, Guid requestId, string fingerprint, CancellationToken ct)
    {
        var existing = await db.CustomerMarketingCampaigns.SingleOrDefaultAsync(x => x.StoreId == storeId && x.PreviewRequestId == requestId, ct);
        if (existing is null) return null;
        EnsureFingerprint(existing, fingerprint);
        return existing;
    }

    public async Task<CustomerMarketingCampaign> SaveAsync(CustomerMarketingCampaign campaign,
        IReadOnlyList<CustomerMarketingCampaignRecipientDraft> recipients, CancellationToken ct)
    {
        if (campaign.PreviewRequestId == Guid.Empty || campaign.RequestFingerprint.Length != 64 || campaign.RecipientCount != recipients.Count || recipients.Count is < 1 or > 5000)
            throw new ArgumentException("Campaign preview identity or recipient count is invalid.");
        var replay = await FindReplayAsync(campaign.StoreId, campaign.PreviewRequestId, campaign.RequestFingerprint, ct);
        if (replay is not null) return replay;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.CustomerMarketingCampaigns.Add(campaign);
            await db.SaveChangesAsync(ct);
            foreach (var recipient in recipients)
                db.CustomerMarketingCampaignRecipients.Add(new CustomerMarketingCampaignRecipient
                {
                    CampaignId = campaign.Id, StoreId = campaign.StoreId, CustomerId = recipient.CustomerId, CustomerNameSnapshot = Clean(recipient.CustomerName),
                    BindingId = recipient.BindingId, BindingVersion = recipient.BindingVersion, ConnectionId = recipient.ConnectionId,
                    ConnectionBinding = recipient.ConnectionBinding, SenderProfileId = recipient.SenderProfileId, ConversationId = recipient.ConversationId,
                    NativePeer = recipient.NativePeer, Phone = recipient.Phone, Status = "Pending", NextAttemptAtUtc = campaign.ScheduledAtUtc
                });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return campaign;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var concurrent = await FindReplayAsync(campaign.StoreId, campaign.PreviewRequestId, campaign.RequestFingerprint, ct);
            if (concurrent is not null) return concurrent;
            throw;
        }
    }

    private static void EnsureFingerprint(CustomerMarketingCampaign existing, string fingerprint)
    {
        if (fingerprint.Length != existing.RequestFingerprint.Length ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(existing.RequestFingerprint), Encoding.ASCII.GetBytes(fingerprint)))
            throw new AssistantException(409, "campaign_preview_idempotency_conflict", "Mã bản xem trước đã được dùng với nội dung khác. Hãy tải lại trang và tạo bản xem trước mới.");
    }

    private static string Clean(string value) => new(value.Where(c => !char.IsControl(c)).Take(200).ToArray());
}
