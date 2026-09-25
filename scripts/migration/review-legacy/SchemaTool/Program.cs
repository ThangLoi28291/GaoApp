using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
var options=new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=.\\SQLEXPRESS;Database=unused;Integrated Security=True;TrustServerCertificate=True").Options;
using var db=new AppDbContext(options,new ReadTenant(1),new ReadUser());
var archive=args.Contains("--archive");
var sql=db.GetService<IMigrator>().GenerateScript(archive?"20260923160000_AddLegacyInvoiceImport":null,"20260923180000_AddLegacyReturnArchive");
File.WriteAllText(archive?"scripts/migration/initial-import/03-return-archive/Apply-Schema.sql":"scripts/migration/review-legacy/SchemaTool/FullSchema.sql",sql);
Console.WriteLine(archive?"Archive schema generated":"Full schema generated");
sealed class ReadTenant(int storeId) : ITenantContext
{
    public int? StoreId => storeId;
    public bool IsHostAdmin => false;
    public string? Subdomain => null;
}
sealed class ReadUser : ICurrentUser
{
    public int? UserId => null;
    public string? UserName => null;
    public int? TerminalId => null;
    public string? TerminalCode => null;
    public bool IsAuthenticated => false;
}
