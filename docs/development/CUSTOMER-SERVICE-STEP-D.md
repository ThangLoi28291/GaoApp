# CSKH bước D — xác minh và tra cứu nghiệp vụ riêng

Nối tiếp A/B/C; source đã bổ sung vào GaoApp, chưa publish hoặc mở cho khách thật.

- Khách: `/support` → **Đơn mua & quyền lợi**. Nhân viên: `/admin/customer-service` → **Xác minh hồ sơ khách**.
- Quyền: CSKH View+Reply+Verify mới (`customer.service.verify`), `catalog.customer.view`, `pos.order.view`. Nhận hội thoại trước khi cấp mã; không tự cấp quyền hoặc thêm menu.
- Nhân viên xác minh trực tiếp hoặc gọi số trong hồ sơ rồi cấp mã một lần. Số liên hệ tự khai không gắn CustomerId. Mã 96 bit/10 phút/5 lần sai; quyền xem 30 phút gắn đúng khách/phiên/hội thoại/cửa hàng, có thu hồi.
- Chỉ đọc đơn POS gắn khách trong 90 ngày, chi tiết và quyền lợi từ service tích lũy hiện tại. Không suy diễn trạng thái giao hàng. Không ghi đơn/đổi phiếu/hoàn tiền, không xem công nợ/giá vốn/ghi chú riêng.
- Dữ liệu tra cứu nằm trong khung riêng, không đưa vào AI/transcript/cache trình duyệt. Kiểm tra trước/sau đọc grant, nhân viên và khách còn hợp lệ.
- Thêm hai bảng trong SQLite CSKH hiện có, không migration SQL nghiệp vụ. Tiếp tục bảo vệ DB/WAL/SHM và Data Protection keys ngoài publish; backup nhất quán như C.
- D chỉ cần publish GaoApp khi server đã có C. Giữ .NET 10 hiện tại và các key/Issuer/URL đang chạy. Nếu chưa có C, publish ChatbotLocal C trước.

Chi tiết luồng, API, nghiệm thu và giới hạn: repository ChatbotLocal `docs/customer-service-step-d/README.md`. Test: `CustomerIdentityStoreTests`, `CustomerBusinessHttpTests` và `node scripts/assistant/customer-service-step-d.browser.cjs`. OTP tự động và Zalo/Facebook chưa được triển khai ở D.