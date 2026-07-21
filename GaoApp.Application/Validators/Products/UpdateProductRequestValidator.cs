using FluentValidation;
using GaoApp.Application.DTOs.Products;

namespace GaoApp.Application.Validators.Products;

public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("Id không hợp lệ.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Vui lòng nhập tên sản phẩm.")
            .MaximumLength(200).WithMessage("Tên tối đa 200 ký tự.");

        RuleFor(x => x.Alias)
            .NotEmpty().WithMessage("Alias không hợp lệ.")
            .MaximumLength(200).WithMessage("Alias tối đa 200 ký tự.");

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

        RuleFor(x => x.RowVersion)
            .NotNull().WithMessage("Thiếu thông tin đồng bộ dữ liệu.");
    }
}