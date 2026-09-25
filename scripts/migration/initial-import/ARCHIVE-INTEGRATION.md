# Source tra cứu trả hàng lịch sử

Copy các file mới trong `app-source/` vào đúng thư mục của bản GaoApp tương ứng. Migration này được đặt ngoài EF operational model để archive không trở thành phiếu nhập/hoàn tiền vận hành. Không cần thêm DbSet hay sửa snapshot của model cho bảng SQL archive.

Áp dụng schema và build/deploy source cùng nhau. Không trỏ ứng dụng chạy vào database rehearsal có cấu hình chưa đầy đủ như một môi trường bán hàng thật. Bộ này không đổi connection string ứng dụng hiện tại.

Trang tra cứu độc lập: `/admin/pos/legacy-returns`. Muốn thêm lối vào trang đơn POS, đặt liên kết sau trong vùng nút của `GaoApp.Web/Areas/Admin/Views/POSOrderPage/Index.cshtml`:

```html
<a href="/admin/pos/legacy-returns" class="po-button">Trả hàng cũ chưa liên kết</a>
```

Trong workspace phát triển hiện tại liên kết đã được thêm. ZIP chỉ kèm những file mới của tính năng archive; không chép đè toàn bộ trang POS đang có các thay đổi khác.

Chức năng đọc dùng quyền `Pos.Order.View` hiện có, không thay menu/role/tài khoản. Không có POST, không tạo phiếu nhập kho, hoàn tiền hay hóa đơn từ archive.
