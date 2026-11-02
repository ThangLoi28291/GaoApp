# WEB05 — QR và tự xác nhận nhận tiền

Trạng thái: **PASS** sau code, test và review độc lập; identity và cleanup đã đạt. Đủ cổng chuyển WEB06. Website chưa mở bán; chưa áp dụng migration vào database cửa hàng.

Khách có thể đặt QR không cần tài khoản hoặc ca POS, mở lại cùng yêu cầu, sao chép thông tin, tải PNG và xem trạng thái. Nhận tiền đúng chuyển đơn sang chờ chuẩn bị; không tự xuất kho, ghi bán, xuất hóa đơn hoặc đánh dấu đã giao.

## Kết quả triển khai

- Website khách chạy riêng trong `GaoApp.Storefront.Web`, không tham chiếu `GaoApp.Web`. Host khách tạo intent/outbox và đọc dữ liệu đúng token; transport, credential và worker ngân hàng nằm ở backend quản trị.
- Giữ DataProtection của ngân hàng theo ứng dụng `GaoApp`; guest và replay tiếp tục dùng `GaoApp.Storefront` cùng keyring riêng. Không đổi khóa bảo vệ cũ để nối hai host.
- Reference website là `W` và 12 chữ số từ sequence/registry toàn hệ thống, giới hạn dương và không quay vòng. QR mở lại không tạo intent hoặc lần thu mới.
- Global receipt được lưu trước khi phân bổ; chống dùng lại giao dịch giữa POS, website và cấu hình tài khoản trùng ở các cửa hàng. Snapshot tài khoản được lấy trước lookup; không suy tài khoản lịch sử từ cấu hình hiện tại.
- Tiền thiếu/thừa, sai binding, sửa giao dịch, nhiều khoản trong cùng phản hồi và tiền đến muộn được giữ đủ bằng chứng để đối soát. Duplicate hoặc khoản đúng đến sau không tự xóa cờ Review.
- Định danh thông báo điện thoại và API có namespace/nguồn riêng; không đoán quy tắc FT/GD/MGD. Khoản chưa chứng minh độc lập có độ tin cậy bất biến, không trở thành tài sản ngân hàng chỉ vì đổi mã review.
- Tài khoản người thụ hưởng mâu thuẫn được lưu vào bằng chứng chưa quy thuộc, không cộng nhầm vào tài khoản yêu cầu. Reference không tìm được vẫn được giữ để đối soát.
- Giữ ngày/giờ gốc và độ chính xác timestamp/ngày/không rõ. Không thay ngày thiếu bằng giờ hiện tại; truy vấn lịch sử có cửa sổ và coverage bền vững, không suy chưa nhận tiền từ phần chưa tra cứu.
- Dòng tiền dùng receipt thật và source link để loại mirror; vẫn tính đúng phần thanh toán đã sử dụng khi tính tiền mặt trả lại. API/UI/CSV giữ nguyên số tiền lớn dưới dạng text.

Guest token vẫn ở fragment `/shop/orders#<token>` và được gửi trong body POST, không dùng số điện thoại hoặc mã đơn làm bí mật. Chuyển liên kết trong cùng trang hủy lượt đọc cũ, xóa dữ liệu/QR cũ và chỉ hiển thị đúng capability đang chọn. Các endpoint đọc QR/PNG có CSRF, giới hạn tốc độ và không cho tự báo đã trả tiền.

Admin có thể xem bằng chứng tại `/admin/storefront/orders/{id}/bank-evidence` bằng quyền xem đơn ở cả controller/service. Không hiển thị credential, raw evidence được bảo vệ hoặc guest token. Không tạo menu; người dùng tự cấu hình đường dẫn qua Quản lý menu.

## Kiểm thử và tính độc lập

`storefront_code` triển khai production; `storefront_tests` sở hữu oracle/fixture/test; `storefront_review` audit độc lập. Coordinator khóa nguồn, build output riêng và đối chiếu hash. Không dùng `Main`/full bootstrap hoặc database cửa hàng để dựng fixture.

Candidate hiện tại: APP V6, 73 tệp; test V16, 36 tệp. Build đạt 0 lỗi; có một warning xUnit2031 đã tồn tại ở `InvoiceNotificationTransportTests`. Kiểm tra nguồn/test/6 assembly/11 bản dependency runtime gồm 126 hash không sai lệch.

