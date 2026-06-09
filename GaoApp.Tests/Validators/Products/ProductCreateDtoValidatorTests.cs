using FluentValidation.TestHelper;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Validators.Products;

namespace GaoApp.Tests.Validators;

public class ProductCreateDtoValidatorTests
{
    private readonly ProductCreateDtoValidator _validator;

    public ProductCreateDtoValidatorTests()
    {
        _validator = new ProductCreateDtoValidator();
    }

    [Fact]
    public void Should_Have_Error_When_Name_Is_Empty()
    {
        var dto = CreateValidDto();
        dto.Name = "";

        var result = _validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Should_Not_Have_Error_When_Valid()
    {
        var dto = CreateValidDto();

        var result = _validator.TestValidate(dto);

        result.ShouldNotHaveAnyValidationErrors();
    }

    private static ProductCreateDto CreateValidDto()
    {
        return new ProductCreateDto
        {
            Name = "Test product",
            Alias = "test-product",
            CategoryId = 1,
            SupplierId = 1,
            BaseUnitId = 1,
            BasePrice = 1000
        };
    }
}