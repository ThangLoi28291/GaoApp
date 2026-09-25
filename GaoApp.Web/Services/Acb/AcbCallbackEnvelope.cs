using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;
using GaoApp.Domain.Entities;
using static GaoApp.Web.Services.Acb.AcbProtocol;

namespace GaoApp.Web.Services.Acb;

public sealed record AcbCallbackEnvelope(string ClientRequestId, int Page, string PayloadHash,
    string RequestCode, int TotalPages, IReadOnlyList<AcbQrNotificationItem> Items)
{
    public static AcbCallbackEnvelope Parse(JsonElement payload)
    {
        var parameters = Path(payload, "requestParameters");
        var master = Path(parameters, "masterMeta");
        var request = Path(parameters, "request");
        var meta = Path(request, "requestMeta");
        var parametersDetail = Path(request, "requestParams");
        var transactions = Path(parametersDetail, "transactions");
        var pagination = Path(parametersDetail, "pagination");
        // checksum is retained as evidence. Its undocumented algorithm is not guessed;
        // financial confirmation is independently obtained through the authenticated retrieve API.
        if (string.IsNullOrWhiteSpace(Text(payload, "requestTrace"))) throw new AcbCallbackValidationException("MISSING_REQUEST_TRACE");
        if (!Guid.TryParse(Text(master, "clientId"), out _) ||
            !Guid.TryParse(Text(master, "clientRequestId"), out var requestId) ||
            string.IsNullOrWhiteSpace(Text(master, "checksum"))) throw new AcbCallbackValidationException("INVALID_MASTER_META");
        var code = Text(meta, "requestCode");
        if (Text(meta, "requestType") != "NOTIFICATION" || code is not ("TRANSACTION_UPDATE" or "TRANSACTION_HISTORY"))
            throw new AcbCallbackValidationException("UNSUPPORTED_NOTIFICATION");
        if (transactions.ValueKind != JsonValueKind.Array || transactions.GetArrayLength() > 1000)
            throw new AcbCallbackValidationException("INVALID_TRANSACTIONS");
        if (!Path(pagination, "page").TryGetInt32Value(out var page) || page < 1 ||
            !Path(pagination, "pageSize").TryGetInt32Value(out var size) || size is < 1 or > 1000 ||
            !Path(pagination, "totalPage").TryGetInt32Value(out var pages) || pages < page || transactions.GetArrayLength() > size)
            throw new AcbCallbackValidationException("INVALID_PAGINATION");
        var entries = new List<AcbQrNotificationItem>();
        foreach (var tx in AcbProtocol.Items(transactions))
        {
            var attributes = Path(tx, "transactionEntityAttribute");
            var orderId = Text(attributes, "custom4");
            if (Text(tx, "transactionStatus") is not ("COMPLETED" or "ERRORCORRECTED") ||
                Path(tx, "amount").ValueKind != JsonValueKind.Number || !Path(tx, "amount").TryGetDecimal(out var amount) ||
                amount < 0 || amount > 9999999999999999.99m || decimal.Round(amount, 2) != amount ||
                !DateTime.TryParseExact(Text(tx, "effectiveDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var businessDate) || orderId.Length > 300)
                throw new AcbCallbackValidationException("INVALID_TRANSACTION_FIELDS");
            var direction = Text(tx, "debitOrCredit");
            // The status-only API omits these fields; list notifications include all four.
            var isList = code == "TRANSACTION_HISTORY" || direction.Length > 0 ||
                Path(tx, "transactionChannel").ValueKind != JsonValueKind.Undefined ||
                Path(tx, "transactionDate").ValueKind != JsonValueKind.Undefined;
            if (isList && (direction is not ("credit" or "debit") ||
                string.IsNullOrWhiteSpace(Text(tx, "transactionChannel")) ||
                !DateTimeOffset.TryParse(Text(tx, "transactionDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                Path(tx, "transactionContent").ValueKind != JsonValueKind.String))
                throw new AcbCallbackValidationException("INVALID_LIST_TRANSACTION_FIELDS");
            entries.Add(new AcbQrNotificationItem { Position = entries.Count, ProviderOrderId = orderId,
                RequestCode = code, BusinessDate = businessDate.Date, Amount = amount,
                TransactionStatus = Text(tx, "transactionStatus"), DebitOrCredit = direction, Content = Text(tx, "transactionContent") });
        }
        using var stream = new MemoryStream();
        // Transport trace, send time and checksum can change on a retry. Compare the business data.
        var businessPayload = JsonSerializer.SerializeToElement(new { clientId = Guid.Parse(Text(master, "clientId")).ToString(), request });
        using (var writer = new Utf8JsonWriter(stream)) { WriteCanonical(writer, businessPayload); writer.Flush(); }
        return new(requestId.ToString(), page, Convert.ToHexString(SHA256.HashData(stream.ToArray())), code, pages, entries);
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var item in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(item.Name); WriteCanonical(writer, item.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item); writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }
}

public sealed class AcbCallbackValidationException(string code) : ArgumentException("Callback không đúng cấu trúc ACB.")
{
    public string Code { get; } = code;
}

internal static class AcbJsonInteger
{
    internal static bool TryGetInt32Value(this JsonElement value, out int result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result);
    }
}
