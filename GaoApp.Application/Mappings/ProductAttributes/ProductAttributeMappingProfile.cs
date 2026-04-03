using AutoMapper;
using GaoApp.Application.DTOs.ProductAttributes;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Mappings.ProductAttributes;

public sealed class ProductAttributeMappingProfile : Profile
{
    public ProductAttributeMappingProfile()
    {
        CreateMap<ProductAttribute, ProductAttributeListItemDto>();

        CreateMap<ProductAttribute, ProductAttributeEditDto>();

        CreateMap<CreateProductAttributeRequest, ProductAttributeEditDto>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.RowVersion, opt => opt.Ignore());

        CreateMap<UpdateProductAttributeRequest, ProductAttributeEditDto>();

        CreateMap<ProductAttributeEditDto, ProductAttribute>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.StoreId, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.RowVersion, opt => opt.Ignore())
            .ForMember(dest => dest.Values, opt => opt.Ignore());
    }
}