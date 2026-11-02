using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;

namespace GaoApp.Infrastructure.Services.Audit;

public static class AuditOutboxEvidence
{
    public static AuditOutboxMessage Create(AuditLog log)
    {
        if (log.StoreId <= 0) throw new InvalidOperationException("A concrete store is required for audit evidence.");
        var eventId = log.EventId ?? Guid.NewGuid();
        var payload = new AuditLog
        {
            EventId = eventId, StoreId = log.StoreId, ActorUserId = log.ActorUserId,
            ActorUserName = Text(log.ActorUserName, 200), Module = log.Module, ActionType = log.ActionType,
            EntityName = Text(log.EntityName, 150), EntityId = Text(log.EntityId, 100),
            EntityDisplay = Text(log.EntityDisplay, 300), Summary = Text(log.Summary, 1000),
            OldValuesJson = AuditSensitiveData.SanitizeJson(log.OldValuesJson),
            NewValuesJson = AuditSensitiveData.SanitizeJson(log.NewValuesJson),
            ChangedColumnsJson = log.ChangedColumnsJson,
            TraceId = Text(log.TraceId, 100), IpAddress = Text(log.IpAddress, 100),
            UserAgent = Text(log.UserAgent, 2000), Path = Text(log.Path, 500),
            IsSuccess = log.IsSuccess, ErrorMessage = Text(log.ErrorMessage, 2000), CreatedAtUtc = log.CreatedAtUtc
        };
        return new()
        {
            EventId = eventId, StoreId = log.StoreId, PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAtUtc = log.CreatedAtUtc, NextAttemptAtUtc = DateTime.UtcNow
        };
    }

    public static AuditLog Read(AuditOutboxMessage message)
    {
        var log = JsonSerializer.Deserialize<AuditLog>(message.PayloadJson)
            ?? throw new InvalidOperationException("Invalid audit evidence.");
        if (log.StoreId != message.StoreId || log.EventId != message.EventId || log.Id != 0)
            throw new InvalidOperationException("Audit evidence identity does not match its envelope.");
        return log;
    }

    private static string? Text(string? value, int length)
    {
        if (value is null) return null;
        var safe = SensitiveRequestData.Redact(value);
        return safe.Length > length ? safe[..length] : safe;
    }
}
