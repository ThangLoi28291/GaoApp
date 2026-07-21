using AutoMapper;
using GaoApp.Application.DTOs.Categories;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Mappings.Categories;

/// <summary>
/// Mapping profile cho Category.
/// </summary>
public class CategoryMappingProfile : Profile
{
    public CategoryMappingProfile()
    {
        // Entity -> Edit DTO
        CreateMap<Category, CategoryEditDto>();

        // Edit DTO -> Entity
        CreateMap<CategoryEditDto, Category>();

        // Entity -> List item DTO
        CreateMap<Category, CategoryListItemDto>()
            .ForMember(
                dest => dest.ParentName,
                opt => opt.MapFrom(src => src.Parent != null ? src.Parent.Name : null));
    }
}