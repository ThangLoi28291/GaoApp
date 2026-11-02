using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Infrastructure.Services.Audit;
using GaoApp.Web.Configuration;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GaoApp.Tests.Security;
public sealed class InvoiceOfficialLinkPrivacyTests
{
    private const string ProtectedToken="CfDJ8_SYNTHETIC_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_TEST_NOT_A_REAL_CAPABILITY";
    [Fact]public void Document_query_token_nested_properties_and_raw_user_agent_are_redacted_before_log_sinks()
    {
        var context=new DefaultHttpContext();context.Request.Path="/invoice-document/download";context.Request.QueryString=new("?token="+ProtectedToken);
        var privacy=new InvoiceLinkLogPrivacy(new HttpContextAccessor{HttpContext=context});var sink=new Sink();
        using var logger=new LoggerConfiguration().Enrich.With(privacy).Filter.With(privacy).WriteTo.Sink(sink).CreateLogger();
        logger.ForContext("UserAgent","Agent "+ProtectedToken).ForContext("Nested",new{Value=ProtectedToken,Url="https://shop.test/invoice-document?token="+ProtectedToken},true)
            .Information("Request={Value}; Trace={TraceId}",ProtectedToken,"trace-p3");
        var message=Assert.Single(sink.Events);var text=message.RenderMessage()+string.Join(" ",message.Properties.Values);
        Assert.DoesNotContain(ProtectedToken,text);Assert.Contains("trace-p3",text);Assert.Contains("[redacted]",text);
    }
    [Fact]public void URL_tokens_are_redacted_or_suppressed_even_without_an_active_http_context()
    {
        var privacy=new InvoiceLinkLogPrivacy();var sink=new Sink();using var logger=new LoggerConfiguration().Enrich.With(privacy).Filter.With(privacy).WriteTo.Sink(sink).CreateLogger();
        logger.Information("URL={Url}","https://shop.test/invoice-document?mode=x&token="+ProtectedToken);
        logger.Error(new InvalidOperationException("https://shop.test/invoice-document/download?token="+ProtectedToken),"Unsafe exception");
        var message=Assert.Single(sink.Events);Assert.DoesNotContain(ProtectedToken,message.RenderMessage());
    }
    [Fact]public void Audit_context_keeps_route_and_trace_but_never_the_document_token()
    {
        var context=new DefaultHttpContext();context.Request.Path="/invoice-document";context.Request.QueryString=new("?token="+ProtectedToken);context.Request.Headers.UserAgent="Agent "+ProtectedToken;
        var audit=new HttpAuditExecutionContextAccessor(new HttpContextAccessor{HttpContext=context},new Tenant()).GetCurrent();
        Assert.Equal("/invoice-document",audit.Path);Assert.DoesNotContain(ProtectedToken,audit.UserAgent);Assert.Equal(context.TraceIdentifier,audit.TraceId);
        Assert.Equal(ProtectedToken,SensitiveRequestData.InvoiceToken(context.Request.Path,context.Request.QueryString.Value));
    }
    private sealed class Sink:ILogEventSink{public List<LogEvent> Events{get;}=[];public void Emit(LogEvent item)=>Events.Add(item);}
    private sealed class Tenant:ITenantContext{public int? StoreId=>1;public bool IsHostAdmin=>false;public string? Subdomain=>"p3-test";}
}
