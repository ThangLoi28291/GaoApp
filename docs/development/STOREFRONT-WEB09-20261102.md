# WEB09 — Chuẩn bị nghiệm thu, tạm chưa publish

**WEB09 phần chuẩn bị trước phát hành: code/test/review độc lập PASS trên APP4/Test7/S6; chưa publish.** Hồ sơ chốt được ghi tại gate và seal cuối; không thay thế nghiệm thu cửa hàng hoặc phát hành thật. Theo yêu cầu “Làm web09 nhưng tạm chưa publish”, không chạy `dotnet publish`/Publish targets, không triển khai/IIS/service/DNS, mở bán, migrate database cửa hàng hoặc chuyển tiền thật.

## Phần đã triển khai

- Website khách hàng vẫn ở project riêng `GaoApp.Storefront.Web`, dùng chung nghiệp vụ và kho hiện tại. Public không phụ thuộc entrypoint ERP.
- Cổng Production `StorefrontHost:ReleaseEnabled=false` tách khỏi preview Development. Admin có cổng riêng `StorefrontRelease:AllowSettingsActivation=false`; bật DB không vượt cổng server.
- Bật/dừng nhận đơn là lệnh riêng, có quyền hiện tại, active membership, cửa hàng, CSRF, rowversion 8 byte và lý do. Revision, trạng thái và AuditLog ghi cùng transaction. Bật kiểm tra lại cấu hình dưới configuration lock; dừng không ghi lại toàn bộ form hoặc đòi sửa cấu hình không liên quan trước.
- Dừng nhận mới vẫn cho phép thử lại placement đã commit, tra cứu/hủy hợp lệ và xử lý tiền/đơn cũ. Khóa cả server hoặc lỗi key/DB là ranh giới khác, không hứa public route còn phục vụ khi host bị khóa.
- `/health` là liveness; `/health/ready` phân biệt `runtimeReady` và `acceptingNewOrders`, trả mã cố định đã lọc. Kiểm tra gồm schema hiện tại, nguồn/chủ thể, danh mục địa chỉ và cấu hình kỹ thuật; HTTP200 không thay owner UAT.
- Khóa ghi mới dùng framework DPAPI CurrentUser trên Windows; framework phải mở được toàn bộ khóa và proof cũ trước khi ghi/luân chuyển. Mất ring khi có lịch sử thì giữ đóng, không tự sinh ring thay thế. Giữ nguyên keyroot/identity qua restart/restore; không export/copy private XML.
- Chuẩn bị template đóng, guard gói public, preflight và worksheet phục hồi offline. Actual publish/package/hosting chưa thực hiện. Không thêm migration hoặc menu tự động.
- Sửa nút Giảm hiệu ứng lên 16px; sửa guard hostname để host sai trả404 trước cổng đóng/mở, giữ hành vi503 của host đúng hoặc chưa cấu hình khi đóng. Không nới kỳ vọng test.

Vận hành tại `/admin/storefront/settings`; xem cần `storefront.settings.view`, bật/dừng cần `storefront.settings.manage`, active membership và CSRF. Người dùng tự tạo menu. Chi tiết: [hướng dẫn vận hành](STOREFRONT-WEB09-OPERATIONS.md), [mẫu UAT](storefront-web09/owner-uat.example.md). Các template cấu hình/acceptance/recovery cùng thư mục `storefront-web09`; hai script là `scripts/storefront/Test-StorefrontPreparation.ps1` và `New-StorefrontRecoveryPlan.ps1`.

## Phạm vi và phần giữ lại

| Plan | Phần kỹ thuật/chuẩn bị | Điều kiện thực tế vẫn giữ |
|---|---|---|
| W09T01 | Đủ 2.066 ca thực PASS và 378 assertion riêng; review độc lập đạt | Đã đạt phần kiểm thử bắt buộc của bước |
| W09T02 | Worksheet và kịch bản UAT riêng | Chủ cửa hàng chưa UAT/ký nhận |
| W09T03 | Bật/dừng, readiness, cấu hình đóng, preflight, guide | Domain/TLS/hosting/identity/ACL; nhận diện/liên hệ/phí/hàng/media thật chưa duyệt |
| W09T04 | Backup/restore thật giữa database GUID riêng, proof/đơn/tiền/kho và redelivery | Publish, mở bán, operator RPO/RTO và theo dõi vận hành thật chưa thực hiện |

