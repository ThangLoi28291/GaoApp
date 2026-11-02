# Trợ lý — tìm hàng trên dữ liệu cũ, bước A

Bản này bổ sung chỉ mục tên suy ra khi đọc, xếp hạng ứng viên, sửa đúng sản phẩm trong chat và cách gọi do quản trị duyệt. Không thay đổi tên hàng gốc, giá, tồn hoặc chứng từ, không thêm migration hay tự tạo menu.

- Quản trị: `/admin/assistant/search`, cần quyền `System.Setting.Update`, đọc danh mục và dùng trợ lý.
- Nhân viên: nút **Sửa đúng sản phẩm** dưới tin trả lời của trợ lý. Chọn đúng mã và gửi cụm từ để quản trị duyệt. Cách gọi chưa duyệt không ảnh hưởng tìm kiếm.
- Các cụm từ đã duyệt áp dụng theo cửa hàng. Khi tìm gần đúng hoặc qua cách gọi đã duyệt, nhân viên vẫn phải xác nhận ứng viên.
- File `store-{storeId}-search-knowledge.json` được lưu trong `Assistant:StoreSettingsDirectory`; giữ thư mục này ngoài Web khi publish và sao lưu trước cập nhật. Không đưa file runtime vào source/publish.
- Publish ChatbotLocal mới trước, sau đó GaoApp mới. ChatbotLocal cũ không nhận trường `search` mới của nguồn danh mục. Rollback GaoApp trước rồi ChatbotLocal.
- Giữ yêu cầu SDK/runtime đã khai báo của GaoApp. Giao diện phát triển nằm ở GaoApp; `/dev/assistant` của ChatbotLocal chỉ là dữ liệu thử.

Tài liệu đầy đủ và công cụ đo nằm trong source ChatbotLocal tại `docs/customer-service-step-a/README.md`, `tools/ChatbotLocal.CatalogEvaluation` và `scripts/Test-CustomerService-StepA.ps1`.

Xuất danh mục: `/admin/assistant/search/catalog.json`. Xuất câu đã duyệt: `/admin/assistant/search/approved-cases.json`; các câu đã dạy được gắn tune. Cần bổ sung câu chưa dùng để chỉnh hệ thống vào holdout trước khi kết luận chất lượng trên danh mục thật.

Kiểm thử: nhóm `GaoApp.Tests.Security.Assistant`, cộng `scripts/assistant/assistant-search-step-a.browser.cjs`. HTTP/SQL dùng dữ liệu thử tách biệt, browser dùng HTTP giả lập; không cần gọi Gemini thật. Chưa tự triển khai lên IIS.