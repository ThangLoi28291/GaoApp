using FluentAssertions;
using GaoApp.Application.DTOs.AttributeValues;
using GaoApp.Application.DTOs.Brands;
using GaoApp.Application.DTOs.Categories;
using GaoApp.Application.DTOs.ProductAttributes;
using GaoApp.Application.DTOs.Units;
using GaoApp.Application.Mappings.AttributeValues;
using GaoApp.Application.Mappings.Brands;
using GaoApp.Application.Mappings.Categories;
using GaoApp.Application.Mappings.ProductAttributes;
using GaoApp.Application.Mappings.Suppliers;
using GaoApp.Application.Mappings.Units;
using GaoApp.Domain.Entities;

namespace GaoApp.Tests.Mappings;

public sealed class ManualMappingContractTests
{
    [Fact]
    public void Brand_mapping_should_preserve_public_fields_and_protect_entity_identity()
    {
        var rowVersion = new byte[] { 1, 2, 3 };
        var entity = new Brand
        {
            Id = 10,
            StoreId = 4,
            Code = "BRD01",
            Name = "Brand 01",
            Description = "Description",
            IsActive = true,
            IsDeleted = true,
            RowVersion = rowVersion
        };

        var listDto = entity.ToListItemDto();
        var editDto = entity.ToEditDto();
        var newEntity = editDto.ToEntity();

        listDto.Should().BeEquivalentTo(new BrandListItemDto
        {
            Id = 10,
            Code = "BRD01",
            Name = "Brand 01",
            Status = true
        });
        editDto.Description.Should().Be("Description");
        editDto.RowVersion.Should().Equal(rowVersion);
        editDto.RowVersion.Should().NotBeSameAs(rowVersion);
        newEntity.Id.Should().Be(0);
        newEntity.StoreId.Should().Be(0);
        newEntity.IsDeleted.Should().BeFalse();
        newEntity.RowVersion.Should().BeNull();
        newEntity.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Category_mapping_should_include_parent_name_and_editable_fields()
    {
        var entity = new Category
        {
            Id = 20,
            Code = "CAT01",
            Name = "Category 01",
            ParentId = 5,
            Parent = new Category { Id = 5, Code = "ROOT", Name = "Root" },
            SortOrder = 9,
            IsActive = false
        };

        var listDto = entity.ToListItemDto();
        var editDto = entity.ToEditDto();
        var newEntity = editDto.ToEntity();

        listDto.ParentName.Should().Be("Root");
        editDto.Should().BeEquivalentTo(new CategoryEditDto
        {
            Id = 20,
            Code = "CAT01",
            Name = "Category 01",
            ParentId = 5,
            SortOrder = 9,
            IsActive = false
        });
        newEntity.Id.Should().Be(20);
        newEntity.ParentId.Should().Be(5);
        newEntity.SortOrder.Should().Be(9);
        newEntity.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Supplier_mapping_should_preserve_status_contact_and_concurrency_fields()
    {
        var rowVersion = new byte[] { 4, 5, 6 };
        var entity = new Supplier
        {
            Id = 30,
            Code = "NCC01",
            Name = "Supplier 01",
            Phone = "0900000000",
            Email = "ncc@example.test",
            Address = "Address",
            ContactName = "Contact",
            TaxCode = "TAX01",
            Note = "Note",
            IsActive = true,
            RowVersion = rowVersion
        };

        var listDto = entity.ToListItemDto();
        var editDto = entity.ToEditDto();

        listDto.Status.Should().BeTrue();
        listDto.Phone.Should().Be("0900000000");
        editDto.Email.Should().Be("ncc@example.test");
        editDto.ContactName.Should().Be("Contact");
        editDto.TaxCode.Should().Be("TAX01");
        editDto.RowVersion.Should().Equal(rowVersion);
        editDto.RowVersion.Should().NotBeSameAs(rowVersion);
    }

    [Fact]
    public void Unit_mapping_should_preserve_conversion_fields_and_not_copy_protected_fields()
    {
        var request = new UpdateUnitRequest
        {
            Id = 40,
            Code = "BOX",
            Name = "Box",
            Status = true,
            IsBase = false,
            SortOrder = 3,
            RowVersion = new byte[] { 7, 8 }
        };

        var editDto = request.ToEditDto();
        var entity = editDto.ToEntity();

        editDto.Id.Should().Be(40);
        editDto.RowVersion.Should().Equal(7, 8);
        entity.Id.Should().Be(0);
        entity.StoreId.Should().Be(0);
        entity.Code.Should().Be("BOX");
        entity.IsBase.Should().BeFalse();
        entity.SortOrder.Should().Be(3);
        entity.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Product_attribute_mapping_should_not_copy_values_or_entity_identity()
    {
        var request = new CreateProductAttributeRequest
        {
            Code = "COLOR",
            Name = "Color",
            Status = true
        };

        var editDto = request.ToEditDto();
        var entity = editDto.ToEntity();

        editDto.Should().BeEquivalentTo(new ProductAttributeEditDto
        {
            Code = "COLOR",
            Name = "Color",
            Status = true
        });
        entity.Id.Should().Be(0);
        entity.StoreId.Should().Be(0);
        entity.Values.Should().BeEmpty();
        entity.Code.Should().Be("COLOR");
        entity.Status.Should().BeTrue();
    }

    [Fact]
    public void Attribute_value_mapping_should_include_attribute_name_and_audit_fields()
    {
        var createdAt = new DateTime(2026, 7, 21, 10, 0, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddHours(1);
        var rowVersion = new byte[] { 9, 10 };
        var entity = new AttributeValue
        {
            Id = 50,
            AttributeId = 6,
            Attribute = new ProductAttribute
            {
                Id = 6,
                Code = "SIZE",
                Name = "Size"
            },
            Code = "L",
            Name = "Large",
            Status = true,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = updatedAt,
            CreatedBy = 11,
            UpdatedBy = 12,
            RowVersion = rowVersion
        };

        var listDto = entity.ToListItemDto();
        var editDto = entity.ToEditDto();
        var newEntity = editDto.ToEntity();

        listDto.AttributeName.Should().Be("Size");
        listDto.CreatedAtUtc.Should().Be(createdAt);
        listDto.UpdatedBy.Should().Be(12);
        editDto.RowVersion.Should().Equal(rowVersion);
        editDto.RowVersion.Should().NotBeSameAs(rowVersion);
        newEntity.Id.Should().Be(0);
        newEntity.StoreId.Should().Be(0);
        newEntity.Attribute.Should().BeNull();
        newEntity.AttributeId.Should().Be(6);
        newEntity.CreatedAtUtc.Should().Be(createdAt);
        newEntity.UpdatedAtUtc.Should().Be(updatedAt);
    }
}
