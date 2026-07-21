namespace GaoApp.Domain.Enums;

public enum BarcodeVerificationRequestStatus
{
    Pending = 1,              // Chờ quản lý xử lý
    Approved = 2,             // Đã duyệt và ghi vào barcode thật
    Rejected = 3,             // Từ chối mã nhân viên nhập
    ConfirmedNoBarcode = 4    // Xác nhận đơn vị này không có mã NSX
}