using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.Assistant;

public sealed record SearchCorrection(Guid Id, int Owner, string Query, int ProductId, int VariantId,
    Guid? ConversationId, string? MessageId, string Status, string Version, Guid LastRequestId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int? ReviewedBy = null, Guid ProposalRequestId = default);
public sealed record SearchKnowledgeDocument(int SchemaVersion, int StoreId, SearchCorrection[] Entries);

// Teaching data lives separately from transcripts. Only explicitly approved search phrases affect retrieval.
// Atomic replacement and a cross-process lock protect IIS recycle overlap and concurrent reviewers.
public sealed class AssistantSearchKnowledge(IWebHostEnvironment environment, IOptionsMonitor<AssistantOptions> options, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public const int MaximumEntries = 5000;
    private string FileName(int store) {
        if (store <= 0) throw AssistantException.Denied();
        return AssistantStorageSafety.SettingsFile(environment, options.CurrentValue, $"store-{store}-search-knowledge.json");
    }
    public async Task<SearchKnowledgeDocument> ReadAsync(int store, CancellationToken ct)
    {
        try {
            await using var file = new FileStream(FileName(store), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, true);
            if (file.Length > 4 * 1024 * 1024) throw new JsonException();
            using var raw = await JsonDocument.ParseAsync(file, cancellationToken: ct);
            AssistantJwt.Unique(raw.RootElement);
            var value = raw.RootElement.Deserialize<SearchKnowledgeDocument>(Json);
            if (value is null || value.SchemaVersion != 1 || value.StoreId != store || value.Entries is null || value.Entries.Length > MaximumEntries
                || value.Entries.Any(e => e is null || e.Id == Guid.Empty || e.Owner <= 0 || !ValidQuery(e.Query) || e.ProductId <= 0 || e.VariantId <= 0
                    || e.Status is not ("pending" or "approved" or "rejected") || !Guid.TryParseExact(e.Version, "N", out _)
                    || e.LastRequestId == Guid.Empty || e.ProposalRequestId == Guid.Empty || e.CreatedAt == default || e.UpdatedAt < e.CreatedAt || e.UpdatedAt > clock.GetUtcNow().AddMinutes(1)
                    || e.Status == "approved" && e.ReviewedBy is not > 0)
                || value.Entries.Select(e => e.Id).Distinct().Count() != value.Entries.Length) throw new JsonException();
            var now = clock.GetUtcNow();
            return value with { Entries = value.Entries.Where(e => e.Status == "approved"
                || e.UpdatedAt > now.AddDays(e.Status == "pending" ? -7 : -30)).ToArray() };
        } catch (FileNotFoundException) { return new(1, store, []); }
        catch (DirectoryNotFoundException) { return new(1, store, []); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or AssistantException) { throw Unavailable(); }
    }
    public static bool ValidQuery(string? query) => !string.IsNullOrWhiteSpace(query) && query.Length <= 200
        && !query.Any(char.IsControl) && AssistantCatalogSearch.IsSpecific(query);
    public async Task<SearchCorrection> ProposeAsync(int store, int owner, string query, int product, int variant,
        Guid request, Guid? conversation, string? message, CancellationToken ct)
    {
        query = query.Trim();
        if (owner <= 0 || !ValidQuery(query) || product <= 0 || variant <= 0 || request == Guid.Empty) throw AssistantException.Invalid();
        return await Change(store, entries => {
            var previous = entries.SingleOrDefault(e => e.Owner == owner && e.ProposalRequestId == request);
            if (previous is not null) {
                if (previous.Query != query || previous.ProductId != product || previous.VariantId != variant || previous.ConversationId != conversation || previous.MessageId != message) throw Conflict();
                return previous;
            }

            if (entries.Count >= MaximumEntries) throw new AssistantException(409, "search_knowledge_full", "Kho cách gọi đã đầy. Quản trị kiểm tra trước khi bổ sung.");
            var now = clock.GetUtcNow();
            var next = new SearchCorrection(Guid.NewGuid(), owner, query, product, variant, conversation, message,
                "pending", Guid.NewGuid().ToString("N"), request, now, now, ProposalRequestId: request);
            entries.Add(next); return next;
        }, ct);
    }
    public async Task<SearchCorrection> ReviewAsync(int store, int reviewer, Guid id, string version, string action,
        Guid request, CatalogAttributes? currentAttributes, CancellationToken ct)
    {
        if (reviewer <= 0 || id == Guid.Empty || request == Guid.Empty || !Guid.TryParseExact(version, "N", out _) || action is not ("approve" or "reject")) throw AssistantException.Invalid();
        return await Change(store, entries => {
            var row = entries.SingleOrDefault(e => e.Id == id) ?? throw new AssistantException(404, "correction_not_found", "Không tìm thấy đề xuất trong cửa hàng hiện tại.");
            var status = action == "approve" ? "approved" : "rejected";
            if (row.LastRequestId == request && row.Status == status && row.ReviewedBy == reviewer) return row;
            if (row.Version != version) throw Conflict();
            if (action == "approve") {
                if (currentAttributes is null) throw new AssistantException(422, "invalid_selection", "Cần kiểm tra lại sản phẩm trước khi duyệt.");
                var proposed = new CatalogAttributes { Sugar = AssistantCatalogSearch.Sugar(row.Query), Capacity = AssistantCatalogSearch.Capacity(row.Query)?.Replace('p', '.') };
                if (currentAttributes.Sugar is not null && proposed.Sugar is not null && currentAttributes.Sugar != proposed.Sugar
                    || currentAttributes.Capacity is not null && proposed.Capacity is not null && AssistantCatalogSearch.Capacity(currentAttributes.Capacity) != AssistantCatalogSearch.Capacity(proposed.Capacity))
                    throw new AssistantException(422, "correction_attribute_conflict", "Cách gọi mâu thuẫn loại đường hoặc dung tích của mã hàng.");
                foreach (var other in entries.Where(e => e.Id != id && e.VariantId == row.VariantId && e.Status == "approved")) {
                    var oldSugar = AssistantCatalogSearch.Sugar(other.Query); var oldCapacity = AssistantCatalogSearch.Capacity(other.Query);
                    if (proposed.Sugar is not null && oldSugar is not null && proposed.Sugar != oldSugar
                        || proposed.Capacity is not null && oldCapacity is not null && AssistantCatalogSearch.Capacity(proposed.Capacity) != oldCapacity)
                        throw new AssistantException(422, "correction_attribute_conflict", "Mã hàng này đã có cách gọi được duyệt với thuộc tính khác. Kiểm tra và thu hồi cách gọi sai trước.");
                }
            }
            var next = row with { Status = status, Version = Guid.NewGuid().ToString("N"), LastRequestId = request, UpdatedAt = clock.GetUtcNow(), ReviewedBy = reviewer };
            entries[entries.IndexOf(row)] = next; return next;
        }, ct);
    }
    private async Task<T> Change<T>(int store, Func<List<SearchCorrection>, T> mutation, CancellationToken ct)
    {
        var path = FileName(store); string? temporary = null;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var lease = await Lock(path + ".lock", ct);
            var document = await ReadAsync(store, ct); var entries = document.Entries.ToList();
            var result = mutation(entries); ct.ThrowIfCancellationRequested();
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(document with { Entries = entries.ToArray() }, Json), ct);
            ct.ThrowIfCancellationRequested(); File.Move(temporary, path, true); temporary = null; return result;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw Unavailable(); }
        finally { if (temporary is not null) { try { File.Delete(temporary); } catch (IOException) { } } }
    }
    private static async Task<FileStream> Lock(string path, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + 2000;
        while (true) {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, true); }
            catch (IOException) when (Environment.TickCount64 < deadline) { await Task.Delay(40, ct); }
        }
    }
    private static AssistantException Conflict() => new(409, "correction_conflict", "Đề xuất đã thay đổi hoặc mã yêu cầu đã được dùng. Tải lại trước khi xử lý.");
    private static AssistantException Unavailable() => new(503, "search_knowledge_unavailable", "Chưa đọc hoặc lưu được cách gọi của cửa hàng. Vui lòng thử lại.");
}