| Hồ sơ thực thi | Kết quả và nguồn dùng trong cổng |
|---|---|
| WEB05 đầy đủ, APP V6/Test V16 | 266/266 PASS, không skip |
| Hồi quy ngân hàng/POS/báo cáo được chọn, APP V6/Test V16 | 498/498 PASS, không skip; gồm một ca migration SQL R1 thật |
| WEB01–WEB04, APP V4/Test V9 | 566 ID: lần đầu 564 PASS và 2 lỗi fixture; giữ nguyên TRX lỗi |
| Retest phạm vi ảnh hưởng, APP V5/Test V10 | 8/8 PASS, gồm đúng hai ID lỗi baseline; sáu ID baseline dùng kết quả mới hơn |

Phép hợp theo ID và thời điểm thực thi có **1.330 ID distinct với kết quả đạt cuối cùng**: 560 từ APP V4/Test V9, 6 từ APP V5/Test V10 và 764 từ APP V6/Test V16. Đây là hồ sơ có tái sử dụng phần không bị thay đổi, không phải tuyên bố toàn bộ 1.330 ca đều chạy trên V6. Bốn TRX chính có 1.338 lượt; tám lượt lặp không được cộng vào số distinct. Các lần chẩn đoán, tái hiện lỗi và target trước đó giữ riêng.

Review xác nhận phần tái sử dụng: V4→V5 chỉ sửa bốn chỗ hiển thị trong ba file; V5→V6 chỉ truyền loại lỗi JSON có kiểu qua transport/facade/diagnostic. Schema, catalog, giá, checkout, giữ hàng và quy tắc nghiệp vụ của baseline không đổi. Bộ 266 và 498 đã chạy lại trên transport V6. Không dùng nhận định assembly Infrastructure/Web vẫn nguyên byte sau V6.

SQL Server `.\SQLEXPRESS` được chọn qua cấu hình test cục bộ; database có prefix GUID được guard. Phần ngân hàng dùng transport có kiểm soát và bằng chứng synthetic, không phải giao dịch thật hoặc chứng nhận merchant. Các ca hồi quy InMemory không thay thế kiểm tra constraint/concurrency/migration trên SQL thật.

Browser canonical của lượt đầy đủ hiện tại có 12 bước QR, đo ở 320/360/390/430/1440px, phím Tab và giảm chuyển động. Nội dung chính 16px, tương phản thấp nhất khoảng 6,57:1, không tràn ngang hoặc control chính dưới 44px. Browser report có hai bước và CSV, không ghi nhận mảng responsive measurement. Số `9007199254740993` và trạng thái ngày ngân hàng/unknown được giữ đúng khi hiển thị và xuất.

## Các lỗi đã được kiểm tra trước và sau sửa

- R18/R19: lịch sử POS soft-delete bị bỏ khỏi chống ghi nhận trùng; mã review có thể làm khoản chưa chứng minh trở thành tài sản. Cùng sáu ID lỗi được xác nhận đạt sau sửa; thêm SQL guard cho độ tin cậy bất biến.
- R20: ba CHECK và giới hạn dưới sequence lệch với schema SQL. Sửa đúng từng tương thích, giữ kiểm tra literal/cờ constraint; không nới normalizer tổng quát.
- R21: trigger review đổi rowversion khiến lưu Review bị rollback. Lưu latch và tải lại phiên bản trong cùng transaction; các ca thiếu/thừa/hết hạn giữ được tiền và trạng thái cần kiểm tra.
- R22: chuyển fragment sang đơn khác giữ token/UI cũ. Cùng browser case đã kiểm tra đúng lookup mới, trạng thái Review, ảnh/timer/copy cũ được xóa và capability không có trong URL/referrer.
- R23: hướng dẫn QR còn nhắc xác nhận COD. Đã sửa chữ hiển thị và nhãn nút, retest cả QR/COD cùng nhánh tạm ngừng nhận đơn.
- R24: JSON ngân hàng lỗi bị đổi thành diagnostic chung sau khi dùng transport chung. Loại lỗi có kiểu được truyền đầy đủ; ca cũ `BANK_RESPONSE_INVALID_JSON`, queued/retry và log an toàn đã đạt.

