# Đợt 5 — Chống ghi trùng thu tiền và cập nhật kiểm thử POS

> Cập nhật đợt 6: [gói staging, lỗi health làm mất phiên và kiểm thử Production](SECURITY-PHASE6-PUBLISHED-STAGING-20260909.md).

Phạm vi: xử lý hai việc còn lại trong source từ đợt 4 — chống ghi trùng yêu cầu thu tiền thủ công và hai test giao diện POS đã lỗi thời. Không backup/reset dữ liệu ứng dụng, không tự áp migration lên database đang chạy, không khởi động lại app. Các test tạo database LocalDB và tiến trình Web riêng, không gọi ngân hàng thật.

## Hành vi mới

- Mỗi lần thu tiền thủ công có `ClientRequestId` (GUID). Request được gắn với `OrderId`; API giỏ hiện tại cũng phải gửi đúng ID này. Thiếu ID bị từ chối, không tự tạo một mã mới ở server cho request cũ.
- SQL transaction khóa theo store/mã yêu cầu, rồi khóa dòng đơn bằng `UPDLOCK`. Unique index `(StoreId, ClientRequestId)` bảo vệ lâu dài; giới hạn này áp dụng cả khoản thu đã xóa mềm. Các Web process dùng chung SQL cùng chịu ràng buộc này.
- Gửi lại cùng mã và cùng nội dung trả trạng thái hiện tại của đơn, không thêm khoản thu. Đổi đơn, phương thức, số tiền, tham chiếu hoặc nhà cung cấp với mã đã dùng bị 409. Khoản thu đã xóa cũng bị 409 khi gửi lại; không được phục hồi bằng request cũ.
- Hai lần thu riêng có cùng số tiền được ghi nhận nếu dùng hai mã khác nhau. Các lần thu đồng thời trên cùng đơn được tuần tự hóa để tổng tiền không bị ghi đè. Quy tắc tiền mặt có thể nhận dư vẫn giữ nguyên.
- Chỉ nhận số đồng nguyên dương, trong giới hạn cột tiền. POS đang tính PaidTotal theo số đồng nguyên; API và JavaScript cùng chặn tiền lẻ dưới một đồng để tránh sai lệch do làm tròn.
- Kiểm tra quyền sở hữu ca và terminal trước khi thu hoặc trả kết quả gửi lại. Request cho giỏ cũ chưa từng được xử lý bị từ chối khi giỏ đã đổi; request đã xử lý có thể đọc lại kết quả đơn gốc.
- API vừa thu vừa chốt dùng ID đơn của kết quả thanh toán, không tìm lại giỏ hiện tại. Callback chốt tiền mặt trong JavaScript cũng giữ ID đơn đã thu, kể cả khi UI đổi giỏ trong khoảng chờ.

Frontend lưu lần thu đang chờ trong `sessionStorage` trước khi gửi HTTP. Khi mất phản hồi hoặc tải lại cùng tab, khôi phục số tiền/phương thức/tham chiếu và gửi lại cùng mã. Nếu đầu vào đã đổi, đối chiếu lần thu cũ trước khi cho thu tiếp; sau phản hồi thành công và đồng bộ UI mới xóa dấu chờ. Không gửi nếu trình duyệt không lưu được dấu chờ. Không xóa storage hay đổi thiết bị để thử lại một giao dịch chưa rõ kết quả; phải đối chiếu lịch sử đơn trước. Dấu chờ thuộc phiên tab, không phải cơ chế đồng bộ yêu cầu offline giữa nhiều thiết bị.

Khóa ứng dụng dùng LockOwner=Transaction, kiểm tra mã trả về, chờ tối đa 5 giây; khi không lấy được khóa trả 409 để client thử lại cùng mã. Commit hoặc rollback giải phóng khóa; xem [Microsoft — sp_getapplock](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql?view=sql-server-ver17). Khóa này không thay unique index và không phải cơ chế giới hạn tải toàn hệ thống.

## Contract API

`POST /admin/pos/{orderId}/payments` nhận `clientRequestId`, `method`, `amount`, `referenceCode`, `provider`. Hai endpoint `cart/current/payments` và `cart/current/payment-and-finalize` nhận thêm `orderId`.

```json
{
  "orderId": 123,
  "clientRequestId": "3ed3b7e4-8bab-4da7-a5e6-b3069f676807",
  "method": 0,
  "amount": 20000,
  "referenceCode": null,
  "provider": null
}
```

GUID minh họa ở trên không dùng lại cho giao dịch khác. `OrderDraftDto` thêm `status` để UI và endpoint tự chốt biết đơn đã hoàn tất khi khôi phục phản hồi. ACB vẫn đi qua quy trình QR/xác nhận và khóa riêng đã có; đợt này không thay cơ chế xác minh giao dịch ngân hàng.

## Hai test giao diện

Test CSS cũ chỉ cho phép `pos.css` trong phần chung, trong khi chức năng QR hiện có thêm `pos.qr-history.css`. Contract được cập nhật cho đúng hai stylesheet chung; vẫn kiểm tra Prime/legacy chỉ chọn đúng stylesheet tương ứng.

