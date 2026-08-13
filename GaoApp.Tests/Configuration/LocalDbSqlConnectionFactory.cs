using Microsoft.Data.SqlClient;

namespace GaoApp.Tests.Configuration;

internal static class LocalDbSqlConnectionFactory
{
    private const int LoginTimeoutErrorNumber = -2;

    static LocalDbSqlConnectionFactory()
        => AppContext.SetSwitch(
            "Switch.Microsoft.Data.SqlClient.EnableRetryLogic",
            true);

    public static SqlConnection Create(string connectionString)
        => new(connectionString)
        {
            RetryLogicProvider = CreateRetryProvider()
        };

    private static SqlRetryLogicBaseProvider CreateRetryProvider()
    {
        return SqlConfigurableRetryFactory.CreateFixedRetryProvider(
            new SqlRetryLogicOption
            {
                NumberOfTries = 2,
                DeltaTime = TimeSpan.FromSeconds(1),
                TransientErrors = [LoginTimeoutErrorNumber]
            });
    }
}
