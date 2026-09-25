using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public class Store : BaseEntity
{
    public string Name { get; set; } = default!;
    public string? ReceiptName { get; set; }
    public string? ReceiptAddress { get; set; }
    public string? ReceiptPhone { get; set; }
    public string? GuestWifiName { get; set; }
    public string? GuestWifiPassword { get; set; }

    public string SubDomain { get; set; } = default!;
    public string SubDomainNormalized { get; set; } = default!;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Feature flag cho luồng bán hàng nhiều LegalEntity.
    /// Migration Phase 22.1 luôn để false để hành vi POS hiện hữu không đổi.
    /// </summary>
    public bool IsMultiLegalEntityEnabled { get; set; }

    /// <summary>
    /// Mốc bắt đầu báo cáo/tách nghiệp vụ theo LegalEntity.
    /// Dữ liệu trước mốc này không được tự suy đoán và backfill allocation.
    /// </summary>
    public DateTime? MultiLegalEntityActivatedAtUtc { get; set; }

    public ICollection<LegalEntity> LegalEntities { get; set; } = new List<LegalEntity>();
    public ICollection<LegalEntityActivationEvent> LegalEntityActivationEvents { get; set; } = new List<LegalEntityActivationEvent>();
}
