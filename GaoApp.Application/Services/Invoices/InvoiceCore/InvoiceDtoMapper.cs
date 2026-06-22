using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

internal static class InvoiceDtoMapper
{
    public static InvoiceHeadDto ToDetailDto(InvoiceHead entity)
    {
        return new InvoiceHeadDto
        {
            Id = entity.Id,
            StoreId = entity.StoreId,
            OrderId = entity.OrderId,
            InvoiceNumber = entity.InvoiceNumber,
            InvoiceDate = entity.InvoiceDate,

            BuyerType = string.IsNullOrWhiteSpace(entity.BuyerType)
                ? InvoiceBuyerTypes.NoInvoice
                : entity.BuyerType,

            BuyerName = entity.BuyerName,
            BuyerLegalName = entity.BuyerLegalName,
            BuyerTaxCode = entity.BuyerTaxCode,
            BuyerAddress = entity.BuyerAddress,
            BuyerEmail = entity.BuyerEmail,
            BuyerPhone = entity.BuyerPhone,

            TotalQuantity = entity.TotalQuantity,
            SubTotal = entity.SubTotal,
            VatAmount = entity.VatAmount,
            GrandTotal = entity.GrandTotal,

            Note = entity.Note,
            IsLocked = entity.IsLocked,
            LockedAtUtc = entity.LockedAtUtc,
            LockedByUserId = entity.LockedByUserId,
            LockReason = entity.LockReason,

            TransactionUuid = entity.TransactionUuid,
            ProviderCode = entity.ProviderCode,
            SupplierTaxCode = entity.SupplierTaxCode,
            InvoiceType = entity.InvoiceType,
            TemplateCode = entity.TemplateCode,
            InvoiceSeries = entity.InvoiceSeries,

            ProviderStatus = entity.ProviderStatus,
            ProviderInvoiceNo = entity.ProviderInvoiceNo,
            ProviderTransactionId = entity.ProviderTransactionId,
            ReservationCode = entity.ReservationCode,
            CodeOfTax = entity.CodeOfTax,

            IssuedAtUtc = entity.IssuedAtUtc,
            LastSyncedAtUtc = entity.LastSyncedAtUtc,
            LastErrorCode = entity.LastErrorCode,
            LastErrorMessage = entity.LastErrorMessage,
            PdfFilePath = entity.PdfFilePath,
            ZipFilePath = entity.ZipFilePath,
            OfficialPdfStatus = entity.OfficialPdfStatus,
            OfficialPdfDownloadedAtUtc = entity.OfficialPdfDownloadedAtUtc,
            OfficialPdfFileName = entity.OfficialPdfFileName,

            OfficialZipXmlStatus = entity.OfficialZipXmlStatus,
            OfficialZipXmlDownloadedAtUtc = entity.OfficialZipXmlDownloadedAtUtc,
            OfficialZipXmlFileName = entity.OfficialZipXmlFileName,

            EmailStatus = entity.EmailStatus,
            EmailSentAtUtc = entity.EmailSentAtUtc,
            LastEmailTo = entity.LastEmailTo,
            EmailSendCount = entity.EmailSendCount,
            LastEmailErrorMessage = entity.LastEmailErrorMessage,

            OriginalInvoiceHeadId = entity.OriginalInvoiceHeadId,
            OriginalInvoiceNo = entity.OriginalInvoiceNo,
            OriginalInvoiceIssuedAtUtc = entity.OriginalInvoiceIssuedAtUtc,
            CorrectionType = entity.CorrectionType,
            AdjustedNote = entity.AdjustedNote,
            AdditionalReferenceDesc = entity.AdditionalReferenceDesc,
            AdditionalReferenceDateUtc = entity.AdditionalReferenceDateUtc,

            Details = entity.Details
                .Where(x => !x.IsDeleted)
                .OrderBy(x => x.Id)
                .Select(ToDetailLineDto)
                .ToList()
        };
    }

    public static InvoiceDetailDto ToDetailLineDto(InvoiceDetail entity)
    {
        return new InvoiceDetailDto
        {
            Id = entity.Id,
            InvoiceHeadId = entity.InvoiceHeadId,
            OrderLineId = entity.OrderLineId,
            ProductVariantId = entity.ProductVariantId,
            SourceType = entity.SourceType,

            ItemName = entity.ItemName,
            UnitName = entity.UnitName,

            Quantity = entity.Quantity,
            UnitPrice = entity.UnitPrice,
            Amount = entity.Amount,

            VatRate = entity.VatRate,
            VatAmount = entity.VatAmount,
            TotalAmount = entity.TotalAmount,

            Note = entity.Note
        };
    }

    public static InvoiceListItemDto ToListItemDto(InvoiceHead entity)
    {
        return new InvoiceListItemDto
        {
            Id = entity.Id,
            OrderId = entity.OrderId,
            OrderNumber = entity.Order?.OrderNumber,
            InvoiceNumber = entity.InvoiceNumber,
            InvoiceDate = entity.InvoiceDate,

            BuyerName = !string.IsNullOrWhiteSpace(entity.BuyerLegalName)
                ? entity.BuyerLegalName
                : entity.BuyerName,

            TotalQuantity = entity.TotalQuantity,
            SubTotal = entity.SubTotal,
            VatAmount = entity.VatAmount,
            GrandTotal = entity.GrandTotal,

            IsLocked = entity.IsLocked,

            DetailCount = entity.Details.Count(d => !d.IsDeleted),

            AutoLineCount = entity.Details.Count(d =>
                !d.IsDeleted &&
                d.SourceType == InvoiceDetailSourceType.FromOrderLine),

            ManualLineCount = entity.Details.Count(d =>
                !d.IsDeleted &&
                d.SourceType == InvoiceDetailSourceType.Manual)
        };
    }
}