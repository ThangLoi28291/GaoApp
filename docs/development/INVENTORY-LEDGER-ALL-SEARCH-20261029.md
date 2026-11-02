# Tối ưu tìm “Tất cả” trên thẻ kho — 29/10/2026

Đã tối ưu truy vấn và kiểm thử lại trên dữ liệu **1.500.047 phát sinh**. Các từ khóa đã đo còn **6.40–6.74 giây**. Không đổi giao diện điện thoại/máy tính, controller/API, DTO, quyền, menu, cấu hình cửa hàng hoặc schema. Giữ nguyên các cửa sổ Chrome; không làm ngày hết hạn voucher.

## Kết quả trước / sau

| Từ khóa ở chế độ Tất cả | Trước | Sau | Số dòng đúng ở cả hai |
|---|---:|---:|---:|
| sua tuoi | 21.37 s | 6.74 s | 46 |
| doi soat | 14.37 s | 6.42 s | 1 |
| no-such-unique-ledger-value | 14.92 s | 6.46 s | 0 |

Các lượt đo dùng cùng SQL engine cục bộ, quy mô/kịch bản phát sinh, cơ sở dữ liệu GUID riêng cho từng lượt. Lượt nghiệm thu cuối bổ sung tên hàng có tiền tố để kiểm tra nhánh hỗn hợp; các tập kết quả dùng so trước/sau không đổi. Đây là phép đo cục bộ, không phải cam kết SLA của host thật.

- Trang cuối của kết quả ít dòng (trang 3, không nạp lại summary): 6.44 s, đủ 46 dòng.
- Tìm kiếm rộng: trang đầu 6.57 s, trang **75.000** 12.30 s, vẫn đủ **1.500.000 dòng**.
- Đồng thời khớp tên hàng và rất nhiều ghi chú: 6.35 s, đủ 1.500.045 dòng.
- Chrome thật, width 1440/390: tìm khoảng 6.66 / 7.02 s; không trùng dòng khi đổi trang, không tràn ngang, không có pageerror. Timeout điện thoại vẫn giữ 20 s.

## Thay đổi

Trước đây nhánh tìm chữ nằm trong phép ghép ID sản phẩm/chứng từ, được đưa vào cả thống kê và phân trang; câu thống kê có trường hợp mất hơn 20 s. Nay bước tìm ID chữ chạy một lần khi kết quả ít, rồi thống kê/trang dùng các ID đó. Bộ nhớ được chặn ở 4.097 ID để chọn cách chạy; **đây không phải giới hạn kết quả**. Khi nhiều dòng khớp, dùng điều kiện trực tiếp trên cùng phạm vi kho/ngày/chứng từ, kể cả khớp đồng thời danh mục và ghi chú/XML, rồi phân trang tại SQL.

Giữ nguyên REPLACE Đ/đ, collation tìm không phân biệt dấu, cách tìm chuỗi con và escape ký tự %/_/[]. Không thêm cache dùng chung giữa yêu cầu/người dùng/cửa hàng. Bước tìm ID chỉ lấy định danh; kết quả cuối luôn kiểm tra lại store, kho, sản phẩm, biến thể và trạng thái xóa. Chỉ một file production thay đổi: InventoryLedgerIndexReadRepository.cs. Hai harness cũ chỉ thêm tùy chọn thư mục output để tránh ghi đè bằng chứng UI-24.

## Nghiệm thu

**620/620 ca .NET**, **23/23 kiểm tra an toàn deployment** đã đạt. Bao gồm 613 ca core, 2 ca hiệu năng/đối chiếu mới và 5 ca SQL/native ledger/giá vốn/nhập kho/tổng kỳ hiện có. Kiểm tra biểu thức tìm cũ với 8 từ khóa × 10 kiểu sắp xếp, ký tự đặc biệt, dữ liệu xóa mềm, trạng thái, ngày/kho, hủy yêu cầu rồi tìm lại, nguồn XML và phân trang không summary. Kiểm tra lại quyền ADMIN giá vốn, liên kết chứng từ và truy cập chéo cửa hàng. Không cộng lặp các lượt thử hoặc các kiểm tra Node lịch sử thành ca mới.

Snapshot dữ liệu trước/sau giữ nguyên cửa hàng, menu, quyền, tồn kho, ledger, cấu hình/voucher; ChangeTracker rỗng sau đọc. Build thành công, 0 lỗi; còn 1 cảnh báo xUnit2031 có sẵn. Không pending model/migration, không có migration mới. **283 view và 441 asset riêng giữ nguyên hash**. 52 verification lịch sử và các artifact UI-24 cũ được giữ lại; các lần thử fixture/locator chưa đạt vẫn lưu riêng.

Đã tạo và kiểm hash gói WebOnly từ 2075 source C# production, không filter/shadow source, không Migrator/seed:

E:\GaoApp\GAOAPP-SA-20260819-01\publish\releases\20261029-045916-e983c1a5

Manifest SHA-256: 35267BBB8B54CC1623577EB6FB875D602D314BF98F2F0229B38684BEA5D7C61B

Không deploy IIS hoặc thay bản Web người dùng đang chạy. Khi dùng source trong Visual, build lại ứng dụng; không cần cập nhật DB cho bản sửa này. Tìm chuỗi con tự do vẫn quét phần dữ liệu đã lọc nên còn phụ thuộc quy mô/độ dài ghi chú; chưa chứng nhận tải đồng thời/WAN/host thật. Nghiệm thu thiết bị thật và hết hạn voucher vẫn hoãn như đã thống nhất.

[Bằng chứng tổng](mobile-ui-20261025/evidence/inventory-ledger-all-search-20261029/runtime-review.json), [đo trước/sau](mobile-ui-20261025/evidence/inventory-ledger-all-search-20261029/performance-review.json), [source/build](mobile-ui-20261025/evidence/inventory-ledger-all-search-20261029/source-review.json), [Chrome](../../TestResults/inventory-ledger-all-search-20261029/candidate/browser-review.json), [ảnh điện thoại](../../TestResults/inventory-ledger-all-search-20261029/candidate/all-search-390-matches.png).
