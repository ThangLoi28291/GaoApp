using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;

namespace GaoApp.Web.Services.CustomerService;

// Offline, read-only deployment preflight. No host, logger, SQL or network is initialized.
public static class CustomerChannelCredentialMaintenance
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Request(string WebPath, Dictionary<string,string?> Settings, string[] PublicRoots);
    public static int Run(TextReader input, TextWriter output, TextWriter error)
    {
        try
        {
            var chars = new char[131073]; var count = 0;
            while (count < chars.Length) { var read=input.Read(chars,count,chars.Length-count); if(read==0)break; count+=read; }
            if(count==chars.Length)throw new InvalidOperationException();
            var request=JsonSerializer.Deserialize<Request>(new string(chars,0,count),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException();
            if(request.Settings is null || request.PublicRoots is null || request.PublicRoots.Length>1000 || !Path.IsPathFullyQualified(request.WebPath) ||
                request.Settings.Keys.Any(k=>!k.StartsWith("CustomerChannels__",StringComparison.OrdinalIgnoreCase)&&k!="Storage__UploadRoot"))throw new InvalidOperationException();
            var configuration=new ConfigurationBuilder().AddJsonFile(Path.Combine(request.WebPath,"appsettings.json"),optional:false)
                .AddInMemoryCollection(request.Settings.Select(p=>new KeyValuePair<string,string?>(p.Key.Replace("__",":"),p.Value))).Build();
            var options=configuration.GetSection(CustomerChannelOptions.Section).Get<CustomerChannelOptions>()??new();
            options.PublicRoots.AddRange(request.PublicRoots);
            var environment=new CredentialEnvironment {ContentRootPath=request.WebPath,WebRootPath=Path.Combine(request.WebPath,"wwwroot")};
            CustomerChannelCredentialSafety.Validate(options,environment,configuration);
            output.WriteLine(JsonSerializer.Serialize(new{status="verified",enabled=options.Enabled,connections=options.Enabled?options.Connections.Count:0,files=options.Enabled?options.Connections.Sum(c=>CustomerChannelCredentialSafety.Files(c).Count()):0}));
            return 0;
        }
        catch { error.WriteLine(CustomerChannelCredentialSafety.ConfigurationError); return 2; }
    }
    private sealed class CredentialEnvironment:IWebHostEnvironment
    {
        public string ApplicationName{get;set;}="GaoApp.Web";
        public string EnvironmentName{get;set;}="Production";
        public string ContentRootPath{get;set;}="";
        public string WebRootPath{get;set;}="";
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
        public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
    }
}
