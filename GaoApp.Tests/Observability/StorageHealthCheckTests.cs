using GaoApp.Application.Common.Options;
using GaoApp.Web.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Observability;

public sealed class StorageHealthCheckTests
{
    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var root = Path.Combine(
            Path.GetTempPath(),
            $"gaoapp-r1.6-storage-{Guid.NewGuid():N}");
        var check = CreateCheck(root);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => check.CheckHealthAsync(
                new HealthCheckContext(),
                cts.Token));

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Healthy_result_does_not_expose_internal_path()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"gaoapp-r1.6-storage-{Guid.NewGuid():N}");

        try
        {
            var result = await CreateCheck(root).CheckHealthAsync(
                new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Equal("Storage is writable.", result.Description);
            Assert.DoesNotContain(root, result.Description, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(root, ".healthcheck_*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static StorageHealthCheck CreateCheck(string root)
        => new(
            new FakeWebHostEnvironment(),
            Options.Create(new StorageOptions
            {
                UploadRoot = root,
                CreateIfMissing = true
            }),
            new GlobalExceptionMiddlewareTests.RecordingLogger<StorageHealthCheck>());

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "GaoApp.Tests";
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