Không coi fixture là hàng/model thật đã khớp hoặc nghiệm thu ngân hàng thật. Điện thoại vật lý, Linux nếu chọn, field INP và actual package chưa xác minh. Tiêu chí gốc WEB08/WEB09 được giữ nguyên; hoàn tất chuẩn bị kỹ thuật không đồng nghĩa đủ điều kiện phát hành. Positive Production/TLS chỉ chạy trên store/database/hostname/certificate kiểm thử riêng, không phải phát hành cửa hàng.

## Bản nguồn và quy trình độc lập

`storefront_code` viết production; `storefront_tests` viết test; `storefront_review` review độc lập; coordinator đóng băng/build và chốt hồ sơ. Oracle 24 nhóm khóa trước code, không phải số test.

| Bằng chứng | Bản hiện tại |
|---|---|
| Baseline | `.artifacts/w9base`: 5.401 input, HEAD `4495ffb7ba7cbd0a5d58507a1bf702398d0a8465`, 2.186 dirty rows có sẵn |
| Production | APP4, 29 path; manifest `WEB09/code/source-ready-v4-sha256.json`, SHA `E564117606DC34369458F6CD7C741684F9958012698839AB8DB47ECA433F777B` |
| Test | Test7, 18 path; manifest `WEB09/tests/source-ready-v7-sha256.json`, SHA `68C47B606A20FDCFECCF8F5E7E053D07628B4206E143E6D6CB0430EE5FDE6A25` |
| Snapshot | `.artifacts/w9s6`: 5.484 input, 47 path thuộc gói mới, 5.433 origin checks; chỉ hai tài liệu tracking loại khỏi live guard, không có loại trừ khác |
| Build | Offline 7 project, 49 đường dẫn output riêng, restore/build exit0/source drift0; một cảnh báo xUnit2031 có sẵn, không lỗi mới |
| READY | Revision1 `9F7BD826197F5A6A3121C124053A3366A4B5ADFFDF435970B4880CDC1608F77D`, 11.948 source/DLL/runtime/archive checks |
| Review READY | `45573B0DB65BB2E6671B04C7CC4C142F87B3EB169DF7B24289757077767917F8`; native/static/PDB/CallerFilePath/entrypoint đều thuộc S6 |

Không copy runtime/private key, node_modules, Git hoặc output cũ vào snapshot; không reset/commit/push công việc khác. Không chạy Publish targets. Public không chứa các entrypoint ERP/test/label-client bị cấm.

## Kết quả cuối trên S6

| Nhóm | Kết quả thực |
|---|---|
| WEB09 | 67/67 PASS, review độc lập đạt |
| WEB08 | 78/78 PASS, chín mẫu lab và review độc lập đạt |
| WEB01–07 | 1.266/1.266 PASS, ca hostname404/no-write và SQL upgrade goldens đã review đạt |
| Bank/POS/report/security | 498/498 PASS, review độc lập đạt |
| Kho/hóa đơn bị tác động | 157/157 PASS, review độc lập đạt |
| WEB00 pure, tách riêng | 378/378 assertion PASS, 377 nhãn ordinal; review độc lập đạt |

S6 không gọi ListTests riêng. Union trực tiếp năm TRX đã xác nhận 2.066 hàng/2.066 testId/2.066 execution riêng biệt, 0 trùng, 0 không đạt, 0 bỏ qua; exact ID/tên và TestDefinitions khớp identity set. Mỗi lượt cùng nguồn/DLL/READY hiện tại, guard11.948 trước/sau không lệch, master5 giữ nguyên, observer V3 khớp root và đóng. Smoke, discovery, lượt lỗi và 378 assertion riêng không cộng vào union. WEB00 kiểm tra10 input không drift,6 output globals và4 subprocess exit0.

