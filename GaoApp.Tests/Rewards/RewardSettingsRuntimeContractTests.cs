using System.Text.RegularExpressions;
using Xunit;

namespace GaoApp.Tests.Rewards;

public sealed class RewardSettingsRuntimeContractTests
{
    private static readonly string RepositoryRoot =
        FindRepositoryRoot();

    [Fact]
    public void Repository_current_settings_should_not_treat_disabled_as_missing()
    {
        var source =
            Read(
                "GaoApp.Infrastructure",
                "Repositories",
                "Rewards",
                "RewardSettingsRepository.cs");

        Assert.Contains(
            "FirstOrDefaultAsync(ct)",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "FirstOrDefaultAsync(x => x.IsEnabled",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Customer_reward_balance_and_redeem_should_not_block_disabled_settings()
    {
        var source =
            Read(
                "GaoApp.Application",
                "Services",
                "Rewards",
                "CustomerRewardService.cs");

        Assert.Contains(
            "IsProgramEnabledAsync",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Chức năng tích điểm đang tắt.",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void POS_should_gate_new_reward_earning_by_program_enabled()
    {
        var source =
            Read(
                "GaoApp.Application",
                "Services",
                "Orders",
                "POSService.cs");

        var normalized =
            Regex.Replace(
                source,
                @"\s+",
                " ");

        Assert.Contains(
            "if (!await _customerRewardService.IsProgramEnabledAsync(ct))",
            normalized,
            StringComparison.Ordinal);

        Assert.Contains(
            "CustomerRewardLedgerType.SaleEarned",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Reward_settings_admin_service_should_not_mutate_existing_vouchers()
    {
        var source =
            Read(
                "GaoApp.Application",
                "Services",
                "Rewards",
                "RewardSettingsAdminService.cs");

        Assert.Contains(
            "RewardSettings",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "CustomerRewardVoucher",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "CustomerRewardLedger",
            source,
            StringComparison.Ordinal);
    }

    private static string Read(
        params string[] parts)
        => File.ReadAllText(
            Path.Combine(
                new[] { RepositoryRoot }
                    .Concat(parts)
                    .ToArray()));

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        })
        {
            var directory =
                new DirectoryInfo(start);

            while (directory is not null)
            {
                if (
                    File.Exists(
                        Path.Combine(
                            directory.FullName,
                            "GaoApp.sln"))
                )
                {
                    return directory.FullName;
                }

                directory =
                    directory.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not locate GaoApp repository root.");
    }
}
