using FluentValidation;

namespace GaoApp.Web.Areas.Admin.ViewModels.Account;

public sealed class ChangeOwnPasswordVm
{
    public string CurrentPassword { get; set; } = "";

    public string NewPassword { get; set; } = "";

    public string ConfirmPassword { get; set; } = "";
}

public sealed class ChangeOwnPasswordValidator : AbstractValidator<ChangeOwnPasswordVm>
{
    public ChangeOwnPasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Nhập mật khẩu hiện tại.")
            .MaximumLength(512).WithMessage("Mật khẩu hiện tại quá dài.");
        RuleFor(x => x.NewPassword).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Nhập mật khẩu mới.")
            .Length(12, 128).WithMessage("Mật khẩu mới phải có từ 12 đến 128 ký tự.");
        RuleFor(x => x.ConfirmPassword).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Nhập lại mật khẩu mới.")
            .MaximumLength(128).WithMessage("Mật khẩu xác nhận quá dài.")
            .Equal(x => x.NewPassword).WithMessage("Mật khẩu xác nhận chưa khớp.");
    }
}
