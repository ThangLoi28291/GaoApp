using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    // Regression from the POS incident: Recalc totals had scale 0; SQL reloaded scale 2.
    private const string LegacyPosFingerprint = "97A15E80CF723F7D8497EE4577AA968EA5EB7FEC326DFF75E1DFDACBF7A6FBE5";
    private static Order SqlScaledExample() => new()
    {
        GrandTotal = 9000.00m, POSShiftId = 1,
        Lines = [new OrderLine { Id = 50, VariantId = 12, Quantity = 1.0000m,
            UnitPrice = 9000.00m, LineDiscount = 0.00m, LineTotal = 9000.00m }]
    };

    [Fact]
    public void Legacy_POS_hash_matches_SQL_values_and_new_hash_survives_recalculation()
    {
        var order = SqlScaledExample();
        Assert.True(AcbPaymentPolicy.MatchesFingerprint(order, LegacyPosFingerprint));
        var canonical = AcbPaymentPolicy.Fingerprint(order);
        order.GrandTotal = 9000m;
        order.Lines.Single().LineTotal = 9000m;
        Assert.Equal(LegacyPosFingerprint, LegacyHash(order));
        Assert.Equal(canonical, AcbPaymentPolicy.Fingerprint(order));
        Assert.True(AcbPaymentPolicy.MatchesFingerprint(order, LegacyPosFingerprint));
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("price")]
    [InlineData("discount")]
    [InlineData("lineTotal")]
    [InlineData("grandTotal")]
    [InlineData("customer")]
    [InlineData("shift")]
    [InlineData("variant")]
    [InlineData("lineId")]
    [InlineData("deleted")]
    public void Legacy_compatibility_cannot_hide_actual_changes_or_round_them_away(string field)
    {
        var order = SqlScaledExample();
        var canonical = AcbPaymentPolicy.Fingerprint(order);
        var line = order.Lines.Single();
        switch (field)
        {
            case "quantity": line.Quantity += 0.00001m; break;
            case "price": line.UnitPrice += 0.001m; break;
            case "discount": line.LineDiscount += 0.001m; break;
            case "lineTotal": line.LineTotal += 0.001m; break;
            case "grandTotal": order.GrandTotal += 0.001m; break;
            case "customer": order.CustomerId = 7; break;
            case "shift": order.POSShiftId++; break;
            case "variant": line.VariantId++; break;
            case "lineId": line.Id++; break;
            case "deleted": line.IsDeleted = true; break;
        }
        Assert.False(AcbPaymentPolicy.MatchesFingerprint(order, LegacyPosFingerprint));
        Assert.False(AcbPaymentPolicy.MatchesFingerprint(order, canonical));
    }

    [Fact]
    public void Canonical_hash_keeps_fractional_precision_and_does_not_depend_on_culture()
    {
        var order = SqlScaledExample();
        order.Lines.Single().Quantity = 1.234500m;
        var expected = AcbPaymentPolicy.Fingerprint(order);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            order.Lines.Single().Quantity = 1.2345m;
            Assert.Equal(expected, AcbPaymentPolicy.Fingerprint(order));
            order.Lines.Single().Quantity = 1.2345000000000000000000000001m;
            Assert.NotEqual(expected, AcbPaymentPolicy.Fingerprint(order));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task New_QR_can_be_reused_checked_and_finalized_after_SQL_scale_round_trip()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        ApplySqlScales(f.Order);
        await f.Db.SaveChangesAsync();
        Assert.Equal(qr!.Id, (await f.Service.TryCreateAsync(100, default))!.Id);
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000m);
        await f.Service.StatusAsync(qr.Id, true, default, manual: true);
        Assert.Equal(AcbSessionStatus.Received, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        f.Order.GrandTotal = 100000m;
        f.Order.Lines.Single().LineTotal = 100000m;
        await f.Service.CompleteAsync(qr.Id, default);
        await f.Service.CompleteAsync(qr.Id, default);
        Assert.Equal(1, f.FinalizeCount);
        Assert.Single(f.Order.Payments, x => x.Provider == "ACB");
    }

    [Fact]
    public async Task Manual_check_recovers_legacy_scale_review_using_fresh_bank_evidence_once()
    {
        await using var f = await Fixture.Create();
        var session = await LegacyReview(f);
        var calls = f.Bank.RetrieveCalls;
        var status = JsonSerializer.SerializeToElement(await f.Service.StatusAsync(session.QrRequestId, true, default, manual: true));
        Assert.True(status.GetProperty("bankQueried").GetBoolean());
        Assert.Equal(calls + 1, f.Bank.RetrieveCalls);
        Assert.Equal(AcbSessionStatus.Received, session.Status);
        Assert.Null(session.ReviewReason);
        await f.Service.CompleteAsync(session.QrRequestId, default);
        ApplySqlScales(f.Order);
        await f.Service.CompleteAsync(session.QrRequestId, default);
        Assert.Equal(1, f.FinalizeCount);
        Assert.Equal(100000m, f.Order.PaidTotal);
        Assert.Single(f.Order.Payments, x => x.Provider == "ACB");
        Assert.NotNull(session.PrintClaimedAtUtc);
    }

    [Theory]
    [InlineData("changed-cart")]
    [InlineData("changed-trace")]
    [InlineData("missing-order")]
    [InlineData("no-current-payment")]
    [InlineData("closed-shift")]
    [InlineData("unrelated-review")]
    public async Task Legacy_review_stays_held_when_new_evidence_or_order_is_not_valid(string scenario)
    {
        await using var f = await Fixture.Create();
        var session = await LegacyReview(f);
        var record = f.Bank.Orders.Single().Value;
        switch (scenario)
        {
            case "changed-cart": f.Order.Lines.Single().Quantity = 2m; break;
            case "changed-trace": record.Trace = "different-bank-trace"; break;
            case "missing-order": f.Bank.Orders.Clear(); break;
            case "no-current-payment": record.Received = null; break;
            case "closed-shift": f.Order.POSShift.Status = POSShiftStatus.Closed; break;
            case "unrelated-review": session.ReviewReason = "ACB báo điều chỉnh ERRORCORRECTED."; break;
        }
        await f.Db.SaveChangesAsync();
        if (scenario == "closed-shift")
            await f.Service.LookupAsync(100, true, default);
        else
            await f.Service.StatusAsync(session.QrRequestId, true, default, manual: true);
        Assert.Equal(AcbSessionStatus.ReviewRequired, session.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CompleteAsync(session.QrRequestId, default));
        Assert.Single(f.Order.Payments);
        Assert.Equal(0, f.FinalizeCount);
        if (scenario == "changed-trace") Assert.Contains("Thông tin QR", session.ReviewReason);
        if (scenario == "unrelated-review") Assert.Contains("ERRORCORRECTED", session.ReviewReason);
    }

    [Fact]
    public async Task Reopening_payment_recovers_the_original_legacy_QR_without_a_second_bank_request()
    {
        await using var f = await Fixture.Create();
        var session = await LegacyReview(f);
        var qr = await f.Service.TryCreateAsync(100, default);
        Assert.Equal(session.QrRequestId, qr!.Id);
        Assert.Equal(AcbSessionStatus.Received, session.Status);
        Assert.Single(f.Bank.Orders);
        Assert.Single(f.Order.Payments);
        Assert.Equal(0, f.FinalizeCount);
    }

    [Fact]
    public async Task Legacy_review_bank_outage_keeps_hold_and_manual_retry_can_recover()
    {
        await using var f = await Fixture.Create();
        var session = await LegacyReview(f);
        f.Bank.FailRetrieve = true;
        await Assert.ThrowsAsync<AcbApiException>(() => f.Service.StatusAsync(session.QrRequestId, true, default, manual: true));
        Assert.Equal(AcbSessionStatus.ReviewRequired, session.Status);
        Assert.Single(f.Order.Payments);
        f.Bank.FailRetrieve = false;
        await f.Service.StatusAsync(session.QrRequestId, true, default, manual: true);
        Assert.Equal(AcbSessionStatus.Received, session.Status);
    }

    private static async Task<AcbQrSession> LegacyReview(Fixture f)
    {
        ApplySqlScales(f.Order);
        f.Order.GrandTotal = 100000m;
        f.Order.Lines.Single().LineTotal = 100000m;
        await f.Db.SaveChangesAsync();
        var qr = await f.Service.TryCreateAsync(100, default);
        var session = await f.Db.Set<AcbQrSession>().SingleAsync();
        session.CartFingerprint = LegacyHash(f.Order);
        session.Status = AcbSessionStatus.ReviewRequired;
        session.ReviewReason = AcbPaymentPolicy.CartChangedReason;
        ApplySqlScales(f.Order);
        f.Bank.Pay(session.ProviderOrderId, 70000m);
        // A prior lookup already saved this evidence but the old code rejected its cart hash.
        f.Db.Add(new AcbPaymentTransaction { StoreId = 1, SessionId = session.Id,
            TransactionNumber = "123", Amount = 70000m, Status = "COMPLETED" });
        (await f.Db.PosPaymentQrRequests.SingleAsync(x => x.Id == qr!.Id)).Status = PosPaymentQrStatus.Failed;
        await f.Db.SaveChangesAsync();
        return session;
    }

    private static void ApplySqlScales(Order order)
    {
        static decimal Sql(decimal value, int scale) => decimal.Parse(value.ToString("F" + scale, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        order.GrandTotal = Sql(order.GrandTotal, 2);
        foreach (var line in order.Lines)
        {
            line.Quantity = Sql(line.Quantity, 4);
            line.UnitPrice = Sql(line.UnitPrice, 2);
            line.LineDiscount = Sql(line.LineDiscount, 2);
            line.LineTotal = Sql(line.LineTotal, 2);
        }
    }

    private static string LegacyHash(Order order) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        order.GrandTotal, order.CustomerId, order.POSShiftId,
        lines = order.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.Id).Select(x => new
        { x.Id, x.VariantId, x.Quantity, x.UnitPrice, x.LineDiscount, x.LineTotal })
    }))));
}
