# CSKH bước C — chat khách, chuyển người và hộp thư

Đã triển khai source, chưa publish hoặc bật trên cửa hàng thật.

- Khách: `/support`; nhân viên: `/admin/customer-service`. Mặc định tắt theo cửa hàng, có liên kết từ chất lượng trợ lý; không tự thêm menu.
- Quyền mới: `customer.service.view`, `.reply`, `.manage`. View bắt buộc; Reply nhận/trả lời; Manage bật/tắt và điều phối. Không tự cấp cho nhân viên.
- Phiên khách dùng cookie Data Protection theo cửa hàng, tên/liên hệ tự khai chưa xác minh. Không tự gắn vào CustomerId, đơn, điểm hoặc công nợ. Có đồng ý lưu và kết thúc phiên để thu hồi quyền trên thiết bị.
- Bot → chờ người → nhân viên → đóng/trả chờ/chuyển bot. Hai người nhận có kiểm tra version. Khi chuyển người, kết quả AI đang chờ bị chặn. Retry giữ RequestId/nội dung và người thực hiện; transcript không nhân đôi.
- API AI khách riêng `/api/customer/v1/messages` trên Chatbot, JWT audience/type khách khác nhân viên; nguồn GaoApp `/api/v1/customer-assistant/...` chỉ đọc hàng có đơn vị bán, retail và chính sách đã duyệt. Không chuyển token ra browser.
- SQLite riêng `customer-service.db` trong Assistant:StoreSettingsDirectory; có thể ghi đè CustomerService:DatabasePath. Cần đường dẫn bền vững ngoài Web và Modify cho GaoAppPool. Không migration SQL nghiệp vụ; thêm provider SQLite 8.0.29 (framework GaoApp vẫn net10.0).
- Retention mặc định 7 ngày, cho phép 1–7; cleanup và lease phục hồi sau recycle. Giữ Data Protection keys và sao lưu SQLite/WAL nhất quán.

Hướng dẫn publish Visual Studio, hợp đồng và bài nghiệm thu đầy đủ trong repository ChatbotLocal: `docs/customer-service-step-c/README.md`. Publish ChatbotLocal trước GaoApp, dùng runtime/key/Issuer/binding hiện tại. Phần giao đơn/cá nhân xác thực và Zalo/Facebook thuộc các bước sau.

Kiểm thử: CustomerServiceStoreTests + CustomerServiceHttpTests (SQLite, SQL/cookie/CSRF/quyền/phạm vi/nguồn retail, takeover và restart). UI: `node scripts/assistant/customer-service-step-c.browser.cjs`, dùng HTML Razor thật do HTTP test tạo. Chưa gọi Gemini thật hoặc gửi tin cho khách thật trong nghiệm thu source.