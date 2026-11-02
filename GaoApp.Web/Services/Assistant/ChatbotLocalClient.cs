using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatbotLocal.Integration;

// Backend sample only. GaoApp supplies both freshly signed tokens from its trusted identity service.
public sealed class AssistantTokenPair(string context, string delegation)
{
    [JsonIgnore] public string Context { get; } = context;
    [JsonIgnore] public string Delegation { get; } = delegation;
    public override string ToString() => "AssistantTokenPair [redacted]";
}
public enum AssistantOperation { Capabilities, ListConversations, GetConversation, History, CreateConversation,
    ResumeConversation, Chat, Quote, Receiving, NormalizeName, RenameConversation, DeleteConversation, RequestStatus, CancelRequest, Feedback, Monitor, ResolveFeedback }
public sealed record AssistantReply(int HttpStatus, JsonElement Body, string? PollPath)
{
    public bool IsSuccess => HttpStatus is >= 200 and < 300;
    public bool IsPending => HttpStatus == 202;
}

// Each call carries its own identity/RequestId. No shared bearer headers, automatic retries or arbitrary URLs.
public sealed class ChatbotLocalClient : IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public ChatbotLocalClient(Uri baseUri, bool allowLoopbackTest = false, HttpMessageHandler? testHandler = null)
    {
        if (!baseUri.IsAbsoluteUri || !(baseUri.Scheme == "https" || allowLoopbackTest && baseUri.IsLoopback && baseUri.Scheme == "http")
            || baseUri.UserInfo.Length != 0 || baseUri.Query.Length != 0 || baseUri.Fragment.Length != 0)
            throw new ArgumentException("A trusted HTTPS base URL is required.", nameof(baseUri));
        if (testHandler is not null && !allowLoopbackTest) throw new ArgumentException("Custom handlers are TEST only.");
        _http = new(testHandler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            { BaseAddress = new(baseUri.AbsoluteUri.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(100) };
    }
    public async Task<AssistantReply> CallAsync(AssistantOperation operation, AssistantTokenPair tokens,
        Guid? requestId = null, object? body = null, Guid? resourceId = null, int limit = 100,
        string? cursor = null, CancellationToken cancellationToken = default, bool includeClarification = false, bool includeProducts = false, bool includeFeedback = false, int reportDays = 1)
    {
        if (string.IsNullOrWhiteSpace(tokens.Context) || string.IsNullOrWhiteSpace(tokens.Delegation)
            || tokens.Context.Length > 16384 || tokens.Delegation.Length > 16384 || requestId == Guid.Empty)
            throw new ArgumentException("A valid token pair and RequestId are required.");
        if ((includeClarification || includeProducts) && operation != AssistantOperation.History) throw new ArgumentException("Clarification metadata is History-only.");
        string Resource() => resourceId is { } id && id != Guid.Empty ? id.ToString() : throw new ArgumentException("ResourceId required.");
        var (method, route) = operation switch
        {
            AssistantOperation.Capabilities => (HttpMethod.Get, "capabilities"),
            AssistantOperation.ListConversations => (HttpMethod.Get, "conversations"),
            AssistantOperation.GetConversation => (HttpMethod.Get, "conversations/" + Resource()),
            AssistantOperation.History => (HttpMethod.Get, "conversations/" + Resource() + "/messages"),
            AssistantOperation.CreateConversation => (HttpMethod.Post, "conversations"),
            AssistantOperation.ResumeConversation => (HttpMethod.Post, "conversations/resume"),
            AssistantOperation.Chat => (HttpMethod.Post, "chat/messages"),
            AssistantOperation.Quote => (HttpMethod.Post, "quotes/lookup"),
            AssistantOperation.Receiving => (HttpMethod.Post, "receiving/resolve"),
            AssistantOperation.NormalizeName => (HttpMethod.Post, "products/normalize-name"),
            AssistantOperation.Feedback => (HttpMethod.Post, "chat/feedback"),
            AssistantOperation.Monitor => (HttpMethod.Get, "operations/summary?days="+(reportDays is 1 or 7?reportDays:throw new ArgumentException("Invalid report period."))),
            AssistantOperation.ResolveFeedback => (HttpMethod.Post, "operations/feedback/"+Resource()+"/resolve"),
            AssistantOperation.RenameConversation => (HttpMethod.Patch, "conversations/" + Resource()),
            AssistantOperation.DeleteConversation => (HttpMethod.Delete, "conversations/" + Resource()),
            AssistantOperation.RequestStatus => (HttpMethod.Get, "requests/" + Resource()),
            AssistantOperation.CancelRequest => (HttpMethod.Post, "requests/" + Resource() + "/cancel"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        if (method != HttpMethod.Get && operation != AssistantOperation.CancelRequest && requestId is null)
            throw new ArgumentException("Mutations require an existing logical RequestId.");
        if (operation is AssistantOperation.ListConversations or AssistantOperation.History)
        {
            if (limit is < 1 or > 200 || cursor?.Length > 2048) throw new ArgumentException("Invalid pagination.");
            route += "?limit=" + limit + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            if (includeClarification) route += "&includeClarification=true";
            if (includeProducts) route += "&includeProducts=true";
            if (includeFeedback) route += "&includeFeedback=true";
        }
        using var request = new HttpRequestMessage(method, "api/v1/" + route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Context);
        request.Headers.Add("X-GaoApp-Delegation", tokens.Delegation);
        if (requestId is { } logical) request.Headers.Add("X-Request-Id", logical.ToString());
        if (body is not null || method == HttpMethod.Post && operation != AssistantOperation.CancelRequest)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body ?? new { }, Json);
            if (bytes.Length > 16384) throw new ArgumentException("Request exceeds 16 KiB.");
            request.Content = new ByteArrayContent(bytes); request.Content.Headers.ContentType = new("application/json");
        }
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        string? poll = null;
        if ((int)response.StatusCode == 202)
        {
            poll = response.Headers.Location?.OriginalString;
            if (requestId is null || poll != "/api/v1/requests/" + requestId)
                throw new InvalidDataException("Invalid pending location; use RequestStatus with the original RequestId.");
        }
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var bytesOut = new MemoryStream(); var buffer = new byte[4096]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        { if (bytesOut.Length + count > 2 * 1024 * 1024) throw new InvalidDataException("Response exceeds 2 MiB."); bytesOut.Write(buffer, 0, count); }
        try
        {
            using var document = JsonDocument.Parse(bytesOut.ToArray());
            return new((int)response.StatusCode, document.RootElement.Clone(), poll);
        }
        catch (JsonException) { throw new InvalidDataException("Invalid assistant JSON response."); }
    }
    public void Dispose() => _http.Dispose();
}
