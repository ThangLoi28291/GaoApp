namespace GaoApp.Domain.Enums;

public enum BarcodeVerificationRequestType
{
    SupplierBarcode = 1,      // Nhân viên nhập mã nhà cung cấp/NSX
    NoSupplierBarcode = 2     // Nhân viên báo đơn vị này không có mã NSX
}