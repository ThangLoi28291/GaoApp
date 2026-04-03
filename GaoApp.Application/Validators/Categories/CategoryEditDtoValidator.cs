using FluentValidation;
using GaoApp.Application.DTOs.Categories;

namespace GaoApp.Application.Validators.Categories;

/// <summary>
/// Validator cho màn hình create/edit category.
/// Chỉ kiểm tra dữ liệu đầu vào cơ bản.
/// Business rule như trùng mã/trùng tên/parent tồn tại để service xử lý.
/// </summary>
public class CategoryEditDtoValidator : AbstractValidator<CategoryEditDto>
{
    public CategoryEditDtoValidator()
    {
        RuleFor(x => x.Code)
            .MaximumLength(50)
            .WithMessage("Mã không được vượt quá 50 ký tự.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên danh mục là bắt buộc.")
            .MaximumLength(200).WithMessage("Tên danh mục không được vượt quá 200 ký tự.");

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Thứ tự không được âm.");

        RuleFor(x => x.ParentId)
            .GreaterThan(0)
            .When(x => x.ParentId.HasValue)
            .WithMessage("Danh mục cha không hợp lệ.");
    }
}