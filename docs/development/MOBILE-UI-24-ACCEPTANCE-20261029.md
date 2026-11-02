# UI-24 — Nghiệm thu và gói phát hành, 29/10/2026

Đã hoàn tất phần tự động/cục bộ của UI-24. Chưa đánh dấu nghiệm thu toàn dự án vì còn Android/iOS, phần cứng, dữ liệu lớn/tải host và đợt rollout server thực tế.

Menu dấu “…” của POS đã gọn lại: chỉ chiếu các lối tắt cũ đủ quyền từ menu cửa hàng; nút **Màn hình khách** mở cửa sổ đúng quầy, độc lập với việc có một dòng menu màn khách. Đã mở popup thật trên POS Prime và legacy qua HTTPS. Sidebar/cấu hình ghim không bị ghi; desktop chỉ đổi phần dropdown được yêu cầu sửa. Route BarcodeManager cũ thiếu Index đã được khôi phục; xem/đổi/lịch sử dùng API hiện có, giữ quyền/CSRF, giữ form khi resize, chặn gửi lại khi mất phản hồi cho đến tải lại xác minh. Lịch sử đọc đúng PagedResult.items và giờ Việt Nam.

| Bước | Đã xác minh | Còn lại |
|---|---|---|
| UI-24a | 260 view + 300 asset baseline; hiện 283/440, không mất/rename; 181 view/26 asset baseline đã khác, 79/274 giữ nguyên; 23/140 file mới có kết luận. 283 Razor và 440 asset riêng có trong package đúng bytes. | Không dùng việc giữ nguyên file để suy ra mọi nhánh runtime đã chạy. |
| UI-24b | 14 probe native trên source/migration hiện tại; menu, vai trò, tenant, form/nháp, desktop, viewport và tablet 768/1024 trong probe release. Ma trận chuẩn 253 trạng thái/1771 phone/506 desktop. | Điện thoại và thiết bị thật. |
| UI-24c | 54 lượt đo qua ba release trên cùng SQL/account, ba route × hai chiều rộng × ba mẫu sau warm-up; không tăng số request/script/form, payload tăng nhỏ; latency nằm trong budget xem xét. | Không chứng nhận tìm Tất cả 1,5 triệu dòng, WAN/radio hoặc 10/20/50 client trên host thật. |
| UI-24d | 613 ca core UI/nghiệp vụ, SQL bảo toàn tiền/SL/trạng thái, thanh toán màn khách dựa dữ liệu đã commit; 234 đối chiếu print; 24 PDF/48 PNG có text/khổ/pixel khớp. | Máy in, QZ/helper/LabelPrintClient, két, scanner thực tế. |
| UI-24e | WebOnly, không Migrator; source C# production 2075 file được compile, không shadow/filter; manifest 2065 file; asset version và gzip SDK khớp. Không model/migration pending. | Cấp môi trường host qua quy trình TH1 hiện hành khi có yêu cầu deploy. |
| UI-24f | Production HTTPS cục bộ thật, SQL GUID, cert test riêng; chuyển baseline → candidate, theo dõi API/asset/error và cookie; không deploy IIS thật. | Target/maintenance/operator và theo dõi trên host thật. |
| UI-24g | ZIP baseline giải nén rồi kiểm hash; chạy release phục hồi với cùng SQL/keys/cookie, giữ barcode mới; không Down migration/khôi phục DB. | Bản cũ mất sửa dropdown/BarcodeManager của UI-24 như dự kiến; dữ liệu vẫn tương thích. |

Kết quả mới nhất: **628/628 .NET, 721/721 Node**, 23/23 kiểm tra an toàn triển khai bằng boundary IIS/process giả lập. Giữ các TRX thất bại ban đầu do fixture manifest/đợi realtime/kiểm tra token qua helper và contract preview/WebGL cũ; không cộng chúng thành test đạt mới. Build không có lỗi; còn một cảnh báo xUnit2031 có sẵn. Không thay controller, business service, schema, quyền hoặc dữ liệu cửa hàng thật. Một script sửa assistant IIS có delta ngoài phạm vi được quan sát, giữ nguyên và không có trong package Web.

UI-17/18/19 đã kiểm lại với cả workspace/P3; UI-20 dùng MigrateAsync thật. Không thay 50 verification lịch sử hoặc tự nhận các ca physical đã đạt. Đối chiếu progress sửa 4 partial hóa đơn chưa được ghi và 3 hash view cũ đã thay từ trước UI-24; đây là audit, không phải 7 sửa view của bước này.

Gói phát hành: E:\GaoApp\GAOAPP-SA-20260819-01\publish\releases\20261029-021210-7efc8129

Manifest SHA-256: A5A0437A6A93E04AC75B67CCDCC37680F8BA3E0664F3A76F4CECA958753CB518

Gói khôi phục: E:\GaoApp\GAOAPP-SA-20260819-01\.artifacts\ui-24\recovery\baseline-b147dd88effb4c24b2bdb3a8d4b1908a.zip

ZIP SHA-256: A760F7B91FD378C73F614208EFF2644870B825B1E86C6E4DC7B1DBFF99E72829

Dùng quy trình TH1 trong [runbook hiện hành](GAOAPP-DEPLOYMENT-RUNBOOK-20260913.md): WebOnly, không Migrator/seed, xác minh hash trước switch; operator thực hiện đúng site/pool khi đã có target và đợt triển khai. Không chạy nguyên placeholder lên host.

[Bằng chứng tổng](mobile-ui-20261025/evidence/ui-24/runtime-review.json), [kiểm kê từng file](mobile-ui-20261025/evidence/ui-24/inventory-review.json), [source](mobile-ui-20261025/evidence/ui-24/source-review.json), [build/package](mobile-ui-20261025/evidence/ui-24/current-build-proof.json), [hiệu năng](../../TestResults/mobile-ui-24-20261029/release/performance-review.json), [ảnh menu POS](../../TestResults/mobile-ui-24-20261029/release/pos-prime-desktop-menu.png).
