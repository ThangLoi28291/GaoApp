using GaoApp.Application.Common.Options;
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
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<StartupValidationService> _logger;

    public StartupValidationService(
        IOptions<ConnectionStringOptions> connectionStringOptions,
        IOptions<AppUrlOptions> appUrlOptions,
        IOptions<TenantOptions> tenantOptions,
        IOptions<StorageOptions> storageOptions,
        IWebHostEnvironment environment,
        ILogger<StartupValidationService> logger)
    {
        _connectionStringOptions = connectionStringOptions;
        _appUrlOptions = appUrlOptions;
        _tenantOptions = tenantOptions;
        _storageOptions = storageOptions;
        _environment = environment;
        _logger = logger;
    }

    public Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Bắt đầu chạy Startup Validation...");

        ValidateConnectionString();
        ValidateAppUrl();
        ValidateTenant();
        ValidateStorage();

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
    /// Kiểm tra BaseUrl là absolute URL hợp lệ.
    /// </summary>
    private void ValidateAppUrl()
    {
        var options = _appUrlOptions.Value;

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException(
                "Startup validation failed: AppUrl:BaseUrl không hợp lệ.");
        }

        if (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Startup validation failed: AppUrl:BaseUrl phải dùng http hoặc https.");
        }
    }

    /// <summary>
    /// Kiểm tra cấu hình tenant cơ bản.
    /// </summary>
    private void ValidateTenant()
    {
        var options = _tenantOptions.Value;

        if (string.IsNullOrWhiteSpace(options.RootDomain))
        {
            throw new InvalidOperationException(
                "Startup validation failed: Tenant:RootDomain bị thiếu.");
        }

        if (options.RootDomain.Contains("://"))
        {
            throw new InvalidOperationException(
                "Startup validation failed: Tenant:RootDomain chỉ được chứa domain thuần, không gồm protocol.");
        }

        if (options.RootDomain.Contains("/"))
        {
            throw new InvalidOperationException(
                "Startup validation failed: Tenant:RootDomain không được chứa ký tự '/'.");
        }

        if (string.Equals(options.RootDomain, "localhost", StringComparison.OrdinalIgnoreCase)
            && _environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Startup validation failed: Production không được dùng Tenant:RootDomain = localhost.");
        }
    }

    /// <summary>
    /// Kiểm tra thư mục upload.
    /// Nếu CreateIfMissing = true thì tự tạo.
    /// Nếu không tạo được thì fail startup.
    /// </summary>
    private void ValidateStorage()
    {
        var options = _storageOptions.Value;

        if (string.IsNullOrWhiteSpace(options.UploadRoot))
        {
            throw new InvalidOperationException(
                "Startup validation failed: Storage:UploadRoot bị thiếu.");
        }

        var absolutePath = Path.IsPathRooted(options.UploadRoot)
            ? options.UploadRoot
            : Path.Combine(_environment.ContentRootPath, options.UploadRoot);

        if (!Directory.Exists(absolutePath))
        {
            if (!options.CreateIfMissing)
            {
                throw new InvalidOperationException(
                    $"Startup validation failed: Thư mục upload không tồn tại: {absolutePath}");
            }

            Directory.CreateDirectory(absolutePath);
            _logger.LogInformation("Đã tự tạo thư mục upload: {UploadPath}", absolutePath);
        }

        // Test quyền ghi cơ bản bằng cách tạo file tạm rồi xóa.
        var testFile = Path.Combine(absolutePath, $".startup_write_test_{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(testFile, "startup validation");
            File.Delete(testFile);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Startup validation failed: Không có quyền ghi vào thư mục upload: {absolutePath}",
                ex);
        }
    }
}