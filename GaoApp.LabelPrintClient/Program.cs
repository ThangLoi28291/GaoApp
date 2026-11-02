using GaoApp.LabelPrintClient;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Bộ kết nối in tem client cần Windows.");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
var options = builder.Configuration.GetSection("LabelClient").Get<LabelClientOptions>() ?? new();
await LabelClientHost.Build(builder, options).RunAsync();
