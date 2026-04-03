using AutoMapper;
using GaoApp.Application.DTOs.AttributeValues;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Mappings.AttributeValues;

public sealed class AttributeValueMappingProfile : Profile
{
    public AttributeValueMappingProfile()
    {
        CreateMap<AttributeValue, AttributeValueListItemDto>()
            .ForMember(dest => dest.AttributeName, opt => opt.MapFrom(src => src.Attribute != null ? src.Attribute.Name : string.Empty));

        CreateMap<AttributeValue, AttributeValueEditDto>();

        CreateMap<CreateAttributeValueRequest, AttributeValueEditDto>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.RowVersion, opt => opt.Ignore());

        CreateMap<UpdateAttributeValueRequest, AttributeValueEditDto>();

        CreateMap<AttributeValueEditDto, AttributeValue>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.StoreId, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.RowVersion, opt => opt.Ignore())
            .ForMember(dest => dest.Attribute, opt => opt.Ignore());
    }
}