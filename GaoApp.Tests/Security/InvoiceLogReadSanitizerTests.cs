using System.Text.Json;
using GaoApp.Application.Common.Security;

namespace GaoApp.Tests.Security;

public sealed class InvoiceLogReadSanitizerTests
{
    internal const string Canary = "B4_SECRET_CANARY_79f1b5";
    internal const string Trace = "915bfc35-5b15-4d9e-8c7a-fc9d68c28c8f";

    public static IEnumerable<object[]> Bodies()
    {
        foreach (var value in new[] {
            "{\"password\":\""+Canary+"\",\"nested\":{\"access_token\":\""+Canary+"\",\"statusCode\":503}}",
            "[{\"headers\":{\"Authorization\":\"Bearer "+Canary+"\",\"Cookie\":\""+Canary+"\"}}]",
            "{\"opaque\":\""+Canary+"\",\"redacted\":true,\"operation\":\"IssueInvoice\"}",
            "{\"password\":{\"traceId\":\""+Trace+"\",\"errorCode\":\"TIMEOUT\"}}",
            "{\"unknownProperty"+Canary+"\":\"x\",\"code\":\"password="+Canary+"\"}",
            "{\"requestId\":\"Bearer "+Canary+"\",\"operation\":\""+Canary+"\"}",
            "{\"statusCode\":200,\"statusCode\":500,\"password\":\""+Canary+"\"}",
            "{\"password\":\""+Canary, "password="+Canary+"&access_token="+Canary,
            "<soap><password>"+Canary+"</password></soap>", "Authorization: Basic "+Canary,
            "-----BEGIN PRIVATE KEY-----\n"+Canary+"\n-----END PRIVATE KEY-----",
            JsonSerializer.Serialize(Canary), "[\""+Canary+"\"]", new string('[',40)+"\""+Canary+"\""+new string(']',40),
            "{\"opaque\":\""+Canary+new string('x',70000)+"\"}", new string(' ',70000)
        }) yield return [value];
    }

    [Theory, MemberData(nameof(Bodies))]
    public void Unknown_or_sensitive_payload_content_never_returns_and_repeated_reads_are_idempotent(string raw)
    {
        var safe = InvoiceLogReadSanitizer.Body(raw)!;
        Assert.DoesNotContain(Canary, safe);
        Assert.Equal(safe, InvoiceLogReadSanitizer.Body(safe));
        Assert.True(safe.Length < 2048);
    }

