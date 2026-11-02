using System.Security.Cryptography;

namespace GaoApp.Web.Services.Assistant;

internal static class AssistantSigningKey
{
    public static RSA Load(AssistantOptions options, IWebHostEnvironment environment)
    {
        var pem = options.ManagedPrivateKeyPem;
        if (pem is null)
        {
            var path = Path.GetFullPath(options.PrivateKeyFile, environment.ContentRootPath);
            if (!File.Exists(path) || new FileInfo(path).Length > 16384) throw new CryptographicException();
            pem = File.ReadAllText(path);
        }
        if (pem.Length > 16384) throw new CryptographicException();
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            if (rsa.KeySize < 2048 || rsa.ExportParameters(true).D is null) throw new CryptographicException();
            return rsa;
        }
        catch { rsa.Dispose(); throw; }
    }
}
