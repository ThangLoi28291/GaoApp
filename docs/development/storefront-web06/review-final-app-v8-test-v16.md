# WEB06 — Review độc lập cuối

**PASS — APP V8 / Test V16.** Không còn finding P0/P1/P2 chưa xử lý trong phạm vi nguồn, ca kiểm thử và bằng chứng được review. Production tiếp tục đóng. Sau khi coordinator niêm phong cổng WEB06, tạm ngưng và báo cáo theo yêu cầu; không bắt đầu WEB07.

## Candidate và tính độc lập

- Production: 137 đường dẫn, manifest `C96933EC39CD325D821408202B048CC6A7B528EC84391F447BE5CA1329509D10`.
- Test: 38 đường dẫn, manifest `937F30A3D49DAE099DE699DFD203F78B06AF365F98198C0F022B3536B233B0F2`.
- Build coordinator đạt 0 lỗi; một warning xUnit2031 đã tồn tại. Đối chiếu cuối độc lập 137 APP +38 test +6 DLL +11 dependency runtime =192, không sai lệch.
- Reviewer chỉ ghi artifact review và probe SQL riêng đã được phép; không sửa APP/test, không build graph chung. Code, oracle/test và review do ba vai trò độc lập thực hiện. Dùng before bytes của workspace và PDB thực, giữ nguyên lịch sử lỗi.

## Kết quả kiểm thử đã tự tính lại

| Nhóm | Bằng chứng được chọn | ID PASS distinct |
|---|---|---:|
| WEB06 | FULL219 APP V8/Test V16 |219|
| POS/ngân hàng/báo cáo | FULL498 APP V8/Test V16 |498|
| Kho/giá vốn/hóa đơn |157 APP V8/Test V16 |157|
| WEB01–05 |795 APP V7/Test V13 +36 Test V14 +1 Test V15 |832|
| Tổng |874 hiện tại +832 giữ theo phiên bản |**1706**|

Cả 1706 ID được chọn đều PASS, 0 fail/skip, không trùng nhóm. Sáu TRX chính có1744 lượt;38 lượt thay thế được giữ trong lịch sử và loại khỏi coverage. Không tuyên bố toàn bộ1706 hoặc832 đã chạy trên APP V8. TRX219/498 cũ chỉ dùng đối chiếu ID và lịch sử. Probe F02, target lặp, diagnostic và các vector assertion không cộng thành ID mới. Bộ219 gồm17 ca model và52 ca nhận diện schema cùng các ca SQL/service/HTTP/race/browser; số lượng này không phải219 luồng end-to-end.

Reviewer đã so từng ID/outcome/version/TRX hash với phép hợp độc lập của coordinator và tester. Bộ157 đúng bốn class đã chọn:100 AutoInvoice,31 SaleValuationCostPolicy,21 InventoryPosPostingContract và5 InventoryMovementSqlServerConcurrency. Các phạm vi fixture không bootstrap Main hoặc dùng database cửa hàng.

Việc giữ832 có cơ sở cụ thể: APP V8 chỉ đổi hai method trong Web, giữ đường SQL Server có rowversion8 byte hợp lệ, thứ tự và câu HOLDLOCK, cùng các kiểm tra nguồn/tiền. Toàn bộ1034 document PDB Web chỉ đổi đúng2 nguồn;1032 còn nguyên. Test755→756 document chỉ đổi2 partial endpoint và thêm1 helper;753 còn nguyên. Bốn DLL production khác giữ nguyên, hai reference assembly Web giữ ABI cùng byte. DLL Web thay đổi. Đây là bằng chứng nguồn/ABI và tương đương đường SQL được review, không phải tuyên bố mọi method IL cùng byte. Các lượt219/498/157 hiện tại đã đạt sau sửa.

## Phạm vi nghiệp vụ W06T01–04

