using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Security;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Security;

public sealed class CustomerInvoiceNotificationAdminHttpTests
{
    private const string Base="/admin/customer-service/invoice-notifications";
    [Fact]
    public async Task Private_customer_invoice_endpoints_require_verified_operator_and_current_store_permissions()
    {
        await using var fixture=await Host.Start();
        using var anonymous=fixture.Client(null);using var reader=fixture.Client(2);using var incompleteVerifier=fixture.Client(3);
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(Base+"/outbox")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await reader.GetAsync(Base+"/customers?query=TEST")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await incompleteVerifier.GetAsync(Base+"/outbox")).StatusCode);
        Assert.Equal(0,fixture.Service.Calls);
        using var allowed=fixture.Client(1,2);
        Assert.Equal(HttpStatusCode.OK,(await allowed.GetAsync(Base+"/outbox")).StatusCode);
        Assert.Equal(2,fixture.Service.LastStore);
    }

    [Fact]
    public async Task Mutations_require_CSRF_and_reject_client_supplied_store_native_peer_and_duplicate_JSON_members()
    {
        await using var fixture=await Host.Start();using var client=fixture.Client(1);
        var valid=new CustomerInvoiceBindingCommand(Guid.NewGuid(),11,"zalo-test",Guid.NewGuid(),42,"in_person","TEST evidence record",true);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(Base+"/bindings",valid)).StatusCode);
        await Token(client);
        foreach(var raw in new[]{
            "{\"requestId\":\""+Guid.NewGuid()+"\",\"nativePeer\":\"sdk-v1:123\"}",
            "{\"storeId\":2,\"customerId\":11}",
            "{\"customerId\":11,\"customerId\":12}"})
        {
            using var body=new StringContent(raw,System.Text.Encoding.UTF8,"application/json");
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(Base+"/bindings",body)).StatusCode);
        }
        Assert.Equal(0,fixture.Service.VerifyCalls);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(Base+"/bindings",valid)).StatusCode);
        Assert.Equal(1,fixture.Service.VerifyCalls);Assert.Equal(1,fixture.Service.LastStore);
    }

    [Fact]
    public async Task Manual_enqueue_requires_reply_and_keeps_exact_source_and_binding_version()
    {
        await using var fixture=await Host.Start();using var verifier=fixture.Client(4);await Token(verifier);
        var command=new CustomerInvoiceSendCommand(Guid.NewGuid(),11,"Invoice",121,"CorrectionZipXml",Guid.NewGuid(),3,new string('A',64),true);
        Assert.Equal(HttpStatusCode.Forbidden,(await verifier.PostAsJsonAsync(Base+"/outbox",command)).StatusCode);
        Assert.Null(fixture.Service.LastSend);
        using var sender=fixture.Client(1);await Token(sender);
        Assert.Equal(HttpStatusCode.Accepted,(await sender.PostAsJsonAsync(Base+"/outbox",command)).StatusCode);
        Assert.Equal(command,fixture.Service.LastSend);
        Assert.Equal(1,fixture.Service.LastStore);
    }

    [Fact]
    public async Task Binding_and_source_conflicts_are_sanitized_and_foreign_details_return_404()
    {
        await using var fixture=await Host.Start();using var client=fixture.Client(1);await Token(client);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(Base+"/outbox/"+Guid.NewGuid())).StatusCode);
        fixture.Service.Conflict=true;
        using var response=await client.PostAsJsonAsync(Base+"/outbox",new CustomerInvoiceSendCommand(Guid.NewGuid(),11,"Order",10,"Receipt",Guid.NewGuid(),1,new string('A',64),true));
        Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
        var text=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("PRIVATE_STORAGE",text);Assert.DoesNotContain("SECRET",text);
        Assert.Contains("invoice_notification_changed",text);
    }

    [Fact]
    public async Task Selected_source_preview_is_store_scoped_and_enqueue_requires_the_reviewed_version_field()
    {
        await using var fixture=await Host.Start();using var client=fixture.Client(1);await Token(client);
        var preview=await client.GetFromJsonAsync<JsonElement>(Base+"/customers/11/source-preview?sourceType=Invoice&sourceId=121&kind=OfficialXml&storeId=2");
        Assert.Equal(new string('A',64),preview.GetProperty("sourceVersion").GetString());Assert.Equal(121,preview.GetProperty("sourceId").GetInt32());Assert.Equal(1,fixture.Service.LastStore);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(Base+"/outbox",new{requestId=Guid.NewGuid(),customerId=11,sourceType="Invoice",sourceId=121,kind="OfficialXml",bindingId=Guid.NewGuid(),bindingVersion=1,confirmed=true})).StatusCode);
        Assert.Null(fixture.Service.LastSend);
    }

    private static async Task Token(HttpClient client)
    {
        var token=await client.GetFromJsonAsync<JsonElement>("/test/csrf");client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.DefaultRequestHeaders.Add("RequestVerificationToken",token.GetProperty("token").GetString());
    }
    private sealed class Host(WebApplication app,Fake service) : IAsyncDisposable
    {
        public Fake Service=>service;
        public static async Task<Host> Start()
        {
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Testing"});builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options=>options.Listen(IPAddress.Loopback,0));
            builder.Services.AddControllersWithViews().AddApplicationPart(typeof(CustomerInvoiceNotificationsController).Assembly);
            builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<ICurrentUser,UserContext>();builder.Services.AddScoped<ITenantContext,Tenant>();
            var database="InvoiceAdminHttp_"+Guid.NewGuid().ToString("N");
            builder.Services.AddDbContext<AppDbContext>(options=>options.UseInMemoryDatabase(database));
            builder.Services.AddScoped<CustomerServiceAccess>();builder.Services.AddSingleton<ResourceAdmission>();
            builder.Services.Configure<ResourceProtectionOptions>(_=>{});builder.Services.Configure<InvoiceNotificationOptions>(_=>{});
            var fake=new Fake();builder.Services.AddSingleton<ICustomerInvoiceNotificationAdmin>(fake);
            builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,Authentication>("Test",_=>{});
            builder.Services.AddAuthorization(options=>{foreach(var right in new[]{CustomerServiceRights.View,CustomerServiceRights.Verify,CustomerServiceRights.Reply})options.AddPolicy(right,policy=>policy.RequireAuthenticatedUser().RequireClaim("permission",right));});
            builder.Services.AddAntiforgery(options=>options.HeaderName="RequestVerificationToken");
            var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();
            app.MapGet("/test/csrf",(HttpContext context,IAntiforgery csrf)=>Results.Json(new{token=csrf.GetAndStoreTokens(context).RequestToken}));
            using(var scope=app.Services.CreateScope())
            {
                var configured=scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
                await using var seed=new AppDbContext(configured,new SeedTenant(),new SeedUser());
                seed.Stores.AddRange(new Store{Id=1,Name="TEST STORE ONE",SubDomain="test-one",SubDomainNormalized="TEST-ONE"},new Store{Id=2,Name="TEST STORE TWO",SubDomain="test-two",SubDomainNormalized="TEST-TWO"});
                seed.Users.AddRange(Enumerable.Range(1,4).Select(id=>new User{Id=id,UserName="TEST-USER-"+id,PasswordHash="SYNTHETIC-TEST-ONLY",IsHostAdmin=id==1}));
                var rights=new[]{CustomerServiceRights.View,CustomerServiceRights.Verify,CustomerServiceRights.Reply,PermissionCodes.Catalog.Customer.View,PermissionCodes.Pos.Order.View};
                seed.Set<Permission>().AddRange(rights.Select((right,index)=>new Permission{Id=index+1,Code=right,Name=right,GroupName="TEST"}));
                foreach(var id in new[]{2,3,4})
                {
                    var indexes=id==2?new[]{0}:id==3?new[]{0,1}:new[]{0,1,3,4};
                    var role=new Role{Id=id,StoreId=1,Code="TEST-ROLE-"+id,Name="TEST",RolePermissions=indexes.Select(index=>new RolePermission{PermissionId=index+1}).ToArray()};
                    seed.Set<Role>().Add(role);seed.UserInStores.Add(new UserInStore{Id=id,StoreId=1,UserId=id,Role=role,IsActive=true});
                }
                foreach(var entry in seed.ChangeTracker.Entries()) if(entry.Metadata.FindProperty("RowVersion") is not null) entry.Property("RowVersion").CurrentValue=new byte[8];
                await seed.SaveChangesAsync();
            }
            await app.StartAsync();return new(app,fake);
        }
        public HttpClient Client(int? user,int store=1)
        {
            var client=new HttpClient(new HttpClientHandler{CookieContainer=new CookieContainer(),AllowAutoRedirect=false}){BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
            if(user is {} id)client.DefaultRequestHeaders.Add("X-Test-User",id.ToString());client.DefaultRequestHeaders.Add("X-Test-Store",store.ToString());return client;
        }
        public async ValueTask DisposeAsync(){await app.StopAsync();await app.DisposeAsync();}
    }
    private sealed class UserContext(IHttpContextAccessor context):ICurrentUser
    {
        public int? UserId=>int.TryParse(context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:null;
        public string? UserName=>"TEST";public int? TerminalId=>null;public string? TerminalCode=>null;public bool IsAuthenticated=>context.HttpContext?.User.Identity?.IsAuthenticated==true;
    }
    private sealed class Tenant(IHttpContextAccessor context):ITenantContext
    {public int? StoreId=>int.TryParse(context.HttpContext?.Request.Headers["X-Test-Store"],out var id)?id:1;public bool IsHostAdmin=>false;public string? Subdomain=>"TEST";}
    private sealed class SeedTenant:ITenantContext{public int? StoreId=>1;public bool IsHostAdmin=>true;public string? Subdomain=>"TEST";public bool CanReadAllStores=>true;public bool CanWriteAllStores=>true;}
    private sealed class SeedUser:ICurrentUser{public int? UserId=>1;public string? UserName=>"TEST";public int? TerminalId=>null;public string? TerminalCode=>null;public bool IsAuthenticated=>true;}
    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if(!int.TryParse(Request.Headers["X-Test-User"],out var id))return Task.FromResult(AuthenticateResult.NoResult());
            var rights=id==2?new[]{CustomerServiceRights.View}:id is 3 or 4?new[]{CustomerServiceRights.View,CustomerServiceRights.Verify}:new[]{CustomerServiceRights.View,CustomerServiceRights.Verify,CustomerServiceRights.Reply};
            var claims=rights.Select(right=>new Claim("permission",right)).Append(new Claim(ClaimTypes.NameIdentifier,id.ToString()));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,"Test")),"Test")));
        }
    }
    private sealed class Fake : ICustomerInvoiceNotificationAdmin
    {
        public int Calls,LastStore,VerifyCalls;public bool Conflict;public CustomerInvoiceSendCommand? LastSend;
        private void Seen(CustomerStaff who){Calls++;LastStore=who.StoreId;}
        public Task<IReadOnlyList<CustomerInvoiceAdminCustomer>> CustomersAsync(CustomerStaff who,string query,CancellationToken ct){Seen(who);return Task.FromResult<IReadOnlyList<CustomerInvoiceAdminCustomer>>([]);}
        public Task<IReadOnlyList<CustomerInvoiceAdminConnection>> ConnectionsAsync(CustomerStaff who,CancellationToken ct){Seen(who);return Task.FromResult<IReadOnlyList<CustomerInvoiceAdminConnection>>([]);}
        public Task<IReadOnlyList<CustomerInvoiceAdminThread>> ConversationsAsync(CustomerStaff who,string connection,string query,CancellationToken ct){Seen(who);return Task.FromResult<IReadOnlyList<CustomerInvoiceAdminThread>>([]);}
        public Task<CustomerInvoiceAdminEvidence> InspectAsync(CustomerStaff who,string connection,Guid conversation,long sequence,CancellationToken ct){Seen(who);return Task.FromResult(new CustomerInvoiceAdminEvidence(conversation,sequence,DateTimeOffset.UtcNow,"TEST",true,null));}
        public Task<IReadOnlyList<CustomerInvoiceAdminBinding>> BindingsAsync(CustomerStaff who,int customer,CancellationToken ct){Seen(who);return Task.FromResult<IReadOnlyList<CustomerInvoiceAdminBinding>>([]);}
        public Task<CustomerInvoiceAdminBinding> VerifyAsync(CustomerStaff who,CustomerInvoiceBindingCommand command,CancellationToken ct){Seen(who);VerifyCalls++;return Task.FromResult(new CustomerInvoiceAdminBinding(Guid.NewGuid(),command.CustomerId,command.ConnectionId,command.ConversationId,command.IncomingSequence,1,DateTime.UtcNow,null,true,null));}
        public Task RevokeAsync(CustomerStaff who,Guid binding,CustomerInvoiceRevokeCommand command,CancellationToken ct){Seen(who);return Task.CompletedTask;}
        public Task<IReadOnlyList<CustomerInvoiceAdminSource>> SourcesAsync(CustomerStaff who,int customer,CancellationToken ct){Seen(who);return Task.FromResult<IReadOnlyList<CustomerInvoiceAdminSource>>([]);}
        public Task<IReadOnlyList<CustomerInvoiceAdminSource>> LookupSourcesAsync(CustomerStaff who,int customer,string query,CancellationToken ct){Seen(who);return Task.FromResult<IReadOnlyList<CustomerInvoiceAdminSource>>([]);}
        public Task<CustomerInvoiceAdminPreview> PreviewAsync(CustomerStaff who,int customer,string sourceType,int sourceId,string kind,CancellationToken ct){Seen(who);return Task.FromResult(new CustomerInvoiceAdminPreview(sourceType,sourceId,kind,new string('A',64),"TEST","TEST","Issued",50000,"TEST OWNER",true,null));}
        public Task<IReadOnlyList<CustomerInvoiceAdminOutbox>> OutboxAsync(CustomerStaff who,string? state,int page,CancellationToken ct){Seen(who);return Task.FromResult<IReadOnlyList<CustomerInvoiceAdminOutbox>>([]);}
        public Task<CustomerInvoiceAdminDetail?> DetailAsync(CustomerStaff who,Guid id,CancellationToken ct){Seen(who);return Task.FromResult<CustomerInvoiceAdminDetail?>(null);}
        public Task<Guid> EnqueueAsync(CustomerStaff who,CustomerInvoiceSendCommand command,CancellationToken ct){Seen(who);if(Conflict)throw new InvalidOperationException("PRIVATE_STORAGE SECRET");LastSend=command;return Task.FromResult(Guid.NewGuid());}
    }
}
