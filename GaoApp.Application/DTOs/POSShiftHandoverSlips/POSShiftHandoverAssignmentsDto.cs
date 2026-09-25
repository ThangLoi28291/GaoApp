namespace GaoApp.Application.DTOs.POSShiftHandoverSlips;

public sealed record POSShiftAssignmentOption(int Id, string Name);
public sealed record POSShiftHandoverAssignmentsDto(
    List<POSShiftAssignmentOption> Terminals);
