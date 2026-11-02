using GaoApp.Web.Areas.Admin.Models.Inventory;

namespace GaoApp.Tests.Ui;

public sealed class StockDocumentNotePresentationTests
{
    [Fact]
    public void Null_note_has_an_empty_nonlegacy_presentation()
    {
        var presentation = StockDocumentNotePresentation.FromNote(null);

        Assert.Equal(string.Empty, presentation.OriginalNote);
        Assert.Equal(string.Empty, presentation.Text);
        Assert.False(presentation.IsLegacy);
        Assert.Empty(presentation.Fields);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  Nhận hàng sau 17 giờ.  ")]
    [InlineData("Giao đủ thùng\r\nKiểm tra hạn dùng\nLiên hệ trước khi đến.")]
    [InlineData("Mã=A|PaymentState=UNKNOWN|SourceNote=Ghi chú người dùng")]
    [InlineData("<script>alert('note')</script> & <b>ghi chú</b>")]
    [InlineData("LEGACY_HDN")]
    [InlineData("LEGACY_HDN khác|MaHD=125114")]
    [InlineData("Ghi chú: LEGACY_HDN|MaHD=125114")]
    public void Ordinary_notes_remain_verbatim_without_interpreting_content(string note)
    {
        var presentation = StockDocumentNotePresentation.FromNote(note);

        Assert.Equal(note, presentation.OriginalNote);
        Assert.Equal(note, presentation.Text);
        Assert.False(presentation.IsLegacy);
        Assert.Empty(presentation.Fields);
    }

    [Fact]
    public void Legacy_metadata_has_readable_labels_in_a_stable_order()
    {
        const string note = "LEGACY_HDN|PaymentState=UNKNOWN|LegacyInputUserId=120854|LegacyStatus=0|LegacyInputUserName=Huy Hưởng|MaHD=125114";

        var presentation = StockDocumentNotePresentation.FromNote(note);

        Assert.True(presentation.IsLegacy);
        Assert.Equal(note, presentation.OriginalNote);
        Assert.Equal("Phiếu nhập được chuyển từ hệ thống cũ.", presentation.Text);
        Assert.Collection(presentation.Fields,
            field => AssertField(field, "Mã phiếu cũ", "125114"),
            field => AssertField(field, "Người nhập (hệ thống cũ)", "Huy Hưởng"),
            field => AssertField(field, "Trạng thái lúc chuyển dữ liệu", "Chờ duyệt"),
            field => AssertField(field, "Thanh toán trên dữ liệu cũ", "Chưa xác định từ dữ liệu cũ"));
    }

    [Fact]
    public void Legacy_prefix_is_case_insensitive()
    {
        const string note = "legacy_hdn|MaHD=125114";

        var presentation = StockDocumentNotePresentation.FromNote(note);

        Assert.True(presentation.IsLegacy);
        Assert.Equal(note, presentation.OriginalNote);
        Assert.Collection(presentation.Fields,
            field => AssertField(field, "Mã phiếu cũ", "125114"));
    }

    [Fact]
    public void Legacy_note_uses_the_requested_compact_summary()
    {
        const string note = "LEGACY_HDN|MaHD=125114|LegacyStatus=0|LegacyInputUserId=120854|LegacyInputUserName=Huy Hường|PaymentState=UNKNOWN";

        var presentation = StockDocumentNotePresentation.FromNote(note);

        Assert.Equal("Phiếu cũ 125114 • Người nhập: Huy Hường • Chờ duyệt • Thanh toán chưa xác định", presentation.Summary);
        Assert.Equal(note, presentation.OriginalNote);
        Assert.False(presentation.HasSourceNote);
    }

    [Fact]
    public void Compact_summary_keeps_source_note_separate_and_omits_missing_fields()
    {
        var presentation = StockDocumentNotePresentation.FromNote(
            "LEGACY_HDN|MaHD=125114|LegacyInputUserName=NULL|LegacyInputUserId=NULL|SourceNote=Kiểm tra đủ thùng\nMã=A|B=C");

        Assert.Equal("Phiếu cũ 125114", presentation.Summary);
        Assert.True(presentation.HasSourceNote);
        Assert.Equal("Kiểm tra đủ thùng\nMã=A|B=C", presentation.Text);
    }

    [Fact]
    public void Source_note_keeps_everything_after_its_first_delimiter_verbatim()
    {
        const string sourceNote = "  Hẹn giao buổi chiều\r\nMã=A|B=C|SourceNote=lần hai|PaymentState=PAID  ";
        const string note = "LEGACY_HDN|MaHD=125114|LegacyStatus=1|SourceNote=" + sourceNote;

        var presentation = StockDocumentNotePresentation.FromNote(note);

        Assert.True(presentation.IsLegacy);
        Assert.Equal(note, presentation.OriginalNote);
        Assert.Equal(sourceNote, presentation.Text);
        Assert.Collection(presentation.Fields,
            field => AssertField(field, "Mã phiếu cũ", "125114"),
            field => AssertField(field, "Trạng thái lúc chuyển dữ liệu", "Đã duyệt"));
    }

    [Theory]
    [InlineData("LEGACY_HDN|")]
    [InlineData("LEGACY_HDN|SourceNote=")]
    [InlineData("LEGACY_HDN|SourceNote= \r\n\t")]
    public void Missing_or_blank_source_note_uses_a_readable_explanation(string note)
    {
        var presentation = StockDocumentNotePresentation.FromNote(note);

        Assert.True(presentation.IsLegacy);
        Assert.Equal(note, presentation.OriginalNote);
        Assert.Equal("Phiếu nhập được chuyển từ hệ thống cũ.", presentation.Text);
        Assert.Empty(presentation.Fields);
    }

    [Theory]
    [InlineData("", "120854", "Mã 120854")]
    [InlineData("NULL", "120854", "Mã 120854")]
    [InlineData("null", "120854", "Mã 120854")]
    [InlineData("Huy Hưởng", "120854", "Huy Hưởng")]
    public void Imported_user_prefers_the_name_and_falls_back_to_a_labeled_id(
        string name, string id, string expectedValue)
    {
        var presentation = StockDocumentNotePresentation.FromNote(
            $"LEGACY_HDN|LegacyInputUserName={name}|LegacyInputUserId={id}");

        Assert.Collection(presentation.Fields,
            field => AssertField(field, "Người nhập (hệ thống cũ)", expectedValue));
    }

    [Theory]
    [InlineData("0", "Chờ duyệt")]
    [InlineData("1", "Đã duyệt")]
    [InlineData("2", "Chưa xác định (mã 2)")]
    [InlineData("CUSTOM", "Chưa xác định (mã CUSTOM)")]
    public void Imported_status_is_described_without_assuming_unknown_codes(
        string code, string expectedValue)
    {
        var presentation = StockDocumentNotePresentation.FromNote($"LEGACY_HDN|LegacyStatus={code}");

        Assert.Collection(presentation.Fields,
            field => AssertField(field, "Trạng thái lúc chuyển dữ liệu", expectedValue));
    }

    [Theory]
    [InlineData("UNKNOWN", "Chưa xác định từ dữ liệu cũ")]
    [InlineData("unknown", "Chưa xác định từ dữ liệu cũ")]
    [InlineData("PAID", "Chưa xác định (mã PAID)")]
    public void Imported_payment_state_does_not_claim_a_current_payment_status(
        string code, string expectedValue)
    {
        var presentation = StockDocumentNotePresentation.FromNote($"LEGACY_HDN|PaymentState={code}");

        Assert.Collection(presentation.Fields,
            field => AssertField(field, "Thanh toán trên dữ liệu cũ", expectedValue));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("NULL")]
    [InlineData("null")]
    public void Blank_or_null_metadata_is_omitted(string value)
    {
        var presentation = StockDocumentNotePresentation.FromNote(
            $"LEGACY_HDN|MaHD={value}|LegacyInputUserName={value}|LegacyInputUserId={value}|LegacyStatus={value}|PaymentState={value}");

        Assert.Empty(presentation.Fields);
    }

    [Fact]
    public void Unrecognized_metadata_is_not_presented_as_a_user_note()
    {
        var presentation = StockDocumentNotePresentation.FromNote(
            "LEGACY_HDN|InternalBatchId=secret-batch|UnknownKey=A=B");

        Assert.Equal("Phiếu nhập được chuyển từ hệ thống cũ.", presentation.Text);
        Assert.Empty(presentation.Fields);
    }

    private static void AssertField(StockDocumentNoteField field, string label, string value)
    {
        Assert.Equal(label, field.Label);
        Assert.Equal(value, field.Value);
    }
}
