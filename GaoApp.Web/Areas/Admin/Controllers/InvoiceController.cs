using GaoApp.Application.Interfaces.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public partial class InvoiceController : Controller
{
    private readonly IInvoiceService _invoiceService;
    private readonly IViettelInvoicePayloadBuilder _viettelPayloadBuilder;
    private readonly IViettelInvoicePreviewService _viettelPreviewService;
    private readonly IViettelInvoiceIssueService _viettelIssueService;
    private readonly IViettelOfficialFileService _viettelOfficialFileService;
    private readonly IViettelInvoiceSyncService _viettelInvoiceSyncService;
    private readonly IViettelInvoiceEmailService _viettelInvoiceEmailService;
    private readonly IInvoiceCorrectionService _invoiceCorrectionService;
    private readonly IInvoiceViettelDashboardService _viettelDashboardService;
    public InvoiceController(
        IInvoiceService invoiceService,
        IViettelInvoicePayloadBuilder viettelPayloadBuilder,
        IViettelInvoicePreviewService viettelPreviewService,
        IViettelInvoiceIssueService viettelIssueService,
        IViettelOfficialFileService viettelOfficialFileService,
        IViettelInvoiceSyncService viettelInvoiceSyncService,
        IViettelInvoiceEmailService viettelInvoiceEmailService,
        IInvoiceCorrectionService invoiceCorrectionService,
        IInvoiceViettelDashboardService viettelDashboardService)
    {
        _invoiceService = invoiceService;
        _viettelPayloadBuilder = viettelPayloadBuilder;
        _viettelPreviewService = viettelPreviewService;
        _viettelIssueService = viettelIssueService;
        _viettelOfficialFileService = viettelOfficialFileService;
        _viettelInvoiceSyncService = viettelInvoiceSyncService;
        _viettelInvoiceEmailService = viettelInvoiceEmailService;
        _invoiceCorrectionService = invoiceCorrectionService;
        _viettelDashboardService = viettelDashboardService;
    }
}