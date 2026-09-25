using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInputInvoiceXmlDocumentParser
{
    InputInvoiceHead Parse(byte[] fileBytes);
}
