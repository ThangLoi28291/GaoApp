# CSKH bước F — chất lượng và cải tiến từ phản hồi

Ngày 24/10/2026. Đường dẫn `/admin/customer-service/quality`, có liên kết nội dung từ hộp thư CSKH, không seed menu/sidebar. View + Manage quản trị chất lượng; Reply cho phép nhân viên đánh giá câu AI. Tác vụ đọc danh mục/đề xuất cách gọi/bộ câu hỏi cần quyền trợ lý, Catalog và System.Setting.Update như A.

Khách Web và nhân viên đánh giá từng câu AI; tách hai nguồn và chống trùng. Báo cáo cửa hàng/kênh 1–7 ngày, tỷ lệ chưa có mẫu là null. Manage xử lý có phiên bản; lỗi mới mở lại. Cách gọi nhân viên biên soạn đi qua kho chờ duyệt A, không tự sửa giá/tồn/chính sách/prompt hoặc huấn luyện model.

Bộ câu hỏi có tune/holdout, chặn nhân bản câu và đưa câu đã dạy vào nghiệm thu độc lập. Chạy đúng ranker nguồn hiện tại, snapshot/hash bộ câu và danh mục, lưu từng ca; mã đáp án mất là lỗi. Ngưỡng truy xuất: ít nhất 20 holdout, đạt từ 95%, không sai mã đầu ở câu rõ hoặc trả lời chắc nhưng sai. Không gọi model/gửi tin trong lần chạy, không coi kết quả là nghiệm thu toàn bộ chat AI.

Tối đa 500 case, 100 active, 10 giây/lần; lưu 10 báo cáo trong 7 ngày. Archive vẫn giữ case; xóa case archive xóa báo cáo liên quan, giữ hash câu đã dạy. Câu chủ động biên soạn phải loại thông tin riêng trước lưu. Phản hồi/review theo hạn transcript 1–7 ngày và xóa cascade; không xuất transcript qua cases.json.

SQLite CSKH schema 4 tự tạo bảng; SQL nghiệp vụ và ZaloMarketing không đổi. Publish GaoApp.Web Release bằng Visual Studio, giữ dữ liệu/keys/cấu hình ngoài Web và sao lưu DB/WAL nhất quán trước cập nhật. Riêng F không cần publish ChatbotLocal hoặc ZaloMarketing. Chưa tự publish hoặc gọi tài khoản thật.

Tài liệu đầy đủ và bài nghiệm thu ở repo ChatbotLocal `docs/customer-service-step-f/README.md`. Script `scripts/Test-CustomerService-StepF.ps1` chạy hồi quy A–F, Edge C–F và xuất bằng chứng.

Kết quả cuối: 205/205 ca GaoApp (35 ca F nằm trong tổng) và 50/50 ca Edge C–F (15 ca F). Không gọi AI/provider thật, không dùng dữ liệu sản xuất, không gửi tin khách và chưa publish server. Gói source delta sau E có manifest/hash và biên bản kiểm thử.
