using GaoApp.Application.DTOs.StoreBankAccounts;

namespace GaoApp.Web.Areas.Admin.ViewModels.StoreBankAccounts;

public class StoreBankAccountIndexVM
{
    public string? SearchString { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public int TotalItems { get; set; }

    public List<StoreBankAccountUpsertDto> Items { get; set; } = new();
}