WEB09 hiện tại đã kiểm chứng hai thứ tự tranh chấp SQL có witness owner/waiter, phục hồi COD với replay proof cũ, và BANK backup45.000 nhận/10.000 đã hoàn/35.000 còn nghĩa vụ → redelivery muộn1.000 thành46.000/10.000/36.000, không ghi trùng hoặc tự chuyển tiền. Hai bản sao SQL dùng COPY_ONLY/CHECKSUM, header/LSN và MOVE vào target GUID riêng; bảo toàn ring tại chỗ. Đây là diễn tập kiểm soát, không tuyên bố zero loss hoặc SLA vận hành thật.

Browser WEB09: bốn luồng PASS, 88 body GET200 thật có SHA đối chiếu nguồn riêng, 22 layout, 10 lượt chữ2x và ba native skip/focus; commit45.000COD → mất phản hồi thật → dừng intake → replay cùng body → guest hủy/giải phóng hold. Admin là controller/view thật trong harness, không phải chứng nhận toàn bộ AdminMain/auth/sidebar. Chữ2x là user text override, không phải native OS zoom/điện thoại thật.

WEB08: giữ đủ chín mẫu đầu, maxLCP2.308s, interaction proxy112ms, maxCLS0.00017126749; ngưỡng/profile không đổi. `Server-Timing` của cả chín navigation đều rỗng; không đo field INP. Observer hiện tại ghi nhận root chính xác trước nhận child; không suy nguyên nhân hoặc chain hồi tố cho lượt V2 lỗi trước đó.

## Lịch sử lỗi và sửa

| Bản/lượt | Kết quả và xử lý |
|---|---|
| Source APP1/launcherV1 | Giữ HOLD và sửa schema trước bootstrap, tên entrypoint package bị cấm, address readiness và binding READY/runner; chưa tính runtime |
| APP2/Test2/S1 | Compile đạt nhưng test source HOLD trước runtime; không outcome |
| APP2/Test3/S2 | Compile CS0535 do test wrapper thiếu callback interface; Test4 bổ sung forward thật |
| APP2/Test4/S3 full | 62P/5F: ba setup vi phạm SQL CHECK, một quote kỳ vọng422 thay404 khi dừng, một nút thật14px. Sửa fixture hợp lệ, kỳ vọng404 chính xác và CSS16px; không nới SQL/font |
| APP3/Test5/S4 full | 66P/1F: static304 hợp lệ không có body trong công cụ strict200. Test6 tắt cache trong hai CDP session trước navigation, vẫn đòi GET200/body thật/SHA/TLS/privacy |
| APP3/Test6/S5 | FullWEB09 67P, WEB08 78P chỉ là lịch sử sau APP4. Prior1.265P/1F phát hiện hồi quy hostname503vs404; APP4 đổi đúng một guard, giữ nguyên test404/no-write |
| ObserverV2 trên S5 Prior | Không khớp root tại mẫu đầu, chưa lưu candidate; root còn sống được xác nhận ở điểm sau nhưng không tạo chain hồi tố. Lượt không tính primary. V3 thêm startup retry/log hữu hạn10s/200ms, vẫn exact PID/start/exe, không nhận child trước match; 2s sau match |

Mọi raw lỗi vẫn giữ. S4 ListTests từng ghi2.066 hàng selection nhưng observer không ghi nhận root; chỉ chấp nhận selection có khoảng thiếu, không test/observerPASS. Phương án sửa READY S5 revision2 đã hủy trước thực thi; helper/manifest giữ làm audit chưa dùng. S6 là revision1 của build mới; số11.948 trùng phương án cũ không làm hai bản tương đương. Không giữ outcome S5 làm hiện tại.

## Tài nguyên và hồ sơ cuối

