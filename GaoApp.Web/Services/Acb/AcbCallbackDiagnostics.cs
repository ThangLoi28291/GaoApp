using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Services.Banking;

namespace GaoApp.Web.Services.Acb;

// Only correlation identifiers and application-owned diagnostics belong in logs.
// Authenticated payloads remain in the restricted database inbox, never in a text log.
public sealed class AcbCallbackDiagnostics(ILogger<AcbCallbackDiagnostics> logger)
{
    private readonly object _gate = new();
    private readonly Queue<AcbCallbackHttpAttempt> _recent = new();
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

    // Bounded, process-local diagnostics only; committed bank evidence stays in the inbox.
    public IReadOnlyList<AcbCallbackHttpAttempt> RecentForStore(int storeId, IReadOnlyCollection<string> unassignedHosts)
    {
        lock (_gate)
            return _recent.Reverse().Where(x => x.StoreId == storeId ||
                (x.StoreId == null && x.RoutedStoreId == storeId) ||
                (x.StoreId == null && x.RoutedStoreId == null && unassignedHosts.Contains(x.RequestHost, StringComparer.OrdinalIgnoreCase)))
                .Take(20).ToArray();
    }

    public static string ErrorCode(Exception error) => error switch
    {
        AcbApiException { ProviderResponseCode: not null } bank => "ACB_" + bank.ProviderResponseCode,
        AcbApiException { BusinessRequestNotSent: true } => "ACB_TOKEN_FAILED",
        AcbApiException { ResponseFailureReason: AcbResponseFailureReason.InvalidJson } => "BANK_RESPONSE_INVALID_JSON",
        AcbApiException => "ACB_REQUEST_FAILED",
        HttpRequestException => "BANK_CONNECTION_FAILED",
        OperationCanceledException => "BANK_TIMEOUT",
        System.Text.Json.JsonException => "BANK_RESPONSE_INVALID_JSON",
        System.Security.Cryptography.CryptographicException => "CONFIG_DECRYPT_FAILED",
        Microsoft.EntityFrameworkCore.DbUpdateException => "DATABASE_WRITE_FAILED",
        _ => "PROCESSING_RETRY"
    };

    public void Http(string diagnosticId, int? storeId, int status, string outcome, int? receiptId, long elapsedMs, Exception? error, string authHeader,
        string? requestHost = null, int? routedStoreId = null, string method = "POST")
    {
        if (HttpMethods.IsPost(method))
        {
            lock (_gate)
            {
                _recent.Enqueue(new(DateTime.UtcNow, diagnosticId, storeId, routedStoreId, status, outcome, receiptId, requestHost ?? ""));
                while (_recent.Count > 200) _recent.Dequeue();
            }
        }
        logger.Log(status >= 400 ? LogLevel.Warning : LogLevel.Information,
            "ACB callback HTTP: DiagnosticId={DiagnosticId}; StoreId={StoreId}; Status={Status}; Outcome={Outcome}; ReceiptId={ReceiptId}; ElapsedMs={ElapsedMs}; AuthHeader={AuthHeader}; RequestHost={RequestHost}; RoutedStoreId={RoutedStoreId}; ExceptionType={ExceptionType}; HResult={HResult}",
            diagnosticId, storeId, status, outcome, receiptId, elapsedMs, authHeader, requestHost, routedStoreId, error?.GetType().Name, error?.HResult);
    }

    public void Process(AcbCallbackReceipt receipt, string outcome, Exception? error = null)
        => logger.Log(error != null || receipt.NeedsReview ? LogLevel.Warning : LogLevel.Information,
            "ACB callback processing: StoreId={StoreId}; ReceiptId={ReceiptId}; ClientRequestId={ClientRequestId}; RequestCode={RequestCode}; Page={Page}/{TotalPages}; Attempt={Attempt}; Outcome={Outcome}; NextAttemptAtUtc={NextAttemptAtUtc}; ExceptionType={ExceptionType}; HResult={HResult}; SafeBankDiagnostic={SafeBankDiagnostic}",
            receipt.StoreId, receipt.Id, receipt.ClientRequestId, receipt.RequestCode, receipt.Page, receipt.TotalPages, receipt.Attempts, outcome,
            receipt.ProcessedAtUtc == null ? receipt.NextAttemptAtUtc : (DateTime?)null, error?.GetType().Name, error?.HResult, error is AcbApiException bank ? bank.Message : null);

    public void WorkerFailure(Exception error) => logger.LogWarning(
        "ACB callback worker failed: Outcome={Outcome}; ExceptionType={ExceptionType}; HResult={HResult}. Saved receipts will retry.",
        ErrorCode(error), error.GetType().Name, error.HResult);
}

public sealed record AcbCallbackHttpAttempt(DateTime AtUtc, string DiagnosticId, int? StoreId, int? RoutedStoreId,
    int Status, string Outcome, int? ReceiptId, string RequestHost);
