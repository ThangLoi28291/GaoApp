namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class EmployeeMobileHomeAcceptanceTests
{
    [Fact]
    public async Task Phone_employee_home_shows_owned_shift_and_configured_tasks_while_dock_desktop_and_store_data_stay_unchanged()
    {
        var root = FullApplicationFixture.SourceRoot();
        var baseline = Environment.GetEnvironmentVariable("GAO_EMPLOYEE_MOBILE_HOME_BASELINE")
            ?? Path.Combine(root, "publish", "releases", "20261029-064334-e9076a04");
        var candidate = Environment.GetEnvironmentVariable("GAO_EMPLOYEE_MOBILE_HOME_RELEASE")
            ?? throw new InvalidOperationException("GAO_EMPLOYEE_MOBILE_HOME_RELEASE must select a current WebOnly package.");
        await EmployeeHomeAcceptanceTests.RunAsync(root, baseline, candidate, "employee-mobile-home.browser.cjs", "employee-phone-home-20261029");
    }
}
