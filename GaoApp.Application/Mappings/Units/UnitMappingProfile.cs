using AutoMapper;
using GaoApp.Application.DTOs.Units;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Mappings.Units;

public sealed class UnitMappingProfile : Profile
{
    public UnitMappingProfile()
    {
        CreateMap<Unit, UnitListItemDto>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.IsActive));

        CreateMap<Unit, UnitEditDto>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.IsActive));

        CreateMap<CreateUnitRequest, UnitEditDto>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.RowVersion, opt => opt.Ignore());

        CreateMap<UpdateUnitRequest, UnitEditDto>();

        CreateMap<UnitEditDto, Unit>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.StoreId, opt => opt.Ignore())
            .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.Status))
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.RowVersion, opt => opt.Ignore());
    }
}