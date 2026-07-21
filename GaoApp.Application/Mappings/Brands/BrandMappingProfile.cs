using AutoMapper;
using GaoApp.Application.DTOs.Brands;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Mappings.Brands;

public sealed class BrandMappingProfile : Profile
{
    public BrandMappingProfile()
    {
        // Entity -> List DTO
        CreateMap<Brand, BrandListItemDto>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.IsActive));

        // Entity -> Edit DTO
        CreateMap<Brand, BrandEditDto>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.IsActive));

        // Create request -> Edit DTO nội bộ
        CreateMap<CreateBrandRequest, BrandEditDto>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.RowVersion, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status));

        // Update request -> Edit DTO nội bộ
        CreateMap<UpdateBrandRequest, BrandEditDto>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status));

        // Edit DTO -> Entity
        // Lưu ý:
        // - Không map Id / StoreId / RowVersion để tránh overwrite không an toàn
        // - IsDeleted do tầng persistence quản lý
        CreateMap<BrandEditDto, Brand>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.StoreId, opt => opt.Ignore())
            .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.Status))
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.RowVersion, opt => opt.Ignore());
    }
}