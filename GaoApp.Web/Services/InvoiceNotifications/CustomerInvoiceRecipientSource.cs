using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;

namespace GaoApp.Web.Services.InvoiceNotifications;

public interface ICustomerInvoiceRecipientSource
{
    Task<IReadOnlyList<CustomerInvoiceRecipientCandidate>> ListAsync(CustomerChannelConnection connection, string query, CancellationToken ct);
    Task<CustomerInvoiceRecipientInspection> InspectAsync(CustomerChannelConnection connection, Guid conversationId, CancellationToken ct);
    Task<CustomerInvoiceRecipientEvidence> ResolveAsync(CustomerChannelConnection connection, Guid conversationId, long incomingSequence, CancellationToken ct);
}
public sealed record CustomerInvoiceRecipientCandidate(Guid ConversationId, string DisplayName, DateTimeOffset IncomingAt);
public sealed record CustomerInvoiceRecipientInspection(CustomerInvoiceRecipientEvidence Evidence, string DisplayName);
public sealed record CustomerInvoiceRecipientEvidence(Guid SenderProfileId, Guid ConversationId, string NativePeer, long IncomingSequence, DateTimeOffset ObservedAtUtc);

/// <summary>Reads authenticated SDK origin evidence; customer ownership still requires the staff verification flow.</summary>
public sealed class CustomerInvoiceRecipientSource(CustomerChannelTransport transport, CustomerChannelRegistry registry, TimeProvider clock) : ICustomerInvoiceRecipientSource
{
    public async Task<IReadOnlyList<CustomerInvoiceRecipientCandidate>> ListAsync(CustomerChannelConnection connection, string query, CancellationToken ct)
    {
        var current = Current(connection);
        try
        {
            if (query.Length > 100 || query.Any(char.IsControl)) throw Invalid();
            // Manual discovery only: Waiting history does not enable AI intake or prove any customer identity.
            var page = await GetAsync(current, "api/v1/inbox/conversations?page=1&kind=Direct&search=" + Uri.EscapeDataString(query.Trim()), ct);
            _ = Current(connection);
            var summaries = page.GetProperty("conversations"); if (summaries.GetArrayLength() > 25) throw Invalid();
            var candidates = new List<CustomerInvoiceRecipientCandidate>(); var ids = new HashSet<Guid>();
            foreach (var summary in summaries.EnumerateArray())
            {
                var id = summary.GetProperty("id").GetGuid(); var kind = summary.GetProperty("conversationKind").GetString();
                if (id == Guid.Empty || !ids.Add(id) || kind is not ("Direct" or "Group")) throw Invalid();
                var observed = summary.GetProperty("lastObservedAtUtc").GetDateTimeOffset();
                if (observed > clock.GetUtcNow().AddMinutes(5)) throw Invalid();
                if (kind == "Direct") candidates.Add(new(id, Name(summary.GetProperty("displayName").GetString()), observed));
            }
            return candidates;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { throw Invalid(); }
    }
    public Task<CustomerInvoiceRecipientInspection> InspectAsync(CustomerChannelConnection connection, Guid conversationId, CancellationToken ct)
        => ReadAsync(connection, conversationId, null, ct);
    public async Task<CustomerInvoiceRecipientEvidence> ResolveAsync(CustomerChannelConnection connection, Guid conversationId, long incomingSequence, CancellationToken ct)
    {
        if (incomingSequence <= 0) throw Invalid();
        return (await ReadAsync(connection, conversationId, incomingSequence, ct)).Evidence;
    }
    private CustomerChannelConnection Current(CustomerChannelConnection connection)
    {
        var current = registry.Get(connection.Id);
        if (!current.Enabled || current.Provider != "zalomarketing" || current.StoreId != connection.StoreId || current.Binding != connection.Binding ||
            !Guid.TryParse(current.AccountId, out var profile) || profile == Guid.Empty)
            throw Invalid();
        return current;
    }
    private async Task<CustomerInvoiceRecipientInspection> ReadAsync(CustomerChannelConnection connection, Guid conversationId, long? incomingSequence, CancellationToken ct)
    {
        var current = Current(connection); var profile = Guid.Parse(current.AccountId);
        if (conversationId == Guid.Empty) throw Invalid();
        try
        {
            // No before cursor: only the current latest incoming anchor can establish a new binding.
            var detail = await GetAsync(current, $"api/v1/inbox/conversations/{conversationId:D}", ct);
            _ = Current(connection);
            if (detail.GetProperty("id").GetGuid() != conversationId || detail.GetProperty("kind").GetString() != "Direct") throw Invalid();
            var proof = detail.GetProperty("recipientEvidence");
            var raw = proof.GetProperty("nativePeer").GetString();
            var latestSequence = proof.GetProperty("latestIncomingSequence").GetInt64();
            if (proof.GetProperty("transport").GetString() != "Sdk" || proof.GetProperty("kind").GetString() != "Direct" ||
                proof.GetProperty("senderProfileId").GetGuid() != profile || proof.GetProperty("conversationId").GetGuid() != conversationId ||
                latestSequence <= 0 || incomingSequence is { } expected && latestSequence != expected || raw is null || !Regex.IsMatch(raw, @"\A[1-9][0-9]{0,31}\z")) throw Invalid();
            var observed = proof.GetProperty("observedAtUtc").GetDateTimeOffset();
            if (observed > clock.GetUtcNow().AddMinutes(5)) throw Invalid();
            var messages = detail.GetProperty("messages");
            if (messages.GetArrayLength() > 50) throw Invalid();
            JsonElement? latest = null; long previous = 0;
            foreach (var message in messages.EnumerateArray())
            {
                var sequence = message.GetProperty("sequence").GetInt64();
                if (sequence <= previous) throw Invalid();
                previous = sequence;
                if (!message.GetProperty("isOutgoing").GetBoolean()) latest = message;
            }
            if (latest is not { } anchor || anchor.GetProperty("sequence").GetInt64() != latestSequence ||
                anchor.GetProperty("senderId").GetString() != raw || anchor.GetProperty("observedAtUtc").GetDateTimeOffset() != observed) throw Invalid();
            return new(new(profile, conversationId, "sdk-v1:" + raw, latestSequence, observed), Name(detail.GetProperty("displayName").GetString()));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw Invalid(); }
    }
    private static string Name(string? value) => new((value ?? "").Where(c => !char.IsControl(c)).Take(120).ToArray());
    private async Task<JsonElement> GetAsync(CustomerChannelConnection connection, string path, CancellationToken ct)
    {
        try { return await transport.ZaloGet(connection, path, ct); }
        catch (Exception e) when (e is HttpRequestException or IOException || e is OperationCanceledException && !ct.IsCancellationRequested || e.GetType().DeclaringType == typeof(CustomerChannelTransport))
        { throw new AssistantException(503, "invoice_recipient_source_unavailable", "Chưa đọc được hội thoại từ ZaloMarketing. Hãy kiểm tra kết nối và quyền đọc hộp thư."); }
    }
    private static AssistantException Invalid() => new(409, "invoice_recipient_evidence_invalid", "Chưa xác minh được hội thoại SDK và tin nhận mới nhất thuộc đúng tài khoản.");
}
