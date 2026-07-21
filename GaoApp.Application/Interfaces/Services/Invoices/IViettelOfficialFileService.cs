using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IViettelOfficialFileService
{
    Task<Result<ViettelOfficialFileResultDto>> DownloadAndSaveAsync(
        int invoiceHeadId,
        ViettelOfficialFileType fileType,
        CancellationToken ct = default);

    Task<Result<ViettelOfficialFileResultDto>> ReadSavedAsync(
        int invoiceHeadId,
        ViettelOfficialFileType fileType,
        CancellationToken ct = default);
}