using GaoApp.Application.Common.Options;
using Microsoft.Extensions.Hosting;

namespace GaoApp.Infrastructure.Data.Migrations;

public enum MigratorOptionalSeedMode
{
    None = 0,
    DemoSeed = 1,
    ProductionBootstrap = 2
}

public sealed class MigratorConfigurationException : Exception
{
    public MigratorConfigurationException(string message)
        : base(message)
    {
    }
}

public sealed class MigratorConfigurationValidator
{
    public MigratorOptionalSeedMode Validate(
        SeedDataOptions seedOptions,
        ProductionBootstrapOptions bootstrapOptions,
        string environmentName)
    {
        if (!seedOptions.HasValidConfiguration())
        {
            throw new MigratorConfigurationException(
                "Startup validation failed: SeedData:DemoUserPassword is required when SeedData:EnableDemoSeed is true.");
        }

        if (!bootstrapOptions.HasValidConfiguration())
        {
            throw new MigratorConfigurationException(
                "Startup validation failed: ProductionBootstrap configuration is invalid. Check required keys, field lengths, code formats, and password strength.");
        }

        if (seedOptions.EnableDemoSeed && bootstrapOptions.Enabled)
        {
            throw new MigratorConfigurationException(
                "Startup validation failed: SeedData:EnableDemoSeed and ProductionBootstrap:Enabled cannot both be true.");
        }

        if (seedOptions.EnableDemoSeed
            && string.Equals(
                environmentName,
                Environments.Production,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new MigratorConfigurationException(
                "Startup validation failed: SeedData:EnableDemoSeed cannot be true in Production.");
        }

        if (seedOptions.EnableDemoSeed)
        {
            return MigratorOptionalSeedMode.DemoSeed;
        }

        return bootstrapOptions.Enabled
            ? MigratorOptionalSeedMode.ProductionBootstrap
            : MigratorOptionalSeedMode.None;
    }
}