SQL chỉ dùng GUID mới. Năm database có sẵn được bảo vệ bằng inventory trước/sau, không đọc nội dung hoặc dọn database không xác định. Observer chỉ metadata năm trường, không command line/environment/secret hoặc process actions. Fixture host kết thúc chủ động có thể có exit-1; không đổi thành exit0 và không tuyên bố toàn máy không còn process.

XML/PFX/profile/backup mới được giữ có metadata/hash/size/provenance; trường thiếu và khoảng quan sát được ghi rõ. Hashing đọc byte nhưng công cụ không parse/hiển thị/copy private XML; framework dùng khóa bình thường là luồng ứng dụng. Không mở rộng consent xóa1.510 XML WEB07. Các lượt S6 đã đóng. Inventory metadata/hash ghi1.050 XML/20 PFX/2 backup ở S6; cộng dồn S3–S6 là2.141 XML/83 PFX/8 backup, được giữ nguyên. Không suy file creator từ path hoặc từ trường metadata bị null. Lỗi nối wrapper identity của báo cáo resource được giữ và đính chính từ metadata đã lưu; dữ liệu watcher gốc không thiếu vì lỗi projection đó.

39 metadata/tài liệu/state được seal WEB08 đã lưu byte chính xác trước khi sửa hồ sơ tổng hợp: archive manifest `1AF74C572E3F28D43F4692ACAA1A2E57613E79A398B0663B04089D5AC6B54954`. Gate/report lịch sử giữ nguyên; không biến outcome cũ thành kết quả hiện tại.




Kiểm tra liên tục cuối sau cả năm nhóm và WEB00:11.948/11.948 hash khớp READY, drift0, master5 đúng nguyên trạng; review độc lập `9EBB44F0005C0ACFE1926638D780CEA6C477D8C8F0D43D8B90BC2000CF80C076`. Không thực thi helper/READY S5 revision2 đã hủy. Metadata summary Prior v1 lỗi biến PowerShell tự động được giữ nguyên, summary-v2/erratum đối chiếu raw và dùng làm bản tổng hợp chính; raw1266PASS và review634210 không đổi.

Hồ sơ chốt: [gate chuẩn bị trước phát hành](../../.artifacts/storefront-rollout/WEB09/coordinator/final-gate-prepublication-v1.json). OriginalGoalComplete, FullReleaseAcceptanceComplete và ReleaseReady vẫn false. W09T02/03/04 gốc còn điều kiện thực tế; không đánh dấu chủ cửa hàng đã UAT, hàng/media thật đã duyệt hoặc website đã mở.



Phân tầng inventory S6:1.045 XML có provider cấu hình + identity quan sát + exact keyroot-before;2 chỉ có provider cấu hình không có external observation lưu;3 chỉ có ancestor context ban đầu vắng. S5 lịch sử giữ1.011 XML thiếu external observation và3 ancestor context, không được retrofit chain;36 có mức bằng chứng mạnh hơn.83 PFX cộng dồn đều khớp hash/size fixture mới;4 native profile giữ lại (2 ở S6), không current profile nào thiếu bằng chứng đóng.988 reference quan sát gốc không thiếu field sau sửa wrapper;1.256 source-identity ROWS (không phải số process distinct) vẫn giữ199 executable-null nguồn. Không còn triple PID/start/exe hoặc PID/start đã biết khớp trong fresh query; đây không phải khẳng định toàn máy sạch hoặc quyền xóa2.141 XML.48 tuple CNG/file known-exact đều không còn hiện tại, không suy thành toàn bộ CNG không có residue.



Inventory đã được review độc lập chấp nhận theo mức bằng chứng giữ lại: `73B29D2FDD87D6A829DC54E1D3AF6CA0EC0C8F98163392536877E0B89EFFF9EB`. Các lỗi tổng hợp metadata được giữ/đính chính, không thay raw TRX/guard hoặc nâng null thành identity hoàn chỉnh. Kết luận kỹ thuật là **PASS_PREPARATION_NO_PUBLISH**; không có test, source edit hoặc tác vụ deployment nào còn chạy. Gate cuối không cấp phép publish hoặc mở bán.

