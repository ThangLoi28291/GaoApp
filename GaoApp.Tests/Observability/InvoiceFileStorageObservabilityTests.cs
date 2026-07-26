using System.Text;
using GaoApp.Infrastructure.Services.Invoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Observability;

public sealed class InvoiceFileStorageObservabilityTests
{
    [Fact]
    public async Task Cleanup_failure_is_safe_warning_and_does_not_replace_primary_failure()
    {
        const string primaryDetail = "synthetic-primary-write-detail";
        const string cleanupDetail = "synthetic-cleanup-secret-detail";
        var root = Path.Combine(
            Path.GetTempPath(),
            $"gaoapp-r1.6-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var logger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<InvoiceFileStorage>();
        var storage = new CleanupFailingStorage(
            new FakeHostEnvironment
            {
                ContentRootPath = root
            },
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Storage:UploadRoot"] = root
                    })
                .Build(),
            logger,
            primaryDetail,
            cleanupDetail);

        try
        {
            var failure = await Assert.ThrowsAsync<IOException>(
                () => storage.SaveAsync(
                    "invoices/test",
                    "invoice.pdf",
                    Encoding.UTF8.GetBytes("%PDF-synthetic"),
                    default));

            Assert.Equal(primaryDetail, failure.Message);
            var warning = Assert.Single(
                logger.Entries,
                entry => entry.Level == LogLevel.Warning);
            Assert.Contains(
                nameof(UnauthorizedAccessException),
                warning.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                cleanupDetail,
                warning.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                primaryDetail,
                warning.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                logger.Entries,
                entry => entry.Level == LogLevel.Error);
            Assert.False(File.Exists(Path.Combine(
                root,
                "invoices",
                "test",
                "invoice.pdf")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class CleanupFailingStorage(
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<InvoiceFileStorage> logger,
        string primaryDetail,
        string cleanupDetail)
        : InvoiceFileStorage(environment, configuration, logger)
    {
        protected override async Task WriteTemporaryFileAsync(
            string temporaryPath,
            byte[] bytes,
            CancellationToken ct)
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                bytes[..Math.Max(1, bytes.Length / 2)],
                ct);
            throw new IOException(primaryDetail);
        }

        protected override void DeleteTemporaryFile(string temporaryPath) =>
            throw new UnauthorizedAccessException(cleanupDetail);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "GaoApp.Tests";
        public string ContentRootPath { get; set; } =
            Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
