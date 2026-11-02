# CSKH bước B — chính sách cửa hàng

Source đã triển khai, chưa publish/duyệt chính sách kinh doanh thật.

- Trang: `/admin/assistant/knowledge`, quyền `System.Setting.Update`, lấy cửa hàng từ phiên đăng nhập. Không tự thêm menu. Có liên kết trong trang chất lượng trợ lý.
- Bản nháp → duyệt → áp dụng theo lịch → thay thế/thu hồi. Nội dung đã duyệt giữ nguyên. Hết hạn/thu hồi không dùng lại bản cũ; hủy lịch chưa áp dụng giữ bản hiện tại.
- Mỗi chủ đề một bản áp dụng: cửa hàng, giao hàng, đổi trả, thanh toán, thành viên, khuyến mãi. Mẫu cấu hình tên/địa chỉ/điện thoại cần quản trị kiểm tra. Có xem trước nguyên văn; nội dung không chạy HTML/script.
- Source API `POST /api/v1/assistant/knowledge/lookup`: delegated bearer + X-Request-Id, kiểm tra quyền `assistant.use` và `assistant.chat` hiện tại trước/sau đọc. Chỉ trả bản đã duyệt/còn hiệu lực, không trả ghi chú căn cứ nội bộ hoặc bản nháp. Nguồn/scope/request/ngày được Chatbot xác minh.
- File `store-{StoreId}-policies.json` trong `Assistant:StoreSettingsDirectory`; phải bảo toàn ngoài thư mục publish. Cấp Modify cho GaoAppPool. Không migration SQL. Giới hạn 600 bản, 2.000 thao tác, 8 MiB; mỗi nội dung 3.000 ký tự.
- Chatbot định dạng chính sách nguyên văn, kèm phiên bản/ngày; AI chỉ phân loại chủ đề cho cách hỏi linh hoạt. Giá/tồn/đơn/điểm riêng khách không được đưa vào kho làm dữ liệu cố định.

Tài liệu đầy đủ và nghiệm thu: repository ChatbotLocal, `docs/customer-service-step-b/README.md`. Gói source bước B có manifest/hash; cần đầy đủ bước A. Publish ChatbotLocal trước GaoApp, dùng runtime đang yêu cầu của mỗi source; không đổi Issuer/binding/key đã hoạt động.

Kiểm thử GaoApp: các lớp Security.Assistant, riêng B có AssistantPolicyHttpTests (SQL/cookie/CSRF/quyền/phạm vi/thu hồi), các ca Policy trong AssistantIntegrationTests (phiên bản/hiệu lực/đồng thời/file lỗi) và `scripts/assistant/assistant-policy-step-b.browser.cjs` (cần chạy HTTP test trước để có HTML Razor thật). Mock không gọi Gemini.