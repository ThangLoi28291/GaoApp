# Khôi phục liên kết ảnh sản phẩm từ bộ file đã chép

Người dùng xác nhận bộ ảnh chạy thử nằm tại `C:\GaoAppData\Uploads\legacy-data` (images/files). Thư mục Data trong source OnlineShop đang trống; không dùng nó làm nguồn file. Không cần chép hoặc nhân đôi bộ ảnh đã có.

## Bước hiện tại: PREVIEW

`Preview-Images.ps1` chỉ SELECT dữ liệu SQL và đọc/hash file ảnh. Nó ghi báo cáo CSV/JSON vào thư mục evidence mới, không INSERT/UPDATE/DELETE database, không sao chép hay sửa file ảnh. `-CheckSetup` chỉ kiểm tra khởi tạo ngoại tuyến.

Từ thư mục `scripts/migration/initial-import`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ..\product-images\Preview-Images.ps1 -Server 'DESKTOP-E059ENS\SQLEXPRESS' -SourceDatabase DataGaoStore -TargetDatabase GaoAppDb -StoreId 1 -LegacyImageRoot 'C:\GaoAppData\Uploads\legacy-data'
```

Ánh xạ Code của ProductDetail qua SKU/barcode đang có của GaoApp, gồm barcode đơn vị quy đổi. Nhiều ứng viên thì báo lỗi để xem, không tự chọn. Thiếu ánh xạ/file hoặc định dạng không hỗ trợ thì xuất báo cáo riêng. Ảnh mã gốc có file hợp lệ được ưu tiên; sau đó theo ID nguồn. Cùng sản phẩm và đường dẫn chỉ giữ một liên kết, ghi các dòng trùng vào báo cáo. Cùng file dùng cho nhiều sản phẩm vẫn được giữ.

Đường dẫn nguồn `/Data/images/...` hoặc `/Data/files/...` được giải mã URL một lần (giữ dấu + đúng nghĩa), rồi tìm dưới thư mục đã xác nhận. Chặn thoát thư mục, junction/symlink và định dạng ngoài jpg/jpeg/png/gif/webp. Báo cáo lưu SHA-256 từng file có mặt; chỉ số lượng file không chứng minh mọi sản phẩm đều có ảnh.

## DRYRUN và ghi metadata

PREVIEW `20260923-211139-875` có 16.136 liên kết trên 16.117 sản phẩm, dùng 14.974 file. Bỏ qua 10.173 dòng thiếu file, 3 đường dẫn không hỗ trợ, 1 mã không ánh xạ; bỏ 57 liên kết trùng cùng sản phẩm và đường dẫn.

`Invoke-Images.ps1` dùng nguyên manifest đã kiểm tra. Trước SQL, nó kiểm hash báo cáo, tham số, đường dẫn và toàn bộ file dùng trong kế hoạch. Trong transaction, nó kiểm nguồn ProductDetail so với biên nhận bước 01 và toàn bộ bảng danh mục trước khi ghi. Script chỉ hỗ trợ khởi tạo khi metadata ảnh đang rỗng, không xóa hoặc ghi đè ảnh đang có.

```powershell
# Từ scripts/migration/initial-import:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ..\product-images\Invoke-Images.ps1 -PreviewDirectory ..\product-images\evidence\20260923-211139-875 -Mode DRYRUN
```

DRYRUN tạo MediaAssets dùng chung theo file, ProductImages theo sản phẩm/đường dẫn và đặt ProductVariant.PrimaryProductImageId theo ảnh chính. Đối chiếu đầy đủ metadata, kiểm các trường khác của variant không đổi, các bảng danh mục khác không đổi; rồi rollback và so hash đích với trước chạy. File ảnh không bị sửa. Identity/rowversion counter có thể tăng dù rollback.

Sau khi người vận hành gửi `IMAGE_DRYRUN_PASS_ROLLED_BACK` và đã kiểm tra evidence, chạy cùng script với `-Mode COMMIT -AllowCommit -SuccessfulDryRunDirectory <thu_muc_dryrun_dat>`. VERIFY dùng cùng PreviewDirectory và `-Mode VERIFY`. COMMIT bắt buộc đúng manifest/hash source của lượt DRYRUN thành công. Chạy lại không ghi trùng nếu biên nhận và dữ liệu vẫn khớp; nếu lệch thì dừng.

Biên nhận `GaoStoreProductImageRunsV1` ghi hash variant trước/sau, hash các trường ngoài ảnh và hash hai bảng metadata. Runner bước 01 đã được mở rộng chỉ chấp nhận đúng thay đổi ảnh có biên nhận này, khớp SQL gói ảnh và dữ liệu hiện tại. Không sửa hash/biên nhận gốc `GaoStoreMigrationRunsV2`. Các thay đổi ngoài phạm vi ảnh vẫn bị chặn.

## Hiển thị và trạng thái kiểm thử

- Source Web đã bổ sung phục vụ jpg/jpeg/png/gif/webp dưới `uploads/legacy-data/images/` và `uploads/legacy-data/files/`. Không mở các thư mục khác hoặc các file XML/PDF/SVG/video trong legacy-data. Cần chạy/deploy source Web mới thì thay đổi này mới có hiệu lực trên ứng dụng đang dùng.
- Kiểm thử HostStorageTests đạt 8/8, gồm HTTP ảnh tên có dấu/khoảng trắng/dấu + và chặn file/thư mục ngoài phạm vi. Kiểm thử biên nhận ảnh ngoại tuyến đạt 15/15; kiểm thử đường dẫn 22/22. Chưa chạy SQL ảnh bởi trợ lý; kết quả thực tế chờ người vận hành.
- Người dùng tự chạy mọi lệnh SQL và kiểm tra giao diện; chưa kết luận gói ảnh hay cutover thật đã hoàn tất.