R1–R17 là các vấn đề thiết kế/nguồn được xử lý trước freeze; không ghi thành các lượt red test chưa từng chạy. Bằng chứng và ánh xạ ca nằm trong thư mục review.

Các fixture cũ được cập nhật để dùng catalog/principal hợp lệ, đúng tenant, schema lịch sử và global bank proof hiện hành. Giữ các kỳ vọng tiền, stale-context, zero-retrieve và quyền. Truy vấn inspection rỗng kiểm tra riêng cả hai cửa hàng, gồm soft-delete. Một helper giả lập từng bỏ qua duplicate-initiate đã bị review chặn trước thực thi; bản V13 chỉ build, không chạy. Oracle `Dictionary.Add` nghiêm ngặt đã được khôi phục. Không gán tay confidence hoặc bỏ guard để làm test qua.

## Migration và vận hành

Migration `20261031210000_AddStorefrontQrBankEvidence` thêm các bảng bằng chứng/processing, source link và global reference sequence theo model đã kiểm chứng. Preflight 51160 từ chối trạng thái ACB cũ chưa có binding bất biến có thể chứng minh; cần giải quyết trên phiên bản cũ trước khi nâng cấp. Không backfill lịch sử bằng tài khoản hiện tại. Down 51161 chặn xóa khi có dữ liệu tiền/bằng chứng/binding mới.

Worker website phải được bật rõ ở backend quản trị; mặc định tắt. Đăng ký namespace/merchant và mapping định danh cần bằng chứng cấu hình của người vận hành, mặc định chưa xác nhận. Kiểm thử kỹ thuật không tự cấp các xác nhận này.

Phí khách trả tiếp tục là `StoreServiceFee`; chi phí trả người/hãng giao ghi riêng. WEB05 kiểm chứng mirror bằng luồng POS thật của hệ thống báo cáo. Adapter website xuất giao → ghi bán → mirror chưa được dựng giả để coi là đã kiểm chứng; ca dương đó thuộc WEB06.

Cleanup toàn đợt đã xóa 952 thư mục GUID được chứng minh thuộc fixture, 3.011 file gồm 1.322 file khóa riêng; không sao chép nội dung khóa. Giữ 91 host log cùng metadata/hash, ảnh và bằng chứng. Không xóa 14 thư mục WEB01 có sẵn chưa đối chiếu được là của đợt thử. Mọi đích xóa đã kiểm tra đường dẫn tuyệt đối và không có reparse point; không còn thư mục runtime/media/keyring thuộc phạm vi được chứng minh.

Inventory cuối có đúng cùng năm tên/trạng thái database GUID có sẵn; đây không phải checksum nội dung database. Database thử của đợt chạy đã được fixture dọn. Không còn process khớp cả PID và thời điểm bắt đầu của fixture; các process không thuộc đợt thử hoặc PID được tái sử dụng được giữ lại. Hai đường dẫn output cố định được phục hồi đúng trạng thái ban đầu (đều không tồn tại); bản output synthetic đã tạo được giữ riêng. Coordinator và reviewer đều xác nhận 126 hash không sai lệch sau cleanup.

Chưa chạy API ngân hàng thật, chuyển tiền, đăng ký merchant, UAT của chủ cửa hàng, migration production hoặc mở bán. Chưa tự đăng sản phẩm và không tạo/sửa menu.

## Bằng chứng

- Nguồn và freeze: `.artifacts/storefront-rollout/WEB05/code/production-ready-v6-sha256.json`, `tests/test-source-frozen-v16-sha256.json`.
- Build/identity: `coordinator/ready-app-v6-test-v16-identity.json` và các manifest assembly/runtime đi kèm.
- TRX current: `tests/full266-app-v6-test-v16/`, `tests/bounded-legacy-app-v6-test-v16/`.
- Phép hợp độc lập: `coordinator/effective-1330-independent-v16.json`; test độc lập: `tests/effective-case-gate-v16.json`.
- Browser current: `tests/canonical-full266-evidence-v16.json`; toàn bộ ảnh và JSON canonical được giữ.
- Findings/repro/audit: `coordinator/findings.json`, `tests/repros/` và `review/`.
- Bản tóm tắt lưu cùng tài liệu: [test cuối](storefront-web05/test-final-v16.txt), [review cuối PASS](storefront-web05/review-final-v16.md), [digest và phạm vi bằng chứng](storefront-web05/evidence-sha256-v16.json).

