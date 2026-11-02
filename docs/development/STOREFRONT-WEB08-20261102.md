# WEB08 — Trải nghiệm, ảnh, animation và 3D

**PASS phần triển khai WEB08 W08T01–04 sau code, test và review độc lập trên APP9/Test5 (S9). Bốn nhóm chính đạt 1.999/1.999 test duy nhất, không lỗi/bỏ qua/trùng; WEB00 riêng đạt 378/378 assertion. Chữ 200%, chín mẫu hiệu năng, nguồn cuối và tài nguyên đều review PASS.** Các điều kiện nghiệm thu hàng thật, thiết bị thực và phát hành còn mở; WEB09 chưa bắt đầu, production chưa mở.

## Chức năng

- Giữ thiết kế trắng–xanh, banner và chữ có tương phản rõ; hoàn thiện trạng thái tải/rỗng/lỗi, phản hồi giỏ hàng, focus/bàn phím, nút dễ chạm và tùy chọn giảm hiệu ứng.
- Xem 3D tùy chọn trên trang chi tiết: tải khi khách mở, xoay, zoom, đặt lại, chọn model và thu gọn. Chỉ có một viewer/context; không auto-spin hoặc vòng render khi nhàn rỗi. Ẩn tab, ra ngoài viewport, đóng hoặc mất context sẽ giải phóng viewer; mở lại cần thao tác rõ ràng.
- Giữ bố cục trong phiên xem khi tải xong hoặc lỗi/tự đóng; thu gọn theo lựa chọn khách. Dùng ảnh khi không có model, WebGL/module/model lỗi, offline hoặc quá hạn. Thao tác mua không phụ thuộc 3D.
- Ảnh đáp ứng 160/320/640/960, không upscale; giữ đủ tám hướng/mirror EXIF. Mọi request và cache hit vẫn kiểm tra publication, tenant, đường dẫn/file và reparse.
- Font Inter Việt hóa, Three.js và các license/provenance được đóng gói cùng website. Không tự đưa model demo thành model hàng thật hoặc thay đổi giá/đơn vị/biến thể theo model.
- APP9 sửa liên kết đầu trang bằng cách ẩn theo chiều cao thực của chính nó; khi focus, chữ 200% được xuống dòng trong viewport. Hai rule CSS thay đổi so với APP8; 33 file khác giữ nguyên.

Project khách hàng riêng là `GaoApp.Storefront.Web`, entry `StorefrontProgram.cs`; dùng chung lớp nghiệp vụ và kho hiện có. Không phụ thuộc runtime project quản trị, Tests hoặc LabelPrint; không thay sổ tiền/kho, schema hay menu. Khởi tạo metadata EF của context DI thật trước listening trong đúng mode preview hợp lệ; không query/ghi DB hoặc ghé route để làm ấm. Chẩn đoán request mặc định tắt.

## Giới hạn tài nguyên

| Tài nguyên | Giới hạn |
|---|---:|
| Model tự chứa; JSON | 8 MiB; 1 MiB |
| Nodes/primitives/materials | 256/128/32 |
| Vertices/triangles theo exposure | 100.000 mỗi loại |
| Texture cạnh/tổng decoded pixels | 2.048 px/8 triệu |
| Estimated asset GPU/known loader working allocation | 64 MiB mỗi loại, tách riêng |
| Viewer/context; mobile DPR/render target | 1; ≤1,5/1 triệu pixel |
| Deadline model | 15 giây |
| Ảnh raw/header/cạnh | 20 MiB/16 triệu pixel/8.192 px |
| Native decode; encoded cache/entry | 2 đồng thời, không queue; 64 MiB/3 MiB |

Hỗ trợ GLB2/glTF2 static triangles tự chứa cùng tập extension đã khai báo; từ chối external URI, codec và cấu trúc chưa hỗ trợ. Hai budget memory là ước tính asset/loader, không phải đo total heap/driver/VRAM. Native import/synchronous parse không thể hủy giữa chừng; preflight/generation/deadline và dispose kết quả muộn vẫn áp dụng.

## Bản kiểm thử và quy trình độc lập