- W06T01: danh sách/lọc và cờ thiếu tồn theo dữ liệu thực; lịch sử người xử lý, quyền/tenant, revision/rowversion, replay cùng key/body và conflict. Quyền controller/service độc lập với menu; không tự tạo menu. Đường dẫn `/admin/storefront/fulfillment` và `/admin/storefront/service-revenue` để chủ cửa hàng tự cấu hình.
- W06T02: nguồn Storefront thực và FK POS nullable có điều kiện; typed hold Web12 chuyển Delivery11, xuất mọi child trong một transaction dưới khóa số dư; giữ lượng giữ khác, chính sách kho âm tin cậy, lát giá vốn gốc và nhận lại đúng số còn ngoài kho. Incident được đối soát bằng chứng thực, không xóa lịch sử xuất hoặc tự ghi hoàn tiền.
- W06T03: nhân viên giao gần, hãng/vận đơn toàn quốc và kết quả được người có quyền cập nhật; HTTP/busy/replay và guest theo dõi được kiểm tra. Không phụ thuộc bắt buộc API hãng vận chuyển trong bước này.
- W06T04: tiền khách trả, người thu còn phải nộp, tiền cửa hàng nhận và khoản giữ lại có chứng từ tách riêng. Có nguồn hàng/ca mở thực khi ghi bán; hàng và phí dịch vụ có chủ thể/thuế/chứng từ riêng, không cộng tiền hoặc xuất/COGS lần hai. Có ca230000=205000+25000, phân bổ theo chủ thể, bank claim, invoice19,4, recovery sau commit và propagation giá vốn/báo cáo. Thuế/hóa đơn theo chính sách hiện hữu; không suy eligibility từ QR hoặc chủ thể từ tồn kho.

Phạm vi kết luận dựa trên các ca và integration boundaries được ghi nhận trong checklist/source notes. Mua hàng/merchant đăng ký ngân hàng, provider hóa đơn và hãng vận chuyển thật vẫn cần UAT ở giai đoạn sau.

## Findings và sửa đã kiểm chứng

- **F02 P1:** APP V1 thực sự nhận thêm Cash1 không nguồn sau native sale hoàn tất. Probe độc lập dựng positive thật, cùng SQL trên DLL V3 bị chặn51200 và rollback giữ tiền/kho. Hai packet gốc được đóng băng. Guard nguồn/hạn mức/bất biến và ordinary-POS control cũng PASS trong bộ hiện tại. QR dùng GlobalReceipt; COD cash/bank dùng CodReceipt; khấu trừ dùng chứng từ riêng. Không claim standalone probe đã chạy lại trên V8.
- **F01 và replay:** original AT cross-store manual ambiguity từng false; riêng replay từng tính nhầm chứng nhận của chính receipt là claim ngoại lai. Nguồn sửa giữ original/global ambiguity, attestation rồi durable ledger rồi certification transaction; confidence3 và toàn bộ số tiền quan sát ở Review không được nâng thành certified inflow thứ hai. Ca nguyên thủy, hai chiều race, đổi mapping và crash sau ledger commit đều PASS hiện tại. Candidate V5 extra-conflict chỉ được phát hiện bằng nguồn và sửa trước runtime; không gọi đó là một lượt V5 thất bại thực.
- **Schema/Down/HTTP/UI:** chỉ nhận12 whole-predicate forms cho15 khác biệt CHECK đã quan sát, giữ negative/literal/disabled/untrusted oracle. Bỏ4 default0 gây UPDATE snapshot không cần thiết; giữ trigger. Lỗi thiếu tồn/trả quá hạn mức giữ đúng status/code. Nút từng có contrast4,24827 được sửa CSS, rồi hai browser hiện tại hoàn thành đủ bước.
- **Provider/auth:** lượt498 cũ453P/45F gồm43 SQL lock qua InMemory,1 null RowVersion và1 HTTP500. APP V8 giới hạn SQL lock đúng provider, giữ manual legacy và fail-closed native version. Hai endpoint dương tính được chuyển sang SQL/HTTPS thực, giữ security/contact/heartbeat/duplicate/Cash30000 và không tạo tiền/chứng nhận. Ba invalid-version input là assertion trong một ID cũ. All498 hiện tại PASS, cùng45 ID lỗi cũ.
- **Fixture lịch sử:** raw old-column INSERT giữ tiền, menu719/quyền/tồn, actual line ID và detached fingerprint; lineSource dùng xuất giao thật và vẫn chặn51090. Guard51200/51201 chỉ được nhận đúng phạm vi/message. Final read sau Down dùng cột lịch sử; không giả định toàn chuỗi nhiều migration là một transaction nguyên khối. Lượt832798P/34F, retake3736P/1F và cùng ID cuối PASS được giữ nguyên.

## SQL và browser canonical hiện tại

