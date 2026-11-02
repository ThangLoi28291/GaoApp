using System.Reflection;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Security;

public sealed class InvoiceErrorReadSanitizerTests
{
    public const string Canary = "B7_SYNTHETIC_SECRET_8c2d9741";
    public static IEnumerable<object[]> UntrustedText => new[]
    {
        Canary, "password=" + Canary, "timeout; token=" + Canary,
        "https://user:" + Canary + "@example.invalid/" + Canary + "?key=" + Canary,
        "{\"inner\":{\"access_token\":\"" + Canary + "\"}}", "<password>" + Canary + "</password>",
        "Bearer " + Canary, "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user:" + Canary)),
        "Không tìm thấy hóa đơn. " + Canary, "HTTP 500 " + Canary,
        string.Concat(Enumerable.Repeat("x",5000)) + Canary,
        "\\u0042\\u0037_SYNTHETIC_SECRET_8c2d9741", Uri.EscapeDataString("password=" + Canary)
    }.Select(x => new object[] { x });

    [Theory, MemberData(nameof(UntrustedText))]
    public void Untrusted_free_text_never_becomes_public_output(string raw)
    {
        Assert.Equal(InvoiceErrorReadSanitizer.GenericMessage, InvoiceErrorReadSanitizer.Message(raw));
        Assert.Equal(InvoiceErrorReadSanitizer.GenericMessage, InvoiceErrorReadSanitizer.UserMessage(new Error(Canary,raw)));
        Assert.Equal(InvoiceErrorReadSanitizer.GenericWorkerResult, InvoiceErrorReadSanitizer.WorkerResult(raw));
        Assert.DoesNotContain(Canary, InvoiceErrorReadSanitizer.Warning(raw));
        Assert.Equal(InvoiceErrorReadSanitizer.GenericMessage, InvoiceErrorReadSanitizer.WithFallback(null,raw));
    }

    [Theory]
    [InlineData("74271835", "INVOICE_ERROR")]
    [InlineData("Viettel.B7_SYNTHETIC_SECRET_8c2d9741", "INVOICE_ERROR")]
    [InlineData("HTTP_500B7_SYNTHETIC_SECRET_8c2d9741", "INVOICE_ERROR")]
    [InlineData("TIMEOUT", "TIMEOUT")]
    [InlineData("HTTP_500", "HTTP_500")]
    [InlineData("UNKNOWN_RESULT", "UNKNOWN_RESULT")]
    [InlineData("Invoice.NoDetails", "Invoice.NoDetails")]
    [InlineData("Invoice.UnitMissing", "Invoice.UnitMissing")]
    [InlineData("Invoice.InputInvoiceStockInsufficient", "Invoice.InputInvoiceStockInsufficient")]
    [InlineData("Viettel.IssueTimeout", "Viettel.IssueTimeout")]
    public void Only_public_named_codes_and_HTTP_status_codes_are_returned(string raw, string expected)
        => Assert.Equal(expected, InvoiceErrorReadSanitizer.Code(raw));

    [Fact]
    public void Empty_diagnostics_stay_empty_and_known_business_messages_remain_useful()
    {
        Assert.Null(InvoiceErrorReadSanitizer.Message(null));
        Assert.Null(InvoiceErrorReadSanitizer.Code(null));
        Assert.Equal("", InvoiceErrorReadSanitizer.Message(" \t"));
        Assert.Equal("", InvoiceErrorReadSanitizer.Code(" \t"));
        const string known = "Mã số thuế phát hành không được trống.";
        Assert.Equal(known, InvoiceErrorReadSanitizer.Message(known));
        Assert.Equal(InvoiceErrorReadSanitizer.GenericMessage, InvoiceErrorReadSanitizer.Message(known+Canary));
        Assert.Contains("không cần đổi mật khẩu Viettel", InvoiceErrorReadSanitizer.Message(Canary,InvoiceCredentialErrors.KeyUnavailableCode));
        Assert.Equal("Đã mở lại 42 hóa đơn sau khi xác thực lại credential Viettel.",
            InvoiceErrorReadSanitizer.WorkerResult("Đã mở lại 42 hóa đơn sau khi xác thực lại credential Viettel."));
    }

    [Theory]
    [InlineData("TIMEOUT")]
    [InlineData("HTTP_500")]
    [InlineData("UNKNOWN_RESULT")]
    [InlineData("Invoice.NoDetails")]
    [InlineData("malicious")]
    public void Repeated_projection_is_idempotent(string code)
    {
        var safeCode = InvoiceErrorReadSanitizer.Code(code);
        var safe = InvoiceErrorReadSanitizer.Message(Canary,code);
        Assert.Equal(safeCode,InvoiceErrorReadSanitizer.Code(safeCode));
        Assert.Equal(safe,InvoiceErrorReadSanitizer.Message(safe,safeCode));
    }

    public static IEnumerable<object[]> StateMatrix =>
        from state in new[] { InvoiceProviderStatus.LocalDraft, InvoiceProviderStatus.ReadyToIssue,
            InvoiceProviderStatus.IssueFailed, InvoiceProviderStatus.Issuing, InvoiceProviderStatus.IssuedWaitingNumber }
        from code in new[] { "TIMEOUT", "HTTP_500", "HTTP_5"+Canary, "UNKNOWN_RESULT", Canary }
        from message in new[] { Canary, "timeout; password="+Canary, "HTTP 500 token="+Canary }
        select new object[] { state,code,message };

    [Theory, MemberData(nameof(StateMatrix))]
    public void Detail_projection_keeps_original_UI_decisions_without_retaining_raw_errors(
        InvoiceProviderStatus status,string code,string message)
    {
        var entity = new InvoiceHead { Id=1,StoreId=1,InvoiceNumber="B7-UNIT",InvoiceDate=DateTime.UtcNow,
            ProviderStatus=status,TransactionUuid=Guid.NewGuid().ToString(),GrandTotal=10,
            LastErrorCode=code,LastErrorMessage=message,LastEmailErrorMessage=Canary };
        entity.Details.Add(new InvoiceDetail { Id=1,InvoiceHeadId=1,ItemName="B7 item",UnitName="Unit",Quantity=1,UnitPrice=10,TotalAmount=10 });
        var raw = new InvoiceHeadDto { Id=1,ProviderStatus=status,TransactionUuid=entity.TransactionUuid,
            GrandTotal=10,LastErrorCode=code,LastErrorMessage=message,
            Details = [new InvoiceDetailDto { Id=1,ItemName="B7 item",UnitName="Unit",Quantity=1,UnitPrice=10,TotalAmount=10 }] };
        var assembly = typeof(InvoiceService).Assembly;
        var mapper = assembly.GetType("GaoApp.Application.Services.Invoices.InvoiceDtoMapper")!;
        var projected = (InvoiceHeadDto)mapper.GetMethod("ToDetailDto")!.Invoke(null,[entity])!;
        var builder = assembly.GetType("GaoApp.Application.Services.Invoices.ViettelInvoiceUiStateBuilder")!;
        var expected = builder.GetMethod("Build")!.Invoke(null,[raw])!;
        var actual = builder.GetMethod("Build")!.Invoke(null,[projected])!;
        foreach (var property in new[] { "CanIssue","RequiresUuidSyncBeforeIssue","CanSyncByUuid","IssueBlockReason" })
            Assert.Equal(expected.GetType().GetProperty(property)!.GetValue(expected),actual.GetType().GetProperty(property)!.GetValue(actual));
        Assert.DoesNotContain(Canary,JsonSerializer.Serialize(projected));
        Assert.Equal(message,entity.LastErrorMessage);Assert.Equal(code,entity.LastErrorCode);
        Assert.Equal(Canary,entity.LastEmailErrorMessage);
    }
}