Implementer `storefront_code` viết production; `storefront_tests` viết/chạy test theo oracle độc lập; `storefront_review` kiểm tra nguồn và bằng chứng. Coordinator khóa snapshot/build. Có lỗi thì sửa, test và review lại; không nới ngưỡng hoặc chọn lượt đo nhanh.

- APP9: 34 đường dẫn production, manifest `94C1FCDCF74C915D0A58D3134C1AC59CF6F4FA62CCE6234D39ABFC6D10905FD3`; Test5: 24 đường dẫn, manifest `E26169AF2C6E44420F2E8A63E3ACFB0D1C7237B2724B65DA42481FE20211A593`.
- S9 tại `.artifacts/w8s9`: 5.261 input, 4.294 origin check, 58 owned paths, hai font deletion đã lưu preimage; không có exclusion source. Source SHA `20D52066195F71E33589D586A5AA1832768AA5B5593B95FFA479678EE6F693D1`.
- Build offline bảy project: 0 lỗi, một cảnh báo xUnit2031 có sẵn; 49 output path nằm trong snapshot. READY `E9ACD4F84BB3A78122F07CF0FDA9EFFEF4990CC2D7F97CA7CED2656C330BB2E2`: 10.587 check, drift0.
- Review nguồn/clone `3FB05D847030E8656AF4CE8520F0B989C7190FEA4BCFB4018E6B5A0A6A69227B`; compiled/PDB/Main IL `DFADB7D25F79FBBFE0008BFC700795251EF3B0483BB79176E598840F6E65772B`; READY độc lập `47749B08402E76149924DEC8B69B5E2864F8C16185330F31365CBF935F35A78B`.

Bốn full TRX S9 tạo primary union; WEB00 pure và kiểm tra bổ sung được đếm riêng. Không giữ outcome S8 sang S9. Runner chính giữ nguyên `8AE1B08C4FE2DD0D09A878EF17C9C578B2A8BAC33344001D879F5C445DD447B4`, noBuild/noRestore, diagnostics=false; watcher exact PID/start/exe trước mỗi canonical owned root. Mỗi nhóm PASS và đóng postguard trước nhóm tiếp theo; cả bốn đều10.587 check/drift0/master5 giữ nguyên/exit0.

Direct union hiện tại: 1.999 rows/1.999 ID duy nhất/1.999 PASS/0 trùng/0 nonpass. Summary `83BC2D18865A6706AC445792E2A26F66D89E1E843B10A066CEA60874C726322A`; review độc lập `179539F3A7AA71F14EF096BE91C08DD2A00033835807A68FAC75CDF5761E0A35` PASS. Chỉ dùng full78, Prior1266, Legacy498 và Affected157 r2. R1 Affected giữ lại/loại khỏi primary vì thiếu quan sát; đã đối chiếu cùng157ID, không giữ outcome lịch sử hoặc cộng pure/supplement.

| Nhóm hiện tại S9 | Kết quả |
|---|---|
| WEB08 | 78/78 PASS, postguard và review độc lập PASS |
| WEB01–07 | 1.266/1.266 PASS, postguard và review độc lập PASS |
| Legacy liên quan | 498/498 PASS, postguard và review độc lập PASS |
| Kho/hóa đơn bị ảnh hưởng | r2: 157/157 PASS, postguard và review độc lập PASS |
| WEB00 pure, đếm riêng | 378/378 assertion PASS, review độc lập PASS |

Legacy498: TRX `0945497D9DF66E607A49FD60018C5CEF7EEC95C050A30AF25B026CFDACD1C273`; review độc lập `46C2B9EEEFD809235D15D4C31026E1143F4B25E1E239A29EBBB941DA7BAC4425` PASS. Hai đường SQL/HTML cố định ban đầu ABSENT; runner finally khôi phục đúng trạng thái ABSENT ban đầu của hai leaf, sau thử đã xác minh ABSENT. Không có hash nội dung transient chưa lưu.

