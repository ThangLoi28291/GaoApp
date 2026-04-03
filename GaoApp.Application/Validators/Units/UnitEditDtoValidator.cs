
using FluentValidation;
using GaoApp.Application.DTOs.Units;

namespace GaoApp.Application.Validators.Units;

public sealed class UnitEditDtoValidator : AbstractValidator<UnitEditDto>
{
    public UnitEditDtoValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Vui lòng nhập mã đơn vị.")
            .MaximumLength(30).WithMessage("Mã đơn vị tối đa 30 ký tự.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Vui lòng nhập tên đơn vị.")
            .MaximumLength(200).WithMessage("Tên đơn vị tối đa 200 ký tự.");

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Thứ tự sắp xếp phải lớn hơn hoặc bằng 0.");
    }
}