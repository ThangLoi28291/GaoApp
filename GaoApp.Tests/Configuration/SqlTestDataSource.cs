namespace GaoApp.Tests.Configuration;

internal static class SqlTestDataSource
{
    internal const string EnvironmentVariable = "GAOAPP_TEST_SQL_SERVER";

    internal static string Current => ExplicitLocalServer() ?? @"(localdb)\MSSQLLocalDB";

    internal static string? ExplicitLocalServer()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable)?.Trim();
        if (string.IsNullOrEmpty(configured)) return null;
        // This override only selects a local test instance. Database names remain fixture-generated GUIDs.
        var host = configured.Split('\\', ',')[0];
        if (!new[] { ".", "localhost", "127.0.0.1", "(localdb)", Environment.MachineName }
                .Contains(host, StringComparer.OrdinalIgnoreCase) || configured.Contains(';'))
            throw new InvalidOperationException($"{EnvironmentVariable} must identify a local SQL Server instance.");
        return configured;
    }
}
