using GaoApp.Application.Common.Options;
using GaoApp.Infrastructure.Security;
using GaoApp.Infrastructure.Services.Invoices;
using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Configuration;

/// <summary>
/// Kiểm tra startup ở mức logic / runtime.
/// Nếu có lỗi sẽ throw exception để app dừng ngay.
/// </summary>
public class StartupValidationService : IStartupValidationService
{
    private readonly IOptions<ConnectionStringOptions> _connectionStringOptions;
    private readonly IOptions<AppUrlOptions> _appUrlOptions;
    private readonly IOptions<TenantOptions> _tenantOptions;
    private readonly IOptions<StorageOptions> _storageOptions;
    private readonly IOptions<InputInvoiceLibraryOptions>? _inputInvoiceLibraryOptions;
    private readonly IOptions<SeedDataOptions> _seedOptions;
    private readonly IOptions<ProxyOptions> _proxyOptions;
    private readonly IOptions<ExternalHttpResilienceOptions>
        _externalHttpResilienceOptions;
    private readonly IDataProtectionKeysPathResolver _dataProtectionKeysPathResolver;
    private readonly IDataProtectionKeysDirectoryValidator _dataProtectionKeysDirectoryValidator;
    private readonly DataProtectionKeysPathState _dataProtectionKeysPathState;
    private readonly IOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>? _keyManagementOptions;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<StartupValidationService> _logger;

    public StartupValidationService(
        IOptions<ConnectionStringOptions> connectionStringOptions,
        IOptions<AppUrlOptions> appUrlOptions,
        IOptions<TenantOptions> tenantOptions,
        IOptions<StorageOptions> storageOptions,
        IOptions<SeedDataOptions> seedOptions,
        IOptions<ProxyOptions> proxyOptions,
        IOptions<ExternalHttpResilienceOptions> externalHttpResilienceOptions,
        IDataProtectionKeysPathResolver dataProtectionKeysPathResolver,
        IDataProtectionKeysDirectoryValidator dataProtectionKeysDirectoryValidator,
        DataProtectionKeysPathState dataProtectionKeysPathState,
        IWebHostEnvironment environment,
        ILogger<StartupValidationService> logger,
        IOptions<InputInvoiceLibraryOptions>? inputInvoiceLibraryOptions = null,
        IOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>? keyManagementOptions = null)
    {
        _connectionStringOptions = connectionStringOptions;
        _appUrlOptions = appUrlOptions;
        _tenantOptions = tenantOptions;
        _storageOptions = storageOptions;
        _inputInvoiceLibraryOptions = inputInvoiceLibraryOptions;
        _seedOptions = seedOptions;
        _proxyOptions = proxyOptions;
        _externalHttpResilienceOptions = externalHttpResilienceOptions;
        _dataProtectionKeysPathResolver = dataProtectionKeysPathResolver;
        _dataProtectionKeysDirectoryValidator = dataProtectionKeysDirectoryValidator;
        _dataProtectionKeysPathState = dataProtectionKeysPathState;
        _environment = environment;
        _keyManagementOptions = keyManagementOptions;
        _logger = logger;
    }

    public Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        // Kích hoạt validator đã đăng ký trong Infrastructure trước mọi
        // phép thử có ghi hoặc thao tác migration/seed ở Development.
        _ = _externalHttpResilienceOptions.Value;

        _logger.LogInformation("Bắt đầu chạy Startup Validation...");

        ValidateConnectionString();
        ValidateAppUrl();
        ValidateTenant();
        ValidateSeedData();
        ValidateProxy();
        ValidateStorage();
        ValidateInputInvoiceLibrary();
        ValidateDataProtection();

        _logger.LogInformation("Startup Validation thành công.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Kiểm tra connection string có tồn tại và parse được hay không.
    /// Chưa bắt buộc phải mở DB connection ở đây để tránh side-effect.
    /// </summary>
    private void ValidateConnectionString()
    {
        var connectionString = _connectionStringOptions.Value.DefaultConnection;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Startup validation failed: ConnectionStrings:DefaultConnection bị thiếu hoặc rỗng.");
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);

            if (string.IsNullOrWhiteSpace(builder.DataSource))
            {
                throw new InvalidOperationException(
                    "Startup validation failed: DefaultConnection thiếu Data Source / Server.");
            }

            if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
            {
                throw new InvalidOperationException(
                    "Startup validation failed: DefaultConnection thiếu Initial Catalog / Database.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            throw new InvalidOperationException(
                "Startup validation failed: ConnectionStrings:DefaultConnection không đúng format SQL Server.",
                ex);
        }
    }

    /// <summary>
    /// Kiểm tra BaseUrl và AdminUrl là absolute URL http/https hợp lệ.
    /// </summary>
    private void ValidateAppUrl()
    {
        var options = _appUrlOptions.Value;

        ValidateHttpUrl(options.BaseUrl, "AppUrl:BaseUrl");
        ValidateHttpUrl(options.AdminUrl, "AppUrl:AdminUrl");
    }

