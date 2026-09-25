# Đợt 3 — tối ưu báo cáo và kiểm tra tải local

Ngày 09/09/2026. Tiếp nối `SECURITY-PHASE2-HOST-PREPARATION-20260909.md`.

Đã sửa điểm nghẽn báo cáo lợi nhuận và tạo bài tải có thể chạy lại. Bài mở rộng 10/20/50 client, 100 lượt/client hoàn thành 8.000 yêu cầu, không lỗi HTTP, không sai tổng tiền/tồn và không ghi trùng. **Chưa nghiệm thu production hoặc chứng nhận 50 máy chạy toàn bộ ứng dụng**: bài burst ngắn cùng cấu hình còn có 18/100 báo cáo bị giới hạn ở mức 50 client.

Theo lựa chọn của người dùng, không backup dữ liệu test. Không reset/xóa database ứng dụng, không khởi động lại app đang chạy. Các bài SQL dùng database LocalDB tên ngẫu nhiên riêng và tự dọn sau bài. File JSON/TRX là kết quả kiểm tra, không chứa bản sao database ứng dụng.

## Thay đổi đã làm

| Phần | Hành vi mới | Ý nghĩa và giới hạn |
|---|---|---|
| Transaction đọc | Đổi riêng báo cáo lợi nhuận/điều chỉnh giá vốn từ Serializable sang Snapshot | Đọc một phiên bản dữ liệu nhất quán trong toàn transaction. Kiểm tra SQL xác nhận hủy đơn hoàn tất khi báo cáo vẫn đang đọc; báo cáo đang chạy giữ số cũ, lần đọc sau thấy số mới. |
| Truy vấn chứng từ | Tách nhánh tìm theo đơn và theo giao dịch kho, ghép bằng UNION trong SQL | Loại bỏ điều kiện OR với subquery chậm. Vẫn lấy được chứng từ có tham chiếu đơn sai nhưng liên kết giao dịch còn tồn tại; không nhân đôi chứng từ. |
| Tổng hợp | Dùng lookup/hash set cho chứng từ, dòng hoàn, pháp nhân, đơn, ca và nhóm thời gian | Giảm việc quét lại toàn bộ tập dữ liệu bên trong vòng lặp từng đơn/nhóm. Các kiểm tra giá vốn, hoàn/hủy, dữ liệu thiếu/sai được giữ. |
| Chỉ mục | SalesReturns(StoreId, CompletedAtUtc); InventoryValuationEntries(StoreId, EntryType, OccurredAtUtc) | Hỗ trợ lọc theo cửa hàng và thời gian, đặc biệt báo cáo điều chỉnh giá vốn. |
| Giới hạn tài nguyên | Tối đa 2 báo cáo xử lý đồng thời, 48 yêu cầu chờ/process; chờ tối đa 5 giây; xử lý tối đa 30 giây theo cancellation token | Hàng chờ không mở thêm truy vấn báo cáo. Quá tải trả 503, mã REPORT_BUSY, Retry-After: 5. UI giải thích cần đợi hoặc thu hẹp ngày; không tự retry liên tục. |
| Giới hạn dữ liệu | Tối đa 100.000 dòng nguồn được đọc vào bộ nhớ cho một báo cáo | Áp dụng TOP trong SQL trước materialization; vượt giới hạn trả lỗi yêu cầu thu hẹp ngày, không trả tổng tiền bị cắt cụt. Đây là tổng số dòng từ nhiều bảng/lượt đọc, không phải 100.000 đơn. |
| Theo dõi | Meter GaoApp.Reports: profit.duration và profit.rejected | Duration tính thời gian xử lý và chờ của yêu cầu được nhận vào hàng chờ; yêu cầu bị từ chối ngay có counter riêng. Cần cấu hình thu metrics trên host để lưu lịch sử. |

Báo cáo vẫn tổng hợp và phân trang chi tiết trong RAM sau khi đọc nguồn; chưa chuyển mọi phép tổng hợp xuống SQL hoặc tạo job báo cáo nền. Giới hạn được dùng chung giữa các cửa hàng trong **một process**. Tăng nhiều web instance sẽ nhân giới hạn lên; cần thiết kế ngân sách dùng chung trước khi làm việc đó.

## Migration và cấu hình host

Migration của đợt này: **20260909061611_EnableSnapshotProfitReads**. Up bật `ALLOW_SNAPSHOT_ISOLATION ON` và tạo hai chỉ mục trên. Không bật `READ_COMMITTED_SNAPSHOT`, không dùng NOLOCK và không xóa dữ liệu nghiệp vụ. Schema preflight chỉ cho phép đúng câu SQL này trong đúng migration, giữ nguyên cơ chế từ chối raw SQL chưa được rà soát.

Source tại lúc kiểm tra còn có migration **20260909062834_AddAcbCallbackStoreRouting** từ phần ACB đang được phát triển cùng workspace. Danh sách kỳ vọng của các test migration đã được cập nhật để bao gồm cả migration đó; không bỏ qua nó khi kiểm tra schema hiện tại.

Các biến môi trường tương ứng:

