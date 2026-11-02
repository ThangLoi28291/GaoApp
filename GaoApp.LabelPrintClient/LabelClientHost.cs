using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Infrastructure.Printing;

namespace GaoApp.LabelPrintClient;

public sealed record LabelClientOptions
{
    public int Port { get; init; } = 17881;
    public string[] AllowedOrigins { get; init; } = [];
    public string PairingToken { get; init; } = "";
    public string StateDirectory { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GaoApp", "LabelPrintClient");
}
public sealed record ClientPrintRequest(Guid RequestId, LabelPrintPayload Payload);
public sealed record ClientPreviewRequest(ProductLabelDesign Design, LabelProduct Product, LabelPrinterSnapshot Printer);

[SupportedOSPlatform("windows")]
public static class LabelClientHost
{
    public const string Protocol = "gao-label-client-v1";
    public static WebApplication Build(WebApplicationBuilder builder, LabelClientOptions options,
        ILabelPrintTransport? transport = null, Func<IReadOnlyList<string>>? installedPrinters = null,
        IA4LabelPrintTransport? a4Transport = null)
    {
        if (options.Port is < 0 or > 65535 || options.PairingToken.Length < 32 || options.AllowedOrigins.Length == 0 ||
            options.AllowedOrigins.Any(x => !Uri.TryCreate(x, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != x ||
                uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new InvalidOperationException("Cấu hình địa chỉ GaoApp và mã kết nối bằng Configure-Client.ps1 trước khi mở bộ kết nối.");
        builder.WebHost.ConfigureKestrel(k => { k.Listen(IPAddress.Loopback, options.Port); k.Limits.MaxRequestBodySize = 2 * 1024 * 1024; });
        builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy.WithOrigins(options.AllowedOrigins)
            .WithMethods("GET", "POST").WithHeaders("Content-Type", "Authorization")));
        builder.Services.AddSingleton(transport ?? new WindowsLabelPrinter());
        builder.Services.AddSingleton<IA4LabelPrintTransport>(a4Transport ?? new WindowsA4LabelPrinter());
        installedPrinters ??= WindowsLabelPrinter.Installed;
        builder.Services.AddSingleton(new ClientPrintJournal(options.StateDirectory));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) ||
                context.Request.Host.Host is not ("127.0.0.1" or "localhost") ||
                !options.AllowedOrigins.Contains(context.Request.Headers.Origin.ToString(), StringComparer.Ordinal))
            { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (context.Request.Method == "OPTIONS" && context.Request.Headers["Access-Control-Request-Private-Network"] == "true")
                context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
            await next();
        });
        app.UseCors();
        app.Use(async (context, next) =>
        {
            var supplied = Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString());
            var expected = Encoding.UTF8.GetBytes("Bearer " + options.PairingToken);
            if (!CryptographicOperations.FixedTimeEquals(supplied, expected))
            { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
            try { await next(); }
            catch (ValidationAppException ex) { await Failure(context, 400, ex.Message); }
            catch (ConflictAppException ex) { await Failure(context, 409, ex.Message); }
            catch (Exception) { await Failure(context, 500, "Chưa xác nhận được kết quả in. Kiểm tra giấy và hàng đợi máy in trước khi in lại."); }
        });
        app.MapGet("/health", () => Results.Ok(new { ok = true, protocol = Protocol, features = new[] { "promotion-a4-v1" } }));
        app.MapGet("/printers", () => Results.Ok(new { ok = true, protocol = Protocol, printers = installedPrinters() }));
        app.MapGet("/results/{requestId:guid}", (Guid requestId, ClientPrintJournal journal) => journal.Result(requestId));
        app.MapPost("/preview", (ClientPreviewRequest request) =>
        {
            Validate(request.Design, request.Printer);
            return Results.File(ProductLabelRenderer.Preview(request.Design, request.Product, request.Printer.Dpi, request.Printer.OffsetXmm, request.Printer.OffsetYmm), "image/png");
        });
        app.MapPost("/preview-a4", (PromotionA4Preview request) =>
        {
            if (request.Payload?.Design?.IsA4 != true) throw new ValidationAppException("Chọn mẫu A4 để xem cả tờ.");
            Validate(request.Payload.Design, request.Payload.Printer);
            return Results.File(PromotionA4Layout.Preview(request.Payload, request.Page), "image/png");
        });
        app.MapPost("/print", async (ClientPrintRequest request, ClientPrintJournal journal, ILabelPrintTransport printer, IA4LabelPrintTransport a4) =>
        {
            if (request.RequestId == Guid.Empty || request.Payload is null) throw new ValidationAppException("Thiếu mã hoặc nội dung lệnh in.");
            if (request.Payload.Items is null || request.Payload.Items.Any(x => x is null || x.Product is null) || request.Payload.Items.Sum(x => (long)x.Quantity) > 1000)
                throw new ValidationAppException("Mỗi lệnh tối đa 1.000 tem.");
            Validate(request.Payload.Design, request.Payload.Printer);
            ProductLabelRenderer.ValidatePayload(request.Payload);
            return Results.Ok(await journal.Send(request, installedPrinters, printer, a4));
        });
        return app;
    }
    private static void Validate(ProductLabelDesign design, LabelPrinterSnapshot printer)
    {
        if (design is null || printer is null || design.Layout != "promotion-shelf" || printer.Dpi is not (203 or 300) ||
            (design.IsA4 ? printer.Dpi != 300 || printer.PrintableWidthMm != 210 : printer.PrintableWidthMm is < 20 or > 108) ||
            printer.OffsetXmm is < -5 or > 5 || printer.OffsetYmm is < -5 or > 5 ||
            string.IsNullOrWhiteSpace(printer.WindowsPrinterName) || printer.WindowsPrinterName.Length > 220)
            throw new ValidationAppException("Cấu hình máy in hoặc mẫu tem khuyến mãi không hợp lệ.");
        design.Validate(printer.PrintableWidthMm);
    }
    private static async Task Failure(HttpContext context, int status, string message)
    { context.Response.StatusCode = status; await context.Response.WriteAsJsonAsync(new { ok = false, protocol = Protocol, message }); }
}
