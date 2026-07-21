namespace GaoApp.Application.DTOs.Products;

public class AttributeWithValuesDto
{
    public int AttributeId { get; set; }
    public string AttributeName { get; set; } = "";
    public List<AttributeValueMiniDto> Values { get; set; } = new();
}

public class AttributeValueMiniDto
{
    public int Id { get; set; }
    public string Code { get; set; } = ""; // dùng build SKU
    public string Name { get; set; } = "";
    public int AttributeId { get; set; }
}