| Biến | Mặc định |
|---|---:|
| Reports__Profit__MaxConcurrent | 2 |
| Reports__Profit__MaxQueued | 48 |
| Reports__Profit__QueueTimeoutMilliseconds | 5000 |
| Reports__Profit__ExecutionTimeoutSeconds | 30 |
| Reports__Profit__MaxSourceRows | 100000 |

Chạy GaoApp.Migrator từ cùng release **trước** Web Production. Tài khoản migration cần quyền bật tùy chọn database và tạo index; không cấp quyền ALTER DATABASE cho tài khoản Web chỉ để phục vụ báo cáo. Cấu hình mới chưa được áp dụng vào app local đang chạy DLL cũ.

Sau migration, người vận hành có thể kiểm tra trên đúng database:

```sql
SELECT snapshot_isolation_state_desc, is_read_committed_snapshot_on
FROM sys.databases
WHERE database_id = DB_ID();
```

`snapshot_isolation_state_desc` phải là `ON`. Giá trị `is_read_committed_snapshot_on` giữ cấu hình đã có trước migration, không bắt buộc là 0. Bật Snapshot tạo thêm chi phí lưu phiên bản dòng; theo dõi version store/tempdb hoặc persistent version store khi SQL dùng ADR, dung lượng đĩa và transaction đọc kéo dài trên host. Down chỉ bỏ hai index, cố ý giữ tùy chọn Snapshot để không làm hỏng các reader khác; chỉ tắt thủ công sau khi xác nhận không còn bên phụ thuộc.

Ngữ nghĩa và chi phí Snapshot theo [Microsoft — transaction isolation](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql?view=sql-server-ver17) và [Microsoft — locking/row versioning](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide?view=sql-server-ver17).

## Bài tải đã chạy

- Windows, .NET 8.0.29, SQL LocalDB; máy có 12 logical CPU và khoảng 16 GB bộ nhớ khả dụng mà runtime báo cáo. Client, Kestrel và SQL cùng máy.
- Kestrel thật, chỉ bind loopback với cổng ngẫu nhiên; HTTP harness yêu cầu token ngẫu nhiên riêng. Database do fixture tạo, không nhận connection string tới database ứng dụng.
- 2.001 đơn cho phần báo cáo: 1 đơn từ nghiệp vụ fixture và 2.000 đơn lịch sử tổng hợp, mỗi đơn 1 dòng, rải 180 ngày. Các đơn tổng hợp tạo chứng từ đủ cho báo cáo; không phải toàn bộ lịch sử checkout thực tế.
- 70% đọc 25 đơn gần nhất, 20% ghi nhập kho cùng SKU/kho qua InventoryMovementService thật, 10% báo cáo 180 ngày kèm so sánh kỳ trước qua service/repository thật. Mỗi client có HTTP client riêng, nghỉ khoảng 40–59 ms giữa lượt.
- Mỗi lần ghi kho gửi lại cùng lệnh hai lần ở service để kiểm tra idempotency. Đối chiếu số giao dịch và biến động tồn sau mỗi mức tải. Mỗi báo cáo thành công được kiểm tra doanh thu 40.200 và giá vốn 20.100.
- Không chạy bộ SQL test khác đồng thời với các bài đo tải này. Các ứng dụng khác trên máy và tác vụ phát triển có thể ảnh hưởng kết quả local.

### Kết quả bài mở rộng — 100 lượt mỗi client

p95 là độ trễ mà 95% yêu cầu không vượt quá. Bài này không có yêu cầu bị từ chối, nên p95 toàn bộ và p95 thành công trùng nhau.

| Client | Tổng HTTP | Đọc đơn p95 | Ghi kho p95 | Báo cáo p95 | HTTP lỗi/bị giới hạn | Thời gian mức tải |
|---:|---:|---:|---:|---:|---:|---:|
| 10 | 1.000 | 8 ms | 53 ms | 258 ms | 0 | 8,06 giây |
| 20 | 2.000 | 10 ms | 80 ms | 627 ms | 0 | 12,04 giây |
| 50 | 5.000 | 12 ms | 86 ms | 2.581 ms | 0 | 30,05 giây |

Ghi kho thành công lần lượt 200/400/1.000, delta tồn khớp chính xác và số giao dịch không nhân đôi dù lệnh được gửi lại. Tổng 800 báo cáo trả số tiền đúng. Peak working set của process client+server khoảng 393–396 MB. CPU trung bình process client+server khoảng 12–14% tính trên 12 logical CPU; **không bao gồm CPU/RAM của SQL Server**. Thời gian trên chỉ tính từng mức tải, không tính tạo database/seed/warmup/dọn fixture.

### Dao động và kết quả trước tối ưu

Giữ cả kết quả chưa tốt để đánh giá, không chỉ lần nhanh nhất:

| Phiên bản bài ngắn 20 lượt/client | Báo cáo bị giới hạn ở 10 / 20 / 50 client |
|---|---|
| Đã có Snapshot/lookup nhưng trước sửa truy vấn UNION, hàng chờ 8 | 4/20 · 24/40 · 86/100 |
| Đã sửa UNION, hàng chờ 8 | 0/20 · 4/40 · 57/100 |
| Cấu hình cuối UNION, hàng chờ 48 | 0/20 · 0/40 · 18/100 |

