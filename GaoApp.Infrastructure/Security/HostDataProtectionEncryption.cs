using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaoApp.Infrastructure.Security;

/// <summary>Owns the PFX for the host lifetime; no certificate-store writes or secret logging.</summary>
public sealed class HostProtectionCertificate : IDisposable
{
    public X509Certificate2? Certificate { get; }

    public HostProtectionCertificate(IConfiguration config, IHostEnvironment environment)
    {
        var path = config["DataProtection:CertificatePath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            if (config.GetValue<bool>("DataProtection:RequirePortableKeys"))
                throw new InvalidOperationException("DataProtection:CertificatePath is required when RequirePortableKeys is enabled.");
            return;
        }
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("DataProtection:CertificatePath must be an absolute path.");
        if (!environment.IsDevelopment())
            _ = DataProtectionKeysPath.Resolve(path, environment.ContentRootPath, AppContext.BaseDirectory, isProduction: true);
        var certificate = new X509Certificate2(path, config["DataProtection:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
        using var rsa = certificate.GetRSAPrivateKey();
        if (!certificate.HasPrivateKey || rsa is null || rsa.KeySize < 2048 ||
            certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
        {
            certificate.Dispose();
            throw new InvalidOperationException("Data Protection requires a valid, unexpired certificate with an RSA private key of at least 2048 bits.");
        }
        Certificate = certificate;
    }

    public void Dispose() => Certificate?.Dispose();
}

public sealed class HostDataProtectionEncryption(
    HostProtectionCertificate certificate, IHostEnvironment environment)
    : IConfigureOptions<KeyManagementOptions>
{
    public void Configure(KeyManagementOptions options)
    {
        // Resolving the owner also validates required portable mode and controls certificate lifetime.
        _ = certificate.Certificate;
        if (!environment.IsDevelopment() && options.XmlEncryptor is null)
            throw new InvalidOperationException("Data Protection keys must be encrypted on this host. Configure DataProtection:CertificatePath and CertificatePassword.");
    }
}

public static class HostDataProtectionEncryptionExtensions
{
    public static IDataProtectionBuilder AddHostKeyEncryption(this IDataProtectionBuilder builder,
        IConfiguration config, IHostEnvironment? environment)
    {
        if (!string.IsNullOrWhiteSpace(config["DataProtection:CertificatePath"]))
        {
            var owner = new HostProtectionCertificate(config, environment ??
                throw new InvalidOperationException("Host environment is required for certificate configuration."));
            builder.Services.AddSingleton<HostProtectionCertificate>(_ => owner);
            // The public API registers BOTH encryption and certificate decryption options.
            builder.ProtectKeysWithCertificate(owner.Certificate!);
        }
        else builder.Services.AddSingleton<HostProtectionCertificate>();
        builder.Services.ConfigureOptions<HostDataProtectionEncryption>();
        return builder;
    }
}
