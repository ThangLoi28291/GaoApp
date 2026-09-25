using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Printing;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Dịch vụ in tem chỉ chạy trên Windows Server.");
if (args.Contains("--list-printers"))
{
    foreach (var name in WindowsLabelPrinter.Installed()) Console.WriteLine(name);
    return;
}
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Services.AddWindowsService(options => options.ServiceName = "GaoApp.LabelPrintServer");
var connection = builder.Configuration["ConnectionStrings:DefaultConnection"];
var storeId = int.TryParse(builder.Configuration["LabelPrinting:StoreId"], out int configuredStore) ? configuredStore : 0;
if (string.IsNullOrWhiteSpace(connection) || storeId <= 0)
    throw new InvalidOperationException("Cấu hình ConnectionStrings:DefaultConnection và LabelPrinting:StoreId trước khi chạy dịch vụ.");
builder.Services.AddScoped<ITenantContext>(_ => { var context = new TenantContext(); context.SetStore(storeId, "label-service"); return context; });
builder.Services.AddSingleton<ICurrentUser, PrintServiceUser>();
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connection));
builder.Services.AddSingleton<ILabelPrintTransport, WindowsLabelPrinter>();
builder.Services.AddScoped<LabelPrintDispatcher>();
builder.Services.AddHostedService<PrintWorker>();
await builder.Build().RunAsync();

sealed class PrintWorker(IServiceScopeFactory scopes, ILogger<PrintWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ids = await db.Set<ProductLabelPrinter>().AsNoTracking().Where(x => x.StoreId == db.CurrentStoreId && x.Enabled).Select(x => x.Id).ToArrayAsync(stoppingToken);
                foreach (int id in ids)
                {
                    using var printerScope = scopes.CreateScope();
                    await printerScope.ServiceProvider.GetRequiredService<LabelPrintDispatcher>().DispatchAsync(id, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Không xử lý được hàng đợi in tem. Lệnh chưa xác định kết quả sẽ không tự gửi lại."); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
sealed class PrintServiceUser : ICurrentUser
{
    public int? UserId => null;
    public string? UserName => "GaoApp Label Print Service";
    public int? TerminalId => null;
    public string? TerminalCode => null;
    public bool IsAuthenticated => false;
}
