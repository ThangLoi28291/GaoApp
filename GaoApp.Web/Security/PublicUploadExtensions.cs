using GaoApp.Infrastructure.Storage;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace GaoApp.Web.Security;

public static class PublicUploadExtensions
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".mp4", ".webm" };

    public static bool IsPublicMedia(string path)
    {
        try
        {
            var normalized = UploadPathResolver.Normalize(path);
            var segments = normalized.Split('/');
            var folder = segments[1];
            if (folder == "legacy-data")
                return segments.Length >= 4 && (segments[2] is "images" or "files") &&
                    Path.GetExtension(normalized).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp";
            return (folder is "products" or "display" or "data") && Extensions.Contains(Path.GetExtension(normalized));
        }
        catch (InvalidOperationException) { return false; }
    }

    public static IApplicationBuilder UsePublicUploads(this IApplicationBuilder app, IWebHostEnvironment env, UploadPathResolver paths)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/uploads", StringComparison.OrdinalIgnoreCase))
            {
                var path = context.Request.Path.Value!;
                if (!IsPublicMedia(path)) { context.Response.StatusCode = 404; return; }
                try
                {
                    _ = paths.Resolve(path);
                    if (!string.IsNullOrEmpty(env.WebRootPath))
                        _ = UploadPathResolver.ResolveUnderRoot(env.WebRootPath, UploadPathResolver.Normalize(path));
                }
                catch (InvalidOperationException) { context.Response.StatusCode = 404; return; }
                context.Response.Headers.XContentTypeOptions = "nosniff";
            }
            await next();
        });
        if (Directory.Exists(paths.Root))
        {
            var provider = new PhysicalFileProvider(paths.Root);
            app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.Register(provider.Dispose);
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = provider, RequestPath = "/uploads",
                ContentTypeProvider = new FileExtensionContentTypeProvider()
            });
        }
        return app;
    }
}
