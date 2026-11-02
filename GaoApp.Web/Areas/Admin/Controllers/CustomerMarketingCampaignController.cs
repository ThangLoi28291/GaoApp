using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Policy = CustomerServiceRights.View), Authorize(Policy = PermissionCodes.CustomerService.MarketingConsent)]
[AutoValidateAntiforgeryToken, Route("admin/customer-service/marketing-campaigns")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerMarketingCampaignController(CustomerServiceAccess access, AppDbContext db, CustomerChannelRegistry registry,
    CustomerMarketingCampaignPreviewStore previewStore, IOptionsMonitor<CustomerMarketingCampaignOptions> campaignOptions, TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var who = await access.Staff(PermissionCodes.CustomerService.MarketingConsent, ct);
        var store = await access.Store(ct);
        var connections = CurrentConnections(who.StoreId);
        var groups = await db.Customers.AsNoTracking().Where(c => c.StoreId == who.StoreId && c.IsActive)
            .Select(c => c.CustomerGroup).Distinct().OrderBy(x => x).Take(50).ToListAsync(ct);
        var campaigns = await db.CustomerMarketingCampaigns.AsNoTracking().Where(x => x.StoreId == who.StoreId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(50)
            .Select(x => new CustomerMarketingCampaignRow(x.Id, x.Name, x.Status, x.Detail, x.RecipientCount, x.ScheduledAtUtc, x.ConnectionId, x.SenderProfileId))
            .ToListAsync(ct);
        return View("~/Areas/Admin/Views/CustomerMarketingCampaign/Index.cshtml",
            new CustomerMarketingCampaignPage(store.Name, campaignOptions.CurrentValue.Enabled, connections.Select(x => new CustomerMarketingCampaignConnection(x.Id, x.Label, x.ProfileId)).ToArray(), groups, campaigns, TempData["Notice"] as string, Guid.NewGuid()));
    }

    [HttpPost("preview")]
    [RequestSizeLimit(22 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 22 * 1024 * 1024, ValueCountLimit = 16, ValueLengthLimit = 16384)]
    public async Task<IActionResult> Preview(CustomerMarketingCampaignForm form, CancellationToken ct)
    {
        var who = await access.Staff(PermissionCodes.CustomerService.MarketingConsent, ct);
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(form.Name) || string.IsNullOrWhiteSpace(form.MessageText) ||
            form.Name.Trim().Length > 120 || form.MessageText.EnumerateRunes().Take(4001).Count() > 4000 ||
            form.CustomerGroup?.Length > 30 || !Guid.TryParse(form.SenderProfileId, out var profileId) || !Guid.TryParse(form.PreviewRequestId, out var requestId) || requestId == Guid.Empty)
            return BadRequest("Thông tin chiến dịch chưa hợp lệ.");
        CustomerOutgoingAsset? asset = null;
        if (form.Asset is { Length: > 0 }) asset = await CustomerOutgoingMedia.Inspect(form.Asset, ct);

        var connection = CurrentConnections(who.StoreId).SingleOrDefault(x => x.Id == form.ConnectionId && x.ProfileId == profileId);
        if (connection.Id is null) throw new AssistantException(409, "campaign_connection_changed", "Tài khoản Zalo đã chọn không còn là kết nối hiện hành.");
        var now = clock.GetUtcNow();
        var scheduled = new DateTimeOffset(DateTime.SpecifyKind(form.ScheduledLocal, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).UtcDateTime;
        if (scheduled < now.AddMinutes(5).UtcDateTime || scheduled > now.AddDays(30).UtcDateTime)
            return BadRequest("Thời gian gửi phải sau ít nhất 5 phút và không quá 30 ngày.");

        var query = from customer in db.Customers.AsNoTracking()
                    join consent in db.CustomerMarketingConsents.AsNoTracking() on new { customer.StoreId, CustomerId = customer.Id } equals new { consent.StoreId, consent.CustomerId }
                    join binding in db.InvoiceRecipientBindings.IgnoreQueryFilters().AsNoTracking() on new { customer.StoreId, CustomerId = customer.Id } equals new { binding.StoreId, binding.CustomerId }
                    where customer.StoreId == who.StoreId && customer.IsActive && customer.Phone != null && customer.Phone != "" &&
                          consent.Channel == "Zalo" && consent.IsGranted && binding.RevokedAtUtc == null &&
                          binding.ConnectionId == connection.Id && binding.ConnectionBinding == connection.Binding && binding.SenderProfileId == connection.ProfileId &&
                          (form.CustomerGroup == null || form.CustomerGroup == "" || customer.CustomerGroup == form.CustomerGroup)
                    orderby customer.Id
                    select new CampaignCandidate(customer.Id, customer.Name, customer.Phone!, binding.Id, binding.Version, binding.ConnectionId,
                        binding.ConnectionBinding, binding.SenderProfileId, binding.ConversationId, binding.NativePeer, binding.Phone);
        var candidates = await query.Take(5001).ToListAsync(ct);
        candidates = candidates.Where(x => NormalizePhone(x.CustomerPhone) == x.BindingPhone).Take(5001).ToList();
        if (candidates.Count == 0) return Conflict("Không có khách đủ điều kiện. Cần đồng ý quảng cáo, liên kết Zalo hiện hành và số điện thoại phải khớp.");
        if (candidates.Count > 5000) return Conflict("Mỗi chiến dịch giới hạn 5.000 khách trong bản P6.");

        var snapshot = candidates.Select(x => new { x.CustomerId, x.BindingId, x.BindingVersion, x.ConnectionId, x.ConnectionBinding,
            x.SenderProfileId, x.ConversationId, x.NativePeer, x.BindingPhone }).ToArray();
        var snapshotJson = JsonSerializer.Serialize(snapshot);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));
        var cleanName = form.Name.Trim();
        var cleanText = form.MessageText.Trim();
        var cleanGroup = string.IsNullOrWhiteSpace(form.CustomerGroup) ? null : form.CustomerGroup.Trim();
        var fingerprint = CustomerMarketingCampaignFingerprint.Compute(cleanName, cleanText, cleanGroup, connection.Id, connection.Binding, connection.ProfileId, scheduled, hash, asset?.Attachment, asset?.Sha256);
        var replay = await previewStore.FindReplayAsync(who.StoreId, requestId, fingerprint, ct);
        if (replay is not null) return await RenderPreviewAsync(replay, ct);
        var campaign = new CustomerMarketingCampaign
        {
            CampaignKey = Guid.NewGuid(), PreviewRequestId = requestId, RequestFingerprint = fingerprint,
            StoreId = who.StoreId, Name = cleanName, MessageText = cleanText,
            AssetKind = asset?.Attachment.Kind, AssetFileName = asset?.Attachment.FileName, AssetContentType = asset?.Attachment.ContentType,
            AssetLength = asset?.Attachment.Length, AssetSha256 = asset?.Sha256, AssetBytes = asset?.Bytes,
            CustomerGroup = cleanGroup,
            ConnectionId = connection.Id, ConnectionBinding = connection.Binding, SenderProfileId = connection.ProfileId,
            SnapshotSha256 = hash, RecipientCount = snapshot.Length, ScheduledAtUtc = scheduled, Status = "Draft",
            CreatedAtUtc = now.UtcDateTime, CreatedByUserId = who.UserId
        };
        campaign = await previewStore.SaveAsync(campaign, candidates.Select(x => new CustomerMarketingCampaignRecipientDraft(x.CustomerId, x.Name,
            x.BindingId, x.BindingVersion, x.ConnectionId, x.ConnectionBinding, x.SenderProfileId, x.ConversationId, x.NativePeer, x.BindingPhone)).ToArray(), ct);
        await access.Recheck(who, PermissionCodes.CustomerService.MarketingConsent, ct);
        return await RenderPreviewAsync(campaign, ct);
    }

    private async Task<IActionResult> RenderPreviewAsync(CustomerMarketingCampaign campaign, CancellationToken ct)
    {
        var recipients = await db.CustomerMarketingCampaignRecipients.AsNoTracking().Where(x => x.StoreId == campaign.StoreId && x.CampaignId == campaign.Id)
            .OrderBy(x => x.Id).Take(101).Select(x => new CustomerMarketingCampaignRecipientPreview(x.CustomerId, x.CustomerNameSnapshot, MaskPhone(x.Phone))).ToListAsync(ct);
        var connection = CurrentConnections(campaign.StoreId).SingleOrDefault(x => x.Id == campaign.ConnectionId && x.Binding == campaign.ConnectionBinding && x.ProfileId == campaign.SenderProfileId);
        return View("~/Areas/Admin/Views/CustomerMarketingCampaign/Preview.cshtml",
            new CustomerMarketingCampaignPreviewPage(campaign.Id, campaign.CampaignKey, campaign.Name, campaign.MessageText,
                connection.Label ?? "Kết nối đã thay đổi", campaign.SenderProfileId, campaign.ScheduledAtUtc, campaign.RecipientCount,
                campaign.SnapshotSha256, recipients.Take(100).ToArray(), campaign.RecipientCount > recipients.Take(100).Count(),
                campaign.AssetFileName, campaign.AssetContentType, campaign.AssetLength, campaign.AssetSha256));
    }

    [HttpPost("{id:int}/schedule")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Schedule(int id, CustomerMarketingCampaignScheduleForm form, CancellationToken ct)
    {
        var who = await access.Staff(PermissionCodes.CustomerService.MarketingConsent, ct);
        var campaign = await db.CustomerMarketingCampaigns.SingleOrDefaultAsync(x => x.Id == id && x.StoreId == who.StoreId && x.Status == "Draft", ct);
        if (campaign is null) return NotFound("Không tìm thấy bản xem trước còn hiệu lực.");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(campaign.SnapshotSha256), Encoding.ASCII.GetBytes(form.SnapshotSha256 ?? "")))
            return Conflict("Danh sách người nhận đã thay đổi; hãy tạo bản xem trước mới.");
        var current = CurrentConnections(who.StoreId).SingleOrDefault(x => x.Id == campaign.ConnectionId && x.Binding == campaign.ConnectionBinding && x.ProfileId == campaign.SenderProfileId);
        if (current.Id is null) return Conflict("Kết nối Zalo đã thay đổi; không thể lên lịch chiến dịch này.");
        var now = clock.GetUtcNow();
        if (campaign.ScheduledAtUtc < now.AddMinutes(5).UtcDateTime || campaign.ScheduledAtUtc > now.AddDays(30).UtcDateTime)
            return Conflict("Thời gian gửi không còn hợp lệ; hãy tạo bản xem trước mới.");
        campaign.Status = "Scheduled";
        campaign.UpdatedAtUtc = now.UtcDateTime;
        await access.Recheck(who, PermissionCodes.CustomerService.MarketingConsent, ct);
        await db.SaveChangesAsync(ct);
        TempData["Notice"] = $"Đã lên lịch chiến dịch {campaign.Name} cho {campaign.RecipientCount} khách lúc {new DateTimeOffset(campaign.ScheduledAtUtc, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm} (UTC+7). Chưa gửi tin nào.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/cancel")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var who = await access.Staff(PermissionCodes.CustomerService.MarketingConsent, ct);
        var campaign = await db.CustomerMarketingCampaigns.SingleOrDefaultAsync(x => x.Id == id && x.StoreId == who.StoreId && (x.Status == "Draft" || x.Status == "Scheduled"), ct);
        if (campaign is null) return Conflict("Chiến dịch đã bắt đầu gửi hoặc đã kết thúc.");
        campaign.Status = "Cancelled";
        campaign.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.CustomerMarketingCampaignRecipients.Where(x => x.CampaignId == campaign.Id && x.Status == "Pending")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Skipped").SetProperty(x => x.Detail, "campaign_cancelled"), ct);
        await access.Recheck(who, PermissionCodes.CustomerService.MarketingConsent, ct);
        await db.SaveChangesAsync(ct);
        TempData["Notice"] = $"Đã hủy chiến dịch {campaign.Name}.";
        return RedirectToAction(nameof(Index));
    }

    private (string Id, string Label, string Binding, Guid ProfileId)[] CurrentConnections(int storeId) => registry.All()
        .Where(c => c.Enabled && c.StoreId == storeId && c.Provider == "zalomarketing" && Guid.TryParse(c.AccountId, out var profile) && profile != Guid.Empty)
        .Select(c => (c.Id, c.Label, c.Binding, Guid.Parse(c.AccountId))).ToArray();

    private static string NormalizePhone(string value) => new(value.Where(char.IsAsciiDigit).ToArray());
    private static string MaskPhone(string value) => value.Length < 7 ? "••••" : value[..3] + "••••" + value[^3..];
    private sealed record CampaignCandidate(int CustomerId, string Name, string CustomerPhone, Guid BindingId, int BindingVersion,
        string ConnectionId, string ConnectionBinding, Guid SenderProfileId, Guid ConversationId, string NativePeer, string BindingPhone);
}

