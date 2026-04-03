using FluentValidation;
using GaoApp.Application.DTOs.Brands;

namespace GaoApp.Application.Validators.Brands;

/// <summary>
/// Validator dùng chung cho dữ liệu Brand ở tầng service.
/// Duplicate check không làm ở đây vì cần truy DB, để service xử lý.
/// </summary>
public sealed class BrandEditDtoValidator : AbstractValidator<BrandEditDto>
{
    public BrandEditDtoValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Vui lòng nhập mã thương hiệu.")
            .MaximumLength(30).WithMessage("Mã thương hiệu tối đa 30 ký tự.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Vui lòng nhập tên thương hiệu.")
            .MaximumLength(200).WithMessage("Tên thương hiệu tối đa 200 ký tự.");

        RuleFor(x => x.Description)
            .MaximumLength(300).WithMessage("Mô tả tối đa 300 ký tự.");
    }
}