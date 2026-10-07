using System.Text.Json.Serialization;

namespace GaoApp.Application.DTOs.Delivery;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryPickingEnvelope(Guid ClientRequestId, string ExpectedVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryPickingReportLine(int LineId, string PickedQuantityText, string? ShortageReason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryPickingReportRequest(Guid ClientRequestId, string ExpectedVersion, IReadOnlyList<DeliveryPickingReportLine> Lines);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryPickingPlanLine(int LineId, string PlannedQuantityText, string? OriginalCoverageText, string? Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryPickingReplacementProposal(int OriginalRootLineId, int VariantId, int? ProductUnitConversionId, string PlannedQuantityText, string OriginalCoverageText);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryPickingPlanRequest(Guid ClientRequestId, string ExpectedVersion, IReadOnlyList<DeliveryPickingPlanLine> Lines, IReadOnlyList<DeliveryPickingReplacementProposal> NewReplacements, string Reason, string CustomerConfirmationNote);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryPickingApprovalLine(int LineId, string AllowedQuantityText, string? ConfirmedOriginalCoverageText);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryPickingApproveRequest(Guid ClientRequestId, string ExpectedVersion, IReadOnlyList<DeliveryPickingApprovalLine> Lines, string Reason, string CustomerConfirmationNote);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryPickingReopenRequest(Guid ClientRequestId, string ExpectedVersion, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryPickingReassignRequest(Guid ClientRequestId, string ExpectedVersion, int PickerUserId, string Reason);

public sealed record DeliveryPickingCommandAck(Guid ClientRequestId, int DeliveryOrderId, string Operation, int AppliedRevision, string AppliedVersion, DateTimeOffset RecordedAtUtc, bool Replayed);

public sealed record DeliveryPickingCapabilitiesDto(bool CanClaim, bool CanReport, bool CanSubmit, bool CanPlan, bool CanApprove, bool CanReopen, bool CanReassign);

public sealed record DeliveryPickingPickerOptionDto(int UserId, string DisplayName);

public sealed record DeliveryPickingLineDto(int LineId, int OriginalRootLineId, bool IsReplacement, int? SourceOrderLineId, int VariantId, int? ProductUnitConversionId, int? SellingUnitId, int? BaseUnitId, string ItemName, string UnitName, string BaseUnitName, string OrderedQuantityText, string BaseMultiplierText, string UnitPriceText, string QuotedGrossText, string LineDiscountText, string AllocatedOrderDiscountText, string QuotedNetText, bool IsActive, string PlannedQuantityText, string PlannedBaseQuantityText, string DraftOriginalCoverageText, string DraftNetText, string? ReportedQuantityText, string? ReportedBaseQuantityText, int? ReporterUserId, string? ReporterName, DateTimeOffset? ReportedAtUtc, string? ShortageReason, string ReportFactKind, string? ApprovedQuantityText, string? ApprovedBaseQuantityText, string? ApprovedOriginalCoverageText, string? ApprovedNetText, int? ApprovedRevision);

public sealed record DeliveryPickingRootCoverageDto(int OriginalRootLineId, string OrderedQuantityText, string DraftRetainedQuantityText, string DraftReplacementCoverageText, string DraftMissingQuantityText, string? ApprovedRetainedQuantityText, string? ApprovedReplacementCoverageText, string? ApprovedMissingQuantityText);

public sealed record DeliveryPickingDetailDto(DeliveryDetailDto Delivery, DateTimeOffset ServerTimeUtc, int? PickerUserId, string? PickerName, DateTimeOffset? AssignedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? SubmittedAtUtc, bool ApprovalRequired, int? ApprovedRevision, int? ApprovedByUserId, DateTimeOffset? ApprovedAtUtc, string QuotedTotalText, string DraftTotalText, string? ApprovedTotalText, IReadOnlyList<DeliveryPickingLineDto> Lines, IReadOnlyList<DeliveryPickingRootCoverageDto> RootCoverage, DeliveryPickingCapabilitiesDto Capabilities, IReadOnlyList<DeliveryPickingPickerOptionDto> PickerOptions);

public sealed record DeliveryPickingReplacementOptionDto(int VariantId, int? ProductUnitConversionId, int SellingUnitId, int BaseUnitId, string ItemName, string UnitName, string BaseUnitName, string BaseMultiplierText, string UnitPriceText, string PriceTier);
