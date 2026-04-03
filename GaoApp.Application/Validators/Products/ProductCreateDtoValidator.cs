using FluentValidation;
using GaoApp.Application.DTOs.Products;

namespace GaoApp.Application.Validators.Products;

public sealed class ProductCreateDtoValidator : AbstractValidator<ProductCreateDto>
{
    public ProductCreateDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Vui lòng nhập tên sản phẩm.")
            .MaximumLength(200).WithMessage("Tên tối đa 200 ký tự.");

        RuleFor(x => x.Alias)
            .MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Alias))
            .WithMessage("Alias tối đa 200 ký tự.");

        RuleFor(x => x.CategoryId)
            .NotNull().WithMessage("Vui lòng chọn danh mục.")
            .GreaterThan(0).WithMessage("Vui lòng chọn danh mục.");

        RuleFor(x => x.SupplierId)
            .NotNull().WithMessage("Vui lòng chọn nhà cung cấp.")
            .GreaterThan(0).WithMessage("Vui lòng chọn nhà cung cấp.");

        RuleFor(x => x.BaseUnitId)
            .NotNull().WithMessage("Vui lòng chọn đơn vị.")
            .GreaterThan(0).WithMessage("Vui lòng chọn đơn vị.");

        RuleFor(x => x.BasePrice)
            .GreaterThanOrEqualTo(0).WithMessage("Giá cơ bản không hợp lệ.");

        RuleFor(x => x.Description)
            .MaximumLength(500).When(x => !string.IsNullOrWhiteSpace(x.Description))
            .WithMessage("Mô tả tối đa 500 ký tự.");
    }
}