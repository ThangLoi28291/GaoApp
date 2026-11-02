using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.InvoiceNotifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.CustomerService;

/// <summary>Store-scoped operator boundary. All identifiers are re-resolved; remote/storage secrets never enter UI DTOs.</summary>
public sealed class CustomerInvoiceNotificationAdminService(AppDbContext db,CustomerServiceAccess access,
    CustomerChannelRegistry registry,ICustomerInvoiceRecipientSource recipientSource,
    IInvoiceNotificationStore notifications,IOptionsMonitor<InvoiceOfficialDocumentLinkOptions>? officialLinks=null) : ICustomerInvoiceNotificationAdmin
{
    private bool LinksConfigured=>!string.IsNullOrWhiteSpace(officialLinks?.CurrentValue.PublicBaseUrl);
    private async Task Check(CustomerStaff original,CancellationToken ct,bool reply=false)
    {
        var fresh=await access.InvoiceNotificationVerifier(ct);
        if(fresh.StoreId!=original.StoreId||fresh.UserId!=original.UserId||reply&&!fresh.CanReply)throw AssistantException.Denied();
    }
    private async Task<bool> OfficialInvoicesAllowed(CustomerStaff who,CancellationToken ct)
    {
        await Check(who,ct);
        var user=await db.Users.IgnoreQueryFilters().AsNoTracking().Where(row=>row.Id==who.UserId&&row.IsActive&&!row.IsDeleted)
            .Select(row=>new{row.IsHostAdmin}).SingleOrDefaultAsync(ct);
        if(user is null)return false;if(user.IsHostAdmin)return true;
        return await db.UserInStores.IgnoreQueryFilters().AsNoTracking().Where(row=>row.StoreId==who.StoreId&&row.UserId==who.UserId&&row.IsActive&&!row.IsDeleted&&row.Role.StoreId==who.StoreId&&!row.Role.IsDeleted)
            .SelectMany(row=>row.Role.RolePermissions).AnyAsync(row=>row.Permission.Code==PermissionCodes.System.Integration.Manage,ct);
    }
    private async Task RequireOfficialInvoices(CustomerStaff who,CancellationToken ct)
    {
        if(!await OfficialInvoicesAllowed(who,ct))throw new AssistantException(403,"invoice_source_permission_required","Cần quyền quản lý tích hợp để xem hoặc gửi hóa đơn chính thức. Phiếu mua hàng vẫn dùng quyền xác minh và trả lời CSKH.");
    }
    private async Task<GaoApp.Domain.Entities.Customer> Customer(CustomerStaff who,int id,CancellationToken ct)
    {
        await Check(who,ct);
        return await db.Customers.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(row=>row.StoreId==who.StoreId&&row.Id==id&&row.IsActive&&!row.IsDeleted,ct)
            ??throw Missing("Không tìm thấy hồ sơ khách tại cửa hàng.");
    }
    private static string Clean(string? text,int max=100)=>CustomerZaloMedia.Clean(text,max);
    private static string? PhoneHint(string? phone)=>string.IsNullOrWhiteSpace(phone)?null:"••••"+Clean(phone)[^Math.Min(4,Clean(phone).Length)..];
    private static string Phone(string? text)
    {
        var value=Regex.Replace(text??"",@"[\s().-]","");
        if(value.StartsWith("+84",StringComparison.Ordinal))value="0"+value[3..];
        else if(value.StartsWith("84",StringComparison.Ordinal))value="0"+value[2..];
        return value;
    }
    private CustomerChannelConnection Connection(CustomerStaff who,string id,bool enabled=true)
    {
        var connection=registry.Get(id);
        if(connection.StoreId!=who.StoreId||connection.Provider!="zalomarketing")throw Missing("Không tìm thấy kết nối Zalo của cửa hàng.");
        if(enabled&&!connection.Enabled)throw new AssistantException(409,"invoice_connection_disabled","Kết nối Zalo đang tắt; chưa thể xác minh người nhận.");
        return connection;
    }
    public async Task<IReadOnlyList<CustomerInvoiceAdminCustomer>> CustomersAsync(CustomerStaff who,string query,CancellationToken ct)
    {
        await Check(who,ct);query=query?.Trim()??"";if(query.Length is <3 or >50)throw AssistantException.Invalid();
        var rows=await db.Customers.IgnoreQueryFilters().AsNoTracking().Where(row=>row.StoreId==who.StoreId&&row.IsActive&&!row.IsDeleted&&(row.Code==query||row.Phone==query))
            .OrderBy(row=>row.Id).Take(11).Select(row=>new{row.Id,row.Name,row.Code,row.Phone}).ToArrayAsync(ct);
        await Check(who,ct);return rows.Take(10).Select(row=>new CustomerInvoiceAdminCustomer(row.Id,Clean(row.Name),Clean(row.Code),PhoneHint(row.Phone))).ToArray();
    }
    public async Task<IReadOnlyList<CustomerInvoiceAdminConnection>> ConnectionsAsync(CustomerStaff who,CancellationToken ct)
    {
        await Check(who,ct);var rows=registry.All().Where(row=>row.StoreId==who.StoreId&&row.Provider=="zalomarketing")
            .Select(row=>new CustomerInvoiceAdminConnection(row.Id,row.Label,"••••"+row.AccountKey[^Math.Min(4,row.AccountKey.Length)..],row.Enabled)).ToArray();
        await Check(who,ct);return rows;
    }
    public async Task<IReadOnlyList<CustomerInvoiceAdminThread>> ConversationsAsync(CustomerStaff who,string connection,string query,CancellationToken ct)
    {
        await Check(who,ct);query=query?.Trim()??"";if(query.Length>80)throw AssistantException.Invalid();
        var channel=Connection(who,connection);var rows=await recipientSource.ListAsync(channel,query,ct);
        await Check(who,ct);if(Connection(who,connection).Binding!=channel.Binding)throw Changed();
        return rows.Select(row=>new CustomerInvoiceAdminThread(row.ConversationId,Clean(row.DisplayName,80),0,row.IncomingAt)).ToArray();
    }
    public async Task<CustomerInvoiceAdminEvidence> InspectAsync(CustomerStaff who,string connection,Guid conversation,long sequence,CancellationToken ct)
    {
        await Check(who,ct);var channel=Connection(who,connection);
        var inspected=await recipientSource.InspectAsync(channel,conversation,ct);var evidence=inspected.Evidence;
        if(sequence>0&&evidence.IncomingSequence!=sequence)throw Changed();
        await Check(who,ct);if(Connection(who,connection).Binding!=channel.Binding)throw Changed();
        return new(evidence.ConversationId,evidence.IncomingSequence,evidence.ObservedAtUtc,Clean(inspected.DisplayName,80),true,null);
    }
    private CustomerInvoiceAdminBinding Binding(InvoiceRecipientBindingSnapshot binding)
    {
        var current=false;try{var config=registry.Get(binding.ConnectionId);current=config.StoreId==binding.StoreId&&config.Binding==binding.ConnectionBinding&&config.Provider=="zalomarketing"&&config.Enabled;}catch(AssistantException){}
        var reason=binding.RevokedAtUtc is not null?"Liên kết đã thu hồi.":!current?"Tài khoản hoặc cấu hình kết nối đã thay đổi; cần xác minh lại.":null;
        return new(binding.Id,binding.CustomerId,binding.ConnectionId,binding.ConversationId,binding.IncomingSequence,binding.Version,binding.VerifiedAtUtc,binding.RevokedAtUtc,current,reason);
    }
    public async Task<IReadOnlyList<CustomerInvoiceAdminBinding>> BindingsAsync(CustomerStaff who,int customer,CancellationToken ct)
    {
        await Customer(who,customer,ct);var rows=await notifications.BindingsAsync(who.StoreId,customer,ct);await Check(who,ct);return rows.Select(Binding).ToArray();
    }
    public async Task<CustomerInvoiceAdminBinding> VerifyAsync(CustomerStaff who,CustomerInvoiceBindingCommand command,CancellationToken ct)
    {
        var customer=await Customer(who,command.CustomerId,ct);
        if(command.RequestId==Guid.Empty||!command.Confirmed||command.ConversationId==Guid.Empty||command.IncomingSequence<=0||
            command.Method is not ("in_person" or "verified_callback")||string.IsNullOrWhiteSpace(command.EvidenceReference)||command.EvidenceReference.Trim().Length is <8 or >240||command.EvidenceReference.Any(char.IsControl))throw AssistantException.Invalid();
        if(Phone(customer.Phone)!="0398946174")throw new AssistantException(409,"invoice_pilot_only","Bản thử chỉ xác minh hồ sơ có số 0398946174; số điện thoại không thay cho bằng chứng danh tính.");
        var connection=Connection(who,command.ConnectionId);
        var evidence=await recipientSource.ResolveAsync(connection,command.ConversationId,command.IncomingSequence,ct);
        await Check(who,ct);await Customer(who,customer.Id,ct);
        if(Connection(who,command.ConnectionId).Binding!=connection.Binding)throw Changed();
        var proof=new InvoiceRecipientBindingProof(who.StoreId,customer.Id,connection.Id,connection.Binding,evidence.SenderProfileId,evidence.ConversationId,
            evidence.IncomingSequence,evidence.NativePeer,Phone(customer.Phone),who.UserId,command.Method,command.EvidenceReference.Trim());
        return Binding(await notifications.VerifyBindingAsync(proof,command.RequestId,ct));
    }
    public async Task RevokeAsync(CustomerStaff who,Guid binding,CustomerInvoiceRevokeCommand command,CancellationToken ct)
    {
        await Check(who,ct);if(binding==Guid.Empty||command.Version<=0||string.IsNullOrWhiteSpace(command.Reason)||command.Reason.Trim().Length is <8 or >240||command.Reason.Any(char.IsControl))throw AssistantException.Invalid();
        await notifications.RevokeBindingAsync(who.StoreId,binding,command.Version,who.UserId,command.Reason.Trim(),ct);
    }
    public Task<IReadOnlyList<CustomerInvoiceAdminSource>> SourcesAsync(CustomerStaff who,int customer,CancellationToken ct)=>ReadSourcesAsync(who,customer,null,ct);
    public Task<IReadOnlyList<CustomerInvoiceAdminSource>> LookupSourcesAsync(CustomerStaff who,int customer,string query,CancellationToken ct)
    {
        query=query?.Trim()??"";if(query.Length is <1 or >50||query.Any(char.IsControl))throw AssistantException.Invalid();
        return ReadSourcesAsync(who,customer,query,ct);
    }
    private async Task<IReadOnlyList<CustomerInvoiceAdminSource>> ReadSourcesAsync(CustomerStaff who,int customer,string? query,CancellationToken ct)
    {
        await Customer(who,customer,ct);
        var canReadInvoices=await OfficialInvoicesAllowed(who,ct);
        var matching=db.Orders.IgnoreQueryFilters().AsNoTracking().Where(row=>row.StoreId==who.StoreId&&!row.IsDeleted&&row.CustomerId==customer&&row.CompletedAtUtc!=null);
        if(query is not null)matching=matching.Where(order=>order.OrderNumber==query||canReadInvoices&&db.InvoiceHeads.IgnoreQueryFilters().Any(invoice=>invoice.StoreId==who.StoreId&&!invoice.IsDeleted&&invoice.OrderId==order.Id&&(invoice.ProviderInvoiceNo==query||invoice.InvoiceNumber==query)));
        var orders=await matching
            .OrderByDescending(row=>row.CompletedAtUtc).ThenByDescending(row=>row.Id).Take(10)
            .Select(row=>new{row.Id,row.OrderNumber,row.CompletedAtUtc,row.GrandTotal,row.Status}).ToArrayAsync(ct);
        var ids=orders.Select(row=>row.Id).ToArray();
        var invoices=await db.InvoiceHeads.IgnoreQueryFilters().AsNoTracking().Where(row=>canReadInvoices&&row.StoreId==who.StoreId&&!row.IsDeleted&&row.OrderId!=null&&ids.Contains(row.OrderId.Value)&&
                row.Order!=null&&row.Order.StoreId==who.StoreId&&!row.Order.IsDeleted&&row.Order.CustomerId==customer)
            .OrderByDescending(row=>row.InvoiceDate).ThenBy(row=>row.LegalEntityId).ThenBy(row=>row.Id).Take(101)
            .Select(row=>new{row.Id,row.OrderId,row.InvoiceNumber,row.InvoiceDate,row.GrandTotal,row.ProviderStatus,row.LegacyReadOnly,row.CorrectionType,row.OriginalInvoiceHeadId,row.IsAutoInvoiceGroup,row.IssuedAtUtc,row.ProviderInvoiceNo,
                row.LegalEntityId,LegalStore=row.LegalEntity==null?(int?)null:row.LegalEntity.StoreId,LegalDeleted=row.LegalEntity!=null&&row.LegalEntity.IsDeleted,
                LegalName=row.LegalEntity!=null&&row.LegalEntity.StoreId==who.StoreId&&!row.LegalEntity.IsDeleted?row.LegalEntity.Name:null,row.OfficialPdfStatus,row.OfficialZipXmlStatus,row.OfficialPdfSha256,row.OfficialPdfByteLength,row.OfficialPdfFileName,row.OfficialZipXmlSha256,row.OfficialZipXmlByteLength,row.OfficialZipXmlFileName,
                Superseded=db.InvoiceHeads.IgnoreQueryFilters().Any(replacement=>replacement.StoreId==who.StoreId&&!replacement.IsDeleted&&replacement.OriginalInvoiceHeadId==row.Id&&replacement.CorrectionType==InvoiceCorrectionType.Replacement&&
                    (replacement.ProviderStatus==InvoiceProviderStatus.Issued||replacement.ProviderStatus==InvoiceProviderStatus.PdfDownloaded||replacement.ProviderStatus==InvoiceProviderStatus.ZipDownloaded||replacement.ProviderStatus==InvoiceProviderStatus.EmailSent))}).ToArrayAsync(ct);
        if(invoices.Length>100)throw new AssistantException(409,"invoice_source_capacity","Có quá nhiều hóa đơn trong 10 đơn đang chọn. Chọn và đối chiếu hóa đơn tại màn hình nguồn trước.");
        var result=new List<CustomerInvoiceAdminSource>();
        foreach(var row in orders)
        {
            var allowed=row.Status==OrderStatus.Completed;
            result.Add(new("Order",row.Id,"Receipt",Clean(row.OrderNumber),"Phiếu mua hàng",row.CompletedAtUtc!.Value,row.GrandTotal,"Cửa hàng",row.Status.ToString(),allowed,allowed?null:"Đơn không còn hoàn tất; kiểm tra nguồn trước khi gửi."));
        }
        foreach(var row in invoices)
        {
            var owner=row.LegalEntityId is null?"Chủ thể cũ":string.IsNullOrWhiteSpace(row.LegalName)?"Chưa xác minh chủ thể":Clean(row.LegalName);
            var legal=row.LegalEntityId is null||row.LegalStore==who.StoreId&&!row.LegalDeleted;
            var issued=row.ProviderStatus is InvoiceProviderStatus.Issued or InvoiceProviderStatus.PdfDownloaded or InvoiceProviderStatus.ZipDownloaded or InvoiceProviderStatus.EmailSent;
            var correction=row.CorrectionType is not null;
            var baseReason=row.IsAutoInvoiceGroup?"Hóa đơn tổng hợp chưa chứng minh được quyền sở hữu của một khách.":row.Superseded?"Hóa đơn đã có bản thay thế chính thức; chọn đúng bản cần gửi.":row.LegacyReadOnly?"Hóa đơn nhập cũ chưa có nguồn đủ để gửi.":!legal?"Chưa xác minh được chủ thể pháp lý của hóa đơn.":!issued||row.IssuedAtUtc is null||string.IsNullOrWhiteSpace(row.ProviderInvoiceNo??row.InvoiceNumber)?"Hóa đơn chưa phát hành chính thức hoặc đã hủy.":orders.Single(order=>order.Id==row.OrderId).Status!=OrderStatus.Completed?"Đơn gốc không còn hoàn tất; kiểm tra nguồn trước khi gửi.":null;
            foreach(var format in new[]{"Pdf","ZipXml","Xml","Link"})
            {
                var pdf=format is "Pdf" or "Link";var kind=(correction?"Correction":"Official")+format;
                var ready=(pdf?row.OfficialPdfStatus:row.OfficialZipXmlStatus)==InvoiceFileDownloadStatus.Downloaded&&
                    (pdf?row.OfficialPdfSha256:row.OfficialZipXmlSha256) is {Length:64}&&
                    (pdf?row.OfficialPdfByteLength:row.OfficialZipXmlByteLength) is >0&&
                    !string.IsNullOrWhiteSpace(pdf?row.OfficialPdfFileName:row.OfficialZipXmlFileName);
                var reason=baseReason??(!ready?"Chưa tải tệp chính thức từ nhà cung cấp.":format=="Link"&&!LinksConfigured?"Chưa cấu hình địa chỉ xem hóa đơn.":null);
                var fileLabel=format switch{"Pdf"=>"PDF","Xml"=>"XML gốc trong gói chính thức","Link"=>"Xem hóa đơn qua liên kết",_=>"Gói ZIP chính thức"};
                result.Add(new("Invoice",row.Id,kind,Clean(row.InvoiceNumber),(correction?"Hóa đơn điều chỉnh / thay thế · ":format=="Link"?"":"Hóa đơn chính thức · ")+fileLabel,
                    row.InvoiceDate,row.GrandTotal,owner,row.ProviderStatus.ToString(),reason is null,reason));
            }
        }
        await Check(who,ct);if(canReadInvoices)await RequireOfficialInvoices(who,ct);return result;
    }
    public async Task<CustomerInvoiceAdminPreview> PreviewAsync(CustomerStaff who,int customer,string sourceType,int sourceId,string kind,CancellationToken ct)
    {
        await Customer(who,customer,ct);
        if(sourceType=="Invoice")await RequireOfficialInvoices(who,ct);
        var preview=await notifications.PreviewSourceAsync(who.StoreId,customer,sourceType,sourceId,kind,ct);
        if(preview.StoreId!=who.StoreId||preview.CustomerId!=customer||preview.SourceType!=sourceType||preview.SourceId!=sourceId||preview.Kind!=kind)throw Changed();
        var owner="Cửa hàng";
        if(sourceType=="Invoice")
        {
            var invoice=await db.InvoiceHeads.IgnoreQueryFilters().AsNoTracking().Where(row=>row.StoreId==who.StoreId&&row.Id==sourceId&&!row.IsDeleted&&row.Order!=null&&row.Order.StoreId==who.StoreId&&!row.Order.IsDeleted&&row.Order.CustomerId==customer)
                .Select(row=>new{row.LegalEntityId,Name=row.LegalEntity!=null&&row.LegalEntity.StoreId==who.StoreId&&!row.LegalEntity.IsDeleted?row.LegalEntity.Name:null}).SingleOrDefaultAsync(ct)
                ??throw Missing("Không tìm thấy hóa đơn thuộc hồ sơ khách.");
            owner=invoice.LegalEntityId is null?"Chủ thể cũ":string.IsNullOrWhiteSpace(invoice.Name)?"Chưa xác minh chủ thể":Clean(invoice.Name);
        }
        else if(sourceType!="Order"||!await db.Orders.IgnoreQueryFilters().AsNoTracking().AnyAsync(row=>row.StoreId==who.StoreId&&row.Id==sourceId&&!row.IsDeleted&&row.CustomerId==customer,ct))throw Missing("Không tìm thấy đơn mua thuộc hồ sơ khách.");
        var label=kind switch{"Receipt"=>"Phiếu mua hàng","OfficialPdf"=>"Hóa đơn chính thức · PDF","OfficialZipXml"=>"Gói ZIP chính thức","OfficialXml"=>"XML gốc trong gói chính thức",
            "CorrectionPdf"=>"Hóa đơn điều chỉnh / thay thế · PDF","CorrectionZipXml"=>"Điều chỉnh / thay thế · Gói ZIP chính thức","CorrectionXml"=>"Điều chỉnh / thay thế · XML gốc trong gói chính thức",
            "OfficialLink"=>"Xem hóa đơn qua liên kết","CorrectionLink"=>"Xem hóa đơn điều chỉnh / thay thế qua liên kết",_=>"Nguồn chưa hỗ trợ"};
        await Check(who,ct);if(sourceType=="Invoice")await RequireOfficialInvoices(who,ct);
        var linkMissing=kind is "OfficialLink" or "CorrectionLink"&&!LinksConfigured;
        return new(sourceType,sourceId,kind,preview.SourceVersion,Clean(preview.Code),label,Clean(preview.Status),preview.GrandTotal,owner,preview.UnsupportedReason is null&&!linkMissing,linkMissing?"Chưa cấu hình địa chỉ xem hóa đơn.":Reason(preview.UnsupportedReason));
    }
    public async Task<IReadOnlyList<CustomerInvoiceAdminOutbox>> OutboxAsync(CustomerStaff who,string? state,int page,CancellationToken ct)
    {
        await Check(who,ct);if(page is <0 or >200||state?.Length>30||state is not null&&!Regex.IsMatch(state,@"\A[A-Za-z]+\z"))throw AssistantException.Invalid();
        var canReadInvoices=await OfficialInvoicesAllowed(who,ct);
        if(!canReadInvoices)
        {
            // Filter before pagination/materialization; official source codes and snapshots remain inaccessible.
            var receipts=await db.InvoiceNotificationOutbox.IgnoreQueryFilters().AsNoTracking().Where(row=>row.StoreId==who.StoreId&&row.SourceType=="Order"&&row.Kind=="Receipt"&&(state==null||state==""||row.State==state))
                .OrderByDescending(row=>row.CreatedAtUtc).ThenBy(row=>row.Id).Skip(page*30).Take(30)
                .Select(row=>new{row.Id,row.CustomerId,row.SourceType,row.SourceId,row.Kind,row.Code,row.State,row.Detail,row.CreatedAtUtc,row.UpdatedAtUtc,row.ZaloJobId,row.Attempts}).ToArrayAsync(ct);
            await Check(who,ct);return receipts.Select(row=>new CustomerInvoiceAdminOutbox(row.Id,row.CustomerId,row.SourceType,row.SourceId,row.Kind,Clean(row.Code),row.State,Reason(row.Detail),row.CreatedAtUtc,row.UpdatedAtUtc,row.ZaloJobId,row.Attempts)).ToArray();
        }
        var rows=await notifications.ListAsync(who.StoreId,state,page+1,ct);await RequireOfficialInvoices(who,ct);return rows.Select(Item).ToArray();
    }
    private static CustomerInvoiceAdminOutbox Item(InvoiceNotificationListItem row)=>new(row.Id,row.CustomerId,row.SourceType,row.SourceId,row.Kind,Clean(row.Code),row.State,Reason(row.Detail),row.CreatedAtUtc,row.UpdatedAtUtc,row.ZaloJobId,row.Attempts);
    private static string? Reason(string? detail)
    {
        if(string.IsNullOrWhiteSpace(detail))return null;var key=Regex.Replace(detail.ToLowerInvariant(),"[^a-z0-9]","");
        if(key.Contains("officiallinknotconfigured"))return "Chưa cấu hình địa chỉ xem hóa đơn.";
        if(key.Contains("sourceorrecipientchanged"))return "Nguồn chứng từ hoặc liên kết người nhận đã thay đổi; lượt gửi giữ lại để đối chiếu.";
        if(key.Contains("sourcechanged")||key.Contains("sourcesnapshot"))return "Nguồn hóa đơn đã thay đổi; lượt gửi giữ lại để đối chiếu.";
        if(key.Contains("revok")||key.Contains("bindingchanged")||key.Contains("recipientchanged"))return "Liên kết người nhận đã thu hồi hoặc thay đổi; không tiếp tục gửi.";
        if(key.Contains("bindingmissing")||key.Contains("recipientmissing")||key.Contains("unverified"))return "Chưa có người nhận đã xác minh cho hồ sơ khách.";
        if(key.Contains("disabled")||key.Contains("paused"))return "Kênh hoặc chức năng đang tắt / tạm dừng.";
        if(key.Contains("missing")||key.Contains("artifact")||key.Contains("official"))return "Chưa có tệp hoặc nguồn chính thức đủ điều kiện gửi.";
        if(key.Contains("unknown")||key.Contains("uncertain")||key.Contains("timeout")||key.Contains("review"))return "Chưa rõ kết quả gửi. Kiểm tra từng phần và kênh gốc trước khi xử lý.";
        return "Lượt gửi cần đối chiếu tại nguồn; chưa đủ điều kiện tiếp tục tự động.";
    }
    public async Task<CustomerInvoiceAdminDetail?> DetailAsync(CustomerStaff who,Guid id,CancellationToken ct)
    {
        await Check(who,ct);
        var source=await db.InvoiceNotificationOutbox.IgnoreQueryFilters().AsNoTracking().Where(row=>row.StoreId==who.StoreId&&row.Id==id).Select(row=>new{row.SourceType,row.Kind}).SingleOrDefaultAsync(ct);
        if(source is not null&&(source.SourceType!="Order"||source.Kind!="Receipt"))await RequireOfficialInvoices(who,ct);
        var detail=await notifications.FindAsync(who.StoreId,id,ct);await Check(who,ct);if(detail is null)return null;
        var official=detail.Item.SourceType!="Order"||detail.Item.Kind!="Receipt"||detail.Snapshot.SourceType=="Invoice"||detail.Snapshot.InvoiceId is not null;
        if(official)await RequireOfficialInvoices(who,ct);
        var parts=new List<CustomerInvoiceAdminPart>();
        if(detail.Prepared is not null)
            parts.AddRange(detail.Prepared.Parts.Take(10).Select((part,index)=>new CustomerInvoiceAdminPart(index+1,part.Kind,
                detail.Item.State is "Unknown" or "Review" or "NeedsReview" or "PartiallySent"?"Uncertain":"Pending",Clean(part.Text,8000),part.FileName is null?null:CustomerZaloMedia.FileName(part.FileName),part.ByteLength,null,null)));
        if(detail.PartsOutcomeJson is {Length:>0 and <=1048576} json)
        {
            try
            {
                using var document=JsonDocument.Parse(json);AssistantJwt.Unique(document.RootElement);
                JsonElement partArray=default;
                if(document.RootElement.TryGetProperty("parts",out var directParts))partArray=directParts;
                else if(document.RootElement.TryGetProperty("deliveries",out var deliveries)&&deliveries.ValueKind==JsonValueKind.Array&&deliveries.GetArrayLength()==1)partArray=deliveries[0].GetProperty("parts");
                if(partArray.ValueKind==JsonValueKind.Array)foreach(var part in partArray.EnumerateArray().Take(10))
                    {
                        var index=part.GetProperty("index").GetInt32();if(index is <1 or >10)continue;
                        string? Value(string key,int max=100)=>part.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?Clean(value.GetString(),max):null;
                        var state=Value("state");if(state is not ("Pending" or "Dispatching" or "Sent" or "FailedBeforeSend" or "Uncertain" or "Cancelled"))state="Uncertain";
                        var previous=parts.FirstOrDefault(row=>row.Index==index);parts.RemoveAll(row=>row.Index==index);
                        var receipt=Value("receiptId",128);if(state!="Sent"||receipt is not null&&!Regex.IsMatch(receipt,@"\A[1-9][0-9]{0,127}\z"))receipt=null;
                        parts.Add(new(index,Value("kind")??previous?.Kind??"File",state??"Uncertain",previous?.Text,previous?.FileName,previous?.ByteLength,receipt,Reason(Value("detail",240))));
                    }
            }
            catch(Exception error)when(error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException){/* Missing evidence stays pending/uncertain; never fabricate receipts. */}
        }
        if(official)await RequireOfficialInvoices(who,ct);
        return new(Item(detail.Item),detail.Binding is null?null:Binding(detail.Binding),Clean(detail.Snapshot.Status),Clean(detail.Snapshot.PaymentStatus),detail.Snapshot.GrandTotal,
            detail.Snapshot.PaidTotal,detail.Snapshot.ChangeDue,detail.Snapshot.BalanceDue,detail.Snapshot.CurrentPoints,parts.OrderBy(row=>row.Index).ToArray());
    }
    public async Task<Guid> EnqueueAsync(CustomerStaff who,CustomerInvoiceSendCommand command,CancellationToken ct)
    {
        await Check(who,ct,reply:true);
        if(command.SourceType=="Invoice")await RequireOfficialInvoices(who,ct);
        if(!command.Confirmed||command.RequestId==Guid.Empty||command.BindingId==Guid.Empty||command.BindingVersion<=0||
            command.ExpectedSourceVersion is not {Length:64}||!command.ExpectedSourceVersion.All(Uri.IsHexDigit))throw AssistantException.Invalid();
        await Customer(who,command.CustomerId,ct);
        var source=await PreviewAsync(who,command.CustomerId,command.SourceType,command.SourceId,command.Kind,ct);
        if(!source.CanQueue)throw new AssistantException(409,"invoice_source_not_ready",source.BlockedReason??"Nguồn chứng từ chưa đủ điều kiện gửi.");
        if(!string.Equals(source.SourceVersion,command.ExpectedSourceVersion,StringComparison.Ordinal))throw new AssistantException(409,"invoice_source_changed","Chứng từ đã thay đổi sau khi xem trước. Đọc lại chứng từ và xác nhận phiên bản mới trước khi gửi.");
        var binding=(await notifications.BindingsAsync(who.StoreId,command.CustomerId,ct)).SingleOrDefault(row=>row.Id==command.BindingId&&row.Version==command.BindingVersion&&row.RevokedAtUtc is null);
        if(binding is null||!Binding(binding).Current)throw Changed();
        await Check(who,ct,reply:true);
        if(command.SourceType=="Invoice")await RequireOfficialInvoices(who,ct);
        return await notifications.EnqueueAsync(new(who.StoreId,command.CustomerId,command.SourceType,command.SourceId,command.Kind,command.RequestId,who.UserId,command.BindingId,command.BindingVersion,command.ExpectedSourceVersion),ct);
    }
    private static AssistantException Missing(string message)=>new(404,"invoice_notification_not_found",message);
    private static AssistantException Changed()=>new(409,"invoice_recipient_changed","Liên kết người nhận hoặc cấu hình đã thay đổi. Kiểm tra và chọn lại trước khi gửi.");
}