Test hash toàn bộ `Index.cshtml` đã không còn phù hợp sau các bổ sung QR/ACB: lịch sử QR, nút tạo/mở lại, mã QR, đếm ngược/trạng thái ACB, các ID hỗ trợ cập nhật nội dung và script đi kèm. Thay test hash bằng các contract cụ thể: danh sách và thứ tự script POS chính xác, các ID QR xuất hiện một lần, token antiforgery một lần, khởi tạo app một lần, hai đoạn adapter vẫn chỉ chạy trong nhánh Prime, thuộc tính hỗ trợ đọc trạng thái. Bộ test modal ID/layout/Prime hiện có vẫn chạy. Không thay hash bằng hash của file hiện tại, không sửa bố cục trang để khôi phục snapshot cũ. Đây là kiểm tra source/hành vi JavaScript, chưa phải nghiệm thu hình ảnh bằng trình duyệt.

## Migration và rollout

Migration `20260909080000_AddPosCollectionIdempotency` thêm cột GUID nullable và unique index có điều kiện `ClientRequestId IS NOT NULL`. Bản ghi trước nâng cấp và đường ACB chưa dùng trường này giữ NULL, không backfill hay xóa giao dịch cũ. Index không lọc `IsDeleted`, tránh tái sử dụng mã của khoản thu đã xóa.

Khi triển khai: áp migration bằng GaoApp.Migrator theo quy trình đợt 2, sau đó triển khai Web và JavaScript từ cùng release. Các tab POS cũ cần tải lại vì request thiếu GUID bị từ chối. Bộ test dùng artifact riêng `Logs/security-phase4-release` để tránh ghi đè DLL của app đang mở; không dùng thư mục này thay gói publish cho host.

Down migration bỏ index/cột mới. Nếu đã có giao dịch dùng mã chống trùng, không downgrade schema như một thao tác rollback thông thường: sẽ mất lịch sử chống gửi lại. Cần đối soát các lần thu đang chờ trước khi lựa chọn rollback. Không chạy Down trên database ứng dụng trong đợt này.

## Kiểm chứng

| Kiểm tra | Kết quả | Bằng chứng |
|---|---|---|
| Hồi quy bảo mật/tenant/payment/ca/POS/kho/giá vốn/migration | 540/540 đạt, 0 skipped; 4 phút 8 giây | `TestResults/security-phase5/phase5-regression.trx`, `Logs/security-phase5-regression.log` |
| Chạy lại nhóm thu tiền sau chặn số tiền không nguyên đồng | 5/5 đạt, 0 skipped; 53 giây | `TestResults/security-phase5/phase5-collection-final.trx`, `Logs/security-phase5-collection-final.log` |
| JavaScript thanh toán/QR/ACB | 39/39 đạt, 0 skipped | `Logs/security-phase5-javascript.log` |
| Release build cuối | 0 lỗi, 10 warning ở test đã có | `Logs/security-phase5-build.log` |

Nhóm 5 test thu tiền là tập con đã có trong lượt 540, được chạy lại sau khi bổ sung kiểm tra đầu vào số đồng nguyên; không cộng hai số thành số test độc lập. Lượt hồi quy có kiểm tra model khớp migration snapshot, database mới và nâng từ baseline. Đã kiểm tra cú pháp JavaScript/PowerShell và whitespace của các file liên quan. Các database/thư mục runtime do Web fixture tạo được dọn sau test. Không có database ứng dụng hay tiến trình app đang mở bị thay đổi bởi các lượt test.

`PaymentCollectionSqlServerTests` dùng Web, cookie và SQL thật để kiểm tra 10/20/50 request cùng lần thu; hai lần thu tiếp theo bằng nhau gửi đồng thời; gửi lại với nội dung khác; gửi lại sau chốt khi đã có giỏ mới; gửi lại khoản thu bị xóa; unique index chặn ghi trực tiếp trùng mã đã xóa; thiếu ID; giỏ đổi; cách ly hai cửa hàng. Số tiền và tồn được đối chiếu trực tiếp trong SQL.

Test Node thực thi module thanh toán: giữ mã qua mất phản hồi/reload, khôi phục đầu vào cũ, mã mới cho lần thu riêng, callback chốt đúng đơn gốc sau đổi giỏ, storage bị chặn/hỏng và các luồng QR/ACB đã có. Bài 10/20/50 ở đây là burst kiểm tra chống ghi trùng một giao dịch, không phải benchmark 50 nhân viên bán hàng độc lập.

Chạy lại trên Windows có .NET 8, Node và SQL LocalDB:

```powershell
./scripts/test-security-workflows.ps1
./scripts/test-security-workflows.ps1 -IncludeRegression
```

`-SkipBuild` chỉ dùng khi artifact khớp source; không build vào artifact trong lúc vstest đang dùng DLL. TRX ở `TestResults/security-phase5`; Web fixture kế thừa từ đợt 4 vẫn lưu log tại `TestResults/security-phase4/web-test-*.log`.

## Bước tiếp theo

Chuẩn bị staging với domain/HTTPS/proxy, SQL, media và Data Protection keys theo đợt 2. Chạy UAT thực tế với các vai trò, máy in/scanner/màn hình phụ và ACB sandbox; sau đó đo 10/20/50 client từ máy khác với tải ổn định, tải đột biến và tải kéo dài. Chỉ kết luận khả năng chạy trên host từ kết quả môi trường đó. Lifetime manager realtime hiện vẫn dành cho một Web instance như đã ghi ở đợt 4.
