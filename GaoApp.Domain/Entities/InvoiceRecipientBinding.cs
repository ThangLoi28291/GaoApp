namespace GaoApp.Domain.Entities;

public sealed class InvoiceRecipientBinding
{
    public Guid Id {get;set;}
    public int StoreId {get;set;}
    public int CustomerId {get;set;}
    public Guid VerificationRequestId {get;set;}
    public string ConnectionId {get;set;}="";
    public string ConnectionBinding {get;set;}="";
    public Guid SenderProfileId {get;set;}
    public Guid ConversationId {get;set;}
    public long IncomingSequence {get;set;}
    public string NativePeer {get;set;}="";
    public string Phone {get;set;}="";
    public int Version {get;set;}=1;
    public int VerifiedByUserId {get;set;}
    public DateTime VerifiedAtUtc {get;set;}
    public string VerificationMethod {get;set;}="";
    public string EvidenceReference {get;set;}="";
    public DateTime? RevokedAtUtc {get;set;}
    public int? RevokedByUserId {get;set;}
    public string? RevocationReason {get;set;}
    public byte[] RowVersion {get;set;}=[];
}
