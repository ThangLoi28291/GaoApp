using System.Globalization;
using System.Text;
using GaoApp.Web.Services.Acb;

namespace GaoApp.Tests.Payments;

public sealed class BankNotificationParserTests
{
    private const string Token = "a0112233445566778899aabbccddeeff";

    public static IEnumerable<object[]> SharedRecognitionCases()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        const string relative = "tools/AcbNotificationProbeAndroid/tests/recognition-golden.tsv";
        while (root != null && !File.Exists(Path.Combine(root.FullName, relative))) root = root.Parent;
        if (root == null) throw new FileNotFoundException("Missing shared Android/Web bank recognition fixture.", relative);
        var lines = File.ReadAllLines(Path.Combine(root.FullName, relative));
        foreach (var line in lines.Skip(1).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var fields = line.Split('\t');
            if (fields.Length != 6) throw new InvalidDataException("Invalid recognition fixture row: " + fields[0]);
            yield return [fields[0], fields[1], fields[2], fields[3], fields[4], fields[5]];
        }
    }

    [Theory]
    [MemberData(nameof(SharedRecognitionCases))]
    public void Notification_parser_matches_shared_Android_recognition_contract(string id, string credit, string amountVnd, string orderId, string senderNameBase64, string contentBase64)
    {
        var data = BankNotificationParser.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(contentBase64)));
        if (credit == "0") { Assert.True(data == null, id); return; }
        Assert.True(data != null, id);
        Assert.Equal(decimal.Parse(amountVnd, CultureInfo.InvariantCulture), data.Amount);
        Assert.Equal(orderId == "-" ? (int?)null : int.Parse(orderId, CultureInfo.InvariantCulture), data.OrderId);
        Assert.Equal(senderNameBase64 == "-" ? null : Encoding.UTF8.GetString(Convert.FromBase64String(senderNameBase64)), data.SenderName);
    }

    [Theory]
    [InlineData("POS1838418QR1838418")]
    [InlineData("POS-1838418-QR-1838418-")]
    [InlineData("pos1838418-qr1838418")]
    [InlineData("POS-1838418QR-1838418-")]
    [InlineData("POS 1838418 QR 1838418 ")]
    public void Notification_parser_reads_joined_POS_QR_and_preserves_bank_evidence(string prefix)
    {
        var data = BankNotificationParser.Parse("Cửa hàng nhận tiền thành công\n"
            + $"+170.000 VND từ KHACH HANG THU tại TMCP Nam A - {prefix}{Token}"
            + " GD TEST12345678 151026-09:35:59. MGD: 41045");
        Assert.NotNull(data);
        Assert.Equal(170000, data.Amount);
        Assert.Equal(1838418, data.OrderId);
        Assert.Equal($"QR-1838418-{Token.ToUpperInvariant()}", data.QrCode);
        Assert.Equal("KHACH HANG THU", data.SenderName);
        Assert.Equal("GD:TEST12345678", data.TransactionCode);
        Assert.Equal(new DateTime(2026, 10, 15, 2, 35, 59, DateTimeKind.Utc), data.BankOccurredAtUtc);
        Assert.Equal(["GD:TEST12345678", "MGD:41045"], data.BankReferences);
    }

    [Theory]
    [InlineData("APOS1838418QR1838418a0112233445566778899aabbccddeeff")]
    [InlineData("POS1838418QROOPS")]
    [InlineData("POS1838418QR1838418a0112233445566778899aabbccddeef")]
    [InlineData("POS1838418QR1838418a0112233445566778899aabbccddeeffF")]
    [InlineData("POS1838418QR1838418a0112233445566778899aabbccddeeff…")]
    [InlineData("POS1838418QR1838418a0112233445566778899aabbccddeeff ...")]
    public void Notification_parser_does_not_guess_incomplete_or_embedded_joined_POS(string reference)
    {
        var data = BankNotificationParser.Parse($"+170000 VND {reference} GD TEST12345678");
        Assert.Null(data?.OrderId);
        Assert.Null(data?.QrCode);
    }

    [Theory]
    [InlineData("POS1838418QR1838419a0112233445566778899aabbccddeeff")]
    [InlineData("POS-1838418-QR-1838419-a0112233445566778899aabbccddeeff")]
    [InlineData("POS1838418QR1838418a0112233445566778899aabbccddeeff POS1838419")]
    [InlineData("POS1838418QR1838418a0112233445566778899aabbccddeeff POS1838418QR1838418b0112233445566778899aabbccddeeff")]
    public void Notification_parser_rejects_conflicting_joined_references(string reference)
    {
        Assert.Null(BankNotificationParser.Parse($"+170000 VND {reference} GD TEST12345678"));
    }

    [Fact]
    public void Notification_parser_reads_actual_167000_format_without_treating_note_account_and_phone_as_debits()
    {
        var data = BankNotificationParser.Parse("Cửa hàng nhận tiền thành công\n"
            + "+ 167.000 VND từ KHACH HANG THU tại TMCP Dau Tu - 111122223333 0900000000 "
            + "POS-1838284-QR-1838284-00112233445566778899AA GD TEST12345678 141026-19:26:44. MGD: 41025");
        Assert.NotNull(data);
        Assert.Equal(167000, data.Amount);
        Assert.Equal(1838284, data.OrderId);
        Assert.Null(data.QrCode); // The short QR token is never promoted into a full request identifier.
        Assert.Equal("KHACH HANG THU", data.SenderName);
        Assert.Equal("GD:TEST12345678", data.TransactionCode);
    }

    [Theory]
    [InlineData("167000")]
    [InlineData("167.000")]
    [InlineData("167,000")]
    [InlineData("167 000")]
    [InlineData("167.000,00")]
    [InlineData("167,000.00")]
    [InlineData("167000.00")]
    [InlineData("167000,00")]
    [InlineData("167 000.00")]
    [InlineData("167 000,00")]
    public void Notification_parser_accepts_only_integer_VND_with_optional_explicit_zero_fraction(string money)
    {
        var data = BankNotificationParser.Parse($"+{money} VND POS-1838284 MGD: 41025");
        Assert.NotNull(data);
        Assert.Equal(167000, data.Amount);
    }

    [Theory]
    [InlineData("+167.000,50 VND")]
    [InlineData("+167000.5 VND")]
    [InlineData("+167,00,0 VND")]
    [InlineData("+167.000.00 VND")]
    [InlineData("+167000 VND +168000 VND")]
    [InlineData("+167000 VND -167000 VND")]
    [InlineData("+167000 VND −100 VND")]
    [InlineData("-167000 VND")]
    [InlineData("+0 VND")]
    [InlineData("+10000000000000000 VND")]
    [InlineData("+167000 USD")]
    [InlineData("+167000")]
    public void Notification_parser_rejects_fractional_ambiguous_or_noncredit_amounts(string text)
    {
        Assert.Null(BankNotificationParser.Parse(text + " POS-1838284 MGD: 41025"));
    }

    [Theory]
    [InlineData("＋１６７.０００ VND POS－1838284")]
    [InlineData("+167\u202f000 VND POS\u20111838284")]
    [InlineData("+167.000 V\u200bND POS\u20131838284")]
    [InlineData("+167.000 VND POS\u20141838284")]
    public void Notification_parser_normalizes_Unicode_amount_spaces_and_POS_dash_without_guessing(string text)
    {
        var data = BankNotificationParser.Parse(text + " MGD: 41025");
        Assert.NotNull(data);
        Assert.Equal(167000, data.Amount);
        Assert.Equal(1838284, data.OrderId);
    }

    [Theory]
    [InlineData("Cửa hàng nhận tiền thành công +170.000 VND từ HO TAI QUANG tại TMCP Nam A", "HO TAI QUANG")]
    [InlineData("Cua hang nhan tien thanh cong +170.000 VND tu KHACH HANG THU tai ACB", "KHACH HANG THU")]
    [InlineData("Cửa hàng nhận tiền thành công +170.000 VND từ HO\u00a0TAI\u200b QUANG tại ACB", "HO TAI QUANG")]
    [InlineData("Cua hang nhan tien thanh cong +170.000 VND tu HO TAI QUANG tai ACB", null)]
    public void Notification_parser_preserves_bank_sender_and_leaves_unaccented_ambiguous_name_unknown(string text, string? sender)
    {
        var data = BankNotificationParser.Parse(text + " MGD: 41045");
        Assert.NotNull(data);
        Assert.Equal(sender, data.SenderName);
    }

    [Theory]
    [InlineData("OTP 123456 +167000 VND POS1838284 MGD: 41025")]
    [InlineData("ＯＴＰ：123456 +167000 VND POS1838284 MGD: 41025")]
    [InlineData("+167000 VND POS1838284 MGD: 41025 SafeKey")]
    public void Notification_parser_excludes_sensitive_content_before_money_extraction(string content)
    {
        Assert.Null(BankNotificationParser.Parse(content));
    }

    [Theory]
    [InlineData("POS0")]
    [InlineData("POS2147483648")]
    [InlineData("POS9999999999")]
    public void Notification_parser_never_accepts_an_invalid_matched_POS_identifier(string pos)
    {
        Assert.Null(BankNotificationParser.Parse($"+167000 VND {pos} MGD: 41025"));
    }

    [Fact]
    public void Notification_parser_preserves_existing_account_fingerprint_while_normalizing_notification_dashes()
    {
        var bank = new GaoApp.Domain.Entities.StoreBankAccount { BankCode = "ACB", AccountNumber = "TEST—123" };
        Assert.Equal(BankNotificationParser.Hash("ACB|TEST—123"), BankNotificationParser.AccountFingerprint(bank));
        Assert.Equal("POS-1838284", BankNotificationParser.Fold("POS—1838284"));
    }
}