Ở bài ngắn cấu hình cuối, p95 báo cáo thành công mức 50 là 5,28 giây; 18 yêu cầu bị giới hạn làm bài này **chưa đạt mục tiêu phục vụ trọn burst 50 client**. Bài mở rộng sau đó đạt 500/500 báo cáo mức 50, p95 2,58 giây. Khác biệt này là lý do cần đo trên staging ổn định, spike/soak dài hơn và theo dõi SQL CPU/IO/waits. Không tăng số báo cáo chạy đồng thời hoặc thời gian chờ chỉ để che quá tải.

Các số “trước” trên đều được đo trong đợt 3, sau khi đã thay isolation/lookup; không phải benchmark source ban đầu trước mọi sửa đổi.

## Cách chạy lại và bằng chứng

Kiểm tra cuối: Release build 0 lỗi, 10 warning đã có trong test; **482/482** test hồi quy và **25/25** test SQL/migration đạt, không skipped. Nhóm SQL gồm nâng cấp database mới/từ baseline, schema đúng model/snapshot, migration chạy lại không đổi schema, chứng từ tham chiếu sai vẫn được lấy vào nguồn, các luồng giá vốn/hoàn/hủy và hủy đơn đồng thời với báo cáo. Một lần kiểm tra trước phát hiện danh sách migration kỳ vọng chưa có migration ACB mới; đã cập nhật và chạy lại nhóm SQL thành công. Kiểm tra JSON cấu hình và diff whitespace của các file liên quan cũng đạt.

Trên máy Windows có .NET 8, Node và SQL LocalDB:

```powershell
./scripts/test-local-report-load.ps1 -IterationsPerClient 100
```

Script build Release ra `Logs/security-phase3-release`, rồi chạy đúng bài LocalReportLoadTests. Có thể dùng `-SkipBuild` khi artifact đã được build từ source hiện tại. Không dùng output test thay bộ publish cho host. JSON được ghi sau warmup và từng mức tải; nếu test fail, kết quả có thể mới chứa một phần. Test tự động bắt lỗi HTTP không mong đợi và sai lệch tiền/tồn; **503 của báo cáo được ghi nhận nhưng không làm correctness test fail**, nên phải đọc số rejected khi đánh giá khả năng đáp ứng.

Bằng chứng:

- `TestResults/security-phase3/local-load-results.json`: bài cuối, 100 lượt/client, kèm SQL warmup không ghi parameter values.
- `TestResults/security-phase3/local-load-before-query-tuning.json`: bài trước sửa UNION.
- `TestResults/security-phase3/query-profile-before-tuning.json`: thời gian SQL warmup trước sửa UNION; chỉ đo ExecuteReader tới khi reader được tạo, không phải toàn bộ thời gian đọc/materialization.
- `TestResults/security-phase3/local-load-after-query-tuning.json`: UNION với hàng chờ 8.
- `TestResults/security-phase3/local-load-final-20-iterations.json`: cấu hình cuối, bài burst ngắn có 18 báo cáo bị giới hạn.
- `TestResults/security-phase3/phase3-local-load.trx` và `Logs/security-phase3-local-load-extended.log`: bài mở rộng cuối.
- `TestResults/security-phase3/phase3-regression.trx`: hồi quy bảo mật/tenant/data/observability, cấu hình liên quan và báo cáo/UI.
- `TestResults/security-phase3/phase3-reports-sql.trx`: nghiệp vụ giá vốn SQL, đọc nhất quán, hủy đơn đồng thời và migration/schema.
- `Logs/security-phase3-release-build.log`: build Release riêng để không đụng DLL app đang khóa.

## Việc còn lại để lên host

1. Tạo staging một web instance với domain/HTTPS/proxy, SQL, media và Data Protection keys theo hướng dẫn đợt 2. Dùng dữ liệu mới theo lựa chọn bỏ dữ liệu test, chạy migration/seed cần thiết từ cùng release.
2. Chạy toàn bộ luồng có đăng nhập/phân quyền, checkout/hoàn, hai cửa hàng, SignalR/màn hình phụ và ACB sandbox. Harness đợt này chưa bao gồm các phần đó hoặc mạng Internet/TLS.
3. Đo 10/20/50 client từ máy phát tải khác, warmup/steady/spike/soak, ghi SQL CPU/IO/lock waits/connection pool/version store và RAM theo thời gian. Điều tra nếu lặp lại timeout hàng chờ báo cáo; cân nhắc job nền cho báo cáo lớn nếu vượt ngân sách đã chọn.
4. Hoàn tất phần còn lại của audit: thu hồi kết nối SignalR đã mở khi quyền thay đổi, UAT nhiều vai trò/cửa hàng và các luồng truy cập dữ liệu chưa được rà soát. Chỉ mở production khi đạt tiêu chí nghiệm thu trên host.

Đợt này cải thiện F06 và cung cấp bằng chứng local có thể chạy lại; chưa phải kết luận mọi lỗi bảo mật/nghiệp vụ của toàn bộ ứng dụng đã được xử lý.
