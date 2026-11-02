namespace GaoApp.Domain.Entities;
public sealed class InvoiceNotificationAudit
{
    public Guid Id {get;set;}
    public int StoreId {get;set;}
    public Guid? BindingId {get;set;}
    public Guid? OutboxId {get;set;}
    public int? ActorUserId {get;set;}
    public string Action {get;set;}="";
    public string Detail {get;set;}="";
    public DateTime CreatedAtUtc {get;set;}
}
