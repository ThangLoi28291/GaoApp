
## 2026-06-09

### Security / Admin Authorization

Đã làm:
- Thêm `[Authorize]` vào `BaseAdminController` để toàn bộ controller kế thừa Admin base mặc định bắt đăng nhập.
- Bổ sung `[Authorize]` cho các controller Admin dạng `Controller` chưa kế thừa `BaseAdminController`.
- Bổ sung `[Authorize]` cho các API Admin dạng `ControllerBase`.
- Thêm `[AllowAnonymous]` cho `AccountController.Login` và `AccountController.AccessDenied`.
- Thêm `[Authorize]` cho `AccountController.Logout`.
- Khóa endpoint debug `/__tenant` và `/__tenant-hash`, chỉ cho chạy trong môi trường Development.
- Giữ `/ping` vì không trả dữ liệu nhạy cảm.

Đã test:
- Chưa đăng nhập vào `/admin/pos` bị chuyển về login.
- Chưa đăng nhập vào `/admin/api/...` không trả dữ liệu.
- Trang `/admin/account/login` vẫn vào được.
- Sau khi đăng nhập, POS/Admin vẫn hoạt động.

Cần kiểm tra khi publish IIS:
- Môi trường Production không truy cập được `/__tenant`.
- Môi trường Production không truy cập được `/__tenant-hash`.
# GaoApp - AI Changelog

File này dùng để ghi ngắn gọn sau mỗi lần hoàn thành chức năng. Không cần tạo file mới cho từng chức năng.

## Mẫu ghi

```md
## 2026-06-09 - Module / Chức năng

### Đã làm
- ...

### File đã sửa
- `path/file.cs`
- `path/file.js`

### Cần test
- ...

### Lưu ý cho lần chat sau
- ...
```

---

## 2026-06-09 - Khởi tạo tài liệu AI

### Đã làm

- Quét source `GaoApp(14).zip`.
- Tạo bộ tài liệu AI context cho dự án.
- Tạo source map theo module.
- Tạo hướng dẫn mở chat mới.

### Cần làm tiếp

- Copy thư mục `docs` vào source.
- Commit Git.
- Từ các lần sau, sau khi PASS một chức năng thì cập nhật file này.

## 2026-06-09 - Promotion/POS

### Đã làm

* Sửa lỗi promotion giảm tiền / giảm % không áp dụng ngay khi mua số lượng 1.
* Nguyên nhân: `OrderLine` mới thêm vào giỏ chưa có `StoreId`, trong khi `PromotionEngine` kiểm tra `line.StoreId <= 0` nên bỏ qua promotion ở lần đầu.
* Đã bổ sung gán `StoreId = order.StoreId` khi thêm hoặc merge dòng hàng trong POS.
* Đã điều chỉnh `PromotionEngine` dùng `effectiveStoreId` từ `line.StoreId` hoặc fallback từ `order.StoreId`.
* Bổ sung SQL tạo/cập nhật các cột lưu snapshot promotion trên `OrderLines` và tổng promotion/combo trên `Orders`.

### File liên quan

* `GaoApp.Application/Services/Orders/POSService.cs`
* `GaoApp.Application/Services/Promotions/PromotionEngine.cs`
* SQL Server:

  * `dbo.OrderLines`
  * `dbo.Orders`

### Cần test

* Tạo giỏ mới, quét sản phẩm có giảm tiền 1 lần, kiểm tra promotion áp dụng ngay.
* Tạo giỏ mới, quét sản phẩm có giảm % 1 lần, kiểm tra promotion áp dụng ngay.
* Quét tiếp số lượng 2 trở lên, kiểm tra tiền giảm nhân đúng theo số lượng.
* Reload lại giỏ, kiểm tra promotion vẫn hiển thị đúng.
* Chốt đơn, kiểm tra dữ liệu promotion được lưu trong `OrderLines`.
* Kiểm tra Buy X Get Y nếu có: dòng hàng tặng giá 0, không làm tăng `GrandTotal`.

### Lưu ý cho lần chat sau

* Khi sửa Promotion/POS cần kiểm tra `StoreId` của `OrderLine` mới tạo trước khi gọi `PromotionEngine`.
* Promotion nên được áp sau khi giá bán/đơn vị bán đã được xác định.
* Không query promotion từng dòng; ưu tiên load active promotions một lần rồi xử lý trên lines đã loaded.


- Khóa endpoint debug `/__tenant` và `/__tenant-hash`, chỉ cho chạy ở môi trường Development.
- Giữ `/ping` vì không trả dữ liệu nhạy cảm.


