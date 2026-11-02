using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.InvoiceNotifications;

internal static class InvoiceNotificationSnapshots
{
    public static string Json<T>(T value)=>JsonSerializer.Serialize(value);
    public static T Parse<T>(string value)=>JsonSerializer.Deserialize<T>(value)??throw new InvalidOperationException("Invalid notification evidence.");
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Phone(string? value)
    { var p=new string((value??"").Where(c=>!char.IsWhiteSpace(c)&&c is not ('-' or '.' or '(' or ')')).ToArray());return p.StartsWith("+84",StringComparison.Ordinal)?"0"+p[3..]:p.StartsWith("84",StringComparison.Ordinal)&&p.Length==11?"0"+p[2..]:p; }
    private static DateTime? Utc(DateTime? value)=>value is {} v?DateTime.SpecifyKind(v,DateTimeKind.Utc):null;
    private static readonly JsonSerializerOptions VersionJson=new(){Converters={new ExactDecimalConverter()}};
    private sealed class ExactDecimalConverter:JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer,decimal value,JsonSerializerOptions options)=>writer.WriteRawValue(value.ToString("G29",CultureInfo.InvariantCulture));
    }
    // Canonical numeric scale changes no value and performs no rounding; SQL may materialize 120 as 120.00.
    public static InvoiceDocumentSnapshot Version(InvoiceDocumentSnapshot value)
    {
        var canonical=value with{SourceVersion="",CurrentPoints=null};
        if(value.SourceType=="Invoice")
            canonical=canonical with{Status=value.Status is "Issued" or "PdfDownloaded" or "ZipDownloaded" or "EmailSent"?"Issued":value.Status,
                PaymentStatus="",PaidTotal=0,ChangeDue=0,BalanceDue=0,DepositAmount=0,Payments=[]};
        return value with{SourceVersion=Hash(JsonSerializer.Serialize(canonical,VersionJson))};
    }
    public static bool Issued(InvoiceHead invoice)=>invoice.ProviderStatus is InvoiceProviderStatus.Issued or InvoiceProviderStatus.PdfDownloaded or InvoiceProviderStatus.ZipDownloaded or InvoiceProviderStatus.EmailSent;

    public static async Task<InvoiceDocumentSnapshot> OrderAsync(AppDbContext db,Order order,decimal? points,CancellationToken ct,bool readFresh=false)
    {
        if(!readFresh&&!db.Entry(order).Collection(x=>x.Lines).IsLoaded)await db.Entry(order).Collection(x=>x.Lines).LoadAsync(ct);
        if(!readFresh&&!db.Entry(order).Collection(x=>x.Payments).IsLoaded)await db.Entry(order).Collection(x=>x.Payments).LoadAsync(ct);
        var customerQuery=readFresh?db.Customers.AsNoTracking():db.Customers;
        var customer=order.CustomerId is {} id?await customerQuery.SingleOrDefaultAsync(x=>x.Id==id&&x.StoreId==order.StoreId,ct):null;
        var storeQuery=readFresh?db.Stores.AsNoTracking():db.Stores;
        var store=await storeQuery.SingleOrDefaultAsync(x=>x.Id==order.StoreId,ct);
        return Version(new InvoiceDocumentSnapshot
        {
            StoreId=order.StoreId,CustomerId=order.CustomerId,SourceType="Order",SourceId=order.Id,OrderId=order.Id,OrderCode=order.OrderNumber,Kind="Receipt",
            Code=order.OrderNumber??order.Id.ToString(),Status=order.Status.ToString(),PaymentStatus=order.PaymentStatus.ToString(),
            StoreName=store?.ReceiptName??store?.Name??"",CustomerName=customer?.Name,CustomerPhone=customer?.Phone,FinalizedAtUtc=Utc(order.CompletedAtUtc),
            Subtotal=order.Subtotal,LineDiscountTotal=Math.Round(order.Lines.Where(x=>!x.IsDeleted).Sum(x=>x.LineDiscount),0,MidpointRounding.AwayFromZero),
            DiscountTotal=order.DiscountTotal,OrderDiscount=order.OrderDiscount,PromotionDiscountTotal=order.PromotionDiscountTotal,
            ComboDiscountTotal=order.ComboDiscountTotal,VoucherDiscountTotal=order.VoucherDiscountTotal,GrandTotal=order.GrandTotal,
            DepositAmount=order.DepositAmount,PaidTotal=order.PaidTotal,ChangeDue=order.ChangeDue,BalanceDue=order.BalanceDue,CurrentPoints=points,
            Lines=order.Lines.Where(x=>!x.IsDeleted).OrderBy(x=>x.Id).Select(x=>new InvoiceDocumentLine(x.Id,x.ItemName,x.SellingUnitName??x.UnitName,x.Quantity,x.UnitPrice,x.LineDiscount,x.LineTotal){PromotionDiscount=x.PromotionDiscount,ComboAllocatedDiscount=x.ComboAllocatedDiscount,PromotionType=x.PromotionType?.ToString(),IsPromotionGift=x.IsPromotionGift}).ToArray(),
            Payments=order.Payments.Where(x=>!x.IsDeleted).OrderBy(x=>x.Id).Select(x=>new InvoiceDocumentPayment(x.Method.ToString(),x.Amount,x.ReferenceCode)).ToArray(),
            UnsupportedReason=order.Status!=OrderStatus.Completed||order.CompletedAtUtc is null?"order_not_finalized":customer is null?"customer_missing":!customer.IsActive?"customer_inactive":store is null?"store_missing":!store.IsActive?"store_inactive":null
        });
    }

    public static async Task<InvoiceDocumentSnapshot> InvoiceAsync(AppDbContext db,InvoiceHead invoice,string kind,CancellationToken ct,bool readFresh=false)
    {
        IQueryable<Order> orderQuery=readFresh?db.Orders.AsNoTracking().Include(x=>x.Lines).Include(x=>x.Payments):db.Orders;
        var order=invoice.OrderId is {} id?await orderQuery.SingleOrDefaultAsync(x=>x.Id==id&&x.StoreId==invoice.StoreId,ct):null;
        var receipt=order is null?null:await OrderAsync(db,order,null,ct,readFresh);
        if(!readFresh&&!db.Entry(invoice).Collection(x=>x.Details).IsLoaded)await db.Entry(invoice).Collection(x=>x.Details).LoadAsync(ct);
        var zip=kind is "OfficialZipXml" or "CorrectionZipXml" or "OfficialXml" or "CorrectionXml";
        var path=zip?invoice.ZipFilePath:invoice.PdfFilePath;var filename=zip?invoice.OfficialZipXmlFileName:invoice.OfficialPdfFileName;
        var sha=zip?invoice.OfficialZipXmlSha256:invoice.OfficialPdfSha256;var length=zip?invoice.OfficialZipXmlByteLength:invoice.OfficialPdfByteLength;
        var status=zip?invoice.OfficialZipXmlStatus:invoice.OfficialPdfStatus;
        var legalQuery=readFresh?db.LegalEntities.AsNoTracking():db.LegalEntities;
        var legalEntity=invoice.LegalEntityId is {} owner?await legalQuery.SingleOrDefaultAsync(x=>x.Id==owner&&x.StoreId==invoice.StoreId,ct):null;
        var ownerOk=invoice.LegalEntityId is null||legalEntity is not null;
        var superseded=await db.InvoiceHeads.AnyAsync(x=>x.OriginalInvoiceHeadId==invoice.Id&&x.CorrectionType==InvoiceCorrectionType.Replacement&&
            (x.ProviderStatus==InvoiceProviderStatus.Issued||x.ProviderStatus==InvoiceProviderStatus.PdfDownloaded||x.ProviderStatus==InvoiceProviderStatus.ZipDownloaded||x.ProviderStatus==InvoiceProviderStatus.EmailSent),ct);
        var extension=Path.GetExtension(filename??"").ToLowerInvariant();
        var contentType=zip?extension switch{".xml"=>"application/xml",".zip"=>"application/zip",_=>null}:extension==".pdf"?"application/pdf":null;
        var artifact=path is not null&&filename is not null&&length is >0&&sha?.Length==64&&contentType is not null
            ?new InvoiceDocumentArtifact(path,filename,contentType,length.Value,sha):null;
        var unsupported=invoice.IsAutoInvoiceGroup?"group_ownership_unproved":invoice.LegacyReadOnly||order is null?"legacy_or_orderless_invoice":
            !ownerOk?"legal_owner_mismatch":receipt?.UnsupportedReason is {} reason?reason:
            invoice.ProviderStatus==InvoiceProviderStatus.Cancelled?"invoice_cancelled":superseded?"invoice_replaced":
            !Issued(invoice)||invoice.IssuedAtUtc is null||string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo??invoice.InvoiceNumber)?"invoice_not_official":
            kind=="DraftPreview"?"draft_requires_explicit_preview":status!=InvoiceFileDownloadStatus.Downloaded||artifact is null?"artifact_proof_missing":
            (kind is "OfficialXml" or "CorrectionXml")&&artifact.ContentType!="application/zip"?"official_xml_parent_zip_required":
            (invoice.CorrectionType is not null)!=(kind is "CorrectionPdf" or "CorrectionZipXml" or "CorrectionXml" or "CorrectionLink")?"correction_kind_mismatch":null;
        return Version(new InvoiceDocumentSnapshot
        {
            StoreId=invoice.StoreId,CustomerId=receipt?.CustomerId,SourceType="Invoice",SourceId=invoice.Id,InvoiceId=invoice.Id,OrderId=invoice.OrderId,
            OriginalInvoiceId=invoice.OriginalInvoiceHeadId,LegalEntityId=invoice.LegalEntityId,OriginalInvoiceCode=invoice.OriginalInvoiceNo,CorrectionType=invoice.CorrectionType?.ToString(),Kind=kind,OrderCode=receipt?.Code,LegalEntityName=legalEntity?.Name,
            Code=invoice.ProviderInvoiceNo??invoice.InvoiceNumber??invoice.Id.ToString(),Status=invoice.ProviderStatus.ToString(),PaymentStatus=receipt?.PaymentStatus??"",
            StoreName=receipt?.StoreName??"",CustomerName=receipt?.CustomerName,CustomerPhone=receipt?.CustomerPhone,FinalizedAtUtc=receipt?.FinalizedAtUtc,IssuedAtUtc=Utc(invoice.IssuedAtUtc),
            Subtotal=invoice.SubTotal,GrandTotal=invoice.GrandTotal,PaidTotal=receipt?.PaidTotal??0,ChangeDue=receipt?.ChangeDue??0,BalanceDue=receipt?.BalanceDue??0,
            Lines=invoice.Details.Where(x=>!x.IsDeleted).OrderBy(x=>x.Id).Select(x=>new InvoiceDocumentLine(x.Id,x.ItemName,x.UnitName,x.Quantity,x.UnitPrice,0,x.TotalAmount)).ToArray(),
            Payments=receipt?.Payments??[],Artifact=artifact,UnsupportedReason=unsupported
        });
    }
}