Affected157 r2: TRX `1F0E7DA9E156972A470870CD085191CCADF1C09F00ECA19366DB610F61954FF3`; review độc lập `316DE82F5004311EE1A12BF0729CA2F81631B3D52CD208E9F3E67D53FAB09C46` PASS. Backup/restore hai đường SQL/HTML của runner chỉ áp dụng Legacy, không phải bằng chứng một lượt sửa file trong Affected.

Review full78 `8C58728BD20712C3F13C58268EF9DD8AA3678861AE5AA1DD44984C9C623A9076` xác nhận 33 model/29 media/16 browser, 17 Node report/362 body SHA, 66 responsive rows (BODY font được đo≥16px; nhãn phụ có thể nhỏ hơn; min contrast khoảng6,57), đủ tám EXIF pixel/mirror, native hidden/visible/dispose/reopen/exit và listener đóng. TRX `23B8A1CA223A8B41F3968F06CDEA455DB46131700B580F6F5BA42DB4A16CE81F`. Đo DPR lúc mở ban đầu không được suy thành giới hạn đã đo liên tục sau reopen.

## Chữ 200% và bàn phím

Native OS-scale API được Chrome nhận nhưng không tăng font thực, nên không được tính PASS. Probe bổ sung dùng inspector user stylesheet **chỉ đổi font-size**, đo thực font/Range; không sửa layout hoặc CSP, không gọi đây là native browser zoom/điện thoại vật lý.

S9 V6 chạy trước primary trên public DLL hiện tại với SQL fixture GUID riêng: 609 lần đo chữ/control đều ratio2,0; 15 trạng thái home/detail/cart tại320/390/1440; không tràn ngang/clip; ba giỏ hàng bằng bàn phím có tổng50.000; 36 asset body SHA và 21 ảnh. Sáu witness baseline/200% cho thấy unfocused và sau Enter có bottom=-12, focus nằm đủ trong viewport và Enter đến main/#main. Exit0, counts trước/sau bằng nhau, host/fixture disposed, post10.587 drift0/master5 giữ nguyên. Summary `311FDA5FD6B42BBD667685A75010A1B618C855BAC3478C10DCD9F886878A7A5D`; review độc lập `5FCF840213D1BBE762BA23C0A104B76F8F8AF876A9D7C5075B728D77223C42B5` PASS.

WEB00 pure hiện tại: 378 assertion executions PASS/377 ordinal Group|Name labels; mười input và sáu global output path được kiểm tra, restore/compiler/run đều exit0. DLL thực `07BE16FC171BA5C679BD55A2253832FC621D9307670D3C94067DF3DAA03CBE02`; review độc lập `BA8EBDD029A4F042FF008EC7DF6A7C393CC37F19AC9B9B841ACB2441ED3A28A9`. Không cộng vào primary TRX.

Ảnh fullPage không ghi focus/scroll cùng thời điểm, nên không dùng riêng ảnh đó để kết luận trạng thái skip-link trong viewport; witness DOM thực và ảnh viewport focus là bằng chứng tương ứng. Một câu/comment S8 cũ trong tài liệu probe đóng băng được ghi erratum; executable wrapper/harness khóa S9. Không sửa âm thầm nguồn đã chốt.

## Hiệu năng và phạm vi bằng chứng

Đủ chín mẫu bình thường S9 đã đo và review độc lập đạt trong full78 đã đóng. Review raw `4E0B00FA27CADF3C3DDB3E214BC490F03018FE20C56280974B1177EFA4BFDD7B`. Mobile390×844/DPR1,5/touch, CPU4×, download1,6Mbps/upload750kbps/150ms, ba cold +ba warm; desktop1440×900/DPR1, ba cold không throttle. Ngưỡng đã khai báo: LCP≤2.500ms, max observed EventTiming≤200ms, CLS≤0,1. Giữ mẫu cold đầu; không thêm product GET làm ấm hoặc retry để chọn kết quả.

