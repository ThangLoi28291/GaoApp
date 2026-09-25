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

    private sealed class ProbeHandler(string metadata, string invoices = "{\"errorCode\":null,\"totalRows\":0,\"invoices\":[]}") : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? AuthScheme)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.Scheme));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Requests.Count == 1 ? metadata : invoices, Encoding.UTF8, "application/json")
            });
        }
    }
}
