using GaoApp.Application.Common.Options;
using GaoApp.Web.Configuration;
using GaoApp.Web.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Configuration;

public sealed class InputInvoiceLibraryConfigurationTests
{
    [Fact]
    public void Safety_defaults_should_disable_library_and_bound_browse()
    {
        var options = new InputInvoiceLibraryOptions();

        Assert.False(options.Enabled);
        Assert.Equal("XML", options.RootPath);
        Assert.InRange(options.MaxMonthPartitions, 1, 12);
        Assert.InRange(options.MaxCandidates, 1, 200);
    }

    [Fact]
    public void Committed_configuration_should_not_hard_code_an_invoice_root()
    {
        var root = FindRepositoryRoot();
        var json = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "appsettings.json"));

        Assert.Contains("\"InputInvoiceLibrary\"", json, StringComparison.Ordinal);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("InputInvoiceLibrary").GetProperty("Enabled").GetBoolean());
        Assert.Contains("\"RootPath\": \"XML\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("XML", true)]
    [InlineData("", true)]
    [InlineData("xml", false)]
    public void Managed_library_follows_image_root_and_creation_policy(string configuredRoot, bool create)
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "gaoapp-library-options");
        using var provider = Provider(contentRoot, "external/uploads", configuredRoot, create);
        var options = provider.GetRequiredService<IOptions<InputInvoiceLibraryOptions>>().Value;
        Assert.Equal(Path.GetFullPath(Path.Combine(contentRoot, "external/uploads/XML")), options.RootPath);
        Assert.Equal(create, options.CreateIfMissing);
    }

    [Fact]
    public void Absolute_external_library_is_preserved_for_existing_deployments()
    {
        var external = Path.Combine(Path.GetTempPath(), "existing-invoices");
        using var provider = Provider(Path.GetTempPath(), "uploads", external, true);
        var options = provider.GetRequiredService<IOptions<InputInvoiceLibraryOptions>>().Value;
        Assert.Equal(external, options.RootPath);
        Assert.False(options.CreateIfMissing);
    }

    [Theory]
    [InlineData("uploads/XML/2026/0312770607/09/invoice.pdf")]
    [InlineData("uploads/XML/2026/0312770607/09/invoice.xml")]
    [InlineData("uploads/XML/2026/0312770607/09/preview.png")]
    public void Input_invoice_documents_are_not_public_image_uploads(string path)
        => Assert.False(PublicUploadExtensions.IsPublicMedia(path));

    private static ServiceProvider Provider(string contentRoot, string uploads, string library, bool create)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:UploadRoot"] = uploads,
            ["Storage:CreateIfMissing"] = create.ToString(),
            ["InputInvoiceLibrary:Enabled"] = "true",
            ["InputInvoiceLibrary:RootPath"] = library
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment { ContentRootPath = contentRoot });
        services.AddWebAndAppOptions(config);
        return services.BuildServiceProvider();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "GaoApp.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