public sealed class CustomerMarketingCampaignForm
{
    [Required, StringLength(36)] public string PreviewRequestId { get; set; } = "";
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [Required, StringLength(8000)] public string MessageText { get; set; } = "";
    [StringLength(30)] public string? CustomerGroup { get; set; }
    [Required, StringLength(48)] public string ConnectionId { get; set; } = "";
    [Required, StringLength(36)] public string SenderProfileId { get; set; } = "";
    [Required] public DateTime ScheduledLocal { get; set; }
    public IFormFile? Asset { get; set; }
}

public sealed class CustomerMarketingCampaignScheduleForm { [Required, StringLength(64)] public string? SnapshotSha256 { get; set; } }
public sealed record CustomerMarketingCampaignConnection(string Id, string Label, Guid ProfileId);
public sealed record CustomerMarketingCampaignRow(int Id, string Name, string Status, string? Detail, int RecipientCount, DateTime ScheduledAtUtc, string ConnectionId, Guid SenderProfileId);
public sealed record CustomerMarketingCampaignPage(string StoreName, bool DispatchEnabled, IReadOnlyList<CustomerMarketingCampaignConnection> Connections, IReadOnlyList<string> CustomerGroups, IReadOnlyList<CustomerMarketingCampaignRow> Campaigns, string? Notice, Guid PreviewRequestId);
public sealed record CustomerMarketingCampaignRecipientPreview(int CustomerId, string Name, string MaskedPhone);
public sealed record CustomerMarketingCampaignPreviewPage(int Id, Guid CampaignKey, string Name, string MessageText, string ConnectionLabel,
    Guid SenderProfileId, DateTime ScheduledAtUtc, int RecipientCount, string SnapshotSha256, IReadOnlyList<CustomerMarketingCampaignRecipientPreview> Recipients, bool HasMoreRecipients,
    string? AssetFileName, string? AssetContentType, long? AssetLength, string? AssetSha256);