    [Fact]
    public void Only_validated_status_trace_and_summary_metadata_survive_nested_JSON()
    {
        var raw = "{\"data\":{\"statusCode\":503,\"requestId\":\""+Trace+"\",\"errorCode\":\"HTTP_503\",\"operation\":\"IssueInvoice\",\"responseLength\":42,\"contentType\":\"application/json\",\"message\":\""+Canary+"\"}}";
        using var safe = JsonDocument.Parse(InvoiceLogReadSanitizer.Body(raw)!);
        Assert.Equal(503, safe.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(Trace, safe.RootElement.GetProperty("requestId").GetString());
        Assert.Equal("HTTP_503", safe.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal("IssueInvoice", safe.RootElement.GetProperty("operation").GetString());
        Assert.Equal(42, safe.RootElement.GetProperty("responseLength").GetInt32());
        Assert.True(safe.RootElement.GetProperty("redacted").GetBoolean());
        Assert.False(safe.RootElement.TryGetProperty("message", out _));
        Assert.False(safe.RootElement.TryGetProperty("data", out _));
    }

    [Theory]
    [InlineData("https://user:B4_SECRET_CANARY_79f1b5@provider.invalid/file/B4_SECRET_CANARY_79f1b5?token=x#secret", "https://provider.invalid/[REDACTED]")]
    [InlineData("https://provider.invalid?api_key=B4_SECRET_CANARY_79f1b5", "https://provider.invalid/[REDACTED]")]
    [InlineData("https://provider.invalid/B4%5fSECRET%5fCANARY%5f79f1b5", "https://provider.invalid/[REDACTED]")]
    [InlineData("http://localhost:5000/auth?password=B4_SECRET_CANARY_79f1b5", "http://localhost:5000/[REDACTED]")]
    [InlineData("/legacy/B4_SECRET_CANARY_79f1b5", "[REDACTED]")]
    [InlineData("not-a-url B4_SECRET_CANARY_79f1b5", "[REDACTED]")]
    [InlineData("javascript:B4_SECRET_CANARY_79f1b5", "[REDACTED]")]
    public void URLs_drop_all_userinfo_paths_queries_and_fragments(string raw,string expected)
    {
        Assert.Equal(expected, InvoiceLogReadSanitizer.Url(raw));
        Assert.Equal(expected, InvoiceLogReadSanitizer.Url(expected));
    }

    [Theory]
    [InlineData("HTTP 500 password=B4_SECRET_CANARY_79f1b5 timeout", "HTTP 500; [REDACTED]")]
    [InlineData("HTTP 500 timeout password=B4_SECRET_CANARY_79f1b5", "HTTP 500; timeout; [REDACTED]")]
    [InlineData("HttpRequestException: Bearer B4_SECRET_CANARY_79f1b5", "HttpRequestException; [REDACTED]")]
    [InlineData("arbitrary provider text B4_SECRET_CANARY_79f1b5", "[REDACTED]")]
    [InlineData("{\"password\":\"B4_SECRET_CANARY_79f1b5\"}", "[REDACTED]")]
    [InlineData("password=timeout HTTP 500", "[REDACTED]")]
    [InlineData("Authorization: Bearer HTTP 503 timeout", "[REDACTED]")]
    [InlineData("access_token=timeout HTTP 500", "[REDACTED]")]
    [InlineData("accessToken=HttpRequestException timeout", "[REDACTED]")]
    [InlineData("prefix {\"\\u0070assword\":\"HTTP 500 timeout\"}", "[REDACTED]")]
    [InlineData("prefix <value>HTTP 500 timeout</value>", "[REDACTED]")]
    [InlineData("HttpRequestException https://u:p@host/HTTP 500 timeout", "HttpRequestException; [REDACTED]")]
    [InlineData("HTTP 503 {\"opaque\":\"timeout\"}", "HTTP 503; [REDACTED]")]
    public void Errors_keep_coarse_diagnostics_without_free_text(string raw,string expected)
    {
        Assert.Equal(expected, InvoiceLogReadSanitizer.Error(raw));
        Assert.Equal(expected, InvoiceLogReadSanitizer.Error(expected));
    }

    [Theory]
    [InlineData("HTTP_503", "HTTP_503")]
    [InlineData("timeout", "TIMEOUT")]
    [InlineData("00000000", "00000000")]
    [InlineData("HTTP_999", "[REDACTED]")]
    [InlineData("password=B4_SECRET_CANARY_79f1b5", "[REDACTED]")]
    [InlineData("Bearer B4_SECRET_CANARY_79f1b5", "[REDACTED]")]
    [InlineData("B4_SECRET_CANARY_79f1b5", "[REDACTED]")]
    [InlineData("Viettel.IssueBusinessFailed", "Viettel.IssueBusinessFailed")]
    [InlineData("viettel.passwordrequired", "Viettel.PasswordRequired")]
    [InlineData("Viettel.B4_SECRET_CANARY_79f1b5", "[REDACTED]")]
    public void Error_codes_are_validated_instead_of_treating_arbitrary_strings_as_safe(string raw,string expected)
    {
        Assert.Equal(expected, InvoiceLogReadSanitizer.Code(raw));
        Assert.Equal(expected, InvoiceLogReadSanitizer.Code(expected));
    }

    [Fact]
    public void Null_empty_and_oversized_fields_are_bounded()
    {
        Assert.Null(InvoiceLogReadSanitizer.Body(null));
        Assert.Null(InvoiceLogReadSanitizer.Error(null));
        Assert.Null(InvoiceLogReadSanitizer.Url(null));
        Assert.Null(InvoiceLogReadSanitizer.Code(null));
        Assert.Equal("", InvoiceLogReadSanitizer.Body(" "));
        Assert.Equal("", InvoiceLogReadSanitizer.Url(""));
        Assert.Equal("[REDACTED]", InvoiceLogReadSanitizer.Error(new('x',17000)));
        Assert.Equal("[REDACTED]", InvoiceLogReadSanitizer.Url(new('x',5000)));
        Assert.Equal("[REDACTED]", InvoiceLogReadSanitizer.Code(new('x',100)));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("pwd")]
    [InlineData("pass")]
    [InlineData("Set-Cookie")]
    [InlineData("X-Api-Key")]
    [InlineData("Authorization")]
    public void Credential_containers_cannot_smuggle_values_as_diagnostic_fields(string name)
    {
        var raw = JsonSerializer.Serialize(new Dictionary<string,object> { [name] = new { traceId=Trace,statusCode=503 } });
        Assert.Equal("{\"redacted\":true}",InvoiceLogReadSanitizer.Body(raw));
    }
}
