namespace GaoApp.Infrastructure.Data.Migrations;

public enum MigratorMode
{
    Unspecified = 0,
    SchemaOnly,
    SecuritySeed,
    Bootstrap,
    DemoSeed
}

public static class MigratorCommand
{
    public const string Usage = "Usage: dotnet GaoApp.Migrator.dll --schema-only | --security-seed | --bootstrap | --demo-seed";

    // Parse before constructing the host: unknown, combined and repeated modes never access SQL.
    public static MigratorMode Parse(IReadOnlyList<string> args)
    {
        if (args.Count != 1) throw new MigratorConfigurationException(Usage);
        return args[0] switch
        {
            "--schema-only" => MigratorMode.SchemaOnly,
            "--security-seed" => MigratorMode.SecuritySeed,
            "--bootstrap" => MigratorMode.Bootstrap,
            "--demo-seed" => MigratorMode.DemoSeed,
            _ => throw new MigratorConfigurationException(Usage)
        };
    }
}
