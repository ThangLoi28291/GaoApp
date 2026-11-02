using GaoApp.Application.Common.Security;
using GaoApp.Application.Common;
using GaoApp.Infrastructure.Services.Audit;
using GaoApp.Web.Configuration;
using GaoApp.Web.Security;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GaoApp.Tests.Security;

public sealed class InvoiceLinkPrivacyTests
{
    private const string Token = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [Fact]
    public void Structured_request_route_nested_values_and_URLs_are_redacted_before_sinks()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/invoice-info/" + Token;
        var privacy = new InvoiceLinkLogPrivacy(new HttpContextAccessor { HttpContext = context });
        var sink = new CapturingSink();
        using var logger = new LoggerConfiguration().Enrich.With(privacy).Filter.With(privacy).WriteTo.Sink(sink).CreateLogger();
        logger.ForContext("RequestPath", context.Request.Path)
            .ForContext("token", Token)
            .ForContext("Nested", new Dictionary<string, object> { ["token"] = Token, ["values"] = new[] { Token, "https://shop.test/invoice-info/" + Token } }, true)
            .Information("HTTP {Path} {Value} Trace={TraceId}", context.Request.Path, Token.ToUpperInvariant(), "trace-123");
        var item = Assert.Single(sink.Events);
        var rendered = item.RenderMessage() + string.Join(" ", item.Properties.Select(x => x.Value.ToString()));
        Assert.DoesNotContain(Token, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/invoice-info/[redacted]", rendered);
        Assert.Contains("trace-123", rendered);
    }

    [Fact]
    public void Unsafe_literal_and_exception_are_suppressed_but_safe_diagnostics_remain()
    {
        var privacy = new InvoiceLinkLogPrivacy();
        var sink = new CapturingSink();
        using var logger = new LoggerConfiguration().Enrich.With(privacy).Filter.With(privacy).WriteTo.Sink(sink).CreateLogger();
        logger.Error(new InvalidOperationException("https://shop.test/invoice-info/" + Token), "Failed request");
        logger.Error("Failed /invoice-info/" + Token);
        logger.Error("Failure type={ExceptionType}; Trace={TraceId}", "InvalidOperationException", "trace-456");
        Assert.Single(sink.Events);
        Assert.Contains("trace-456", sink.Events[0].RenderMessage());
    }

    [Fact]
    public void Audit_context_never_persists_invoice_link_or_user_agent_token()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/invoice-info/" + Token;
        context.Request.Headers.UserAgent = "Test " + Token;
        var audit = new HttpAuditExecutionContextAccessor(new HttpContextAccessor { HttpContext = context }, new Tenant());
        var current = audit.GetCurrent();
        Assert.Equal("/invoice-info/[redacted]", current.Path);
        Assert.DoesNotContain(Token, current.UserAgent);
        Assert.Equal(context.TraceIdentifier, current.TraceId);
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
    private sealed class Tenant : ITenantContext
    {
        public int? StoreId => 1;
        public bool IsHostAdmin => false;
        public string? Subdomain => "test";
    }
}
