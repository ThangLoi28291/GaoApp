namespace GaoApp.Application.DTOs.POS
{
    public class HeldOrderDto
    {
        public int OrderId { get; set; }
        public string? HoldCode { get; set; }
        public string? HoldNote { get; set; }
        public DateTime? HeldAtUtc { get; set; }

        public int LineCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal Subtotal { get; set; }

        // Bổ sung để UI hiển thị đúng khách hàng
        public int? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }

        // =========================================================
        // BƯỚC 4.1:
        // Thông tin ca POS / terminal / nhân viên giữ đơn
        // để UI tách đúng "ca hiện tại" và "ca khác"
        // =========================================================

        // Shift đang giữ đơn
        public int? PosShiftId { get; set; }
        public string? ShiftCode { get; set; }

        // Terminal của shift đang giữ đơn
        public string? TerminalId { get; set; }
        public string? TerminalName { get; set; }

        // Nhân viên mở ca / đang giữ đơn
        public int? HeldByUserId { get; set; }
        public string? HeldByUserName { get; set; }

        // Hỗ trợ hiển thị mềm ở UI
        public bool IsCurrentShift { get; set; }
        public bool IsCurrentTerminal { get; set; }
    }
}