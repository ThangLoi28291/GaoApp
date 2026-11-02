using System.Net;
using GaoApp.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace GaoApp.Tests.Security;

public sealed class ChatbotCatalogPathTests
{
    [Fact]
    public async Task Test_key_resolves_from_content_root_before_and_after_relocation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gao-chatbot-path-" + Guid.NewGuid().ToString("N"));
        var original = Path.Combine(directory, "original");
        var relocated = Path.Combine(directory, "relocated");
        try
        {
            Directory.CreateDirectory(Path.Combine(original, "GaoApp.Web"));
            Directory.CreateDirectory(Path.Combine(original, "secrets"));
            await File.WriteAllTextAsync(Path.Combine(original, "secrets", "test-api-key.txt"), new string('a', 32));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ChatbotTestApi:Enabled"] = "true",
                ["ChatbotTestApi:ApiKeyFilePath"] = "../secrets/test-api-key.txt"
            }).Build();

            async Task AssertKeyWasFound(string contentRoot)
            {
                var controller = new ChatbotCatalogController(null!, configuration,
                    new AssistantIntegrationTests.EnvironmentStub(contentRoot));
                var context = new DefaultHttpContext();
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                context.Request.Headers["X-Chatbot-Test-Key"] = new string('b', 32);
                controller.ControllerContext = new ControllerContext { HttpContext = context };
                // A wrong key gives 401 after finding the file; a missing file gives 503.
                Assert.IsType<UnauthorizedResult>(await controller.Context(default));
            }

            await AssertKeyWasFound(Path.Combine(original, "GaoApp.Web"));
            Directory.Move(original, relocated);
            await AssertKeyWasFound(Path.Combine(relocated, "GaoApp.Web"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
