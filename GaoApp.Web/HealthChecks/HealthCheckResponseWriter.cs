using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GaoApp.Web.HealthChecks;

/// <summary>
/// Ghi response JSON rõ ràng cho health check endpoint.
/// </summary>
public static class HealthCheckResponseWriter
{
    public static async Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();

        object response;

        if (environment.IsDevelopment())
        {
            response = new
            {
                status = report.Status.ToString(),
                totalDuration = report.TotalDuration.TotalMilliseconds,
                entries = report.Entries.Select(x => new
                {
                    name = x.Key,
                    status = x.Value.Status.ToString(),
                    description = x.Value.Description,
                    duration = x.Value.Duration.TotalMilliseconds,
                    exception = x.Value.Exception?.Message,
                    data = x.Value.Data.ToDictionary(
                        d => d.Key,
                        d => d.Value)
                })
            };
        }
        else
        {
            // Không lộ exception, đường dẫn lưu trữ hoặc chi tiết hạ tầng ở Production.
            response = new
            {
                status = report.Status.ToString()
            };
        }

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await context.Response.WriteAsync(json, context.RequestAborted);
    }
}
