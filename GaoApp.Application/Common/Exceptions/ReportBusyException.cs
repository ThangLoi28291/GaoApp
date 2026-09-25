namespace GaoApp.Application.Common.Exceptions;

public sealed class ReportBusyException(string message = "Báo cáo đang bận. Vui lòng đợi vài giây rồi thử lại.") : AppException(message);
