using Microsoft.Extensions.Options;

namespace GaoApp.Web.Configuration;

// Hosts are deployment-owned; the receiving StoreId is selected by a host administrator in the database.
public sealed class AcbCallbackRoutingOptions
{
    public const string SectionName = "AcbCallbackRouting";
    public List<string> Hosts { get; set; } = [];
    public bool Handles(string host) => Hosts.Any(x => string.Equals(x.Trim(), host, StringComparison.OrdinalIgnoreCase));
}

public sealed class AcbCallbackRoutingValidator : IValidateOptions<AcbCallbackRoutingOptions>
{
    public ValidateOptionsResult Validate(string? name, AcbCallbackRoutingOptions options)
    {
        if (options.Hosts == null || options.Hosts.Count > 32)
            return ValidateOptionsResult.Fail("AcbCallbackRouting:Hosts must contain at most 32 exact host names.");
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in options.Hosts)
        {
            var host = value?.Trim() ?? "";
            if (Uri.CheckHostName(host) != UriHostNameType.Dns || !host.Contains('.') ||
                host.Split('.').Any(label => label.Length is 0 or > 63 ||
                    !char.IsAsciiLetterOrDigit(label[0]) || !char.IsAsciiLetterOrDigit(label[^1]) ||
                    label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
                return ValidateOptionsResult.Fail("AcbCallbackRouting:Hosts requires exact DNS names, without a URL scheme, path, wildcard or port.");
            if (!hosts.Add(host)) return ValidateOptionsResult.Fail("AcbCallbackRouting:Hosts contains duplicate host names.");
        }
        return ValidateOptionsResult.Success;
    }
}

public static class AcbCallbackRoutingRegistration
{
    public static IServiceCollection AddAcbCallbackRouting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<AcbCallbackRoutingOptions>, AcbCallbackRoutingValidator>();
        services.AddOptions<AcbCallbackRoutingOptions>().Bind(configuration.GetSection(AcbCallbackRoutingOptions.SectionName)).ValidateOnStart();
        services.AddScoped<GaoApp.Web.Services.Acb.AcbCallbackRoutingService>();
        return services;
    }
}
