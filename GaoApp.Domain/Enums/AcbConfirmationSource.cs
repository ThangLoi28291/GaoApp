namespace GaoApp.Domain.Enums;

// Persisted audit values: append new members; never renumber existing ones.
public enum AcbConfirmationSource
{
    ScheduledCheck = 1,
    ManualCheck = 2,
    Callback = 3,
    DailyCallback = 4,
    InvoiceLookup = 5,
    CancellationCheck = 6,
    QrRecoveryCheck = 7,
    OfflineManual = 8
}