Fresh schema GUID `d20210af6d3b451f8017ee99a1aefc00` có expected/live fingerprint `A41319EB5ED0AB52F656CFBECA709540FE41012DECD7B0DE7B1ECB35254190B8`; cả8 vùng metadata0 mismatch, raw CHECK enabled/trusted và trigger enabled, InvoiceDetails/Head quantity19,4.

Canonical browser hiện tại: national `92523e4e9ca948089afaefba1ad281f7`, local `96375a7e0e1b4addbfc6806ebbd1e8e7`. Mỗi luồng10/10 bước,36 DOM records(35 responsive+1 keyboard),38 PNG ở320/360/390/430/1440, font16, minimum measured contrast4,5006935751; không lỗi JS/contrast/touch/overflow được ghi nhận. Mỗi luồng có1 prepare response abort cố ý; replay giữ đúng key/payload. SQL DMV thực có đúng owner/waiter/database/resource trong thời gian giữ khóa.27 query-version mỗi luồng đều được ràng buộc response-body digest;13 privacy oracle vectors khác số request thực.8 static response bodies mỗi luồng khớp nguồn. Node/GUID/method/branch và timestamps đối chiếu actual TRX; không gọi đây là artifact attachment của TRX.

Reviewer thực sự xem8 PNG hiện tại ở5 chiều rộng và cả hai cách giao, gồm focus, thiếu hàng, đang giao, delivered-unremitted, completed, maintenance và token-cleared; chưa thấy blocker. Không claim xem tay toàn76 ảnh. Admin chỉ feature Razor body trong host tối thiểu, bảng nhỏ có vùng cuộn ngang; guest là process public riêng. Đây chưa phải UAT toàn layout/sidebar hoặc máy người dùng.

## Cleanup và bằng chứng cuối

- Fresh master read chỉ tên/trạng thái: đúng5 R2 GUID database ban đầu,0 R1; không đọc/checksum nội dung database cũ.
- 1515 đích GUID sở hữu trong6 root đã kiểm tra exact path/phạm vi/recorded ownership/no-reparse và hiện không còn;4812 file hết,1649 private XML keys không được archive.3151 safe log/metadata có hash đúng và exact tree.14 old unmatched là những thư mục còn lại được giữ nguyên.
- 93 runtime CNG before/create/cleanup triples trong archive và1 diagnostic chỉ before/cleanup được đối chiếu; không bịa after-create riêng. Fresh read94 đúng tên CurrentUser và tệp crypto đều absent; không enumerate/delete khóa khác, không trust-store install hay copy private key bytes.
- 364 process references không có identity sở hữu trùng đang sống.55556/3140/29824 giữ identity ban đầu. Fresh audit thấy PID58612 đã được dùng lại bởi pwsh có thời điểm khởi tạo khác; để nguyên. Không claim mọi PID số cũ đều absent hay dừng process không sở hữu.
- Hai output SQL/HTML đã phục hồi đúng trạng thái ban đầu là absent. Final192 hash khớp;189 tester evidence files và43 direct tracked-digest files khớp length/hash.76 PNG hiện tại được ràng buộc transitively.

Tài liệu `STOREFRONT-WEB06-20261031.md` và test summary đã được đọc về nghiệp vụ, phiên bản, giới hạn và cleanup. Header pending-review sẽ do coordinator đổi sau kết luận này; gate/seal ràng buộc các bản prose cuối, tránh self-hash cycle. Production chưa mở bán, chưa migration database cửa hàng hoặc giao dịch ngân hàng/hoàn tiền thật.

Bằng chứng reviewer trọng yếu: `app-v8-source-and-sql-equivalence-review.md`, `test-v16-frozen-source-review.md`, `app-v8-test-v16-ready192-and-semantic-retention-closure.json`, `full219-app-v8-test-v16-canonical-independent-audit.json`, `legacy498-app-v8-test-v16-independent-audit.json`, `effective1706-app-v8-test-v16-independent-union.json`, `final-cleanup-identity-evidence-app8-test16-independent.json`. Digest riêng: `review-final-evidence-sha256-app8-test16.json`.

**Kết luận reviewer: PASS WEB06 trong phạm vi đã review.** Coordinator có thể đóng cổng, niêm phong prose/hash và tạm ngưng; WEB07 không được bắt đầu.
