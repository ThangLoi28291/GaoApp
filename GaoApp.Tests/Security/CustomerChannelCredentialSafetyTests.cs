using System.Text.Json;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Security;

public sealed class CustomerChannelCredentialSafetyTests:IDisposable
{
    private const string Token="SYNTHETIC_CHANNEL_TOKEN_NEVER_REAL";
    private readonly string root=Path.Combine(Path.GetTempPath(),"GaoChannelCredentialTests",Guid.NewGuid().ToString("N"));
    private string Web=>Path.Combine(root,"web");
    private string Private=>Path.Combine(root,"private");
    private string FilePath=>Path.Combine(Private,"arbitrary-name.txt");
    private AssistantIntegrationTests.EnvironmentStub Env()=>new(Web){WebRootPath=Path.Combine(Web,"wwwroot")};
    private CustomerChannelConnection Channel()=>new(){Id="facebook-test",StoreId=1,Provider="facebook",AccountId="12345",Enabled=true,GraphVersion="v26.0",AccessTokenFile=FilePath,AppSecretFile=FilePath,VerifyTokenFile=FilePath};
    private CustomerChannelOptions Options()=>new(){CredentialDirectory=Private,WorkerEnabled=false,Connections=[Channel()]};
    public CustomerChannelCredentialSafetyTests(){Directory.CreateDirectory(Path.Combine(Web,"wwwroot"));Directory.CreateDirectory(Private);File.WriteAllText(FilePath,Token);}
    [Fact] public void Private_files_work_without_changing_channel_binding_and_disabled_channels_need_no_credentials()
    {
        var options=Options();CustomerChannelCredentialSafety.Validate(options,Env());
        Assert.Equal(Token,new CustomerChannelRegistry(new AssistantIntegrationTests.Monitor<CustomerChannelOptions>(options),Env()).Secret(FilePath));
        var before=options.Connections[0].Binding;options.Connections[0].AccessTokenFile=Path.Combine(Private,"relocated.txt");Assert.Equal(before,options.Connections[0].Binding);
        CustomerChannelCredentialSafety.Validate(new(),Env());
        CustomerChannelCredentialSafety.Validate(new(){Connections=[new(){Id="disabled-test",StoreId=1,Provider="facebook",AccountId="12345"}]},Env());
    }
    [Theory][InlineData("malformed")][InlineData("missing")][InlineData("public")]
    public void Globally_disabled_channels_allow_unused_legacy_configuration_and_reenable_restores_validation(string kind)
    {
        var options=Options();options.Enabled=false;
        if(kind=="malformed")options.Connections[0].StoreId=0;
        else if(kind=="missing")File.Delete(FilePath);
        else options.CredentialDirectory=Path.Combine(Web,"wwwroot");

        Assert.True(options.Valid());
        CustomerChannelCredentialSafety.Validate(options,Env());
        Assert.True(new CustomerChannelCredentialValidator(Env(),new ConfigurationBuilder().Build()).Validate(null,options).Succeeded);

        options.Enabled=true;
        Assert.Throws<InvalidOperationException>(()=>CustomerChannelCredentialSafety.Validate(options,Env()));
    }
    [Fact] public void Global_switch_disables_workers_lookup_and_secret_reads_and_can_be_reenabled()
    {
        var options=Options();options.Enabled=false;options.WorkerEnabled=true;
        var registry=new CustomerChannelRegistry(new AssistantIntegrationTests.Monitor<CustomerChannelOptions>(options),Env());
        using(var locked=new FileStream(FilePath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            CustomerChannelCredentialSafety.Validate(options,Env());
            Assert.False(registry.WorkerEnabled);Assert.Empty(registry.All());
            Assert.Equal(404,Assert.Throws<AssistantException>(()=>registry.Get(options.Connections[0].Id)).Status);
            Assert.Equal(503,Assert.Throws<AssistantException>(()=>registry.Secret(FilePath)).Status);
            Assert.Throws<InvalidOperationException>(()=>CustomerChannelCredentialSafety.Read(FilePath,options,Env()));
        }

        options.Enabled=true;
        Assert.True(registry.WorkerEnabled);Assert.Single(registry.All());
        Assert.Equal(Token,registry.Secret(FilePath));
    }
    [Theory][InlineData("facebook")][InlineData("zalomarketing")]
    public async Task Global_switch_prevents_dispatch_even_with_a_previously_obtained_connection(string provider)
    {
        var options=Options();var connection=options.Connections[0];connection.Provider=provider;
        if(provider=="zalomarketing"){connection.AccountId=Guid.NewGuid().ToString("D");connection.BaseUrl="https://zalo.example.test/";}
        var registry=new CustomerChannelRegistry(new AssistantIntegrationTests.Monitor<CustomerChannelOptions>(options),Env());
        var previousConnection=registry.Get(connection.Id);
        options.Enabled=false;
        var calls=0;
        using var http=new HttpClient(new CustomerChannelTransportTests.Handler(_=>{calls++;return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));}));
        var transport=new CustomerChannelTransport(http,registry,TimeProvider.System);
        var item=new CustomerChannelDispatch(Guid.NewGuid(),Guid.NewGuid(),connection.Id,connection.Binding,provider=="facebook"?"999111":Guid.NewGuid().ToString("D"),"42","TEST reply","agent",1,"pending",null,DateTimeOffset.UtcNow,0);

