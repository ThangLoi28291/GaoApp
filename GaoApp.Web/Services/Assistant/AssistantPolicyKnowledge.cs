using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.Assistant;

public sealed record PolicyRevision(Guid Id, string Topic, string Title, string Body, DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil, string SourceNote, string Status, int Author, DateTimeOffset CreatedAt,
    int? ReviewedBy = null, DateTimeOffset? ReviewedAt = null, int? WithdrawnBy = null, DateTimeOffset? WithdrawnAt = null);
public sealed record PolicyOperation(Guid RequestId, int Actor, string Fingerprint, Guid RevisionId);
public sealed record PolicyDocument(int SchemaVersion, int StoreId, string Version, PolicyRevision[] Revisions, PolicyOperation[] Operations);
public sealed record PolicyDraft(string Topic, string Title, string Body, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveUntil, string SourceNote);

// Store policies are explicitly approved snapshots. Transcripts, product prices and customer records are never imported.
public sealed class AssistantPolicyKnowledge(IWebHostEnvironment environment, IOptionsMonitor<AssistantOptions> options, TimeProvider clock)
{
    public static readonly string[] Topics = ["store", "delivery", "returns", "payment", "membership", "promotions"];
    public static string Label(string topic) => topic switch { "store" => "Thông tin cửa hàng", "delivery" => "Giao hàng", "returns" => "Đổi trả", "payment" => "Thanh toán", "membership" => "Thành viên / tích điểm", "promotions" => "Khuyến mãi", _ => "Không xác định" };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private string FileName(int store) {
        if (store <= 0) throw AssistantException.Denied();
        return AssistantStorageSafety.SettingsFile(environment, options.CurrentValue, $"store-{store}-policies.json");
    }
    public static bool Text(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max
        && !text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));
    public static bool ValidDraft(PolicyDraft d) => Topics.Contains(d.Topic) && Text(d.Title, 160) && Text(d.Body, 3000)
        && Text(d.SourceNote, 400) && d.EffectiveFrom != default && (d.EffectiveUntil is null || d.EffectiveUntil > d.EffectiveFrom);
    public static PolicyRevision? Current(PolicyDocument doc, string topic, DateTimeOffset at) {
        // Never resurrect an older policy after its replacement expires or is withdrawn.
        var latest = doc.Revisions.Where(r => r.Topic == topic && r.Status is "approved" or "withdrawn" && r.EffectiveFrom <= at)
            .OrderByDescending(r => r.EffectiveFrom).FirstOrDefault();
        return latest is { Status: "approved" } && (latest.EffectiveUntil is null || at < latest.EffectiveUntil) ? latest : null;
    }
    public async Task<PolicyDocument> ReadAsync(int store, CancellationToken ct) {
        try {
            await using var stream = new FileStream(FileName(store), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, true);
            if (stream.Length > 8 * 1024 * 1024) throw new JsonException();
            using var raw = await JsonDocument.ParseAsync(stream, cancellationToken: ct); AssistantJwt.Unique(raw.RootElement);
            var doc = raw.RootElement.Deserialize<PolicyDocument>(Json);
            if (doc is null || doc.SchemaVersion != 1 || doc.StoreId != store || !Guid.TryParseExact(doc.Version, "N", out _)
                || doc.Revisions is null || doc.Operations is null || doc.Revisions.Length > 600 || doc.Operations.Length > 2000
                || doc.Revisions.Any(r => r is null || r.Id == Guid.Empty || r.Author <= 0 || r.CreatedAt == default
                    || !ValidDraft(new(r.Topic, r.Title, r.Body, r.EffectiveFrom, r.EffectiveUntil, r.SourceNote))
                    || r.Status is not ("draft" or "approved" or "withdrawn" or "cancelled")
                    || r.Status != "draft" && (r.ReviewedBy is not > 0 || r.ReviewedAt is null)
                    || r.Status is "withdrawn" or "cancelled" && (r.WithdrawnBy is not > 0 || r.WithdrawnAt is null))
                || doc.Revisions.Select(r => r.Id).Distinct().Count() != doc.Revisions.Length
                || doc.Operations.Any(o => o is null) || doc.Operations.Select(o => o.RequestId).Distinct().Count() != doc.Operations.Length
                || doc.Operations.Any(o => o is null || o.RequestId == Guid.Empty || o.Actor <= 0 || o.Fingerprint?.Length != 64 || !doc.Revisions.Any(r => r.Id == o.RevisionId))
                || doc.Revisions.Where(r => r.Status is "approved" or "withdrawn").GroupBy(r => (r.Topic,r.EffectiveFrom)).Any(g => g.Count() > 1)) throw new JsonException();
            return doc;
        } catch (FileNotFoundException) { return new(1, store, Guid.Empty.ToString("N"), [], []); }
        catch (DirectoryNotFoundException) { return new(1, store, Guid.Empty.ToString("N"), [], []); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or AssistantException) { throw Unavailable(); }
    }
    public Task<PolicyRevision> SaveAsync(int store, int actor, PolicyDraft draft, string version, Guid request, CancellationToken ct) {
        if (!ValidDraft(draft)) throw AssistantException.Invalid();
        return Change(store, actor, version, request, new { action = "save", draft }, rows => {
            if (rows.Count >= 600) throw new AssistantException(409, "policy_full", "Kho đã đủ 600 phiên bản. Cần lưu trữ kho trước khi thêm.");
            var row = new PolicyRevision(Guid.NewGuid(), draft.Topic, draft.Title.Trim(), draft.Body.Trim(), draft.EffectiveFrom,
                draft.EffectiveUntil, draft.SourceNote.Trim(), "draft", actor, clock.GetUtcNow()); rows.Add(row); return row;
        }, ct);
    }
    public Task<PolicyRevision> ReviewAsync(int store, int actor, Guid id, string action, string version, Guid request, bool confirmed, CancellationToken ct) {
        if (id == Guid.Empty || action is not ("approve" or "withdraw")) throw AssistantException.Invalid();
        return Change(store, actor, version, request, new { action, id, confirmed }, rows => {
            var row = rows.SingleOrDefault(r => r.Id == id) ?? throw new AssistantException(404, "policy_not_found", "Không tìm thấy phiên bản trong cửa hàng này.");
            PolicyRevision next;
            if (action == "approve") {
                if (row.Status != "draft" || !confirmed || row.Body.Contains("[CẦN XÁC NHẬN]", StringComparison.OrdinalIgnoreCase)
                    || row.EffectiveUntil <= clock.GetUtcNow()) throw new AssistantException(422, "policy_not_ready", "Kiểm tra nội dung, bỏ phần cần xác nhận và xác nhận duyệt; chính sách phải còn hiệu lực.");
                if (rows.Any(r => r.Topic == row.Topic && r.Status is "approved" or "withdrawn" && r.EffectiveFrom >= row.EffectiveFrom))
                    throw new AssistantException(409, "policy_schedule_conflict", "Ngày áp dụng phải sau các phiên bản đã duyệt cùng chủ đề. Hủy bản hẹn chưa áp dụng nếu cần.");
                next = row with { Status = "approved", ReviewedBy = actor, ReviewedAt = clock.GetUtcNow() };
            } else {
                if (row.Status != "approved") throw new AssistantException(409, "policy_state_conflict", "Chỉ thu hồi phiên bản đang được duyệt.");
                next = row with { Status = row.EffectiveFrom > clock.GetUtcNow() ? "cancelled" : "withdrawn", WithdrawnBy = actor, WithdrawnAt = clock.GetUtcNow() };
            }
            rows[rows.IndexOf(row)] = next; return next;
        }, ct);
    }
    private async Task<PolicyRevision> Change(int store, int actor, string version, Guid request, object payload, Func<List<PolicyRevision>, PolicyRevision> mutation, CancellationToken ct) {
        if (actor <= 0 || request == Guid.Empty || !Guid.TryParseExact(version, "N", out _)) throw AssistantException.Invalid();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, Json))));
        var path = FileName(store); string? temp = null;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var lease = await Lock(path + ".lock", ct); var doc = await ReadAsync(store, ct);
            var prior = doc.Operations.SingleOrDefault(o => o.RequestId == request);
            if (prior is not null) {
                if (prior.Actor != actor || prior.Fingerprint != hash) throw Conflict();
                return doc.Revisions.Single(r => r.Id == prior.RevisionId);
            }
            if (doc.Version != version) throw Conflict();
            if (doc.Operations.Length >= 2000) throw new AssistantException(409, "policy_full", "Kho nhật ký đã đầy; cần lưu trữ trước khi tiếp tục.");
            var rows = doc.Revisions.ToList(); var result = mutation(rows);
            var next = doc with { Version = Guid.NewGuid().ToString("N"), Revisions = rows.ToArray(), Operations = [..doc.Operations, new(request, actor, hash, result.Id)] };
            temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(next, Json), ct); ct.ThrowIfCancellationRequested();
            File.Move(temp, path, true); temp = null; return result;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw Unavailable(); }
        finally { if (temp is not null) { try { File.Delete(temp); } catch (IOException) { } } }
    }
    private static async Task<FileStream> Lock(string path, CancellationToken ct) {
        var deadline = Environment.TickCount64 + 2000;
        while (true) { ct.ThrowIfCancellationRequested(); try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, true); }
            catch (IOException) when (Environment.TickCount64 < deadline) { await Task.Delay(40, ct); } }
    }
    private static AssistantException Conflict() => new(409, "policy_conflict", "Kho đã thay đổi hoặc mã yêu cầu đã được dùng. Tải lại và kiểm tra trước khi lưu.");
    private static AssistantException Unavailable() => new(503, "policy_source_unavailable", "Chưa đọc hoặc lưu được chính sách cửa hàng. Vui lòng thử lại.");
}
