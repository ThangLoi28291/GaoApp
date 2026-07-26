using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure;
using GaoApp.Infrastructure.Services.Invoices;
using GaoApp.Web.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Configuration;

public sealed class ExternalHttpResiliencyTests
{
    private const string BaseUrl = "https://provider.example";
    private const string SyntheticPassword = "synthetic-password-value";
    private const string SuccessfulTaxLookupJson =
        """
        {
          "code": "00",
          "data": {
            "name": "Synthetic Business",
            "address": "Synthetic Address"
          }
        }
        """;

    [Fact]
    public void External_http_timeouts_should_be_finite_and_bounded()
    {
        var options = new ExternalHttpResilienceOptions();

        Assert.True(options.HasValidTimeouts());
        Assert.Equal(0, ExternalHttpResilienceOptions.AutomaticRetryCount);
        Assert.Equal(TimeSpan.Zero, ExternalHttpResilienceOptions.AutomaticRetryDelay);
        Assert.Equal(1, ExternalHttpResilienceOptions.SafeGetTransientRetryCount);
        Assert.InRange(
            ExternalHttpResilienceOptions.SafeGetTransientRetryDelay,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));
        Assert.InRange(
            options.AuthenticationTimeout,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(ExternalHttpResilienceOptions.MaximumTimeoutSeconds));
        Assert.InRange(
            options.SafeReadTimeout,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(ExternalHttpResilienceOptions.MaximumTimeoutSeconds));
        Assert.InRange(
            options.NonIdempotentWriteTimeout,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(ExternalHttpResilienceOptions.MaximumTimeoutSeconds));
        Assert.InRange(
            options.FileDownloadTimeout,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(ExternalHttpResilienceOptions.MaximumTimeoutSeconds));

        options.SafeReadTimeoutSeconds =
            ExternalHttpResilienceOptions.MaximumTimeoutSeconds + 1;

