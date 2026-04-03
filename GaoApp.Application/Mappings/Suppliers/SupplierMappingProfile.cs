using AutoMapper;
using GaoApp.Application.DTOs.Suppliers;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Mappings.Suppliers;

public sealed class SupplierMappingProfile : Profile
{
    public SupplierMappingProfile()
    {
        CreateMap<Supplier, SupplierEditDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.IsActive));

        CreateMap<Supplier, SupplierListItemDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.IsActive));

        // Chỉ dùng cho create entity mới.
        // Update sẽ gán field thủ công để tránh map đè bừa.
        CreateMap<SupplierEditDto, Supplier>()
            .ForMember(d => d.IsActive, opt => opt.MapFrom(s => s.Status))
            .ForMember(d => d.Id, opt => opt.Ignore());
    }
}