| Mẫu | LCP ms | Max observed EventTiming ms | CLS |
|---|---:|---:|---:|
| Mobile cold0 | 2464 | 96 | 0,00017127 |
| Mobile cold1 | 1804 | 96 | 0,00017127 |
| Mobile cold2 | 1812 | 112 | 0,00017127 |
| Mobile warm0 | 1828 | 96 | 0,00017127 |
| Mobile warm1 | 1800 | 96 | 0,00017127 |
| Mobile warm2 | 1816 | 96 | 0,00017127 |
| Desktop cold0 | 1052 | 32 | 0 |
| Desktop cold1 | 304 | 32 | 0 |
| Desktop cold2 | 268 | 32 | 0 |

Cả chín navigation.serverTiming đều là []; chẩn đoán request ở mặc định tắt. Không dùng lượt diagnostic cũ thay cho các mẫu bình thường này.

EventTiming lab trên tương tác đã chạy không phải field INP/CrUX. Responsive dùng320/360/390/430/768/1440. Điện thoại vật lý, Linux runtime và model/ảnh hàng thật của cửa hàng chưa được xác minh.

Review Prior1266/tenant `AA77FDA4078DB2AE6265F3F9C0DAEF7784CFFEA6116AB92CDBB8C6DC678503D6` PASS; TRX `32F585B088DCFA997E7C394432809A68A696BB4BD8CEA730A34B9EF8272D655F`. 18 row của ma trận được đối chiếu với current test/host/log, phân biệt public process và SQL/service-only; gap metadata W01 Production cũ được giữ rõ. Own media200, foreign/unlinked/raw/unpublish404, giả StoreId/giá/CSRF và gate negatives đều current PASS. Không tuyên bố đã chạy general TenantDataScopeSqlServer hoặc raw unbound public query. QR thực thuộc các ca WEB05 sản phẩm ảnh; không có tuyên bố một ca kết hợp timeout3D→hoàn tấtQR.

## Lỗi và tài nguyên giữ lại

Lịch sử đầy đủ tại `.artifacts/storefront-rollout/WEB08/coordinator/CURRENT-CHECKPOINT.json`: đã sửa toolbar gây scroll, nền renderer, fixture native hidden, đường dẫn Windows, ảnh lỗi trước listener, late layout shift, oracle retry ép refetch, metadata cold startup và skip-link chữ200%. S8 từng đạt1.999 test chính nhưng chỉ còn là lịch sử sau APP9; không tính thành kết quả hiện tại.

Affected r1 S9 thực chạy157/157 PASS nhưng không tính primary: watcher hết180 giây chờ trước khi root test bắt đầu14 giây, capture muộn không còn root sống. Giữ toàn bộ TRX/tài nguyên. Watcher V4 chỉ tăng thời gian chờ xuất hiện root lên900 giây và sửa thông báo tương ứng; không đổi cách nhận diện, assertion, deadline test/app hoặc ngưỡng hiệu năng. Review nguồn V4 `B01D0BD6414C567E13A328753EEAC3A5E7D65D583846274215FDCAF066E0E65D` PASS, full157 r2 đã chạy157/157 PASS với watcher trước root48 giây/SeenRoottrue/không lỗi/đóng đủ. Ba nhóm trước giữ nguyên vì source/DLL/test không đổi; union chỉ dùng r2 cho Affected.

WEB07 đã xóa đúng1.510 XML/1.533.668byte theo đồng ý trực tiếp và review độc lập; giữ nguyên1.272 XML context-only/1.291.920byte. Consent cũ không mở rộng sang WEB08.

Kiểm kê hiện tại S9: 10 family đã khai báo/no-reparse; chủ động giữ1.031 XML/1.047.151byte (24 provenance đầy đủ,1.007 gap ghi rõ), không có PFX; giữ hai native profile có bằng chứng đóng bình thường. 4.833 reference rows/468 numeric PID queries không có exact owned match;25 tên TLS CurrentUser và file đã biết đều vắng; master5 giữ nguyên. Cả hai lượt Affected được giữ; gap quan sát r1 không được bổ sung ngược. Summary `13A17C3CA48D66BB77DE81CD71BAF6A9FEDC5112964BA7E81D2609877FB25266`; review độc lập `92B4D45266546984CC689919C768F29781BBEC938F6418974364C3CB814C1148` PASS.

