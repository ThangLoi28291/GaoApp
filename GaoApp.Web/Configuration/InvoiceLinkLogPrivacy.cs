using GaoApp.Application.Common.Security;
using Serilog.Core;
using Serilog.Events;

namespace GaoApp.Web.Configuration;

/// <summary>Sanitizes structured framework/app events, including route values, before any sink sees them.</summary>
public sealed class InvoiceLinkLogPrivacy(IHttpContextAccessor? accessor = null) : ILogEventEnricher, ILogEventFilter
{
    private string? Token => SensitiveRequestData.InvoiceToken(accessor?.HttpContext?.Request.Path.Value, accessor?.HttpContext?.Request.QueryString.Value);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var token = Token;
        foreach (var property in logEvent.Properties.ToArray())
            logEvent.AddOrUpdateProperty(new(property.Key, Sanitize(property.Value, property.Key, token)));
    }

    public bool IsEnabled(LogEvent logEvent)
    {
        // Templates and exception objects are immutable. Suppress an unsafe event;
        // GlobalExceptionMiddleware logs sanitized type, stack and trace diagnostics separately.
        var token = Token;
        var literal = logEvent.MessageTemplate.Text;
        var exception = logEvent.Exception?.ToString();
        return literal == SensitiveRequestData.Redact(literal, token) &&
            (exception == null || exception == SensitiveRequestData.Redact(exception, token));
    }

    private static LogEventPropertyValue Sanitize(LogEventPropertyValue value, string? key, string? token)
    {
        if (string.Equals(key, "token", StringComparison.OrdinalIgnoreCase)) return new ScalarValue("[redacted]");
        return value switch
        {
            ScalarValue { Value: string text } => new ScalarValue(SensitiveRequestData.Redact(text, token)),
            ScalarValue { Value: PathString path } => new ScalarValue(SensitiveRequestData.Redact(path.Value, token)),
            ScalarValue { Value: Uri uri } => new ScalarValue(SensitiveRequestData.Redact(uri.ToString(), token)),
            SequenceValue sequence => new SequenceValue(sequence.Elements.Select(x => Sanitize(x, null, token))),
            StructureValue structure => new StructureValue(structure.Properties.Select(x =>
                new LogEventProperty(x.Name, Sanitize(x.Value, x.Name, token))), structure.TypeTag),
            DictionaryValue dictionary => new DictionaryValue(dictionary.Elements.Select(x =>
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    (ScalarValue)Sanitize(x.Key, null, token), Sanitize(x.Value, x.Key.Value?.ToString(), token)))),
            _ => value
        };
    }
}
