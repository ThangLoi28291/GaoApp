using System.Net;
using System.Text;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Services.Invoices;

namespace GaoApp.Tests.Invoices;

public sealed class ViettelCustomFieldsConnectionTests
{
    [Theory]
    [InlineData("{\"errorCode\":null,\"description\":null,\"customFields\":[]}")]
    [InlineData("{\"customFields\":[{\"keyTag\":\"reference\",\"valueType\":\"text\",\"keyLabel\":\"Reference\"}]}")]
    [InlineData("{\"data\":{\"errorCode\":null,\"customFields\":[]}}")]
    [InlineData("{\"result\":{\"errorCode\":null,\"customFields\":[]}}")]
    [InlineData("{\"ERRORCODE\":null,\"CUSTOMFIELDS\":[]}")]
    // Same shape as the live demo response; metadata values are synthetic.
    [InlineData("{\"errorCode\":null,\"description\":null,\"customFields\":[{\"id\":null,\"invoiceTemplatePrototypeId\":1,\"keyTag\":\"reference\",\"valueType\":\"text\",\"keyLabel\":\"Reference\",\"isRequired\":false,\"isSeller\":false}]}")]
    public async Task Recognized_metadata_should_continue_to_read_only_invoice_check(string metadata)
    {
        var handler = new ProbeHandler(metadata);
        var result = await TestAsync(handler);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(0, result.Value.TotalRows);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Contains("/getCustomFields?taxCode=0100000001&templateCode=2%2FLKD3", handler.Requests[0].Url);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.EndsWith("/getInvoices/0100000001", handler.Requests[1].Url);
        Assert.All(handler.Requests, request => Assert.Equal("Basic", request.AuthScheme));
        Assert.DoesNotContain(handler.Requests, request => request.Url.Contains("createInvoice"));
    }

    [Theory]
    [InlineData("{\"errorCode\":\"INVALID_TEMPLATE\",\"customFields\":[]}")]
    [InlineData("{\"success\":false,\"customFields\":[]}")]
    [InlineData("{\"customFields\":[],\"data\":{\"errorCode\":\"500\"}}")]
    [InlineData("{\"errorCode\":\"200\",\"customFields\":\"unexpected\"}")]
    [InlineData("{\"errorCode\":\"200\",\"customFields\":{}}")]
    [InlineData("{\"customFields\":[42]}")]
    [InlineData("{\"customFields\":null}")]
    [InlineData("{\"errorCode\":null,\"description\":null,\"customFields\":null}")]
    [InlineData("{\"errorCode\":\"\",\"customFields\":null}")]
    [InlineData("{\"errorCode\":null,\"description\":\"Invalid template\",\"customFields\":null}")]
    [InlineData("{\"customFields\":[],\"CUSTOMFIELDS\":[]}")]
    [InlineData("{\"customFields\":[],\"result\":[]}")]
    [InlineData("{\"debug\":{\"customFields\":[]}}")]
    [InlineData("{\"errorCode\":null,\"description\":null}")]
    public async Task Invalid_or_ambiguous_metadata_should_not_be_reported_as_connected(string metadata)
    {
        var handler = new ProbeHandler(metadata);
        var result = await TestAsync(handler);

        Assert.True(result.IsFailure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Valid_metadata_should_not_hide_invoice_list_failure()
    {
        var handler = new ProbeHandler("{\"errorCode\":null,\"customFields\":[]}", "{\"errorCode\":\"ACCESS_DENIED\"}");
        var result = await TestAsync(handler);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.BasicAuthBusinessFailed", result.Error.Code);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static async Task<Result<TestInvoiceProviderLoginResultDto>> TestAsync(ProbeHandler handler)
    {
        using var http = new HttpClient(handler);
        return await new ViettelInvoiceAuthClient(http).TestConnectionAsync(
            "https://provider.example", "test-user", "test-password", InvoiceProviderAuthMode.BasicAuth,
            "0100000001", "2", "2/LKD3", "C26MTM");
    }

    [Theory]
    [InlineData("{\"message\":\"NOT_FOUND_DATA\"}")]
    [InlineData("{\"code\":400,\"message\":\"NOT_FOUND_DATA\"}")]
    [InlineData("{\"errorCode\":\"NOT_FOUND_DATA\",\"description\":\"Không tìm thấy\"}")]
    public async Task Missing_optional_metadata_should_verify_invoice_access_and_report_warning(string metadata)
    {
        var handler = new ProbeHandler(metadata) { MetadataStatus = HttpStatusCode.BadRequest };
        var result = await TestAsync(handler);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(0, result.Value.TotalRows);
        Assert.Contains("getInvoices", result.Value.Message);
        Assert.DoesNotContain("Đã test getCustomFields", result.Value.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.WarningMessage));
        Assert.DoesNotContain(handler.Requests, x => x.Url.Contains("createInvoice"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{\"message\":\"NOT_FOUND_DATA\"}")]
    [InlineData(HttpStatusCode.Forbidden, "{\"message\":\"NOT_FOUND_DATA\"}")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"message\":\"NOT_FOUND_DATA\"}")]
    [InlineData(HttpStatusCode.BadRequest, "{\"message\":\"ACCESS_DENIED\"}")]
    [InlineData(HttpStatusCode.BadRequest, "{\"message\":\"NOT_FOUND_DATA\",\"code\":\"ACCESS_DENIED\"}")]
    [InlineData(HttpStatusCode.BadRequest, "{\"message\":\"NOT_FOUND_DATA\",\"data\":{\"errorCode\":\"ACCESS_DENIED\"}}")]
    [InlineData(HttpStatusCode.BadRequest, "{\"message\":\"NOT_FOUND_DATA\",\"MESSAGE\":\"ACCESS_DENIED\"}")]
    [InlineData(HttpStatusCode.BadRequest, "<html>NOT_FOUND_DATA</html>")]
    public async Task Other_errors_must_not_be_treated_as_absent_metadata(HttpStatusCode status, string metadata)
    {
        var handler = new ProbeHandler(metadata) { MetadataStatus = status };
        var result = await TestAsync(handler);
        Assert.True(result.IsFailure);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{\"message\":\"Unauthorized\"}")]
    [InlineData(HttpStatusCode.BadRequest, "{\"message\":\"NOT_FOUND_DATA\"}")]
    [InlineData(HttpStatusCode.OK, "{\"errorCode\":\"ACCESS_DENIED\"}")]
    [InlineData(HttpStatusCode.OK, "{}")]
    public async Task Missing_metadata_does_not_hide_failed_or_ambiguous_invoice_check(HttpStatusCode status, string invoices)
    {
        var handler = new ProbeHandler("{\"message\":\"NOT_FOUND_DATA\"}", invoices)
        { MetadataStatus = HttpStatusCode.BadRequest, InvoiceStatus = status };
        var result = await TestAsync(handler);
        Assert.True(result.IsFailure);
        Assert.Equal(2, handler.Requests.Count);
    }

    private sealed class ProbeHandler(string metadata, string invoices = "{\"errorCode\":null,\"totalRows\":0,\"invoices\":[]}") : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? AuthScheme)> Requests { get; } = [];
        public HttpStatusCode MetadataStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode InvoiceStatus { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.Scheme));
            return Task.FromResult(new HttpResponseMessage(Requests.Count == 1 ? MetadataStatus : InvoiceStatus)
            {
                Content = new StringContent(Requests.Count == 1 ? metadata : invoices, Encoding.UTF8, "application/json")
            });
        }
    }
}