        Assert.Equal(503,(await Assert.ThrowsAsync<AssistantException>(()=>transport.Dispatch(previousConnection,item,default))).Status);
        Assert.Equal(0,calls);
    }
    [Fact] public void Disabled_configuration_binds_and_validates_but_cannot_reload_as_enabled_with_missing_credentials()
    {
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            {"CustomerChannels:Enabled","false"},{"CustomerChannels:WorkerEnabled","true"},
            {"CustomerChannels:Connections:0:Enabled","true"},{"CustomerChannels:Connections:0:AccessTokenFile","legacy/missing-token.txt"}
        }).Build();
        var services=new ServiceCollection();
        services.AddSingleton<IValidateOptions<CustomerChannelOptions>>(new CustomerChannelCredentialValidator(Env(),configuration));
        services.AddOptions<CustomerChannelOptions>().Bind(configuration.GetSection("CustomerChannels")).Validate(o=>o.Valid(),"Invalid customer channel configuration.");
        using var provider=services.BuildServiceProvider();
        var monitor=provider.GetRequiredService<IOptionsMonitor<CustomerChannelOptions>>();
        var registry=new CustomerChannelRegistry(monitor,Env(),configuration);

        Assert.False(monitor.CurrentValue.Enabled);Assert.False(registry.WorkerEnabled);Assert.Empty(registry.All());
        configuration["CustomerChannels:Enabled"]="true";
        Assert.ThrowsAny<Exception>(()=>configuration.Reload());
        Assert.Equal(503,Assert.Throws<AssistantException>(()=>registry.All()).Status);
        configuration["CustomerChannels:Enabled"]="false";configuration.Reload();
        Assert.Empty(registry.All());
    }
    [Theory][InlineData("wwwroot")][InlineData("release")][InlineData("upload")][InlineData("other-iis-site")][InlineData("ancestor")]
    public void Public_upload_release_other_IIS_mapping_and_ancestor_directories_are_rejected_even_when_worker_is_off(string location)
    {
        var options=Options();var upload=Path.Combine(root,"upload");Directory.CreateDirectory(upload);
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Storage:UploadRoot",upload}}).Build();
        var dir=location switch{"wwwroot"=>Path.Combine(Web,"wwwroot"),"release"=>Web,"upload"=>upload,"ancestor"=>root,_=>Private};
        if(location=="other-iis-site")options.PublicRoots.Add(Private);
        options.CredentialDirectory=dir;var file=Path.Combine(dir,"arbitrary-token.txt");File.WriteAllText(file,Token);options.Connections[0].AccessTokenFile=file;
        var error=Assert.Throws<InvalidOperationException>(()=>CustomerChannelCredentialSafety.Validate(options,Env(),configuration));
        Assert.DoesNotContain(Token,error.ToString());Assert.DoesNotContain(root,error.ToString());
        options.Connections[0].Enabled=false;Assert.Throws<InvalidOperationException>(()=>CustomerChannelCredentialSafety.Validate(options,Env(),configuration));
    }
    [Theory][InlineData("relative")][InlineData("traversal")][InlineData("outside")][InlineData("prefix")][InlineData("missing-root")]
    public void Relative_traversal_escape_and_sibling_prefix_paths_are_rejected(string kind)
    {
        var options=Options();var path=kind switch{"relative"=>"private/token.txt","traversal"=>Path.Combine(Private,"..","private","arbitrary-name.txt"),"outside"=>Path.Combine(root,"token.txt"),"prefix"=>Path.Combine(root,"private-sibling","token.txt"),_=>FilePath};
        if(kind=="missing-root")options.CredentialDirectory=Path.Combine(root,"absent");
        Assert.ThrowsAny<Exception>(()=>CustomerChannelCredentialSafety.Read(path,options,Env()));
    }
    [Theory][InlineData("missing")][InlineData("huge")][InlineData("short")][InlineData("control")][InlineData("invalid-utf8")]
    public void Missing_oversized_invalid_and_unreadable_credentials_fail_safely(string kind)
    {
        if(kind=="missing")File.Delete(FilePath);
        else if(kind=="huge"){using var file=File.Create(FilePath);file.SetLength(1024L*1024*1024);}
        else if(kind=="invalid-utf8")File.WriteAllBytes(FilePath,Enumerable.Repeat((byte)0xff,32).ToArray());
        else File.WriteAllText(FilePath,kind=="short"?"short":Token+"\0");
        var registry=new CustomerChannelRegistry(new AssistantIntegrationTests.Monitor<CustomerChannelOptions>(Options()),Env());
        var error=Assert.Throws<AssistantException>(()=>registry.Secret(FilePath));Assert.Equal(503,error.Status);Assert.DoesNotContain(Token,error.ToString());Assert.DoesNotContain(root,error.ToString());
        Assert.Throws<InvalidOperationException>(()=>CustomerChannelCredentialSafety.Validate(Options(),Env()));
    }
    [Fact] public void Exclusive_file_lock_is_not_bypassed_and_UTF8_BOM_with_trailing_newline_is_supported()
    {
        File.WriteAllText(FilePath,Token+"\r\n",new System.Text.UTF8Encoding(true));
        Assert.Equal(Token,CustomerChannelCredentialSafety.Read(FilePath,Options(),Env()));
        using var locked=new FileStream(FilePath,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        Assert.Throws<InvalidOperationException>(()=>CustomerChannelCredentialSafety.Validate(Options(),Env()));
    }
    [Fact] public void Windows_UNC_device_ADS_and_short_alias_spellings_are_rejected_before_filesystem_access()
    {
        if(!OperatingSystem.IsWindows())return;
        foreach(var path in new[]{@"\\127.0.0.1\unreachable\token.txt","//127.0.0.1/unreachable/token.txt",@"\/127.0.0.1\unreachable\token.txt",@"\\?\C:\private\token.txt",FilePath+":token",Path.Combine(root,"PRIVAT~1","token.txt")})
            Assert.Throws<InvalidOperationException>(()=>CustomerChannelCredentialSafety.Read(path,Options(),Env()));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Junction_or_file_symlink_to_public_files_is_rejected(bool directoryLink)
    {
        var link=Path.Combine(Private,directoryLink?"linked":"linked.txt");var target=Path.Combine(Web,"wwwroot","public.txt");File.WriteAllText(target,Token);
        if(directoryLink && OperatingSystem.IsWindows())
        {
            using var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",$"/c mklink /J \"{link}\" \"{Path.Combine(Web,"wwwroot")}\""){CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true})!;
            process.WaitForExit();Assert.Equal(0,process.ExitCode);
        }
        else if(directoryLink)Directory.CreateSymbolicLink(link,Path.Combine(Web,"wwwroot"));
        else
        {
            // File symlinks may need Developer Mode on Windows. A directory junction
            // exercises the ancestor guard on all Windows test hosts without privilege.
            if(OperatingSystem.IsWindows()){
                using var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",$"/c mklink /J \"{link}\" \"{Path.Combine(Web,"wwwroot")}\""){CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true})!;
                process.WaitForExit();Assert.Equal(0,process.ExitCode);target=Path.Combine(link,"public.txt");
            }else{File.CreateSymbolicLink(link,target);target=link;}
        }
        try{Assert.ThrowsAny<Exception>(()=>CustomerChannelCredentialSafety.Read(directoryLink?Path.Combine(link,"public.txt"):target,Options(),Env()));}
        finally{if(Directory.Exists(link))Directory.Delete(link);else File.Delete(link);}
    }
    [Fact] public void Validated_options_reject_unsafe_reload_and_registry_fails_closed_without_outbound_calls()
    {
        var data=new Dictionary<string,string?>{{"CustomerChannels:CredentialDirectory",Private},{"CustomerChannels:Connections:0:Id","facebook-test"},{"CustomerChannels:Connections:0:StoreId","1"},{"CustomerChannels:Connections:0:Provider","facebook"},{"CustomerChannels:Connections:0:AccountId","12345"},{"CustomerChannels:Connections:0:Enabled","true"},{"CustomerChannels:Connections:0:GraphVersion","v26.0"},{"CustomerChannels:Connections:0:AccessTokenFile",FilePath},{"CustomerChannels:Connections:0:AppSecretFile",FilePath},{"CustomerChannels:Connections:0:VerifyTokenFile",FilePath}};
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        var services=new ServiceCollection();services.AddSingleton<IValidateOptions<CustomerChannelOptions>>(new CustomerChannelCredentialValidator(Env(),configuration));services.AddOptions<CustomerChannelOptions>().Bind(configuration.GetSection("CustomerChannels"));
        using var provider=services.BuildServiceProvider();var monitor=provider.GetRequiredService<IOptionsMonitor<CustomerChannelOptions>>();var registry=new CustomerChannelRegistry(monitor,Env(),configuration);
        Assert.Equal(Token,registry.Secret(FilePath));
        configuration["CustomerChannels:Connections:0:AccessTokenFile"]=Path.Combine(Web,"wwwroot","public.txt");
        Assert.ThrowsAny<Exception>(()=>configuration.Reload());
        Assert.Equal(503,Assert.Throws<AssistantException>(()=>registry.All()).Status);
        Assert.Equal(503,Assert.Throws<AssistantException>(()=>registry.Secret(FilePath)).Status);
        configuration["CustomerChannels:Connections:0:AccessTokenFile"]=FilePath;configuration.Reload();Assert.Equal(Token,registry.Secret(FilePath));
    }
    [Fact] public void Public_directory_alias_to_private_storage_is_rejected_at_startup()
    {
        var link=Path.Combine(Web,"wwwroot","public-alias");
        if(OperatingSystem.IsWindows()){
            using var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",$"/c mklink /J \"{link}\" \"{Private}\""){CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true})!;
            process.WaitForExit();Assert.Equal(0,process.ExitCode);
        }else Directory.CreateSymbolicLink(link,Private);
        try{Assert.Throws<InvalidOperationException>(()=>CustomerChannelCredentialSafety.Validate(Options(),Env()));}
        finally{Directory.Delete(link);}
    }
    [Fact] public void Offline_preflight_reads_all_mappings_and_returns_only_counts_and_sanitized_failures()
    {
        File.WriteAllText(Path.Combine(Web,"appsettings.json"),JsonSerializer.Serialize(new{CustomerChannels=Options()}));
        var request=new CustomerChannelCredentialMaintenance.Request(Web,new(),[]);var output=new StringWriter();var error=new StringWriter();
        Assert.Equal(0,CustomerChannelCredentialMaintenance.Run(new StringReader(JsonSerializer.Serialize(request)),output,error));Assert.DoesNotContain(Token,output.ToString());Assert.DoesNotContain(root,output.ToString());
        Assert.Equal(2,CustomerChannelCredentialMaintenance.Run(new StringReader(JsonSerializer.Serialize(request with{PublicRoots=[Private]})),output,error));Assert.DoesNotContain(Token,error.ToString());Assert.DoesNotContain(root,error.ToString());
        Assert.Equal(2,CustomerChannelCredentialMaintenance.Run(new StringReader(new string('x',131073)),output,error));
    }
    [Fact] public void Offline_preflight_skips_disabled_legacy_credentials_and_reports_no_active_files()
    {
        var options=Options();options.Enabled=false;options.CredentialDirectory="missing/legacy";
        options.Connections[0].AccessTokenFile="wwwroot/missing-token.txt";
        File.WriteAllText(Path.Combine(Web,"appsettings.json"),JsonSerializer.Serialize(new{CustomerChannels=options}));
        var request=new CustomerChannelCredentialMaintenance.Request(Web,new(),[Private]);
        var output=new StringWriter();var error=new StringWriter();
        Assert.Equal(0,CustomerChannelCredentialMaintenance.Run(new StringReader(JsonSerializer.Serialize(request)),output,error));
        using var result=JsonDocument.Parse(output.ToString());
        Assert.False(result.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(0,result.RootElement.GetProperty("files").GetInt32());
        Assert.Equal(0,result.RootElement.GetProperty("connections").GetInt32());
        Assert.Equal("",error.ToString());
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
