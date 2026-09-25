using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Web.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace GaoApp.Tests.Observability;

public sealed class GlobalExceptionMiddlewareTests
{
    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task Expected_failure_is_mapped_without_error_log(
        Exception exception,
        int expectedStatus)
    {
        var (context, logger) = await InvokeAsync(exception);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Theory]
    [MemberData(nameof(UnexpectedFrameworkFailures))]
    public async Task Untyped_framework_failure_is_500_and_logged_once(
        Exception exception)
    {
        var (context, logger) = await InvokeAsync(exception);
        var body = await ReadBodyAsync(context);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            context.Response.StatusCode);
        Assert.Contains("Có lỗi hệ thống xảy ra.", ReadMessage(body));
        Assert.DoesNotContain(exception.Message, body, StringComparison.Ordinal);
        var error = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.Null(error.Exception);
        Assert.False(
            string.IsNullOrWhiteSpace(error.StructuredState["Fingerprint"]?.ToString()));
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Unexpected_exception_should_not_pass_original_exception_to_logger()
    {
        var expected = new InvalidOperationException(
            "Password=synthetic-original-exception");

        var (_, logger) = await InvokeAsync(expected);

        var error = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.Null(error.Exception);
    }

    [Fact]
    public async Task Unexpected_exception_should_log_safe_stack_frames()
    {
        var logger = new RecordingLogger<GlobalExceptionMiddleware>();
        var context = CreateContext();
        var middleware = CreateMiddleware(
            _ => ThrowSyntheticTechnicalFailure(),
            logger);

        await middleware.InvokeAsync(context);

        var error = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        var stackFrames = Assert.IsType<string>(
            error.StructuredState["StackFrames"]);
        Assert.False(string.IsNullOrWhiteSpace(stackFrames));
        Assert.Contains(
            nameof(ThrowSyntheticTechnicalFailure),
            stackFrames,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "synthetic-stack-trace-detail",
            stackFrames,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            @"C:\",
            stackFrames,
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(error.Exception);
    }

    [Fact]
    public async Task Unexpected_exception_should_log_safe_inner_type_chain()
    {
        var logger = new RecordingLogger<GlobalExceptionMiddleware>();
        var context = CreateContext();
        var middleware = CreateMiddleware(
            _ => ThrowSyntheticFailureWithInner(),
            logger);

        await middleware.InvokeAsync(context);
        var error = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        var innerTypes = Assert.IsType<string>(
            error.StructuredState["InnerExceptionTypes"]);
        Assert.Contains(
            nameof(HttpRequestException),
            innerTypes,
            StringComparison.Ordinal);
        Assert.DoesNotContain("outer-secret", innerTypes, StringComparison.Ordinal);
        Assert.DoesNotContain("inner-secret", innerTypes, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", innerTypes, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=", innerTypes, StringComparison.Ordinal);
        Assert.Null(error.Exception);
    }

    [Fact]
    public async Task Unexpected_exception_should_log_stable_fingerprint()
    {
        var sameStackFingerprints = new List<string>();

        for (var i = 0; i < 2; i++)
        {
            var logger = new RecordingLogger<GlobalExceptionMiddleware>();
            var middleware = CreateMiddleware(
                _ => ThrowSyntheticTechnicalFailure(),
                logger);

            await middleware.InvokeAsync(CreateContext());

            var error = Assert.Single(
                logger.Entries,
                entry => entry.Level == LogLevel.Error);
            sameStackFingerprints.Add(
                Assert.IsType<string>(
                    error.StructuredState["Fingerprint"]));
        }

        var differentLogger = new RecordingLogger<GlobalExceptionMiddleware>();
        var differentMiddleware = CreateMiddleware(
            _ => ThrowDifferentSyntheticTechnicalFailure(),
            differentLogger);
        await differentMiddleware.InvokeAsync(CreateContext());
        var differentFingerprint = Assert.IsType<string>(
            Assert.Single(
                differentLogger.Entries,
                entry => entry.Level == LogLevel.Error)
            .StructuredState["Fingerprint"]);

        Assert.Equal(sameStackFingerprints[0], sameStackFingerprints[1]);
        Assert.NotEqual(sameStackFingerprints[0], differentFingerprint);
    }

    [Fact]
    public async Task Bad_http_request_is_400_without_error_log()
    {
        var (context, logger) = await InvokeAsync(
            new BadHttpRequestException("malformed request"));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Ef_concurrency_is_conflict()
    {
        const string secret =
            "Server=private-sql;Password=synthetic-secret";
        var (context, logger) = await InvokeAsync(
            new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException(
                secret));

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        var body = await ReadBodyAsync(context);
        Assert.Contains(
            "Dữ liệu đã được thay đổi",
            ReadMessage(body),
            StringComparison.Ordinal);
        Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Unexpected_exception_rendered_log_should_not_contain_secrets()
    {
        var secrets = new[]
        {
            "private-sql",
            "secret-db",
            "secret-user",
            "synthetic-password",
            "synthetic-token",
            @"C:\private\app",
            "buyer@example.invalid",
            "secret-uuid"
        };
        var exception = new InvalidOperationException(
            "Server=private-sql;Database=secret-db;User Id=secret-user;" +
            "Password=synthetic-password;access_token=synthetic-token;" +
            @"C:\private\app;buyer@example.invalid;transactionUuid=secret-uuid",
            new HttpRequestException(
                "Server=inner-private-sql;Password=inner-synthetic-password"));
        var (context, logger) = await InvokeAsync(exception);
        var body = await ReadBodyAsync(context);
        var renderedLogs = string.Join(
            Environment.NewLine,
            logger.Entries.Select(RenderLogEntry));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains(
            "Có lỗi hệ thống xảy ra.",
            ReadMessage(body),
            StringComparison.Ordinal);
        Assert.Contains(context.TraceIdentifier, body, StringComparison.Ordinal);
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                secret,
                renderedLogs,
                StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(nameof(HttpRequestException), renderedLogs, StringComparison.Ordinal);
        Assert.Single(logger.Entries, x => x.Level == LogLevel.Error);
        Assert.Null(
            Assert.Single(
                logger.Entries,
                x => x.Level == LogLevel.Error)
            .Exception);
    }

    [Fact]
    public async Task Public_500_response_should_remain_generic_in_development()
    {
        const string detail = "Password=development-only-detail";
        var (context, _) = await InvokeAsync(
            new InvalidDataException(detail));

        Assert.DoesNotContain(
            detail,
            await ReadBodyAsync(context),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_without_error_log()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var logger = new RecordingLogger<GlobalExceptionMiddleware>();
        var context = CreateContext();
        context.RequestAborted = cts.Token;
        var middleware = CreateMiddleware(
            _ => Task.FromException(new OperationCanceledException(cts.Token)),
            logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => middleware.InvokeAsync(context));

        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Non_caller_operation_cancellation_is_unexpected_500()
    {
        var (context, logger) = await InvokeAsync(new OperationCanceledException());

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            context.Response.StatusCode);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Lower_layer_warning_plus_global_error_should_not_duplicate_error_event()
    {
        var logger = new RecordingLogger<GlobalExceptionMiddleware>();
        var context = CreateContext();
        var middleware = CreateMiddleware(
            _ =>
            {
                logger.LogWarning(
                    "Lower layer degraded safely. TraceId={TraceId}",
                    context.TraceIdentifier);
                return ThrowSyntheticTechnicalFailure();
            },
            logger);

        await middleware.InvokeAsync(context);

        Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning);
        Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Exception_after_response_started_is_rethrown()
    {
        var feature = new StartedResponseFeature();
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());
        features.Set<IHttpResponseFeature>(feature);
        var context = new DefaultHttpContext(features)
        {
            TraceIdentifier = "trace-started"
        };
        var logger = new RecordingLogger<GlobalExceptionMiddleware>();
        var expected = new InvalidDataException("must propagate");
        var middleware = CreateMiddleware(
            _ => Task.FromException(expected),
            logger);

        var actual = await Assert.ThrowsAsync<InvalidDataException>(
            () => middleware.InvokeAsync(context));

        Assert.Same(expected, actual);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Pos_hold_inventory_shortage_is_409_with_structured_payload_and_warning_log()
    {
        const string actionHint =
            "Hãy giảm số lượng hoặc kiểm tra tồn kho rồi thử lại.";
        var exception = PosAppException.StateConflict(
            PosErrorCodes.CartHoldInsufficientInventory,
            "Không thể giữ đơn vì Nước suối không đủ tồn khả dụng tại Kho chính. " +
            "Khả dụng: 0 Chai; cần giữ: 1 Chai. " + actionHint,
            actionHint,
            new
            {
                itemName = "Nước suối",
                warehouseName = "Kho chính",
                availableBaseQty = 0m,
                requiredBaseQty = 1m,
                baseUnitName = "Chai",
                sellingQuantity = 1m,
                sellingUnitName = "Chai",
                multiplier = 1m
            });

        var (context, logger) = await InvokeAsync(exception);
        var body = await ReadBodyAsync(context);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(
            PosErrorCodes.CartHoldInsufficientInventory,
            root.GetProperty("errorCode").GetString());
        Assert.Equal(
            PosErrorTypes.StateConflict,
            root.GetProperty("errorType").GetString());
        Assert.Equal(actionHint, root.GetProperty("actionHint").GetString());
        Assert.Contains(
            "Nước suối không đủ tồn khả dụng",
            root.GetProperty("message").GetString() ?? string.Empty,
            StringComparison.Ordinal);

        var metadata = root.GetProperty("metadata");
        Assert.Equal("Nước suối", metadata.GetProperty("itemName").GetString());
        Assert.Equal("Kho chính", metadata.GetProperty("warehouseName").GetString());
        Assert.Equal(0m, metadata.GetProperty("availableBaseQty").GetDecimal());
        Assert.Equal(1m, metadata.GetProperty("requiredBaseQty").GetDecimal());
        Assert.Equal("Chai", metadata.GetProperty("baseUnitName").GetString());

        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning);
    }

    public static IEnumerable<object[]> ExpectedFailures()
    {
        yield return new object[]
        {
            new ValidationAppException("validation"), StatusCodes.Status400BadRequest
        };
        yield return new object[]
        {
            new BusinessRuleException("business"), StatusCodes.Status400BadRequest
        };
        yield return new object[]
        {
            new NotFoundAppException("missing"), StatusCodes.Status404NotFound
        };
        yield return new object[]
        {
            new ConflictAppException("conflict"), StatusCodes.Status409Conflict
        };
        yield return new object[]
        {
            new ConcurrencyException("concurrency"), StatusCodes.Status409Conflict
        };
        yield return new object[]
        {
            new ForbiddenAppException("forbidden"), StatusCodes.Status403Forbidden
        };
    }

    public static IEnumerable<object[]> UnexpectedFrameworkFailures()
    {
        yield return new object[]
        {
            new InvalidOperationException("synthetic invalid operation detail")
        };
        yield return new object[]
        {
            new ArgumentNullException(
                "value",
                "synthetic null argument detail")
        };
        yield return new object[]
        {
            new UnauthorizedAccessException(
                "synthetic filesystem permission detail")
        };
        yield return new object[]
        {
            new JsonException("synthetic internal json detail")
        };
    }

    private static async Task<(DefaultHttpContext Context, RecordingLogger<GlobalExceptionMiddleware> Logger)>
        InvokeAsync(Exception exception)
    {
        var logger = new RecordingLogger<GlobalExceptionMiddleware>();
        var context = CreateContext();
        var middleware = CreateMiddleware(
            _ => Task.FromException(exception),
            logger);

        await middleware.InvokeAsync(context);
        return (context, logger);
    }

    private static GlobalExceptionMiddleware CreateMiddleware(
        RequestDelegate next,
        RecordingLogger<GlobalExceptionMiddleware> logger)
        => new(next, logger);

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-r1.6"
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/synthetic";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(
            context.Response.Body,
            leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static async Task<string> ReadMessageAsync(HttpContext context)
        => ReadMessage(await ReadBodyAsync(context));

    private static string ReadMessage(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("message").GetString() ?? string.Empty;
    }

    private static Task ThrowSyntheticTechnicalFailure()
    {
        throw new InvalidOperationException("synthetic-stack-trace-detail");
    }

    private static Task ThrowDifferentSyntheticTechnicalFailure()
    {
        throw new JsonException("synthetic-different-stack-detail");
    }

    private static Task ThrowSyntheticFailureWithInner()
    {
        throw new InvalidOperationException(
            "Password=outer-secret",
            new HttpRequestException("Server=inner-secret"));
    }

    private static string RenderLogEntry(LogEntry entry)
    {
        var state = string.Join(
            Environment.NewLine,
            entry.StructuredState.Select(
                pair => $"{pair.Key}={pair.Value}"));
        return string.Join(
            Environment.NewLine,
            entry.Message,
            state,
            entry.Exception?.ToString() ?? string.Empty);
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }

    public sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> StructuredState,
        Exception? Exception);

    public sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var structuredState =
                state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.Ordinal)
                    : new Dictionary<string, object?>(StringComparer.Ordinal);

            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                structuredState,
                exception));
        }
    }
}
