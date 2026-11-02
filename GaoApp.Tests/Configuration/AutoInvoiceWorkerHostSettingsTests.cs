using GaoApp.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace GaoApp.Tests.Configuration;

public sealed class AutoInvoiceWorkerHostSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GaoApp-worker-host-" + Guid.NewGuid().ToString("N"));
    private string App => Path.Combine(_root, "release");
    private string HostDirectory => Path.Combine(_root, "private");

    public AutoInvoiceWorkerHostSettingsTests()
    {
        Directory.CreateDirectory(App);
        Directory.CreateDirectory(HostDirectory);
        File.WriteAllText(Path.Combine(App, "appsettings.json"), """
            {"ConnectionStrings":{"DefaultConnection":"bundled-default"},"DataProtection":{"KeysPath":"bundled-keys"}}
            """);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Debug_and_published_hosts_load_private_machine_settings_over_bundled_defaults(string environment)
    {
        var path = Path.Combine(HostDirectory, $"worker.appsettings.{environment}.json");
        File.WriteAllText(path, """
            {"ConnectionStrings":{"DefaultConnection":"machine-db"},"DataProtection":{"KeysPath":"machine-keys"}}
            """);
        using var config = Defaults();
        Assert.Equal(path, AutoInvoiceWorkerHostSettings.Load(config, environment, App, HostDirectory));
        Assert.Equal("machine-db", config["ConnectionStrings:DefaultConnection"]);
        Assert.Equal("machine-keys", config["DataProtection:KeysPath"]);
        // Publishing a new default cannot replace the private machine file.
        File.WriteAllText(Path.Combine(App, "appsettings.json"), "{}");
        using var nextRelease = Defaults();
        AutoInvoiceWorkerHostSettings.Load(nextRelease, environment, App, HostDirectory);
        Assert.Equal("machine-db", nextRelease["ConnectionStrings:DefaultConnection"]);
    }

    [Fact]
    public void Real_generic_host_startup_arguments_do_not_allow_bundled_json_to_override_private_settings()
    {
        var path = WriteHostFile();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = App,
            Args = new[] { "--environment", "Production", "--host-settings", path }
        });
        using var config = builder.Configuration;
        AutoInvoiceWorkerHostSettings.Load(config, builder.Environment.EnvironmentName, App, HostDirectory);
        Assert.Equal("Production", builder.Environment.EnvironmentName);
        Assert.Equal("host-db", config["ConnectionStrings:DefaultConnection"]);
        Assert.Equal("host-keys", config["DataProtection:KeysPath"]);
    }

    [Fact]
    public void Environment_and_command_line_keep_precedence_over_host_file()
    {
        var hostPath = WriteHostFile();
        var envPrefix = "GAOAPP_WORKER_TEST_" + Guid.NewGuid().ToString("N") + "_";
        var envKey = envPrefix + "DataProtection__KeysPath";
        Environment.SetEnvironmentVariable(envKey, "environment-keys");
        try
        {
            using var config = Defaults();
            // A prefixed source is used to avoid changing the developer's real variables.
            config.AddEnvironmentVariables(envPrefix);
            config.AddCommandLine(new[] { "--host-settings", hostPath, "--ConnectionStrings:DefaultConnection", "command-db" });
            AutoInvoiceWorkerHostSettings.Load(config, "Production", App, HostDirectory);
            Assert.Equal("environment-keys", config["DataProtection:KeysPath"]);
            Assert.Equal("command-db", config["ConnectionStrings:DefaultConnection"]);
        }
        finally { Environment.SetEnvironmentVariable(envKey, null); }
    }

    [Fact]
    public void Explicit_missing_file_fails_instead_of_silently_using_another_database_or_key_ring()
    {
        using var config = Defaults();
        config.AddCommandLine(new[] { "--host-settings", Path.Combine(HostDirectory, "missing.json") });
        Assert.Throws<FileNotFoundException>(() => AutoInvoiceWorkerHostSettings.Load(config, "Development", App, HostDirectory));
    }

    [Fact]
    public void Missing_default_file_preserves_existing_development_configuration()
    {
        using var config = Defaults();
        Assert.Null(AutoInvoiceWorkerHostSettings.Load(config, "Development", App, HostDirectory));
        Assert.Equal("bundled-default", config["ConnectionStrings:DefaultConnection"]);
    }

    [Theory]
    [InlineData("relative.json")]
    [InlineData("inside-release")]
    public void Host_file_cannot_depend_on_working_directory_or_be_overwritten_by_publish(string candidate)
    {
        using var config = Defaults();
        var path = candidate == "inside-release" ? Path.Combine(App, "private.json") : candidate;
        config.AddCommandLine(new[] { "--host-settings", path });
        Assert.Throws<InvalidOperationException>(() => AutoInvoiceWorkerHostSettings.Load(config, "Development", App, HostDirectory));
    }

    private ConfigurationManager Defaults()
    {
        var config = new ConfigurationManager();
        config.AddJsonFile(Path.Combine(App, "appsettings.json"), optional: false, reloadOnChange: false);
        config.AddEnvironmentVariables();
        return config;
    }

    private string WriteHostFile()
    {
        var path = Path.Combine(HostDirectory, "private.json");
        File.WriteAllText(path, """
            {"ConnectionStrings":{"DefaultConnection":"host-db"},"DataProtection":{"KeysPath":"host-keys"}}
            """);
        return path;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
