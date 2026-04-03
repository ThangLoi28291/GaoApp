using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("POSShifts")]
public class POSShift : BaseStoreEntity, IAuditTrackedEntity
{
    public int TerminalId { get; set; }
    public POSTerminal Terminal { get; set; } = default!;

    public int OpenedByUserId { get; set; }
    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public POSShiftStatus Status { get; set; } = POSShiftStatus.Open;

    [StringLength(30)]
    public string? ShiftCode { get; set; }

    public decimal OpeningCash { get; set; }

    [StringLength(300)]
    public string? OpenNote { get; set; }

    public decimal CashSalesTotal { get; set; }
    public decimal NonCashSalesTotal { get; set; }

    public decimal CashRefundTotal { get; set; }
    public decimal NonCashRefundTotal { get; set; }

    public int RefundCount { get; set; }
    public int VoidCount { get; set; }

    public decimal CashInTotal { get; set; }
    public decimal CashOutTotal { get; set; }

    public decimal ClosingCashExpected { get; set; }
    public decimal? ClosingCashActual { get; set; }

    public int? ClosedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    [StringLength(300)]
    public string? CloseNote { get; set; }

    public int? CurrentOrderId { get; set; }
    public Order? CurrentOrder { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = default!;

    [NotMapped]
    public bool IsClosed => Status == POSShiftStatus.Closed;

    [NotMapped]
    public decimal? CashDifference =>
        ClosingCashActual.HasValue
            ? ClosingCashActual.Value - ClosingCashExpected
            : null;

    [NotMapped]
    public decimal RefundTotal => CashRefundTotal + NonCashRefundTotal;

    public ICollection<POSShiftCashTransaction> CashTransactions { get; set; } = new List<POSShiftCashTransaction>();

    /// <summary>
    /// QUAN TRỌNG:
    /// Chỉ các phương thức tiền mặt mới ảnh hưởng két tiền mặt cuối ca.
    /// Nếu enum của bạn sau này có thêm CashOnHand / CashDrawer... thì bổ sung ở đây.
    /// </summary>
    private static bool IsCashMethod(PaymentMethod method)
    {
        var name = method.ToString().Trim().ToLowerInvariant();

        return name == "cash"
               || name == "tienmat"
               || name == "cashpayment";
    }

    public void RecalcExpected()
    {
        ClosingCashExpected =
            OpeningCash
            + CashSalesTotal
            + CashInTotal
            - CashOutTotal
            - CashRefundTotal;
    }

    public void AddCashIn(decimal amount)
    {
        EnsureOpen();

        if (amount <= 0)
            throw new InvalidOperationException("Số tiền nộp thêm phải > 0.");

        CashInTotal += amount;
        RecalcExpected();
    }

    public void AddCashOut(decimal amount)
    {
        EnsureOpen();

        if (amount <= 0)
            throw new InvalidOperationException("Số tiền rút ra phải > 0.");

        CashOutTotal += amount;
        RecalcExpected();
    }

    public void AddSaleAmount(decimal amount, PaymentMethod method)
    {
        EnsureOpen();
        if (amount <= 0) return;

        if (IsCashMethod(method))
            CashSalesTotal += amount;
        else
            NonCashSalesTotal += amount;

        RecalcExpected();
    }

    public void ReverseSaleAmount(decimal amount, PaymentMethod method)
    {
        EnsureOpen();
        if (amount <= 0) return;

        if (IsCashMethod(method))
        {
            CashSalesTotal -= amount;
            if (CashSalesTotal < 0) CashSalesTotal = 0;
        }
        else
        {
            NonCashSalesTotal -= amount;
            if (NonCashSalesTotal < 0) NonCashSalesTotal = 0;
        }

        RecalcExpected();
    }

    /// <summary>
    /// Ghi nhận tiền refund theo method thực tế.
    /// - Cash => cộng CashRefundTotal
    /// - Khác Cash => cộng NonCashRefundTotal
    /// </summary>
    public void AddRefundAmount(decimal amount, PaymentMethod method)
    {
        EnsureOpen();

        if (amount <= 0)
            throw new InvalidOperationException("Số tiền hoàn phải > 0.");

        if (IsCashMethod(method))
            CashRefundTotal += amount;
        else
            NonCashRefundTotal += amount;

        RecalcExpected();
    }

    public void IncreaseRefundCount()
    {
        EnsureOpen();
        RefundCount++;
    }

    public void ReverseRefundAmount(decimal amount, PaymentMethod method)
    {
        EnsureOpen();

        if (amount <= 0)
            throw new InvalidOperationException("Số tiền hoàn phải > 0.");

        if (IsCashMethod(method))
        {
            CashRefundTotal -= amount;
            if (CashRefundTotal < 0) CashRefundTotal = 0;
        }
        else
        {
            NonCashRefundTotal -= amount;
            if (NonCashRefundTotal < 0) NonCashRefundTotal = 0;
        }

        RecalcExpected();
    }

    public void DecreaseRefundCount()
    {
        EnsureOpen();

        if (RefundCount > 0)
            RefundCount--;
    }

    public void Close(int closedByUserId, decimal closingCashActual, string? closeNote = null)
    {
        EnsureOpen();

        if (closingCashActual < 0)
            throw new InvalidOperationException("Tiền thực tế kiểm quỹ không được âm.");

        RecalcExpected();

        Status = POSShiftStatus.Closed;
        ClosedByUserId = closedByUserId;
        ClosedAtUtc = DateTime.UtcNow;
        ClosingCashActual = closingCashActual;
        CloseNote = closeNote;
    }

    private void EnsureOpen()
    {
        if (Status != POSShiftStatus.Open)
            throw new InvalidOperationException("Ca không ở trạng thái đang mở.");
    }
    public void IncreaseVoidCount()
    {
        EnsureOpen();
        VoidCount++;
    }

    public void DecreaseVoidCount()
    {
        EnsureOpen();

        if (VoidCount > 0)
            VoidCount--;
    }
}