    private static void ValidateHttpUrl(string? value, string configurationKey)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException(
                $"Startup validation failed: {configurationKey} không phải absolute URL hợp lệ.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"Startup validation failed: {configurationKey} phải dùng http hoặc https.");
        }
    }

    /// <summary>
    /// Kiểm tra cấu hình tenant cơ bản.
    /// </summary>
    private void ValidateTenant()
    {
        var options = _tenantOptions.Value;
        var rootDomain = options.RootDomain;

        if (string.IsNullOrWhiteSpace(rootDomain))
        {
            throw new InvalidOperationException(
                "Startup validation failed: Tenant:RootDomain bị thiếu.");
        }

        if (rootDomain.Contains("://", StringComparison.Ordinal) ||
            rootDomain.Contains('/') ||
            rootDomain.Contains('\\') ||
            rootDomain.Contains(':') ||
            rootDomain.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException(
                "Startup validation failed: Tenant:RootDomain phải là domain thuần, không chứa protocol, path, port hoặc khoảng trắng.");
        }

        if (!IsValidRootDomain(rootDomain))
        {
            throw new InvalidOperationException(
                "Startup validation failed: Tenant:RootDomain không phải domain hợp lệ.");
        }

        if (string.Equals(rootDomain, "localhost", StringComparison.OrdinalIgnoreCase) &&
            _environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Startup validation failed: Production không được dùng Tenant:RootDomain = localhost.");
        }

        var adminSubdomain = options.AdminSubdomain;

        if (string.IsNullOrWhiteSpace(adminSubdomain))
        {
            throw new InvalidOperationException(
                "Startup validation failed: Tenant:AdminSubdomain bị thiếu.");
        }

        if (adminSubdomain.Any(char.IsWhiteSpace) ||
            !IsValidDnsLabel(adminSubdomain))
        {
            throw new InvalidOperationException(
                "Startup validation failed: Tenant:AdminSubdomain chỉ được là một nhãn gồm chữ, số hoặc dấu gạch ngang; không được là URL hoặc full domain.");
        }
    }

    /// <summary>
    /// Kiểm tra thư mục upload.
    /// Nếu CreateIfMissing = true thì tự tạo.
    /// Nếu không tạo được hoặc không ghi được thì fail startup.
    /// </summary>
    private void ValidateStorage()
    {
        var options = _storageOptions.Value;

        if (string.IsNullOrWhiteSpace(options.UploadRoot))
        {
            throw new InvalidOperationException(
                "Startup validation failed: Storage:UploadRoot bị thiếu.");
        }

        var absolutePath = ResolvePath(options.UploadRoot, "Storage:UploadRoot");

        if (_environment.IsProduction() &&
            (!Path.IsPathFullyQualified(options.UploadRoot) ||
             IsInsideContentRoot(absolutePath)))
        {
            throw new InvalidOperationException(
                "Startup validation failed: Production phải dùng Storage:UploadRoot tuyệt đối và nằm ngoài thư mục publish.");
        }

        EnsureWritableDirectory(
            absolutePath,
            options.CreateIfMissing,
            "Storage:UploadRoot",
            logCreatedDirectory: true);
    }

    private void ValidateInputInvoiceLibrary()
    {
        if (_inputInvoiceLibraryOptions is null)
            return;

        var options = _inputInvoiceLibraryOptions.Value;
        if (!options.Enabled)
            return;
        if (string.IsNullOrWhiteSpace(options.RootPath) ||
            !Path.IsPathFullyQualified(options.RootPath))
        {
            throw new InvalidOperationException(
                "Startup validation failed: InputInvoiceLibrary:RootPath phải là đường dẫn tuyệt đối khi được bật.");
        }

        string root;
        try
        {
            root = Path.GetFullPath(options.RootPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException(
                "Startup validation failed: InputInvoiceLibrary:RootPath không hợp lệ.",
                exception);
        }

        if (!Directory.Exists(root) && options.CreateIfMissing)
        {
            // The managed XML folder follows the same creation policy as image storage.
            GaoApp.Infrastructure.Storage.UploadPathResolver.ResolveUnderRoot(
                Path.GetDirectoryName(root)!, Path.GetFileName(root));
            Directory.CreateDirectory(root);
        }
        if (!Directory.Exists(root))
            throw new InvalidOperationException(
                "Startup validation failed: InputInvoiceLibrary:RootPath không tồn tại.");
        try
        {
            if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException(
                    "Startup validation failed: InputInvoiceLibrary:RootPath không được là reparse point.");
            _ = Directory.EnumerateFileSystemEntries(root).Take(1).ToList();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Startup validation failed: Không thể đọc InputInvoiceLibrary:RootPath.",
                exception);
        }
    }

    private void ValidateDataProtection()
    {
        var absolutePath = _dataProtectionKeysPathResolver.Resolve();

        _dataProtectionKeysDirectoryValidator.Validate(absolutePath);

        // Chỉ cho phép Data Protection cấu hình key repository sau khi
        // đường dẫn đã được resolve và kiểm tra khả năng ghi thành công.
        _dataProtectionKeysPathState.Initialize(absolutePath);
        _ = _keyManagementOptions?.Value;
    }

    private void ValidateSeedData()
    {
        var options = _seedOptions.Value;

        if (options.EnableDemoSeed &&
            string.IsNullOrWhiteSpace(options.DemoUserPassword))
        {
            throw new InvalidOperationException(
                "Startup validation failed: SeedData:DemoUserPassword bắt buộc khi SeedData:EnableDemoSeed = true.");
        }
    }

    private void ValidateProxy()
    {
        var options = _proxyOptions.Value;
        ProxyTrustValidation.Validate(options);

        if (!options.EnableForwardedHeaders)
        {
            return;
        }

        var knownProxies = options.KnownProxies ?? new List<string>();
        for (var index = 0; index < knownProxies.Count; index++)
        {
            if (!IPAddress.TryParse(knownProxies[index], out _))
            {
                throw new InvalidOperationException(
                    $"Startup validation failed: Proxy:KnownProxies:{index} không phải địa chỉ IP hợp lệ.");
            }
        }

        var knownNetworks = options.KnownNetworks ?? new List<string>();
        for (var index = 0; index < knownNetworks.Count; index++)
        {
            ValidateCidr(knownNetworks[index], index);
        }
    }

    private static void ValidateCidr(string? value, int index)
    {
        var parts = value?.Split('/', StringSplitOptions.None);

        if (parts is null ||
            parts.Length != 2 ||
            string.IsNullOrWhiteSpace(parts[0]) ||
            string.IsNullOrWhiteSpace(parts[1]) ||
            !IPAddress.TryParse(parts[0], out var address) ||
            !int.TryParse(parts[1], out var prefixLength))
        {
            throw new InvalidOperationException(
                $"Startup validation failed: Proxy:KnownNetworks:{index} không đúng định dạng CIDR.");
        }

        var maximumPrefixLength = address.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => 32,
            System.Net.Sockets.AddressFamily.InterNetworkV6 => 128,
            _ => -1
        };

        if (maximumPrefixLength < 0 ||
            prefixLength < 0 ||
            prefixLength > maximumPrefixLength)
        {
            throw new InvalidOperationException(
                $"Startup validation failed: Proxy:KnownNetworks:{index} có prefix length không hợp lệ.");
        }
    }

    private static bool IsValidRootDomain(string value)
    {
        if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Length > 253 || value.StartsWith('.') || value.EndsWith('.'))
        {
            return false;
        }

        var labels = value.Split('.', StringSplitOptions.None);
        return labels.Length >= 2 && labels.All(IsValidDnsLabel);
    }

    private static bool IsValidDnsLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 63 ||
            value.StartsWith('-') ||
            value.EndsWith('-'))
        {
            return false;
        }

        return value.All(character =>
            (character >= 'a' && character <= 'z') ||
            (character >= 'A' && character <= 'Z') ||
            (character >= '0' && character <= '9') ||
            character == '-');
    }

    private string ResolvePath(string path, string configurationKey)
    {
        try
        {
            if (Path.IsPathRooted(path) &&
                !Path.IsPathFullyQualified(path))
            {
                throw new InvalidOperationException(
                    $"Startup validation failed: {configurationKey} phải là đường dẫn tương đối hoặc fully qualified.");
            }

            return Path.IsPathFullyQualified(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException(
                $"Startup validation failed: {configurationKey} không phải đường dẫn hợp lệ.",
                ex);
        }
    }

    private void EnsureWritableDirectory(
        string absolutePath,
        bool createIfMissing,
        string configurationKey,
        bool logCreatedDirectory)
    {
        string? testFile = null;

        try
        {
            if (!Directory.Exists(absolutePath))
            {
                if (!createIfMissing)
                {
                    throw new InvalidOperationException(
                        $"Startup validation failed: Thư mục cấu hình bởi {configurationKey} không tồn tại: {absolutePath}");
                }

                Directory.CreateDirectory(absolutePath);

                if (logCreatedDirectory)
                {
                    _logger.LogInformation(
                        "Đã tự tạo thư mục cho {ConfigurationKey}: {DirectoryPath}",
                        configurationKey,
                        absolutePath);
                }
            }

            testFile = Path.Combine(
                absolutePath,
                $".startup_write_test_{Guid.NewGuid():N}.tmp");

            File.WriteAllText(testFile, "startup validation");
            File.Delete(testFile);
            testFile = null;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Startup validation failed: Không thể sử dụng thư mục cấu hình bởi {configurationKey}: {absolutePath}",
                ex);
        }
        finally
        {
            if (testFile is not null)
            {
                try
                {
                    File.Delete(testFile);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(
                        cleanupException,
                        "Không thể xóa file kiểm tra startup trong thư mục cấu hình bởi {ConfigurationKey}.",
                        configurationKey);
                }
            }
        }
    }

    private bool IsInsideContentRoot(string path)
    {
        var contentRoot = Path.GetFullPath(_environment.ContentRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return candidate.StartsWith(contentRoot, comparison);
    }
}
