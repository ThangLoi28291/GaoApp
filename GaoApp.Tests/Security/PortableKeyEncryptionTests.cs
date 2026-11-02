using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GaoApp.Infrastructure.Security;
using GaoApp.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GaoApp.Infrastructure.Services.Invoices;

namespace GaoApp.Tests.Security;

public sealed class PortableKeyEncryptionTests
{
    [Fact]
    public void Worker_using_Web_PFX_can_read_existing_invoice_password_without_reentering_or_replacing_keys()
    {
        using var files = new HostFiles();
        var keysPath = Path.Combine(files.Uploads, "shared-keys");
        Directory.CreateDirectory(keysPath);
        var webPfx = MakeCertificate("web");
        var wrongPfx = MakeCertificate("wrong-worker");
        const string password = "synthetic-provider-password";
        string ciphertext;
        using (var web = BuildHost(webPfx, ownsKeys: true))
            ciphertext = new InvoiceProviderCredentialProtector(web.GetRequiredService<IDataProtectionProvider>()).Protect(password);
        var keyPath = Directory.GetFiles(keysPath, "key-*.xml").Single();
        var originalKey = File.ReadAllBytes(keyPath);

        using (var wrongWorker = BuildHost(wrongPfx, ownsKeys: false))
        {
            var protector = new InvoiceProviderCredentialProtector(wrongWorker.GetRequiredService<IDataProtectionProvider>());
            Assert.Throws<CryptographicException>(() => protector.Unprotect(ciphertext));
        }
        using (var repairedWorker = BuildHost(webPfx, ownsKeys: false))
        {
            var protector = new InvoiceProviderCredentialProtector(repairedWorker.GetRequiredService<IDataProtectionProvider>());
            Assert.Equal(password, protector.Unprotect(ciphertext));
        }
        Assert.Single(Directory.GetFiles(keysPath, "key-*.xml"));
        Assert.Equal(originalKey, File.ReadAllBytes(keyPath));

        string MakeCertificate(string name)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=GaoApp test " + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            var path = Path.Combine(files.Uploads, name + ".pfx");
            File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, "test-only-password"));
            return path;
        }
        ServiceProvider BuildHost(string pfx, bool ownsKeys)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:KeysPath"] = keysPath,
                ["DataProtection:CertificatePath"] = pfx,
                ["DataProtection:CertificatePassword"] = "test-only-password",
                ["DataProtection:RequirePortableKeys"] = "true"
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging(b => b.ClearProviders());
            services.AddSingleton<IConfiguration>(config);
            services.AddSingleton<IHostEnvironment>(files.Environment);
            services.AddInfrastructure(config, files.Environment);
            if (!ownsKeys) services.AddDataProtection().DisableAutomaticKeyGeneration();
            var provider = services.BuildServiceProvider();
            provider.GetRequiredService<DataProtectionKeysPathState>().Initialize(keysPath);
            return provider;
        }
    }

    [Fact]
    public void Separate_hosts_can_decrypt_certificate_protected_keys_without_machine_certificate_store()
    {
        using var files = new HostFiles();
        var certificatePath = Path.Combine(files.Uploads, "test.pfx");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=GaoApp test only", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(certificatePath, cert.Export(X509ContentType.Pfx, "test-only-password"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DataProtection:CertificatePath"] = certificatePath, ["DataProtection:CertificatePassword"] = "test-only-password", ["DataProtection:RequirePortableKeys"] = "true" }).Build();
        var keysPath = Path.Combine(files.Uploads, "keys"); Directory.CreateDirectory(keysPath);
        config["DataProtection:KeysPath"] = keysPath;
        string protectedValue;
        using (var first = BuildHost())
            protectedValue = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("test-credential").Protect("synthetic-credential");
        using (var second = BuildHost())
            Assert.Equal("synthetic-credential", second.GetRequiredService<IDataProtectionProvider>().CreateProtector("test-credential").Unprotect(protectedValue));
        var xml = File.ReadAllText(Directory.GetFiles(keysPath, "key-*.xml").Single());
        Assert.Contains("encryptedSecret", xml); Assert.Contains("EncryptedData", xml);
        Assert.DoesNotContain("<value>", xml);

        ServiceProvider BuildHost()
        {
            var services = new ServiceCollection(); services.AddLogging(b => b.ClearProviders());
            services.AddSingleton<IConfiguration>(config); services.AddSingleton<IHostEnvironment>(files.Environment);
            services.AddInfrastructure(config, files.Environment);
            var provider = services.BuildServiceProvider();
            provider.GetRequiredService<DataProtectionKeysPathState>().Initialize(keysPath);
            return provider;
        }
    }

    [Fact]
    public void Portable_mode_requires_certificate_and_production_rejects_unencrypted_keys()
    {
        using var files = new HostFiles();
        var missing = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:RequirePortableKeys"] = "true" }).Build();
        Assert.Throws<InvalidOperationException>(() => new HostProtectionCertificate(missing, files.Environment));
        using var noCert = new HostProtectionCertificate(new ConfigurationBuilder().Build(), files.Environment);
        var validation = new HostDataProtectionEncryption(noCert, files.Environment);
        Assert.Throws<InvalidOperationException>(() => validation.Configure(new KeyManagementOptions()));
    }
}