Inventory/hash/size/provenance chỉ áp dụng phạm vi ghi rõ. Hashing có đọc byte; không parse/hiển thị/copy nội dung private XML. Các gap PID/exe/parent không nâng thành per-file creator proof. Không tác động process/database/file không xác định, không tuyên bố toàn máy sạch hoặc cộng các tier provenance chồng nhau thành số creator. Các resource packet S3–S8 và blank probe giữ riêng theo thời điểm, không gộp lẫn với số S9.

Nối metadata S3–S9: 2.095 XML đã ghi nhận/2.127.806byte và13 profile đã khai báo, bao gồm số S9 ở trên; không cộng lần nữa. Review `5EAA2A9F71FCA50460099575FE8806BA9C1285005B09E20FC57B8BD0144F75F2` PASS. Đây là đối chiếu metadata đã lưu, không quét lại private file/profile/OS/key lịch sử. Hai giới hạn nullable-exit/supplement cũ được giữ; không tuyên bố cả13 profile exit0 hoặc vắng mới đo.25 TLS S9 mới kiểm tra và25 TLS S8 lịch sử không gộp thành50 key mới xác minh.

Chốt nguồn cuối00:23:09–00:24:17UTC:10.587 source/runtime/tool/origin check, drift0, đúng READY S9; không chạy app/test/SQL. Rows SHA `B44B7D17D9366010D2652FA598CDC0D6136335D1B1725623546321B642D44E62` tại `coordinator/final-ready-check-v1`; review độc lập `26398EDC3DAC822F35B8AD198126A0AADFB9BF04F109ECC14556DE668BBF32A0` PASS. Một hash-check đọc riêng đã lỡ khởi chạy trùng cũng kết thúc10.587/drift0 lúc00:25:33UTC; không chạy thêm test, không cộng vào số kiểm tra nghiệp vụ.

## Đường dẫn và điều kiện phát hành

- Khách: `/shop`, `/shop/products/{id}`; Xem3D xuất hiện khi có model phù hợp đã liên kết.
- Quản trị hiện có: `/admin/media-library`, `/admin/storefront/products/{id}`, `/admin/storefront/products/media`. Controller/service vẫn kiểm tra quyền; chủ cửa hàng tự tạo/sắp menu bằng `AdminMenuItems`. WEB08 không tạo menu tự động.
- Runtime hiện chỉ configured Development preview/loopback. Chưa triển khai production, áp migration cửa hàng hoặc chuyển tiền thật. Không commit/push/reset công việc khác.
- Trước phát hành cần cửa hàng duyệt dữ liệu/ảnh/model hàng thật, thử trên thiết bị thực, xác minh môi trường triển khai và UAT. Fixture kỹ thuật không phải hàng thật đã nghiệm thu; những điều này không được coi là đã PASS bởi lab.
- WEB09 chưa bắt đầu. Kết luận hiện tại là PASS phần triển khai W08T01–04, không phải nghiệm thu mở bán.

Mapping yêu cầu `5628038517D44F1207EF879A0E6B4D2366CFCAF4021DC9166FB4AB9A8ED58C4E` đối chiếu nguyên văn plan/oracle/A01–A09 với source và ID test hiện tại. Theo A01/oracle trước viết code, việc cửa hàng duyệt ảnh/mô tả/model khớp SKU thật và UAT trước phát hành được giữ riêng. Fixture không thỏa tiêu chí khớp hàng thật; không coi các điều kiện đó đã đạt hoặc tự bỏ tiêu chí hoàn thành ban đầu.

Bàn giao nguồn: [APP9](../../.artifacts/storefront-rollout/WEB08/code/final-code-handoff-app-v9.md). Các manifest và review cụ thể nằm tại [hồ sơ WEB08](../../.artifacts/storefront-rollout/WEB08/).

Hồ sơ ký chốt: [gate phần triển khai](../../.artifacts/storefront-rollout/WEB08/coordinator/final-gate-implementation-v1.json). Gate được tạo sau khi review độc lập gói tài liệu cuối; hash tài liệu trong gate dùng để kiểm tra đúng bản bàn giao.