        Assert.False(options.HasValidTimeouts());
        Assert.Throws<InvalidOperationException>(() => options.SafeReadTimeout);
    }

    [Theory]
    [InlineData("AuthenticationTimeoutSeconds", 0)]
    [InlineData("AuthenticationTimeoutSeconds", 61)]
    [InlineData("FileDownloadTimeoutSeconds", 0)]
    [InlineData("FileDownloadTimeoutSeconds", 61)]
    public void Registered_external_http_validator_should_reject_invalid_boundaries(
        string configurationKey,
        int value)
    {
        using var provider = BuildExternalOptionsProvider(
            new Dictionary<string, string?>
            {
                [$"{ExternalHttpResilienceOptions.SectionName}:{configurationKey}"] =
                    value.ToString()
            });

        var options = provider.GetRequiredService<
            IOptions<ExternalHttpResilienceOptions>>();

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = options.Value);

        Assert.Contains(
            "External HTTP timeouts must be between 1 and 60 seconds.",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public void Registered_external_http_validator_should_accept_valid_boundaries(
        int value)
    {
        using var provider = BuildExternalOptionsProvider(
            new Dictionary<string, string?>
            {
                ["ExternalHttpResilience:AuthenticationTimeoutSeconds"] =
                    value.ToString(),
                ["ExternalHttpResilience:SafeReadTimeoutSeconds"] =
                    value.ToString(),
                ["ExternalHttpResilience:NonIdempotentWriteTimeoutSeconds"] =
                    value.ToString(),
                ["ExternalHttpResilience:FileDownloadTimeoutSeconds"] =
                    value.ToString()
            });

        var options = provider.GetRequiredService<
            IOptions<ExternalHttpResilienceOptions>>().Value;

        Assert.True(options.HasValidTimeouts());
        Assert.Equal(value, options.AuthenticationTimeoutSeconds);
        Assert.Equal(value, options.FileDownloadTimeoutSeconds);
    }

    [Fact]
    public void Startup_validation_should_force_registered_external_http_options()
    {
        using var provider = BuildExternalOptionsProvider(
            new Dictionary<string, string?>
            {
                ["ExternalHttpResilience:AuthenticationTimeoutSeconds"] = "0"
            });
        var externalOptions = provider.GetRequiredService<
            IOptions<ExternalHttpResilienceOptions>>();
        var service = new StartupValidationService(
            connectionStringOptions: null!,
            appUrlOptions: null!,
            tenantOptions: null!,
            storageOptions: null!,
            seedOptions: null!,
            proxyOptions: null!,
            externalHttpResilienceOptions: externalOptions,
            dataProtectionKeysPathResolver: null!,
            dataProtectionKeysDirectoryValidator: null!,
            dataProtectionKeysPathState: null!,
            environment: null!,
            logger: NullLogger<StartupValidationService>.Instance);

        var exception = Assert.Throws<OptionsValidationException>(
            () =>
            {
                _ = service.ValidateAsync();
            });

        Assert.Contains(
            "External HTTP timeouts must be between 1 and 60 seconds.",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_200_empty_object_should_fail()
    {
        var handler = JsonHandler("{}");
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueAmbiguousResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_200_malformed_json_should_fail()
    {
        var handler = JsonHandler("{not-json");
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_success_code_without_confirmation_should_fail()
    {
        var handler = JsonHandler("""{"errorCode":"200"}""");
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueAmbiguousResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_nested_echo_confirmation_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var handler = JsonHandler(
            $$"""
            {
              "status": "ERROR",
              "requestEcho": {
                "reservationCode": "ECHO-ONLY",
                "secret": "{{rawSentinel}}"
              }
            }
            """);
        var logs = new RecordingLogRepository();
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, logs));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
        Assert.DoesNotContain(
            rawSentinel,
            result.Error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            rawSentinel,
            SerializeLogs(logs),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_nested_invoice_number_in_debug_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var handler = JsonHandler(
            $$"""
            {
              "debug": {
                "invoiceNo": "SYNTHETIC-INVOICE",
                "secret": "{{rawSentinel}}"
              }
            }
            """);
        var logs = new RecordingLogRepository();
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, logs));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueAmbiguousResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
        Assert.DoesNotContain(
            rawSentinel,
            result.Error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            rawSentinel,
            SerializeLogs(logs),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_single_result_confirmation_without_conflict_should_succeed()
    {
        var handler = JsonHandler(
            """
            {
              "errorCode": null,
              "result": {
                "invoiceNo": "SYNTHETIC-INVOICE",
                "transactionID": "SYNTHETIC-TRANSACTION"
              }
            }
            """);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsSuccess);
        Assert.Equal("SYNTHETIC-INVOICE", result.Value.InvoiceNo);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_log_failure_should_keep_provider_result_and_emit_safe_telemetry()
    {
        const string secret = "synthetic-log-secret";
        var handler = JsonHandler(
            """
            {
              "errorCode": null,
              "result": {
                "invoiceNo": "SYNTHETIC-INVOICE",
                "transactionID": "SYNTHETIC-TRANSACTION"
              }
            }
            """);
        var logs = new RecordingLogRepository
        {
            AddException = new IOException(secret)
        };
        var logger = new RecordingLogger<ViettelInvoiceIssueClient>();
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, logs, logger));

        Assert.True(result.IsSuccess);
        var message = Assert.Single(logger.Messages);
        Assert.Contains(nameof(IOException), message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_result_success_data_failure_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var handler = JsonHandler(
            $$"""
            {
              "result": { "invoiceNo": "INV-RESULT" },
              "data": {
                "status": "ERROR",
                "errorCode": "500",
                "message": "{{rawSentinel}}"
              }
            }
            """);
        var logs = new RecordingLogRepository();
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, logs));

        AssertSanitizedFailure(result, rawSentinel);
        Assert.DoesNotContain(rawSentinel, SerializeLogs(logs), StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_data_success_result_failure_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "status": "FAILED", "errorCode": "500" },
              "data": { "invoiceNo": "INV-DATA" }
            }
            """);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_error_object_with_confirmation_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var handler = JsonHandler(
            $$"""
            {
              "error": { "code": "500", "message": "{{rawSentinel}}" },
              "result": { "invoiceNo": "INV-RESULT" }
            }
            """);
        var logs = new RecordingLogRepository();
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, logs));

        AssertSanitizedFailure(result, rawSentinel);
        Assert.DoesNotContain(rawSentinel, SerializeLogs(logs), StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_errors_array_with_confirmation_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "errors": [{ "code": "500" }],
              "result": { "invoiceNo": "INV-RESULT" }
            }
            """);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_empty_error_object_with_confirmation_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "error": {},
              "result": { "invoiceNo": "INV-RESULT" }
            }
            """);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_empty_errors_array_with_confirmation_should_succeed()
    {
        var handler = JsonHandler(
            """
            {
              "errors": [],
              "result": { "invoiceNo": "INV-RESULT" }
            }
            """);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsSuccess);
        Assert.Equal("INV-RESULT", result.Value.InvoiceNo);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_result_and_data_both_have_confirmation_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "invoiceNo": "INV-RESULT" },
              "data": { "invoiceNo": "INV-DATA" }
            }
            """);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueConflictingResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_single_data_confirmation_without_conflict_should_succeed()
    {
        var handler = JsonHandler(
            """{"data":{"invoiceNo":"INV-DATA"}}""");
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsSuccess);
        Assert.Equal("INV-DATA", result.Value.InvoiceNo);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_explicit_business_error_should_fail()
    {
        var handler = JsonHandler(
            """{"errorCode":"BUSINESS_ERROR","invoiceNo":"must-not-confirm"}""");
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueBusinessFailed", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Email_200_empty_object_should_fail()
    {
        var handler = JsonHandler("{}");
        var result = await SendSyntheticEmailAsync(
            CreateEmailClient(handler),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.SendEmailAmbiguousResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Email_200_malformed_json_should_fail()
    {
        var handler = JsonHandler("{not-json");
        var result = await SendSyntheticEmailAsync(
            CreateEmailClient(handler),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.SendEmailInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Email_nested_ok_message_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var handler = JsonHandler(
            $$"""
            {
              "status": "ERROR",
              "details": {
                "message": "OK",
                "secret": "{{rawSentinel}}"
              }
            }
            """);
        var logs = new RecordingLogRepository();
        var result = await SendSyntheticEmailAsync(
            CreateEmailClient(handler, logs),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
        Assert.DoesNotContain(
            rawSentinel,
            result.Error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            rawSentinel,
            SerializeLogs(logs),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_confirmed_response_should_succeed()
    {
        var handler = JsonHandler("""{"code":"200","message":"OK"}""");
        var result = await SendSyntheticEmailAsync(
            CreateEmailClient(handler),
            default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsSuccess);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Email_single_recognized_success_should_succeed()
    {
        var handler = JsonHandler(
            """{"result":{"code":"200","message":"OK"}}""");
        var result = await SendSyntheticEmailAsync(
            CreateEmailClient(handler),
            default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsSuccess);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Email_result_success_data_failure_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "code": "200", "message": "OK" },
              "data": { "status": "ERROR", "errorCode": "500" }
            }
            """);
        var result = await SendSyntheticEmailAsync(
            CreateEmailClient(handler),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Email_data_success_result_failure_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "status": "FAILED", "errorCode": "500" },
              "data": { "code": "200", "message": "OK" }
            }
            """);
        var result = await SendSyntheticEmailAsync(
            CreateEmailClient(handler),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Email_error_object_with_ok_message_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var handler = JsonHandler(
            $$"""
            {
              "error": { "message": "{{rawSentinel}}" },
              "result": { "message": "OK" }
            }
            """);
        var logs = new RecordingLogRepository();
        var result = await SendSyntheticEmailAsync(
            CreateEmailClient(handler, logs),
            default);

        AssertSanitizedFailure(result, rawSentinel);
        Assert.DoesNotContain(rawSentinel, SerializeLogs(logs), StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Email_result_and_data_both_claim_success_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "message": "OK" },
              "data": { "code": "200" }
            }
            """);
        var result = await SendSyntheticEmailAsync(
            CreateEmailClient(handler),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.SendEmailConflictingResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Email_explicit_business_error_should_fail()
    {
        var handler = JsonHandler("""{"code":"BUSINESS_ERROR"}""");
        var result = await SendSyntheticEmailAsync(
            CreateEmailClient(handler),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.SendEmailBusinessFailed", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Lookup_200_empty_object_should_fail_not_not_found()
    {
        var handler = JsonHandler("{}");
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.LookupAmbiguousResponse", result.Error.Code);
        Assert.NotEqual("NOT_FOUND_DATA", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Lookup_malformed_json_should_fail()
    {
        var handler = JsonHandler("{not-json");
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.LookupInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Lookup_nested_invoice_number_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var handler = JsonHandler(
            $$"""
            {
              "status": "ERROR",
              "debug": {
                "invoiceNo": "ECHO-INVOICE",
                "secret": "{{rawSentinel}}"
              }
            }
            """);
        var logs = new RecordingLogRepository();
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler, logs));

        Assert.True(result.IsFailure);
        Assert.NotEqual("NOT_FOUND_DATA", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
        Assert.DoesNotContain(
            rawSentinel,
            result.Error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            rawSentinel,
            SerializeLogs(logs),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lookup_single_explicit_not_found_should_be_domain_success()
    {
        var handler = JsonHandler(
            """{"errorCode":"NOT_FOUND_DATA","description":"Không tìm thấy"}""");
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsFound);
        Assert.Equal("NOT_FOUND_DATA", result.Value.ErrorCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Lookup_single_confirmed_found_should_succeed()
    {
        var handler = JsonHandler(
            """
            {
              "errorCode": null,
              "result": [{
                "invoiceNo": "SYNTHETIC-INVOICE",
                "transactionID": "SYNTHETIC-TRANSACTION"
              }]
            }
            """);
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsFound);
        Assert.Equal("SYNTHETIC-INVOICE", result.Value.InvoiceNo);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Lookup_result_found_data_failure_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "invoiceNo": "INV-RESULT" },
              "data": { "status": "ERROR", "errorCode": "500" }
            }
            """);
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Lookup_data_found_result_failure_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "status": "FAILED", "errorCode": "500" },
              "data": { "invoiceNo": "INV-DATA" }
            }
            """);
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Lookup_found_and_not_found_conflict_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "invoiceNo": "INV-RESULT" },
              "data": { "errorCode": "NOT_FOUND_DATA" }
            }
            """);
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.LookupConflictingResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Lookup_error_object_with_invoice_number_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var handler = JsonHandler(
            $$"""
            {
              "error": { "message": "{{rawSentinel}}" },
              "result": { "invoiceNo": "INV-RESULT" }
            }
            """);
        var logs = new RecordingLogRepository();
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler, logs));

        AssertSanitizedFailure(result, rawSentinel);
        Assert.DoesNotContain(rawSentinel, SerializeLogs(logs), StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Lookup_result_and_data_both_found_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "invoiceNo": "INV-RESULT" },
              "data": { "invoiceNo": "INV-DATA" }
            }
            """);
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.LookupConflictingResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Lookup_explicit_business_error_should_fail()
    {
        var handler = JsonHandler(
            """{"errorCode":"BUSINESS_ERROR","description":"provider-secret"}""");
        var logs = new RecordingLogRepository();
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler, logs));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.LookupBusinessFailed", result.Error.Code);
        Assert.DoesNotContain(
            "provider-secret",
            SerializeLogs(logs),
            StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_200_empty_object_should_fail()
    {
        var handler = JsonHandler("{}");
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.GetInvoicesAmbiguousResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_malformed_json_should_fail()
    {
        var handler = JsonHandler("{not-json");
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.GetInvoicesInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_nested_invoices_array_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var handler = JsonHandler(
            $$"""
            {
              "status": "ERROR",
              "details": {
                "invoices": [],
                "secret": "{{rawSentinel}}"
              }
            }
            """);
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
        Assert.DoesNotContain(
            rawSentinel,
            result.Error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_recognized_empty_array_should_succeed()
    {
        var handler = JsonHandler(
            """{"errorCode":null,"totalRows":0,"invoices":[]}""");
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.RemoteInvoices);
        Assert.Equal(0, result.Value.TotalRemoteRows);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_single_recognized_empty_list_should_succeed()
    {
        var handler = JsonHandler(
            """
            {
              "result": {
                "errorCode": null,
                "totalRows": 0,
                "invoices": []
              }
            }
            """);
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.RemoteInvoices);
        Assert.Equal(0, result.Value.TotalRemoteRows);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_single_recognized_nonempty_list_should_succeed()
    {
        var handler = JsonHandler(
            """
            {
              "errorCode": null,
              "totalRows": 1,
              "invoices": [{
                "invoiceNo": "SYNTHETIC-INVOICE",
                "transactionUuid": "SYNTHETIC-TRANSACTION"
              }]
            }
            """);
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.RemoteInvoices);
        Assert.Equal("SYNTHETIC-INVOICE", result.Value.RemoteInvoices[0].InvoiceNo);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_result_list_data_failure_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "totalRows": 0, "invoices": [] },
              "data": { "status": "ERROR", "errorCode": "500" }
            }
            """);
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_data_list_result_failure_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "status": "FAILED", "errorCode": "500" },
              "data": { "totalRows": 0, "invoices": [] }
            }
            """);
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_root_list_result_failure_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "totalRows": 0,
              "invoices": [],
              "result": { "status": "ERROR", "errorCode": "500" }
            }
            """);
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_error_object_with_list_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "error": { "code": "500" },
              "result": { "totalRows": 0, "invoices": [] }
            }
            """);
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_result_and_data_both_contain_list_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "totalRows": 0, "invoices": [] },
              "data": { "totalRows": 0, "invoices": [] }
            }
            """);
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.GetInvoicesConflictingResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_non_empty_item_without_stable_identity_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "errorCode": null,
              "totalRows": 1,
              "invoices": [{
                "buyerName": "Synthetic Buyer"
              }]
            }
            """);
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.GetInvoicesInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_explicit_business_error_should_fail()
    {
        var handler = JsonHandler(
            """{"errorCode":"BUSINESS_ERROR","invoices":[]}""");
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.GetInvoicesBusinessFailed", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Basic_auth_custom_fields_malformed_should_fail()
    {
        var handler = JsonHandler("{not-json");
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.BasicAuthInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Basic_auth_get_invoices_malformed_should_fail()
    {
        var responseNumber = 0;
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    Interlocked.Increment(ref responseNumber) == 1
                        ? """{"errorCode":null,"result":[]}"""
                        : "{not-json")));
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.BasicAuthInvalidResponse", result.Error.Code);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Basic_auth_empty_object_should_fail()
    {
        var handler = JsonHandler("{}");
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.BasicAuthAmbiguousResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Basic_auth_nested_result_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var handler = JsonHandler(
            $$"""
            {
              "debug": {
                "result": [],
                "secret": "{{rawSentinel}}"
              }
            }
            """);
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.BasicAuthAmbiguousResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
        Assert.DoesNotContain(
            rawSentinel,
            result.Error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Basic_auth_get_invoices_empty_object_should_fail()
    {
        var responseNumber = 0;
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    Interlocked.Increment(ref responseNumber) == 1
                        ? """{"errorCode":null,"result":[]}"""
                        : "{}")));
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.BasicAuthAmbiguousResponse", result.Error.Code);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Basic_auth_nested_invoices_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var responseNumber = 0;
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    Interlocked.Increment(ref responseNumber) == 1
                        ? """{"errorCode":null,"result":[]}"""
                        : $$"""
                          {
                            "debug": {
                              "invoices": [],
                              "secret": "{{rawSentinel}}"
                            }
                          }
                          """)));
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.BasicAuthAmbiguousResponse", result.Error.Code);
        Assert.Equal(2, handler.CallCount);
        Assert.DoesNotContain(
            rawSentinel,
            result.Error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Basic_auth_single_recognized_responses_should_succeed()
    {
        var responseNumber = 0;
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    Interlocked.Increment(ref responseNumber) == 1
                        ? """{"errorCode":null,"result":[]}"""
                        : """{"errorCode":null,"totalRows":0,"invoices":[]}""")));
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsSuccess);
        Assert.Equal(0, result.Value.TotalRows);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Basic_auth_control_success_with_single_payload_should_succeed()
    {
        var responseNumber = 0;
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    Interlocked.Increment(ref responseNumber) == 1
                        ? """{"errorCode":"200","result":[]}"""
                        : """
                          {
                            "errorCode": "200",
                            "result": { "totalRows": 0, "invoices": [] }
                          }
                          """)));
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Basic_auth_custom_fields_result_success_data_failure_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": [],
              "data": { "status": "ERROR", "errorCode": "500" }
            }
            """);
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Basic_auth_list_result_success_data_failure_should_fail()
    {
        var responseNumber = 0;
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    Interlocked.Increment(ref responseNumber) == 1
                        ? """{"result":[]}"""
                        : """
                          {
                            "result": { "totalRows": 0, "invoices": [] },
                            "data": { "status": "ERROR", "errorCode": "500" }
                          }
                          """)));
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsFailure);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Basic_auth_error_object_with_result_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "error": { "code": "500" },
              "result": []
            }
            """);
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Basic_auth_result_and_data_both_have_payload_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": [],
              "data": { "result": [] }
            }
            """);
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.BasicAuthConflictingResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Token_login_empty_object_should_fail()
    {
        var handler = JsonHandler("{}");
        var client = new ViettelInvoiceAuthClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(
            BaseUrl,
            "synthetic-user",
            SyntheticPassword,
            InvoiceProviderAuthMode.TokenLogin,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.TokenMissing", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Token_login_nested_debug_token_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var handler = JsonHandler(
            $$"""
            {
              "debug": {
                "access_token": "{{rawSentinel}}"
              }
            }
            """);
        var client = new ViettelInvoiceAuthClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(
            BaseUrl,
            "synthetic-user",
            SyntheticPassword,
            InvoiceProviderAuthMode.TokenLogin,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.TokenMissing", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
        Assert.DoesNotContain(
            rawSentinel,
            result.Error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_result_token_data_failure_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "access_token": "synthetic-token" },
              "data": { "status": "ERROR", "errorCode": "500" }
            }
            """);
        var result = await TestSyntheticTokenAsync(handler);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Token_data_token_result_failure_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "status": "FAILED", "errorCode": "500" },
              "data": { "access_token": "synthetic-token" }
            }
            """);
        var result = await TestSyntheticTokenAsync(handler);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Token_error_object_with_token_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var handler = JsonHandler(
            $$"""
            {
              "error": { "message": "{{rawSentinel}}" },
              "result": { "access_token": "synthetic-token" }
            }
            """);
        var result = await TestSyntheticTokenAsync(handler);

        AssertSanitizedFailure(result, rawSentinel);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Token_result_and_data_both_have_token_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "access_token": "synthetic-result-token" },
              "data": { "access_token": "synthetic-data-token" }
            }
            """);
        var result = await TestSyntheticTokenAsync(handler);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.TokenLoginConflictingResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Token_single_recognized_token_should_succeed()
    {
        var handler = JsonHandler(
            """{"result":{"access_token":"synthetic-token"}}""");
        var result = await TestSyntheticTokenAsync(handler);

        Assert.True(result.IsSuccess);
        Assert.Equal("***", result.Value.TokenPreview);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Ambiguous_results_should_not_contain_raw_provider_body()
    {
        const string rawSentinel = "raw-provider-body-must-not-leak";
        var handler = JsonHandler(
            $$"""{"unexpected":"{{rawSentinel}}"}""");
        var logs = new RecordingLogRepository();
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, logs));

        Assert.True(result.IsFailure);
        Assert.DoesNotContain(
            rawSentinel,
            result.Error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            rawSentinel,
            SerializeLogs(logs),
            StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Preview_content_type_without_pdf_signature_should_fail()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    "{}",
                    "application/pdf")));
        var client = new ViettelInvoicePreviewClient(
            new HttpClient(handler),
            new RecordingLogRepository());

        var result = await client.CreateDraftPreviewAsync(
            1,
            BaseUrl,
            "synthetic-user",
            SyntheticPassword,
            InvoiceProviderAuthMode.BasicAuth,
            "0000000000",
            new ViettelInvoicePayloadDto(),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Preview_nested_file_bytes_in_error_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var fileBytes = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("%PDF-synthetic"));
        var handler = JsonHandler(
            $$"""
            {
              "error": {
                "fileToBytes": "{{fileBytes}}",
                "secret": "{{rawSentinel}}"
              }
            }
            """);
        var logs = new RecordingLogRepository();
        var client = new ViettelInvoicePreviewClient(
            new HttpClient(handler),
            logs);

        var result = await client.CreateDraftPreviewAsync(
            1,
            BaseUrl,
            "synthetic-user",
            SyntheticPassword,
            InvoiceProviderAuthMode.BasicAuth,
            "0000000000",
            new ViettelInvoicePayloadDto(),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
        Assert.DoesNotContain(
            rawSentinel,
            result.Error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            rawSentinel,
            SerializeLogs(logs),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_single_recognized_valid_file_should_succeed()
    {
        var fileBytes = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("%PDF-synthetic"));
        var handler = JsonHandler(
            $$"""
            {
              "result": {
                "fileName": "synthetic-preview.pdf",
                "fileToBytes": "{{fileBytes}}"
              }
            }
            """);
        var client = new ViettelInvoicePreviewClient(
            new HttpClient(handler),
            new RecordingLogRepository());

        var result = await client.CreateDraftPreviewAsync(
            1,
            BaseUrl,
            "synthetic-user",
            SyntheticPassword,
            InvoiceProviderAuthMode.BasicAuth,
            "0000000000",
            new ViettelInvoicePayloadDto(),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "%PDF-synthetic",
            Encoding.UTF8.GetString(result.Value.FileBytes));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Preview_result_file_data_failure_should_fail()
    {
        var fileBytes = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("%PDF-synthetic"));
        var handler = JsonHandler(
            $$"""
            {
              "result": { "fileToBytes": "{{fileBytes}}" },
              "data": { "status": "ERROR", "errorCode": "500" }
            }
            """);
        var result = await PreviewSyntheticInvoiceAsync(
            CreatePreviewClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Preview_data_file_result_failure_should_fail()
    {
        var fileBytes = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("%PDF-synthetic"));
        var handler = JsonHandler(
            $$"""
            {
              "result": { "status": "FAILED", "errorCode": "500" },
              "data": { "fileToBytes": "{{fileBytes}}" }
            }
            """);
        var result = await PreviewSyntheticInvoiceAsync(
            CreatePreviewClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Preview_error_object_with_file_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var fileBytes = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("%PDF-synthetic"));
        var handler = JsonHandler(
            $$"""
            {
              "error": { "message": "{{rawSentinel}}" },
              "result": { "fileToBytes": "{{fileBytes}}" }
            }
            """);
        var logs = new RecordingLogRepository();
        var result = await PreviewSyntheticInvoiceAsync(
            CreatePreviewClient(handler, logs));

        AssertSanitizedFailure(result, rawSentinel);
        Assert.DoesNotContain(rawSentinel, SerializeLogs(logs), StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Preview_result_and_data_both_have_file_should_fail()
    {
        var fileBytes = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("%PDF-synthetic"));
        var handler = JsonHandler(
            $$"""
            {
              "result": { "fileToBytes": "{{fileBytes}}" },
              "data": { "fileToBytes": "{{fileBytes}}" }
            }
            """);
        var result = await PreviewSyntheticInvoiceAsync(
            CreatePreviewClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.PreviewConflictingResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Preview_pdf_signature_should_succeed()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    "%PDF-synthetic",
                    "application/octet-stream")));
        var client = new ViettelInvoicePreviewClient(
            new HttpClient(handler),
            new RecordingLogRepository());

        var result = await client.CreateDraftPreviewAsync(
            1,
            BaseUrl,
            "synthetic-user",
            SyntheticPassword,
            InvoiceProviderAuthMode.BasicAuth,
            "0000000000",
            new ViettelInvoicePayloadDto(),
            default);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("%PDF", Encoding.UTF8.GetString(result.Value.FileBytes));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Official_file_content_type_without_zip_signature_should_fail()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    "{}",
                    "application/zip")));
        var client = new ViettelOfficialFileClient(
            new HttpClient(handler),
            new RecordingLogRepository());

        var result = await client.DownloadOfficialFileAsync(
            1,
            ViettelOfficialFileType.ZipXml,
            BaseUrl,
            "synthetic-user",
            SyntheticPassword,
            InvoiceProviderAuthMode.BasicAuth,
            "0000000000",
            "SYNTHETIC-INVOICE",
            "SYNTHETIC-TEMPLATE",
            "SYNTHETIC-SERIES",
            DateTime.UtcNow,
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Official_file_nested_file_bytes_in_error_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var fileBytes = Convert.ToBase64String(
            [0x50, 0x4B, 0x03, 0x04, 0x00]);
        var handler = JsonHandler(
            $$"""
            {
              "error": {
                "fileToBytes": "{{fileBytes}}",
                "secret": "{{rawSentinel}}"
              }
            }
            """);
        var logs = new RecordingLogRepository();
        var client = new ViettelOfficialFileClient(
            new HttpClient(handler),
            logs);

        var result = await DownloadSyntheticOfficialZipAsync(client);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
        Assert.DoesNotContain(
            rawSentinel,
            result.Error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            rawSentinel,
            SerializeLogs(logs),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Official_file_single_recognized_zip_should_succeed()
    {
        var fileBytes = Convert.ToBase64String(
            [0x50, 0x4B, 0x03, 0x04, 0x00]);
        var handler = JsonHandler(
            $$"""
            {
              "result": {
                "fileName": "synthetic.zip",
                "fileToBytes": "{{fileBytes}}"
              }
            }
            """);
        var client = new ViettelOfficialFileClient(
            new HttpClient(handler),
            new RecordingLogRepository());

        var result = await DownloadSyntheticOfficialZipAsync(client);

        Assert.True(result.IsSuccess);
        Assert.Equal(0x50, result.Value.FileBytes[0]);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Official_file_result_file_data_failure_should_fail()
    {
        var fileBytes = Convert.ToBase64String(
            [0x50, 0x4B, 0x03, 0x04, 0x00]);
        var handler = JsonHandler(
            $$"""
            {
              "result": { "fileToBytes": "{{fileBytes}}" },
              "data": { "status": "ERROR", "errorCode": "500" }
            }
            """);
        var result = await DownloadSyntheticOfficialZipAsync(
            CreateOfficialFileClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Official_file_data_file_result_failure_should_fail()
    {
        var fileBytes = Convert.ToBase64String(
            [0x50, 0x4B, 0x03, 0x04, 0x00]);
        var handler = JsonHandler(
            $$"""
            {
              "result": { "status": "FAILED", "errorCode": "500" },
              "data": { "fileToBytes": "{{fileBytes}}" }
            }
            """);
        var result = await DownloadSyntheticOfficialZipAsync(
            CreateOfficialFileClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Official_file_error_object_with_file_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var fileBytes = Convert.ToBase64String(
            [0x50, 0x4B, 0x03, 0x04, 0x00]);
        var handler = JsonHandler(
            $$"""
            {
              "error": { "message": "{{rawSentinel}}" },
              "result": { "fileToBytes": "{{fileBytes}}" }
            }
            """);
        var logs = new RecordingLogRepository();
        var result = await DownloadSyntheticOfficialZipAsync(
            CreateOfficialFileClient(handler, logs));

        AssertSanitizedFailure(result, rawSentinel);
        Assert.DoesNotContain(rawSentinel, SerializeLogs(logs), StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Official_file_result_and_data_both_have_file_should_fail()
    {
        var fileBytes = Convert.ToBase64String(
            [0x50, 0x4B, 0x03, 0x04, 0x00]);
        var handler = JsonHandler(
            $$"""
            {
              "result": { "fileToBytes": "{{fileBytes}}" },
              "data": { "fileToBytes": "{{fileBytes}}" }
            }
            """);
        var result = await DownloadSyntheticOfficialZipAsync(
            CreateOfficialFileClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.DownloadFileConflictingResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Official_file_single_recognized_pdf_should_succeed()
    {
        var fileBytes = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("%PDF-synthetic"));
        var handler = JsonHandler(
            $$"""
            {
              "data": {
                "fileName": "synthetic.pdf",
                "fileToBytes": "{{fileBytes}}"
              }
            }
            """);
        var result = await DownloadSyntheticOfficialFileAsync(
            CreateOfficialFileClient(handler),
            ViettelOfficialFileType.Pdf);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("%PDF", Encoding.UTF8.GetString(result.Value.FileBytes));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Official_file_zip_signature_should_succeed()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(
                        [0x50, 0x4B, 0x03, 0x04, 0x00])
                }));
        var client = new ViettelOfficialFileClient(
            new HttpClient(handler),
            new RecordingLogRepository());

        var result = await client.DownloadOfficialFileAsync(
            1,
            ViettelOfficialFileType.ZipXml,
            BaseUrl,
            "synthetic-user",
            SyntheticPassword,
            InvoiceProviderAuthMode.BasicAuth,
            "0000000000",
            "SYNTHETIC-INVOICE",
            "SYNTHETIC-TEMPLATE",
            "SYNTHETIC-SERIES",
            DateTime.UtcNow,
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0x50, result.Value.FileBytes[0]);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(
        """{"result":{"invoiceNo":"INV-RESULT"},"data":{"errorCode":"500"}}""")]
    [InlineData(
        """{"data":{"errorCode":"500"},"result":{"invoiceNo":"INV-RESULT"}}""")]
    public async Task Issue_conflict_result_should_not_depend_on_json_property_order(
        string responseBody)
    {
        var handler = JsonHandler(responseBody);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(
        """{"result":{"invoiceNo":"INV-RESULT"},"data":{"errorCode":"NOT_FOUND_DATA"}}""")]
    [InlineData(
        """{"data":{"errorCode":"NOT_FOUND_DATA"},"result":{"invoiceNo":"INV-RESULT"}}""")]
    public async Task Lookup_conflict_result_should_not_depend_on_json_property_order(
        string responseBody)
    {
        var handler = JsonHandler(responseBody);
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.LookupConflictingResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(
        """{"invoices":[],"result":{"errorCode":"500"}}""")]
    [InlineData(
        """{"result":{"errorCode":"500"},"invoices":[]}""")]
    public async Task List_conflict_result_should_not_depend_on_json_property_order(
        string responseBody)
    {
        var handler = JsonHandler(responseBody);
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(
        """
        {
          "errorCode": "200",
          "errorCode": "500",
          "result": { "invoiceNo": "INV-001" }
        }
        """)]
    [InlineData(
        """
        {
          "errorCode": "500",
          "errorCode": "200",
          "result": { "invoiceNo": "INV-001" }
        }
        """)]
    public async Task Issue_duplicate_error_code_should_fail_in_both_orders(
        string responseBody)
    {
        var handler = JsonHandler(responseBody);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_duplicate_result_envelope_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "invoiceNo": "INV-001" },
              "result": { "status": "ERROR", "errorCode": "500" }
            }
            """);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_duplicate_error_branch_should_fail()
    {
        const string rawSentinel = "provider-secret";
        var handler = JsonHandler(
            $$"""
            {
              "error": null,
              "error": {
                "code": "500",
                "message": "{{rawSentinel}}"
              },
              "result": { "invoiceNo": "INV-001" }
            }
            """);
        var logs = new RecordingLogRepository();
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, logs));

        AssertSanitizedFailure(result, rawSentinel);
        Assert.DoesNotContain(
            rawSentinel,
            SerializeLogs(logs),
            StringComparison.Ordinal);
        Assert.Equal("Viettel.IssueInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_duplicate_confirmation_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": {
                "invoiceNo": "INV-001",
                "invoiceNo": "INV-002"
              }
            }
            """);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_case_insensitive_duplicate_control_name_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "errorCode": "200",
              "ErrorCode": "500",
              "result": { "invoiceNo": "INV-001" }
            }
            """);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_escaped_equivalent_duplicate_name_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "errorCode": "200",
              "error\u0043ode": "500",
              "result": { "invoiceNo": "INV-001" }
            }
            """);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Issue_duplicate_in_unknown_nested_object_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "invoiceNo": "INV-001" },
              "debug": {
                "trace": "A",
                "trace": "B"
              }
            }
            """);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Email_duplicate_success_and_failure_code_should_fail()
    {
        var handler = JsonHandler(
            """{"code":"200","code":"500"}""");
        var result = await SendSyntheticEmailAsync(
            CreateEmailClient(handler),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.SendEmailInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Email_duplicate_message_should_fail()
    {
        var handler = JsonHandler(
            """{"message":"OK","message":"FAILED"}""");
        var result = await SendSyntheticEmailAsync(
            CreateEmailClient(handler),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.SendEmailInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Email_duplicate_result_envelope_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": { "code": "200" },
              "result": { "code": "500" }
            }
            """);
        var result = await SendSyntheticEmailAsync(
            CreateEmailClient(handler),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.SendEmailInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Lookup_duplicate_invoice_identity_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": {
                "invoiceNo": "INV-001",
                "invoiceNo": "INV-002"
              }
            }
            """);
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.LookupInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Lookup_duplicate_not_found_and_success_marker_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "errorCode": "NOT_FOUND_DATA",
              "errorCode": "200",
              "result": { "invoiceNo": "INV-001" }
            }
            """);
        var result = await SearchSyntheticTransactionAsync(
            CreateLookupClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.LookupInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_duplicate_invoices_property_should_fail()
    {
        var handler = JsonHandler(
            """{"invoices":[],"invoices":[]}""");
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.GetInvoicesInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task List_item_duplicate_identity_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "invoices": [
                {
                  "invoiceNo": "INV-A",
                  "invoiceNo": "INV-B"
                }
              ]
            }
            """);
        var result = await GetSyntheticInvoicesAsync(
            CreateListClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.GetInvoicesInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Token_duplicate_access_token_should_fail()
    {
        const string firstToken = "synthetic-token-a";
        const string secondToken = "synthetic-token-b";
        var handler = JsonHandler(
            $$"""
            {
              "access_token": "{{firstToken}}",
              "access_token": "{{secondToken}}"
            }
            """);
        var result = await TestSyntheticTokenAsync(handler);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.TokenLoginInvalidResponse", result.Error.Code);
        Assert.DoesNotContain(
            firstToken,
            result.Error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            secondToken,
            result.Error.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Basic_auth_duplicate_result_should_fail()
    {
        var handler = JsonHandler(
            """
            {
              "result": [],
              "result": { "status": "ERROR" }
            }
            """);
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.BasicAuthInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Basic_auth_get_invoices_duplicate_result_should_fail()
    {
        var responseNumber = 0;
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    Interlocked.Increment(ref responseNumber) == 1
                        ? """{"result":[]}"""
                        : """
                          {
                            "result": { "invoices": [] },
                            "result": { "status": "ERROR" }
                          }
                          """)));
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.BasicAuthInvalidResponse", result.Error.Code);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Basic_auth_invoice_item_duplicate_identity_should_fail()
    {
        var responseNumber = 0;
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    Interlocked.Increment(ref responseNumber) == 1
                        ? """{"result":[]}"""
                        : """
                          {
                            "invoices": [
                              {
                                "invoiceNo": "INV-A",
                                "invoiceNo": "INV-B"
                              }
                            ]
                          }
                          """)));
        var result = await TestSyntheticBasicAuthAsync(
            new ViettelInvoiceAuthClient(new HttpClient(handler)));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.BasicAuthInvalidResponse", result.Error.Code);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Preview_duplicate_file_bytes_should_fail()
    {
        var firstFile = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("%PDF-first"));
        var secondFile = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("%PDF-second"));
        var handler = JsonHandler(
            $$"""
            {
              "fileToBytes": "{{firstFile}}",
              "fileToBytes": "{{secondFile}}"
            }
            """);
        var result = await PreviewSyntheticInvoiceAsync(
            CreatePreviewClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.PreviewInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Official_file_duplicate_file_bytes_should_fail()
    {
        var firstFile = Convert.ToBase64String(
            [0x50, 0x4B, 0x03, 0x04, 0x01]);
        var secondFile = Convert.ToBase64String(
            [0x50, 0x4B, 0x03, 0x04, 0x02]);
        var handler = JsonHandler(
            $$"""
            {
              "fileToBytes": "{{firstFile}}",
              "fileToBytes": "{{secondFile}}"
            }
            """);
        var result = await DownloadSyntheticOfficialZipAsync(
            CreateOfficialFileClient(handler));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.FileInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData("""{"errorCode":"200","errorCode":"500","result":{"invoiceNo":"INV-001"}}""")]
    [InlineData("""{"errorCode":"500","errorCode":"200","result":{"invoiceNo":"INV-001"}}""")]
    public async Task Duplicate_response_result_should_not_depend_on_property_order(
        string responseBody)
    {
        var handler = JsonHandler(responseBody);
        var result = await IssueSyntheticInvoiceAsync(
            CreateIssueClient(handler, new RecordingLogRepository()));

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueInvalidResponse", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task VietQr_duplicate_response_member_should_fail_without_retry()
    {
        const string taxCode = "0000000000-001";
        var handler = JsonHandler(
            """
            {
              "code": "00",
              "code": "500",
              "data": {
                "name": "Synthetic Business",
                "address": "Synthetic Address"
              }
            }
            """);
        var logger = new RecordingLogger<VietQrTaxCodeLookupService>();
        var result = await CreateTaxLookupClient(handler, logger)
            .LookupBusinessAsync(taxCode, default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "TaxCodeLookup.InvalidProviderResponse",
            result.Error.Code);
        Assert.DoesNotContain(
            taxCode,
            string.Join("|", logger.Messages),
            StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void Strict_json_should_detect_exact_case_duplicate()
    {
        Assert.Equal(
            "DuplicatePropertyName",
            ValidateStrictProviderJson(
                """{"code":"200","code":"500"}"""));
    }

    [Fact]
    public void Strict_json_should_detect_case_insensitive_duplicate()
    {
        Assert.Equal(
            "DuplicatePropertyName",
            ValidateStrictProviderJson(
                """{"code":"200","Code":"500"}"""));
    }

    [Fact]
    public void Strict_json_should_detect_escaped_equivalent_duplicate()
    {
        Assert.Equal(
            "DuplicatePropertyName",
            ValidateStrictProviderJson(
                """{"errorCode":"200","error\u0043ode":"500"}"""));
    }

    [Fact]
    public void Strict_json_should_detect_nested_object_duplicate()
    {
        Assert.Equal(
            "DuplicatePropertyName",
            ValidateStrictProviderJson(
                """{"debug":{"trace":"A","trace":"B"}}"""));
    }

    [Fact]
    public void Strict_json_should_detect_array_item_duplicate()
    {
        Assert.Equal(
            "DuplicatePropertyName",
            ValidateStrictProviderJson(
                """{"items":[{"id":"A","id":"B"}]}"""));
    }

    [Fact]
    public void Strict_json_should_accept_unique_properties()
    {
        Assert.Equal(
            "None",
            ValidateStrictProviderJson(
                """{"code":"200","result":{"invoiceNo":"INV-001"}}"""));
    }

    [Fact]
    public void Strict_json_should_allow_same_name_in_different_objects()
    {
        Assert.Equal(
            "None",
            ValidateStrictProviderJson(
                """
                {
                  "result": { "code": "200" },
                  "data": { "code": "200" }
                }
                """));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Strict_json_should_accept_empty_object_and_array(
        string responseBody)
    {
        Assert.Equal(
            "None",
            ValidateStrictProviderJson(responseBody));
    }

    [Fact]
    public void Unique_direct_property_lookup_should_report_duplicate()
    {
        Assert.Equal(
            "Duplicate",
            GetUniqueDirectPropertyResult(
                """{"code":"200","Code":"500"}""",
                "code"));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Issue_invoice_should_never_retry_http_failures(
        HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(Response(statusCode, "{\"password\":\"provider-secret\"}")));
        var logs = new RecordingLogRepository();
        var client = CreateIssueClient(handler, logs);

        var result = await client.IssueInvoiceAsync(
            invoiceHeadId: 1,
            baseUrl: BaseUrl,
            username: "synthetic-user",
            password: SyntheticPassword,
            authMode: InvoiceProviderAuthMode.BasicAuth,
            supplierTaxCode: "0000000000",
            payload: new ViettelInvoicePayloadDto(),
            ct: default);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
        Assert.Single(logs.Items);

        var persistedLog = logs.Items[0];
        var serializedLog = string.Join(
            "|",
            persistedLog.RequestUrl,
            persistedLog.RequestBody,
            persistedLog.ResponseBody,
            persistedLog.ErrorMessage);

        Assert.DoesNotContain(SyntheticPassword, serializedLog, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-secret", serializedLog, StringComparison.Ordinal);
        Assert.DoesNotContain("0000000000", serializedLog, StringComparison.Ordinal);
        Assert.Contains("\"redacted\":true", serializedLog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_email_should_not_retry_provider_failure()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(Response(HttpStatusCode.InternalServerError, "failed")));
        var client = new ViettelInvoiceEmailClient(
            new HttpClient(handler),
            new RecordingLogRepository());

        var result = await client.SendEmailToCustomerAsync(
            invoiceHeadId: 1,
            baseUrl: BaseUrl,
            username: "synthetic-user",
            password: SyntheticPassword,
            authMode: InvoiceProviderAuthMode.BasicAuth,
            supplierTaxCode: "0000000000",
            transactionUuid: "synthetic-uuid",
            buyerEmail: "nobody@example.invalid",
            providerInvoiceNo: null,
            ct: default);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Send_email_timeout_should_return_failure_once()
    {
        var handler = new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK, "{}");
        });
        var client = new ViettelInvoiceEmailClient(
            new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(80)
            },
            new RecordingLogRepository());

        var result = await SendSyntheticEmailAsync(client, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.SendEmailTimeout", result.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Send_email_transport_failure_should_not_retry()
    {
        var handler = new RecordingHandler((_, _) =>
            throw new HttpRequestException("synthetic email transport failure"));
        var client = new ViettelInvoiceEmailClient(
            new HttpClient(handler),
            new RecordingLogRepository());

        var result = await SendSyntheticEmailAsync(client, default);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Send_email_caller_cancellation_should_propagate_once()
    {
        var handler = new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK, "{}");
        });
        var client = new ViettelInvoiceEmailClient(
            new HttpClient(handler),
            new RecordingLogRepository());
        using var cancellation = new CancellationTokenSource();
        var operation = SendSyntheticEmailAsync(client, cancellation.Token);

        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Safe_get_should_retry_one_transient_response_then_succeed()
    {
        var responseNumber = 0;
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Interlocked.Increment(ref responseNumber) == 1
                    ? Response(HttpStatusCode.InternalServerError, "failed")
                    : Response(
                        HttpStatusCode.OK,
                        """
                        {
                          "code": "00",
                          "data": {
                            "name": "Synthetic Business",
                            "address": "Synthetic Address"
                          }
                        }
                        """)));
        var logger = new RecordingLogger<VietQrTaxCodeLookupService>();
        var client = CreateTaxLookupClient(handler, logger);

        var result = await client.LookupBusinessAsync(
            "0000000000",
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Safe_get_should_stop_after_bounded_transient_retry_count()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "failed")));
        var client = CreateTaxLookupClient(
            handler,
            new RecordingLogger<VietQrTaxCodeLookupService>());

        var result = await client.LookupBusinessAsync("0000000000", default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ExternalHttpResilienceOptions.SafeGetTransientRetryCount + 1,
            handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Safe_get_should_retry_scoped_transient_status_once(
        HttpStatusCode statusCode)
    {
        var responseNumber = 0;
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Interlocked.Increment(ref responseNumber) == 1
                    ? Response(statusCode, "failed")
                    : Response(HttpStatusCode.OK, SuccessfulTaxLookupJson)));
        var client = CreateTaxLookupClient(
            handler,
            new RecordingLogger<VietQrTaxCodeLookupService>());

        var result = await client.LookupBusinessAsync("0000000000", default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Safe_get_should_retry_transport_failure_then_succeed()
    {
        var responseNumber = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            if (Interlocked.Increment(ref responseNumber) == 1)
                throw new HttpRequestException("synthetic transport failure");

            return Task.FromResult(
                Response(HttpStatusCode.OK, SuccessfulTaxLookupJson));
        });
        var client = CreateTaxLookupClient(
            handler,
            new RecordingLogger<VietQrTaxCodeLookupService>());

        var result = await client.LookupBusinessAsync("0000000000", default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Safe_get_should_retry_provider_timeout_then_succeed()
    {
        var responseNumber = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            if (Interlocked.Increment(ref responseNumber) == 1)
                throw new OperationCanceledException("synthetic provider timeout");

            return Task.FromResult(
                Response(HttpStatusCode.OK, SuccessfulTaxLookupJson));
        });
        var client = CreateTaxLookupClient(
            handler,
            new RecordingLogger<VietQrTaxCodeLookupService>());

        var result = await client.LookupBusinessAsync("0000000000", default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Safe_get_should_not_retry_non_transient_client_errors(
        HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(Response(statusCode, "failed")));
        var client = CreateTaxLookupClient(
            handler,
            new RecordingLogger<VietQrTaxCodeLookupService>());

        var result = await client.LookupBusinessAsync("0000000000", default);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Safe_get_caller_cancellation_should_propagate_without_retry()
    {
        var handler = new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK, SuccessfulTaxLookupJson);
        });
        var client = CreateTaxLookupClient(
            handler,
            new RecordingLogger<VietQrTaxCodeLookupService>());
        using var cancellation = new CancellationTokenSource();
        var operation = client.LookupBusinessAsync(
            "0000000000",
            cancellation.Token);

        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Candidate_fallback_should_be_distinct_from_retry(
        bool firstCandidateReturnsNotFound)
    {
        const string originalTaxCode = "0000000000-001";
        var requestNumber = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            if (Interlocked.Increment(ref requestNumber) > 1)
            {
                return Task.FromResult(
                    Response(HttpStatusCode.OK, SuccessfulTaxLookupJson));
            }

            return Task.FromResult(
                firstCandidateReturnsNotFound
                    ? Response(HttpStatusCode.NotFound, "not found")
                    : Response(HttpStatusCode.OK, """{"code":"00","data":{}}"""));
        });
        var client = CreateTaxLookupClient(
            handler,
            new RecordingLogger<VietQrTaxCodeLookupService>());

        var result = await client.LookupBusinessAsync(
            originalTaxCode,
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
        Assert.EndsWith(
            originalTaxCode,
            handler.RequestUris[0],
            StringComparison.Ordinal);
        Assert.EndsWith(
            originalTaxCode.Replace("-", string.Empty),
            handler.RequestUris[1],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_cancellation_should_propagate_without_retry()
    {
        var handler = new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK, "{}");
        });
        var client = CreateIssueClient(handler, new RecordingLogRepository());
        using var callerCancellation = new CancellationTokenSource();
        var operation = client.IssueInvoiceAsync(
            invoiceHeadId: 1,
            baseUrl: BaseUrl,
            username: "synthetic-user",
            password: SyntheticPassword,
            authMode: InvoiceProviderAuthMode.BasicAuth,
            supplierTaxCode: "0000000000",
            payload: new ViettelInvoicePayloadDto(),
            ct: callerCancellation.Token);

        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await callerCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Provider_timeout_should_return_sanitized_failure_once()
    {
        var handler = new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK, "{}");
        });
        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(80)
        };
        var client = new ViettelInvoiceIssueClient(
            httpClient,
            new RecordingLogRepository());

        var result = await client.IssueInvoiceAsync(
            invoiceHeadId: 1,
            baseUrl: BaseUrl,
            username: "synthetic-user",
            password: SyntheticPassword,
            authMode: InvoiceProviderAuthMode.BasicAuth,
            supplierTaxCode: "0000000000",
            payload: new ViettelInvoicePayloadDto(),
            ct: default);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueTimeout", result.Error.Code);
        Assert.DoesNotContain(SyntheticPassword, result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Final_http_request_exception_should_be_sanitized_failure()
    {
        var handler = new RecordingHandler((_, _) =>
            throw new HttpRequestException(
                $"host=secret-host password={SyntheticPassword}"));
        var logs = new RecordingLogRepository();
        var client = CreateIssueClient(handler, logs);

        var result = await client.IssueInvoiceAsync(
            invoiceHeadId: 1,
            baseUrl: BaseUrl,
            username: "synthetic-user",
            password: SyntheticPassword,
            authMode: InvoiceProviderAuthMode.BasicAuth,
            supplierTaxCode: "0000000000",
            payload: new ViettelInvoicePayloadDto(),
            ct: default);

        Assert.True(result.IsFailure);
        Assert.Equal("Viettel.IssueTransportFailed", result.Error.Code);
        Assert.DoesNotContain("secret-host", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticPassword, result.Error.Message, StringComparison.Ordinal);
        Assert.Equal("HttpRequestException", logs.Items.Single().ErrorMessage);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Token_login_should_not_return_access_token_preview()
    {
        const string accessToken = "synthetic-access-token-that-must-not-leak";
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    $"{{\"access_token\":\"{accessToken}\"}}")));
        var client = new ViettelInvoiceAuthClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(
            baseUrl: BaseUrl,
            username: "synthetic-user",
            password: SyntheticPassword,
            authMode: InvoiceProviderAuthMode.TokenLogin,
            supplierTaxCode: string.Empty,
            invoiceType: string.Empty,
            templateCode: string.Empty,
            invoiceSeries: string.Empty,
            ct: default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsSuccess);
        Assert.Equal("***", result.Value.TokenPreview);
        Assert.DoesNotContain(accessToken, result.Value.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Other_viettel_post_clients_should_not_retry_provider_failure()
    {
        var authHandler = FailureHandler();
        var authResult = await new ViettelInvoiceAuthClient(
            new HttpClient(authHandler))
            .TestConnectionAsync(
                BaseUrl,
                "synthetic-user",
                SyntheticPassword,
                InvoiceProviderAuthMode.TokenLogin,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                default);

        var previewHandler = FailureHandler();
        var previewResult = await new ViettelInvoicePreviewClient(
            new HttpClient(previewHandler),
            new RecordingLogRepository())
            .CreateDraftPreviewAsync(
                1,
                BaseUrl,
                "synthetic-user",
                SyntheticPassword,
                InvoiceProviderAuthMode.BasicAuth,
                "0000000000",
                new ViettelInvoicePayloadDto(),
                default);

        var fileHandler = FailureHandler();
        var fileResult = await new ViettelOfficialFileClient(
            new HttpClient(fileHandler),
            new RecordingLogRepository())
            .DownloadOfficialFileAsync(
                1,
                ViettelOfficialFileType.Pdf,
                BaseUrl,
                "synthetic-user",
                SyntheticPassword,
                InvoiceProviderAuthMode.BasicAuth,
                "0000000000",
                "synthetic-invoice",
                "synthetic-template",
                "synthetic-series",
                DateTime.UtcNow,
                default);

        var lookupHandler = FailureHandler();
        var lookupResult = await new ViettelInvoiceLookupClient(
            new HttpClient(lookupHandler),
            new RecordingLogRepository())
            .SearchByTransactionUuidAsync(
                1,
                BaseUrl,
                "synthetic-user",
                SyntheticPassword,
                InvoiceProviderAuthMode.BasicAuth,
                "0000000000",
                "synthetic-uuid",
                default);

        var listHandler = FailureHandler();
        var listResult = await new ViettelInvoiceListClient(
            new HttpClient(listHandler))
            .GetInvoicesAsync(
                BaseUrl,
                "synthetic-user",
                SyntheticPassword,
                InvoiceProviderAuthMode.BasicAuth,
                "0000000000",
                "1",
                "synthetic-template",
                "synthetic-series",
                DateTime.UtcNow.Date.AddDays(-1),
                DateTime.UtcNow.Date,
                ct: default);

        Assert.True(authResult.IsFailure);
        Assert.True(previewResult.IsFailure);
        Assert.True(fileResult.IsFailure);
        Assert.True(lookupResult.IsFailure);
        Assert.True(listResult.IsFailure);
        Assert.Equal(1, authHandler.CallCount);
        Assert.Equal(1, previewHandler.CallCount);
        Assert.Equal(1, fileHandler.CallCount);
        Assert.Equal(1, lookupHandler.CallCount);
        Assert.Equal(1, listHandler.CallCount);

        static RecordingHandler FailureHandler() =>
            new((_, _) =>
                Task.FromResult(
                    Response(HttpStatusCode.InternalServerError, "failed")));
    }

    [Fact]
    public async Task Provider_exception_log_should_not_contain_customer_or_secret_data()
    {
        const string customerTaxCode = "0000000000-001";
        var handler = new RecordingHandler((_, _) =>
            throw new HttpRequestException(
                $"token=synthetic-access-token password={SyntheticPassword}"));
        var logger = new RecordingLogger<VietQrTaxCodeLookupService>();
        var client = CreateTaxLookupClient(handler, logger);

        var result = await client.LookupBusinessAsync(
            customerTaxCode,
            default);

        Assert.True(result.IsFailure);
        var logText = string.Join("|", logger.Messages);
        Assert.DoesNotContain(customerTaxCode, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticPassword, logText, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-access-token", logText, StringComparison.Ordinal);
        Assert.Contains("HttpRequestException", logText, StringComparison.Ordinal);
        Assert.Equal(
            ExternalHttpResilienceOptions.SafeGetTransientRetryCount + 1,
            handler.CallCount);
    }

    [Fact]
    public async Task Cancelled_file_save_should_not_publish_final_or_partial_file()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"gaoapp-r1-5-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);

        try
        {
            var environment = new TestHostEnvironment
            {
                ContentRootPath = testRoot
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Storage:UploadRoot"] = testRoot
                    })
                .Build();
            var storage = new InvoiceFileStorage(environment, configuration);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => storage.SaveAsync(
                    "invoices/test",
                    "invoice.pdf",
                    Encoding.UTF8.GetBytes("%PDF-synthetic"),
                    cancellation.Token));

            Assert.Empty(Directory.EnumerateFiles(testRoot, "*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Successful_file_save_should_publish_complete_bytes_without_temp()
    {
        var testRoot = CreateTestRoot();

        try
        {
            var storage = CreateFileStorage(testRoot);
            var expected = Encoding.UTF8.GetBytes("%PDF-complete-synthetic");

            var storedPath = await storage.SaveAsync(
                "invoices/test",
                "invoice.pdf",
                expected,
                default);

            var finalPath = Path.Combine(
                testRoot,
                "invoices",
                "test",
                "invoice.pdf");
            Assert.Equal("uploads/invoices/test/invoice.pdf", storedPath);
            Assert.Equal(expected, await File.ReadAllBytesAsync(finalPath));
            Assert.Empty(Directory.EnumerateFiles(
                testRoot,
                "*.tmp",
                SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Existing_final_file_should_be_atomically_replaced()
    {
        var testRoot = CreateTestRoot();

        try
        {
            var storage = CreateFileStorage(testRoot);
            var original = Encoding.UTF8.GetBytes("%PDF-original");
            var replacement = Encoding.UTF8.GetBytes("%PDF-complete-replacement");

            await storage.SaveAsync(
                "invoices/test",
                "invoice.pdf",
                original,
                default);
            await storage.SaveAsync(
                "invoices/test",
                "invoice.pdf",
                replacement,
                default);

            var finalPath = Path.Combine(
                testRoot,
                "invoices",
                "test",
                "invoice.pdf");
            Assert.Equal(replacement, await File.ReadAllBytesAsync(finalPath));
            Assert.Empty(Directory.EnumerateFiles(
                testRoot,
                "*.tmp",
                SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Interrupted_temporary_write_should_not_publish_or_leave_temp()
    {
        var testRoot = CreateTestRoot();

        try
        {
            var storage = new InterruptingInvoiceFileStorage(
                CreateHostEnvironment(testRoot),
                CreateStorageConfiguration(testRoot));

            await Assert.ThrowsAsync<IOException>(
                () => storage.SaveAsync(
                    "invoices/test",
                    "invoice.pdf",
                    Encoding.UTF8.GetBytes("%PDF-complete-synthetic"),
                    default));

            var finalPath = Path.Combine(
                testRoot,
                "invoices",
                "test",
                "invoice.pdf");
            Assert.False(File.Exists(finalPath));
            Assert.Empty(Directory.EnumerateFiles(
                testRoot,
                "*",
                SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static string ValidateStrictProviderJson(string json)
    {
        var method = GetViettelClientHelperMethod(
            "TryParseStrictProviderJson");
        object?[] arguments = [json, null];
        var validationResult = method.Invoke(null, arguments);

        (arguments[1] as IDisposable)?.Dispose();

        return validationResult?.ToString() ?? string.Empty;
    }

    private static string GetUniqueDirectPropertyResult(
        string json,
        string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        var method = GetViettelClientHelperMethod(
            "GetUniqueDirectProperty");
        object?[] arguments =
        [
            document.RootElement,
            propertyName,
            default(JsonElement)
        ];
        var lookupResult = method.Invoke(null, arguments);

        return lookupResult?.ToString() ?? string.Empty;
    }

    private static MethodInfo GetViettelClientHelperMethod(
        string methodName)
    {
        var helperType = typeof(ViettelInvoiceIssueClient)
            .Assembly
            .GetType(
                "GaoApp.Infrastructure.Services.Invoices.ViettelClientHelper",
                throwOnError: true)!;

        return helperType.GetMethod(
                   methodName,
                   BindingFlags.Public | BindingFlags.Static) ??
               throw new InvalidOperationException(
                   $"Không tìm thấy helper method {methodName}.");
    }

    private static RecordingHandler JsonHandler(string body) =>
        new((_, _) =>
            Task.FromResult(Response(HttpStatusCode.OK, body)));

    private static Task<Result<ViettelInvoiceIssueResultDto>>
        IssueSyntheticInvoiceAsync(ViettelInvoiceIssueClient client) =>
        client.IssueInvoiceAsync(
            invoiceHeadId: 1,
            baseUrl: BaseUrl,
            username: "synthetic-user",
            password: SyntheticPassword,
            authMode: InvoiceProviderAuthMode.BasicAuth,
            supplierTaxCode: "0000000000",
            payload: new ViettelInvoicePayloadDto(),
            ct: default);

    private static ViettelInvoiceEmailClient CreateEmailClient(
        RecordingHandler handler,
        RecordingLogRepository? logs = null) =>
        new(
            new HttpClient(handler),
            logs ?? new RecordingLogRepository());

    private static ViettelInvoiceLookupClient CreateLookupClient(
        RecordingHandler handler,
        RecordingLogRepository? logs = null) =>
        new(
            new HttpClient(handler),
            logs ?? new RecordingLogRepository());

    private static Task<Result<ViettelInvoiceLookupResultDto>>
        SearchSyntheticTransactionAsync(ViettelInvoiceLookupClient client) =>
        client.SearchByTransactionUuidAsync(
            1,
            BaseUrl,
            "synthetic-user",
            SyntheticPassword,
            InvoiceProviderAuthMode.BasicAuth,
            "0000000000",
            "synthetic-uuid",
            default);

    private static ViettelInvoiceListClient CreateListClient(
        RecordingHandler handler) =>
        new(new HttpClient(handler));

    private static Task<Result<ViettelInvoiceListSyncResultDto>>
        GetSyntheticInvoicesAsync(ViettelInvoiceListClient client) =>
        client.GetInvoicesAsync(
            BaseUrl,
            "synthetic-user",
            SyntheticPassword,
            InvoiceProviderAuthMode.BasicAuth,
            "0000000000",
            "1",
            "synthetic-template",
            "synthetic-series",
            DateTime.UtcNow.Date.AddDays(-1),
            DateTime.UtcNow.Date,
            ct: default);

    private static ViettelInvoicePreviewClient CreatePreviewClient(
        RecordingHandler handler,
        RecordingLogRepository? logs = null) =>
        new(
            new HttpClient(handler),
            logs ?? new RecordingLogRepository());

    private static Task<Result<ViettelInvoicePreviewFileDto>>
        PreviewSyntheticInvoiceAsync(
            ViettelInvoicePreviewClient client) =>
        client.CreateDraftPreviewAsync(
            1,
            BaseUrl,
            "synthetic-user",
            SyntheticPassword,
            InvoiceProviderAuthMode.BasicAuth,
            "0000000000",
            new ViettelInvoicePayloadDto(),
            default);

    private static ViettelOfficialFileClient CreateOfficialFileClient(
        RecordingHandler handler,
        RecordingLogRepository? logs = null) =>
        new(
            new HttpClient(handler),
            logs ?? new RecordingLogRepository());

    private static Task<Result<ViettelOfficialFileResultDto>>
        DownloadSyntheticOfficialZipAsync(
            ViettelOfficialFileClient client) =>
        DownloadSyntheticOfficialFileAsync(
            client,
            ViettelOfficialFileType.ZipXml);

    private static Task<Result<ViettelOfficialFileResultDto>>
        DownloadSyntheticOfficialFileAsync(
            ViettelOfficialFileClient client,
            ViettelOfficialFileType fileType) =>
        client.DownloadOfficialFileAsync(
            1,
            fileType,
            BaseUrl,
            "synthetic-user",
            SyntheticPassword,
            InvoiceProviderAuthMode.BasicAuth,
            "0000000000",
            "SYNTHETIC-INVOICE",
            "SYNTHETIC-TEMPLATE",
            "SYNTHETIC-SERIES",
            DateTime.UtcNow,
            default);

    private static Task<Result<TestInvoiceProviderLoginResultDto>>
        TestSyntheticTokenAsync(RecordingHandler handler) =>
        new ViettelInvoiceAuthClient(new HttpClient(handler))
            .TestConnectionAsync(
                BaseUrl,
                "synthetic-user",
                SyntheticPassword,
                InvoiceProviderAuthMode.TokenLogin,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                default);

    private static Task<Result<TestInvoiceProviderLoginResultDto>>
        TestSyntheticBasicAuthAsync(ViettelInvoiceAuthClient client) =>
        client.TestConnectionAsync(
            BaseUrl,
            "synthetic-user",
            SyntheticPassword,
            InvoiceProviderAuthMode.BasicAuth,
            "0000000000",
            "1",
            "synthetic-template",
            "synthetic-series",
            default);

    private static void AssertSanitizedFailure<T>(
        Result<T> result,
        string rawSentinel)
    {
        Assert.True(result.IsFailure);
        Assert.DoesNotContain(
            rawSentinel,
            result.Error.Code,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            rawSentinel,
            result.Error.Message,
            StringComparison.Ordinal);
    }

    private static string SerializeLogs(RecordingLogRepository logs) =>
        string.Join(
            "|",
            logs.Items.SelectMany(
                log => new[]
                {
                    log.RequestUrl,
                    log.RequestBody,
                    log.ResponseBody,
                    log.ErrorCode,
                    log.ErrorMessage
                }));

    private static ServiceProvider BuildExternalOptionsProvider(
        IReadOnlyDictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                "Server=localhost;Database=GaoAppR15Tests;Integrated Security=true",
            ["ExternalHttpResilience:AuthenticationTimeoutSeconds"] = "20",
            ["ExternalHttpResilience:SafeReadTimeoutSeconds"] = "20",
            ["ExternalHttpResilience:NonIdempotentWriteTimeoutSeconds"] = "30",
            ["ExternalHttpResilience:FileDownloadTimeoutSeconds"] = "45"
        };

        foreach (var (key, value) in overrides)
            values[key] = value;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static Task<
        GaoApp.Application.Common.Results.Result<
            ViettelInvoiceSendEmailResultDto>> SendSyntheticEmailAsync(
        ViettelInvoiceEmailClient client,
        CancellationToken ct) =>
        client.SendEmailToCustomerAsync(
            invoiceHeadId: 1,
            baseUrl: BaseUrl,
            username: "synthetic-user",
            password: SyntheticPassword,
            authMode: InvoiceProviderAuthMode.BasicAuth,
            supplierTaxCode: "0000000000",
            transactionUuid: "synthetic-uuid",
            buyerEmail: "nobody@example.invalid",
            providerInvoiceNo: null,
            ct: ct);

    private static string CreateTestRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"gaoapp-r1-5-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static TestHostEnvironment CreateHostEnvironment(string root) =>
        new()
        {
            ContentRootPath = root
        };

    private static IConfiguration CreateStorageConfiguration(string root) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Storage:UploadRoot"] = root
                })
            .Build();

    private static InvoiceFileStorage CreateFileStorage(string root) =>
        new(
            CreateHostEnvironment(root),
            CreateStorageConfiguration(root));

    private static ViettelInvoiceIssueClient CreateIssueClient(
        RecordingHandler handler,
        RecordingLogRepository logs,
        ILogger<ViettelInvoiceIssueClient>? logger = null)
    {
        return new ViettelInvoiceIssueClient(
            new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(2)
            },
            logs,
            logger);
    }

    private static VietQrTaxCodeLookupService CreateTaxLookupClient(
        RecordingHandler handler,
        RecordingLogger<VietQrTaxCodeLookupService> logger)
    {
        return new VietQrTaxCodeLookupService(
            new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(2)
            },
            Options.Create(
                new TaxCodeLookupOptions
                {
                    Enabled = true,
                    BaseUrl = BaseUrl,
                    TimeoutSeconds = 2
                }),
            logger);
    }

    private static HttpResponseMessage Response(
        HttpStatusCode statusCode,
        string body,
        string contentType = "application/json")
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        private int _callCount;

        public RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        public int CallCount => Volatile.Read(ref _callCount);
        public List<string> RequestUris { get; } = [];
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            lock (RequestUris)
            {
                RequestUris.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
            }
            Started.TrySetResult();
            return _send(request, cancellationToken);
        }
    }

    private sealed class InterruptingInvoiceFileStorage
        : InvoiceFileStorage
    {
        public InterruptingInvoiceFileStorage(
            IHostEnvironment environment,
            IConfiguration configuration)
            : base(environment, configuration)
        {
        }

        protected override async Task WriteTemporaryFileAsync(
            string temporaryPath,
            byte[] bytes,
            CancellationToken ct)
        {
            var partialLength = Math.Max(1, bytes.Length / 2);
            await File.WriteAllBytesAsync(
                temporaryPath,
                bytes[..partialLength],
                ct);
            throw new IOException("Synthetic interrupted temporary write.");
        }
    }

    private sealed class RecordingLogRepository : IInvoiceIntegrationLogRepository
    {
        public List<InvoiceIntegrationLog> Items { get; } = [];
        public Exception? AddException { get; set; }

        public Task AddAsync(
            InvoiceIntegrationLog log,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (AddException is not null)
                return Task.FromException(AddException);

            Items.Add(log);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<List<InvoiceIntegrationLog>> GetLatestByInvoiceHeadAsync(
            int invoiceHeadId,
            int take = 20,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(List<InvoiceIntegrationLog> Items, int Total)> QueryAsync(
            InvoiceIntegrationLogQueryDto query,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<InvoiceIntegrationLog?> GetByIdAsync(
            int id,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<List<InvoiceIntegrationLog>> GetLogsForDashboardAsync(
            DateTime fromDate,
            DateTime toDate,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<InvoiceIntegrationLogCleanupResultDto> CleanupAsync(
            InvoiceIntegrationLogCleanupRequestDto request,
            DateTime nowUtc,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "GaoApp.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
