using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IViettelOfficialFileClient
{
    Task<Result<ViettelOfficialFileResultDto>> DownloadOfficialFileAsync(
     int invoiceHeadId,
     ViettelOfficialFileType fileType,
     string baseUrl,
     string username,
     string password,
     InvoiceProviderAuthMode authMode,
     string supplierTaxCode,
     string invoiceNo,
     string templateCode,
     string invoiceSeries,
     DateTime issuedAtUtc,
     CancellationToken ct = default);
}