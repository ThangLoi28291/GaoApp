namespace GaoApp.Domain.Entities;

public sealed class InvoiceNotificationOutbox
{
    public Guid Id {get;set;}
    public int StoreId {get;set;}
    public int? CustomerId {get;set;}
    public string SourceType {get;set;}="";
    public int SourceId {get;set;}
    public string SourceVersion {get;set;}="";
    public string Kind {get;set;}="";
    public string Code {get;set;}="";
    public string SnapshotJson {get;set;}="";
    public string SnapshotSha256 {get;set;}="";
    public Guid RequestId {get;set;}
    public string IdempotencyKey {get;set;}="";
    public Guid? BindingId {get;set;}
    public int? BindingVersion {get;set;}
    public string? BindingSnapshotJson {get;set;}
    public string State {get;set;}="Pending";
    public string? Detail {get;set;}
    public string? PreparedJson {get;set;}
    public string? PartsOutcomeJson {get;set;}
    public Guid? ZaloJobId {get;set;}
    public Guid? LeaseToken {get;set;}
    public DateTime? LeaseExpiresAtUtc {get;set;}
    public DateTime? FirstAttemptAtUtc {get;set;}
    public DateTime NextAttemptAtUtc {get;set;}
    public int Attempts {get;set;}
    public DateTime CreatedAtUtc {get;set;}
    public DateTime? UpdatedAtUtc {get;set;}
    public int? CreatedByUserId {get;set;}
    public byte[] RowVersion {get;set;}=[];
}
