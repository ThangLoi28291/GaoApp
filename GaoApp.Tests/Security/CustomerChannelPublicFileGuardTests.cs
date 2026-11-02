using GaoApp.Web.Services.CustomerService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace GaoApp.Tests.Security;

public sealed class CustomerChannelPublicFileGuardTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GaoChannelPublicGuardTests", Guid.NewGuid().ToString("N"));
    private string Web => Path.Combine(root, "wwwroot");
    private AssistantIntegrationTests.EnvironmentStub Environment() => new(root) { WebRootPath = Web };
    private static IConfiguration Configuration(bool enabled, string? credential = null) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["CustomerChannels:Enabled"] = enabled.ToString(),
        ["CustomerChannels:Connections:0:StoreId"] = "legacy-invalid-store",
        ["CustomerChannels:Connections:0:AccessTokenFile"] = credential
    }).Build();

    public CustomerChannelPublicFileGuardTests() => Directory.CreateDirectory(Web);

    [Theory]
    [InlineData(true, @"C:\private\unusual-credential.png", "/uploads/products/unusual-credential.png")]
    [InlineData(false, "legacy/unusual-credential.png", "/images/UNUSUAL-CREDENTIAL.PNG")]
    [InlineData(false, "legacy/token.txt", "/%74oken.txt")]
    public void Configured_credential_names_are_blocked_in_any_public_folder_without_binding_options(bool enabled, string credential, string request)
    {
        Assert.True(CustomerChannelPublicFileGuard.Blocked(request, Environment(), Configuration(enabled, credential)));
    }

    [Theory]
    [InlineData("/ChannelCredentials/token.txt")]
    [InlineData("/images/channelcredentials/token.png")]
    public void Reserved_directory_is_blocked_without_any_channel_configuration(string request)
    {
        Assert.True(CustomerChannelPublicFileGuard.Blocked(request, Environment(), Configuration(false)));
    }

    [Fact]
    public void Disabled_legacy_configuration_allows_normal_static_files_and_application_routes()
    {
        var configuration = Configuration(false, "legacy/private-token.txt");
        foreach (var path in new[] { "/", "/admin/customer-service/channels", "/css/site.css", "/uploads/products/product.png" })
            Assert.False(CustomerChannelPublicFileGuard.Blocked(path, Environment(), configuration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disabled_channels_block_aliases_with_different_filenames_from_webroot_or_static_provider(bool separateProvider)
    {
        var providerRoot = separateProvider ? Path.Combine(root, "provider") : Web;
        var target = Path.Combine(root, "private");
        Directory.CreateDirectory(providerRoot); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "renamed.png"), "SYNTHETIC_PRIVATE_FILE");
        var alias = Path.Combine(providerRoot, "alias");
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{alias}\" \"{target}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(alias, target);
        try
        {
            using var provider = new PhysicalFileProvider(providerRoot);
            var environment = Environment(); environment.WebRootFileProvider = provider;
            Assert.True(CustomerChannelPublicFileGuard.Blocked("/alias/renamed.png", environment, Configuration(false, Path.Combine(target, "configured-token.txt"))));
            Assert.False(CustomerChannelPublicFileGuard.Blocked("/css/site.css", environment, Configuration(false, Path.Combine(target, "configured-token.txt"))));
        }
        finally { Directory.Delete(alias); }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
