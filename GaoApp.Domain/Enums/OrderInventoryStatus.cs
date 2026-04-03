namespace GaoApp.Domain.Enums;

public enum OrderInventoryStatus
{
    None = 0,                // draft / chưa finalize
    Sufficient = 1,          // đủ tồn, xử lý inventory bình thường
    PendingApproval = 2,     // có âm kho / provisional / cần manager duyệt
    Approved = 3,            // manager đã duyệt xong
    Resolved = 4             // đã nhập bù / revalue / xử lý xong
}