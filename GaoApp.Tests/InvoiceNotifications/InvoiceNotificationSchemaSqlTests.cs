using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Migrations;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Infrastructure.Services.InvoiceNotifications;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.InvoiceNotifications;

// Fixed local server, GUID database, synthetic prerequisites only. Never reads GaoApp settings or any business database.
public sealed class InvoiceNotificationSchemaSqlTests
{
    [Fact,Trait("Category","Integration"),Trait("Scope","P3DisposableSql")]
    public async Task MinimalDeltaEnforcesProofUniquenessSourceKindsAndRollbackEvidence()
    {
        var name="GaoApp_P3Notifications_Test_"+Guid.NewGuid().ToString("N");
        if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^GaoApp_P3Notifications_Test_[a-f0-9]{32}$"))throw new InvalidOperationException("Unsafe disposable SQL name.");
        var masterString="Server=lpc:.\\SQLEXPRESS;Database=master;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;Pooling=false;";
        await using var master=new SqlConnection(masterString);await master.OpenAsync();var created=false;
        try
        {
            await Execute(master,"CREATE DATABASE ["+name+"];");created=true;
            var connectionString=new SqlConnectionStringBuilder(masterString){InitialCatalog=name}.ConnectionString;
            await using var connection=new SqlConnection(connectionString);await connection.OpenAsync();
            await Execute(connection,"CREATE TABLE [Stores]([Id] int NOT NULL PRIMARY KEY); CREATE TABLE [Customers]([Id] int NOT NULL PRIMARY KEY); CREATE TABLE [InvoiceHeads]([Id] int NOT NULL PRIMARY KEY); INSERT [Stores] VALUES(1);INSERT [Customers] VALUES(2),(99);INSERT [InvoiceHeads] VALUES(3);");
            await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options,new Tenant(),new User());
            var generator=db.GetService<IMigrationsSqlGenerator>();var migration=new AddInvoiceNotificationsP3();
            await Execute(connection,"CREATE TABLE [__EFMigrationsHistory]([MigrationId] nvarchar(150) NOT NULL PRIMARY KEY,[ProductVersion] nvarchar(32) NOT NULL); INSERT [__EFMigrationsHistory] VALUES('20261025090000_AddAdminMenuPinSettings','10.0.13');");
            var script=await File.ReadAllTextAsync(ScriptPath());
            for(var run=0;run<2;run++)
            {
                // SQLCMD can start with QUOTED_IDENTIFIER OFF; filtered indexes require the script to set its own session options.
                await Execute(connection,"SET QUOTED_IDENTIFIER OFF;SET ANSI_WARNINGS OFF;SET CONCAT_NULL_YIELDS_NULL OFF;SET ARITHABORT OFF;SET NUMERIC_ROUNDABORT ON;");
                foreach(var batch in System.Text.RegularExpressions.Regex.Split(script,@"^\s*GO\s*$",System.Text.RegularExpressions.RegexOptions.Multiline|System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    if(!string.IsNullOrWhiteSpace(batch))await Execute(connection,batch);
                Assert.Equal(1,await Scalar(connection,"SELECT CASE WHEN SESSIONPROPERTY('ANSI_NULLS')=1 AND SESSIONPROPERTY('QUOTED_IDENTIFIER')=1 AND SESSIONPROPERTY('ANSI_PADDING')=1 AND SESSIONPROPERTY('ANSI_WARNINGS')=1 AND SESSIONPROPERTY('CONCAT_NULL_YIELDS_NULL')=1 AND SESSIONPROPERTY('ARITHABORT')=1 AND SESSIONPROPERTY('NUMERIC_ROUNDABORT')=0 THEN 1 ELSE 0 END;"));
            }
            Assert.Equal(3,await Scalar(connection,"SELECT COUNT(*) FROM sys.tables WHERE [name] IN ('InvoiceRecipientBindings','InvoiceNotificationOutbox','InvoiceNotificationAudit')"));
            var id=Guid.NewGuid();var request=Guid.NewGuid();var profile=Guid.NewGuid();var conversation=Guid.NewGuid();
            var insert="INSERT [InvoiceRecipientBindings]([Id],[StoreId],[CustomerId],[VerificationRequestId],[ConnectionId],[ConnectionBinding],[SenderProfileId],[ConversationId],[IncomingSequence],[NativePeer],[Phone],[Version],[VerifiedByUserId],[VerifiedAtUtc],[VerificationMethod],[EvidenceReference]) VALUES(@id,1,2,@request,'synthetic-pilot',REPLICATE('A',64),@profile,@conversation,1,'sdk-v1:123','0398946174',1,7,SYSUTCDATETIME(),'in_person','Synthetic proof');";
            await InsertBinding(connection,insert,id,request,profile,conversation);
            var duplicate=await Assert.ThrowsAsync<SqlException>(()=>InsertBinding(connection,insert,Guid.NewGuid(),Guid.NewGuid(),profile,Guid.NewGuid()));
            Assert.Contains(duplicate.Number,new[]{2601,2627});
            var invalid=await Assert.ThrowsAsync<SqlException>(()=>InsertBinding(connection,insert.Replace("VALUES(@id,1,2,","VALUES(@id,1,99,").Replace(",1,'sdk-v1:123'",",0,'sdk-v1:123'"),Guid.NewGuid(),Guid.NewGuid(),profile,Guid.NewGuid()));
            Assert.Equal(547,invalid.Number);
            var outboxId=Guid.NewGuid();var version=new string('D',64);
            var snapshot=JsonSerializer.Serialize(new InvoiceDocumentSnapshot{StoreId=1,CustomerId=2,SourceType="Order",SourceId=3,SourceVersion=version,Kind="Receipt",Code="SYN"});
            var proof=JsonSerializer.Serialize(new InvoiceRecipientBindingSnapshot(id,1,2,"synthetic-pilot",new string('A',64),profile,conversation,1,"sdk-v1:123","0398946174",1,DateTime.UtcNow,null));
            await using(var insertOutbox=new SqlCommand("INSERT [InvoiceNotificationOutbox]([Id],[StoreId],[CustomerId],[SourceType],[SourceId],[SourceVersion],[Kind],[Code],[SnapshotJson],[SnapshotSha256],[RequestId],[IdempotencyKey],[BindingId],[BindingVersion],[BindingSnapshotJson],[State],[NextAttemptAtUtc],[Attempts],[CreatedAtUtc]) VALUES(@id,1,2,'Order',3,@version,'Receipt','SYN',@snapshot,@hash,@id,@key,@binding,1,@proof,'Pending',DATEADD(second,-1,SYSUTCDATETIME()),0,SYSUTCDATETIME());",connection))
            {
                insertOutbox.Parameters.AddWithValue("@id",outboxId);insertOutbox.Parameters.AddWithValue("@version",version);insertOutbox.Parameters.AddWithValue("@snapshot",snapshot);
                insertOutbox.Parameters.AddWithValue("@hash",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))));insertOutbox.Parameters.AddWithValue("@key","gao-invoice-"+outboxId.ToString("N"));
                insertOutbox.Parameters.AddWithValue("@binding",id);insertOutbox.Parameters.AddWithValue("@proof",proof);await insertOutbox.ExecuteNonQueryAsync();
            }
            var sourceError=await Assert.ThrowsAsync<SqlException>(()=>Execute(connection,"UPDATE [InvoiceNotificationOutbox] SET [SourceType]='InputInvoice';"));Assert.Equal(547,sourceError.Number);
            var service=new EfInvoiceNotificationStore(db,Options.Create(new InvoiceNotificationOptions{Enabled=true}));
            var claimed=Assert.IsType<InvoiceNotificationWorkItem>(await service.ClaimAsync(default));Assert.Equal(outboxId,claimed.Id);Assert.NotEqual(Guid.Empty,claimed.LeaseToken);
            Assert.Null(db.Database.CurrentTransaction);Assert.Null(await service.ClaimAsync(default));
            await service.RecordReviewAsync(claimed,"Synthetic schema claim settled",default);
            var rollback=await Assert.ThrowsAsync<SqlException>(async()=>{foreach(var command in generator.Generate(migration.DownOperations))await Execute(connection,command.CommandText);});
            Assert.Equal(51000,rollback.Number);
            Assert.Equal(1,await Scalar(connection,"SELECT COUNT(*) FROM [InvoiceRecipientBindings]"));
            await Execute(connection,"DELETE FROM [InvoiceNotificationAudit];DELETE FROM [InvoiceNotificationOutbox];DELETE FROM [InvoiceRecipientBindings];");
            foreach(var command in generator.Generate(migration.DownOperations))await Execute(connection,command.CommandText);
            Assert.Equal(0,await Scalar(connection,"SELECT COUNT(*) FROM sys.tables WHERE [name] IN ('InvoiceRecipientBindings','InvoiceNotificationOutbox','InvoiceNotificationAudit')"));
        }
        finally
        {
            if(created)
            {
                if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^GaoApp_P3Notifications_Test_[a-f0-9]{32}$"))throw new InvalidOperationException("Unsafe disposable SQL cleanup.");
                await Execute(master,"ALTER DATABASE ["+name+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+name+"];");
            }
        }
    }
    private static string ScriptPath([System.Runtime.CompilerServices.CallerFilePath]string sourceFile="")
    {
        var directory=new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while(!File.Exists(Path.Combine(directory.FullName,"GaoApp.sln")))directory=directory.Parent??throw new DirectoryNotFoundException("Reviewed GaoApp source root missing.");
        return Path.Combine(directory.FullName,"docs","deployment","invoice-notifications-p3.sql");
    }
    private static async Task Execute(SqlConnection connection,string sql){await using var command=new SqlCommand(sql,connection){CommandTimeout=60};await command.ExecuteNonQueryAsync();}
    private static async Task<int> Scalar(SqlConnection connection,string sql){await using var command=new SqlCommand(sql,connection);return Convert.ToInt32(await command.ExecuteScalarAsync());}
    private static async Task InsertBinding(SqlConnection connection,string sql,Guid id,Guid request,Guid profile,Guid conversation)
    {await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@id",id);command.Parameters.AddWithValue("@request",request);command.Parameters.AddWithValue("@profile",profile);command.Parameters.AddWithValue("@conversation",conversation);await command.ExecuteNonQueryAsync();}
    private sealed class Tenant:ITenantContext{public int? StoreId=>1;public bool IsHostAdmin=>false;public string? Subdomain=>"p3-synthetic";}
    private sealed class User:ICurrentUser{public int? UserId=>7;public string? UserName=>"Synthetic operator";public int? TerminalId=>null;public string? TerminalCode=>null;public bool IsAuthenticated=>true;}
}
