using FluentValidation;
using GaoApp.Application.DTOs.StoreBankAccounts;


namespace GaoApp.Application.Validators.StoreBankAccounts;
public class StoreBankAccountUpsertValidator
    : AbstractValidator<StoreBankAccountUpsertDto>
{
    public StoreBankAccountUpsertValidator()
    {
        RuleFor(x => x.BankCode)
            .NotEmpty()
            .MaximumLength(30);

        RuleFor(x => x.BankName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.AccountNumber)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.AccountName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.ProviderCode)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.VietQrBankBin)
            .MaximumLength(100);

        RuleFor(x => x.NoteTemplate)
            .MaximumLength(500);
    }
}
