using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("Usage: Verifier <server> <targetDatabase>. Read only.");
var cs = new SqlConnectionStringBuilder { DataSource=args[0],InitialCatalog=args[1],IntegratedSecurity=true,Encrypt=true,TrustServerCertificate=true };
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs.ConnectionString).Options;
AppDbContext Context(int? store) => new(options,new ReadTenant(store),new ReadUser());
await using var db = Context(1);
var controller = new LegacyReturnArchiveController(db);
var page = (LegacyReturnArchivePage)((ViewResult)await controller.Index(null)).Model!;
var total = await db.Database.SqlQuery<long>($"SELECT COUNT_BIG(*) Value FROM dbo.LegacyReturnArchives WHERE StoreId={1}").SingleAsync();
Check(total>0 && page.Total==total && page.Items.Count==Math.Min(50,total),"Archive list/count");
var second=(LegacyReturnArchivePage)((ViewResult)await controller.Index(null,2)).Model!;
Check(second.Total==total && second.Items.Count==Math.Min(50,Math.Max(0,total-50))
 && !second.Items.Any(x=>page.Items.Any(y=>y.LegacyOrderId==x.LegacyOrderId)),"Pagination does not repeat rows");
var first=page.Items[0];
var detail=(LegacyReturnArchiveDetail)((ViewResult)await controller.Details(first.LegacyOrderId,default)).Model!;
using var header=JsonDocument.Parse(detail.HeaderJson);
using var lines=JsonDocument.Parse(detail.DetailsJson);
Check(header.RootElement.GetProperty("ID").GetInt64()==first.LegacyOrderId && lines.RootElement.ValueKind==JsonValueKind.Array,"Raw source identity/lines");
var search=(LegacyReturnArchivePage)((ViewResult)await controller.Index(first.LegacyOrderId.ToString())).Model!;
Check(search.Items.Any(x=>x.LegacyOrderId==first.LegacyOrderId),"Lookup by legacy OrderID");
var hostile=(LegacyReturnArchivePage)((ViewResult)await controller.Index("' OR 1=1;--")).Model!;
Check(hostile.Total==0,"Search stays parameterized");
Check(await controller.Index(new string('a',201)) is BadRequestObjectResult,"Search length guard");
Check(await controller.Details(long.MaxValue,default) is NotFoundResult,"Missing detail");
await using var other = Context(int.MaxValue);
var otherController=new LegacyReturnArchiveController(other);
var otherPage=(LegacyReturnArchivePage)((ViewResult)await otherController.Index(null)).Model!;
Check(otherPage.Total==0 && await otherController.Details(first.LegacyOrderId,default) is NotFoundResult,"Cross-store isolation");
await using var unscoped=Context(null);
Check(await new LegacyReturnArchiveController(unscoped).Index(null) is ForbidResult,"Missing tenant denied");
Check(typeof(LegacyReturnArchiveController).GetCustomAttribute<AuthorizeAttribute>()?.Policy==PermissionCodes.Pos.Order.View,"Existing view permission");
var actions=typeof(LegacyReturnArchiveController).GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly);
Check(actions.All(x=>x.GetCustomAttribute<HttpGetAttribute>()!=null),"Archive has only GET actions");
Console.WriteLine(JsonSerializer.Serialize(new { Status="ARCHIVE_READ_VERIFIED",Count=total,Checks=11,Target=args[1] }));
static void Check(bool value,string name) { if(!value) throw new InvalidOperationException(name); }
sealed class ReadTenant(int? store):ITenantContext { public int? StoreId=>store; public bool IsHostAdmin=>false; public string? Subdomain=>null; }
sealed class ReadUser:ICurrentUser { public int? UserId=>null; public string? UserName=>null; public int? TerminalId=>null; public string? TerminalCode=>null; public bool IsAuthenticated=>false; }
