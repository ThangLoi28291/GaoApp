# GaoApp Long-Lived Development Workflow

## 1. Purpose

Tài liệu này quy định cách khảo sát, triển khai, review và tích hợp các thay đổi GaoApp có vòng đời dài hoặc rủi ro cao. Quy trình ưu tiên khả năng truy vết: mỗi kết luận phải có source evidence, mỗi thay đổi phải nằm trong allowed scope, và mỗi trạng thái chỉ được chuyển khi có đủ bằng chứng.

## 2. Roles

### Coordinator

- Khóa Task Contract, gồm mục tiêu, nghiệp vụ, task level, base branch, base commit, expected parent, mandatory read files, allowed/locked files và test plan.
- Giải quyết câu hỏi làm thay đổi phạm vi hoặc quyết định nghiệp vụ.
- Cho phép commit bằng câu lệnh rõ ràng `COMMIT ALLOWED`.
- Khóa dependency, thứ tự integration, integration branch và các điều kiện `TASK PASS`.
- Chỉ Coordinator được kết luận trạng thái cuối `TASK PASS`, và chỉ sau khi có `INDEPENDENT REVIEW PASS` cùng required GitHub CI đạt trên chính một immutable reviewed commit SHA, không có commit mới sau review/CI.
- Trong vai Coordinator, không trực tiếp sửa implementation, không commit thay Implementation Agent và không merge.
- Xác nhận `TASK PASS` không đồng nghĩa Coordinator thực hiện merge; Coordinator chỉ xác nhận task đủ điều kiện cho người có thẩm quyền tích hợp.
- Không tự coi Implementation Handoff là Independent Review.

### Implementation Agent

- Chạy pre-flight, giữ đúng branch/base commit/expected parent và chỉ sửa allowed files.
- Đọc source/call sites/tests cần thiết trước khi sửa.
- Thực hiện thay đổi nhỏ nhất đáp ứng contract.
- Chạy validation được contract cho phép, cung cấp evidence và rollback.
- Chỉ báo implementation status là `READY FOR COORDINATOR REVIEW` hoặc `BLOCKED`; không được tuyên bố `INDEPENDENT REVIEW PASS` hoặc `TASK PASS`.
- Không commit trước `COMMIT ALLOWED`.
- Không merge trong mọi trường hợp. Chỉ push hoặc tạo/cập nhật PR khi được giao rõ; quyền push/PR không bao gồm quyền merge.

### Independent Reviewer

- Review đúng một immutable reviewed commit SHA; ghi base branch, base commit, expected parent, reviewed SHA, parent của reviewed SHA và diff `expected-parent..reviewed-sha`.
- Xác minh reviewed commit là child đúng của expected parent, trừ khi Task Contract khóa một ancestry khác.
- Tự đọc diff, source và call sites liên quan; không dựa riêng vào Implementation Handoff.
- Kiểm tra correctness, security, tenant/data isolation, concurrency, migrations và regression tests theo level.
- Phân biệt defect do task tạo với finding đã tồn tại hoặc ngoài scope.
- Chỉ cung cấp verdict, finding và evidence; không thực hiện implementation hoặc remediation.
- Không sửa file, tạo remediation patch, commit, push hoặc merge trong review; `Reviewer-created changes` luôn là `None`.
- Chỉ kết luận `INDEPENDENT REVIEW PASS` hoặc `INDEPENDENT REVIEW FAIL`; verdict chỉ áp dụng cho `Reviewed commit SHA`.
- Không được kết luận task tổng thể là `TASK PASS` và không được bỏ qua required GitHub CI.
- Commit mới hoặc thay đổi reviewed SHA làm review cũ mất hiệu lực.

## 3. Task levels

### Level A — Discovery/documentation

Khảo sát read-only hoặc thay đổi tài liệu; không làm thay đổi runtime, schema hoặc dữ liệu.

Yêu cầu tối thiểu:

- exact branch/HEAD/status baseline;
- danh sách source đã đọc và search targets;
- không build/test nếu contract cấm;
- nếu tạo tài liệu, chỉ sửa đúng allowed documentation files;
- xác minh không có source/migration/database changes.

### Level B — Bounded workflow/module change

Thay đổi giới hạn trong một workflow/module, không thay đổi nền tảng dữ liệu, có thể cô lập bằng tests.

Yêu cầu tối thiểu:

- contract rõ input/output/state/permission;
- unit/integration tests phù hợp;
- không mở rộng schema hoặc posting foundation ngoài scope;
- conflict matrix cho shared service/controller/DTO/test fixture.

### Level C — High-risk data/foundation change

Bất kỳ task nào có một trong các đặc điểm sau là Level C:

- schema hoặc migration;
- inventory/costing;
- tenant/security;
- concurrency/idempotency;
- transaction boundary;
- workflow nhiều module;
- ảnh hưởng dữ liệu lịch sử.

Yêu cầu bổ sung:

- correctness/security/data review bắt buộc;
- kế hoạch migration/rollback và dữ liệu hiện hữu;
- durable database constraints cho invariant quan trọng;
- test đồng thời/idempotency/tenant boundary;
- independent review trước CI acceptance.

### Store/LegalEntity checklist cho Level B/C

Task chạm purchase, receipt, inventory, warehouse hoặc supplier invoice/XML phải khóa và kiểm tra:

- `StoreId` là tenant/operational boundary và mọi query/write/link đều cùng Store;
- legal-owner identity là `LegalEntityId` trực tiếp hoặc được suy ra rõ từ required `WarehouseId`;
- warehouse thuộc legal entity đã chọn và cùng Store; đổi legal entity làm warehouse cũ invalid;
- PO, receipt và supplier invoice liên quan phải cùng legal entity theo invariant đã khóa;
- PO supplier phải cùng Store; multi-PO receipt tương lai chỉ gom cùng Store/LegalEntity/Supplier;
- seller tax code của XML resolve `Supplier`, buyer tax code resolve `LegalEntity`; không dùng lẫn hai identity;
- invoice/receipt legal-entity mismatch là hard blocker, không phải warning/reason override;
- mọi thay đổi legal entity/warehouse trước Confirm và mọi override hợp lệ phải có audit evidence.

## 4. Lifecycle

Mọi task đi theo chuỗi:

`DRAFT → CLARIFYING → READY → IMPLEMENTING → HANDOFF REVIEW → COMMIT ALLOWED → INDEPENDENT REVIEW → CI VERIFIED → PASS`

### DRAFT

Mục tiêu đang được hình thành. Chưa triển khai.

### CLARIFYING

Coordinator và người thực hiện đang khóa nghiệp vụ, scope, dependency, expected parent, allowed files và validation.

### READY

Task Contract đầy đủ, không còn ambiguity làm thay đổi kết quả, branch/base khả dụng.

#### Task Contract bất biến tại READY

Khi Task Contract đạt `READY`, toàn bộ contract trở thành bất biến. Implementation Agent và Independent Reviewer không được tự thay đổi, diễn giải mở rộng hoặc bỏ qua bất kỳ trường nào. Nội dung bất biến gồm tối thiểu:

- Task ID;
- level;
- mục tiêu;
- nghiệp vụ khóa;
- ngoài phạm vi;
- branch;
- base branch;
- base commit;
- expected parent;
- mandatory read files;
- search targets/call sites;
- allowed modified files;
- locked files;
- concurrent conflicts;
- risks;
- implementation plan;
- test plan;
- evidence;
- commit message;
- rollback;
- PASS criteria;
- handoff format.

Nếu cần thay đổi bất kỳ trường nào sau `READY`:

1. Implementation Agent phải dừng.
2. Coordinator đưa task về `CLARIFYING`.
3. Coordinator ban hành một Task Contract revision rõ ràng.
4. Chỉ sau khi revision được đánh dấu `READY` thì Implementation Agent hoặc Independent Reviewer mới được tiếp tục.

Implementation Agent không được tự thêm file vào allowlist hoặc tự đổi test plan.

### IMPLEMENTING

Agent thực hiện đúng task. Nếu gặp thay đổi ngoài scope, conflict hoặc expected-parent mismatch thì dừng và báo.

### HANDOFF REVIEW

Implementation Handoff đã có; Coordinator rà scope, evidence, tests và risks. Chưa được commit nếu chưa có `COMMIT ALLOWED`.

### COMMIT ALLOWED

Coordinator cho phép commit rõ ràng. Commit phải chứa đúng allowed diff và dùng message đã khóa/đề xuất được duyệt.

### INDEPENDENT REVIEW

Reviewer độc lập khóa reviewed commit SHA, xác minh parent/diff từ expected parent, rồi tự đọc diff/source/call sites. Review không tạo thay đổi. Kết quả chỉ là `INDEPENDENT REVIEW PASS` hoặc `INDEPENDENT REVIEW FAIL` cho commit đó; đây là review verdict của một commit, chưa phải `TASK PASS`. Không che finding bằng sửa ngoài scope. Bất kỳ commit mới hoặc thay đổi reviewed SHA nào đều yêu cầu review mới.

Khi `INDEPENDENT REVIEW FAIL`:

1. Independent Reviewer trả báo cáo FAIL kèm finding và evidence.
2. Coordinator xác minh từng finding.
3. Finding không hợp lệ được Coordinator bác bỏ kèm lý do.
4. Finding hợp lệ được Coordinator chuyển bằng remediation prompt về đúng Implementation chat của task.
5. Không chuyển remediation sang Reviewer chat.
6. Implementation Agent chỉ sửa file được Coordinator cho phép trong remediation contract.
7. Implementation Agent tạo commit mới sau `COMMIT ALLOWED`; không amend commit đã review.
8. Commit mới phải được Independent Reviewer review lại.
9. Required GitHub CI phải chạy lại trên đúng SHA mới.
10. Review và CI của SHA cũ không được dùng để kết luận SHA mới.

Nếu finding chỉ thuộc tài liệu thì remediation chỉ được sửa tài liệu; không mở rộng sang source code.

### CI VERIFIED

Required GitHub CI/checks đã đạt trên đúng `Reviewed commit SHA`. Local build/test không thay thế CI khi contract yêu cầu CI. `CI VERIFIED` chưa tự động đồng nghĩa `TASK PASS`.

### PASS

Tên đầy đủ của trạng thái này là `TASK PASS`. Chỉ Coordinator được đặt trạng thái này khi đồng thời thỏa:

1. Independent Reviewer đã kết luận `INDEPENDENT REVIEW PASS`.
2. Required GitHub CI đã đạt.
3. Review và CI áp dụng cho cùng một immutable commit SHA.
4. Không có commit mới sau review hoặc CI.

Implementation Agent và Independent Reviewer không được tuyên bố `TASK PASS`. Việc Coordinator kết luận `TASK PASS` không phải là hành động merge.

### Thuật ngữ khóa

- `INDEPENDENT REVIEW PASS`: Reviewer xác nhận `Reviewed commit SHA` đạt yêu cầu review; đây không phải trạng thái cuối của task.
- `CI VERIFIED`: required CI đạt trên cùng `Reviewed commit SHA`; trạng thái này không thay thế review verdict.
- `TASK PASS`: trạng thái cuối chỉ do Coordinator kết luận sau khi hai điều kiện trên cùng đạt trên một immutable SHA và không có commit mới.

## 5. Branch, base commit, expected parent and Git rules

- Task Contract phải ghi source, base branch, base commit, expected parent SHA đầy đủ và working branch.
- Base commit là SHA nền gốc mà task hoặc release được tạo từ đó. Base commit phải là SHA đầy đủ, không dùng tên branch thay thế và không tự thay đổi trong quá trình task hoặc remediation.
- Expected parent là commit phải trở thành parent trực tiếp của commit Implementation tiếp theo.
- Với implementation commit đầu tiên, expected parent thường bằng base commit.
- Với remediation commit, expected parent có thể là commit đã review FAIL gần nhất; base commit vẫn giữ nguyên để truy vết nền ban đầu.
- Expected parent là bất biến trong một Task Contract revision đã `READY`. Nếu HEAD khởi đầu khác expected parent, Implementation Agent phải dừng và báo Coordinator trước khi sửa.
- Không tự pull/fetch/reset/rebase/amend/merge để “sửa” hoặc ép lịch sử khớp expected parent.
- Không đổi branch ngoài thao tác được contract cho phép.
- Pre-flight tối thiểu:

```powershell
git branch --show-current
git rev-parse HEAD
git log -1 --oneline
git status --short
git status --branch --short
```

- Working tree bẩn: liệt kê staged/unstaged/untracked; không restore/reset/stash thay đổi không rõ chủ sở hữu.
- Commit chỉ sau `COMMIT ALLOWED`.
- Implementation Agent, Independent Reviewer và Coordinator không merge. Merge chỉ do người có thẩm quyền ngoài ba vai trò này thực hiện sau khi Coordinator tuyên bố readiness.
- Push/PR chỉ thực hiện nếu user/Coordinator giao rõ; quyền push/PR không bao gồm merge.
- Review phải dùng commit SHA bất biến và kiểm tra `git rev-parse <reviewed-sha>^` bằng expected parent, trừ ancestry khác đã được khóa trong contract.

Ví dụ lịch sử:

```text
Task đầu:
Base commit: A
Expected parent: A
Implementation commit: B, parent của B là A

Remediation:
Base commit: A
Expected parent: B
Fix commit: C, parent của C là B
```

## 6. Allowed files and locked files

- `Allowed modified files` là allowlist tuyệt đối.
- File không nằm trong allowlist là locked, kể cả thay đổi “nhỏ”, generated file hoặc formatting.
- Không dùng formatter/code cleanup/generator có thể chạm file ngoài phạm vi.
- Nếu cần sửa file ngoài allowlist để hoàn thành đúng nghiệp vụ:
  1. dừng;
  2. nêu file, lý do và tác động;
  3. yêu cầu Coordinator đưa task về `CLARIFYING` và ban hành Task Contract revision;
  4. chỉ tiếp tục sau khi revision được đánh dấu `READY`.
- File phát sinh bất ngờ:
  1. không xóa/restore;
  2. xác định tracked/untracked, timestamp nếu hữu ích và diff;
  3. báo Coordinator;
  4. không nhận là kết quả task nếu không do task tạo.

Scope validation chuẩn:

```powershell
$allowed = @(
  # exact relative paths from Task Contract
)
$changed = @(
  git diff --name-only
  git diff --cached --name-only
) | Sort-Object -Unique
$unexpected = $changed | Where-Object { $_ -notin $allowed }
```

Đối với file mới chưa tracked, cần kiểm tra thêm `git status --short` hoặc dùng intent-to-add có chủ ý để `git diff` nhìn thấy file; không được để untracked file lọt khỏi scope validation.

## 7. Read-only discovery and source evidence

- Dùng `rg`, `rg --files`, `Get-Content`, `git log`, `git blame`, `git diff` hoặc công cụ read-only tương đương.
- Implementation Agent được search read-only toàn repository nhưng không phải đọc toàn bộ repository.
- Không đọc tuần tự toàn repository. Bắt đầu từ entity/interface/controller/service rồi theo call chain và call sites.
- Không kết luận tính năng tồn tại chỉ từ tên file.
- Evidence hợp lệ cần chỉ rõ file và symbol/method; với behavior quan trọng phải đọc caller và callee.
- Migration/model snapshot chỉ chứng minh schema; không chứng minh workflow đang được gọi.
- Test source chỉ chứng minh contract test tồn tại; không nói test đã chạy nếu chưa chạy.

### Mandatory read files

- `MANDATORY READ FILES` là danh sách source hoặc tài liệu Coordinator bắt buộc Implementation Agent đọc trước khi sửa.
- Danh sách này không yêu cầu đọc toàn bộ repository; search targets/call sites có thể dẫn tới các file bổ sung cần đọc read-only.
- Chỉ `ALLOWED MODIFIED FILES` được sửa; quyền đọc hoặc search không cấp quyền thay đổi file.
- Nếu mandatory file không tồn tại, sai đường dẫn hoặc không đủ để triển khai đúng contract, Implementation Agent phải dừng và báo Coordinator.
- Implementation Handoff phải liệt kê các file thực sự đã đọc.
- Không được tuyên bố đã đọc một file nếu chưa mở nội dung file đó.

## 8. Clarifications and business decisions

Phải dừng ở `CLARIFYING` khi câu trả lời có thể thay đổi:

- state model hoặc permission;
- schema/migration/data backfill;
- cost/quantity/accounting result;
- tenant boundary;
- idempotency/transaction behavior;
- allowed files/dependency;
- rollback khả thi.

Không cần dừng cho chi tiết có thể xác minh chắc chắn trong source và không mở rộng scope.

Mọi quyết định dài hạn phải được thêm vào `DECISION-LOG.md`; không chỉ nằm trong chat hoặc commit message.

## 9. Pre-existing and out-of-scope findings

- Không tự sửa finding ngoài task.
- Ghi vào `BACKLOG-FINDINGS.md` với evidence, impact, classification, target task và scope decision.
- Dùng Git history/blame khi cần chứng minh behavior đã tồn tại trước expected parent.
- Finding không được gọi là regression do task nếu diff task không tạo ra nó.
- Ngoại lệ: nếu finding là correctness/security/data blocker trực tiếp làm thay đổi mới không an toàn, task phải dừng hoặc Coordinator phải tách blocker làm dependency trước.

Classification chuẩn:

- `Existing and correct`
- `Existing but incomplete`
- `Missing`
- `Pre-existing defect`
- `Needs business decision`
- `Closed — Not a defect`

## 10. Correctness, security and data exceptions

Dù file nằm ngoài allowlist, reviewer phải nêu blocker khi diff mới:

- cho phép cross-tenant read/write;
- có thể ghi duplicate inventory/cost/accounting;
- làm mất/ghi đè dữ liệu;
- phá transaction atomicity;
- tạo migration destructive không có kế hoạch;
- làm Confirm không idempotent hoặc không concurrency-safe.
- làm suy yếu một trong hai boundary: Store tenant/operational hoặc LegalEntity legal owner;
- cho phép warehouse khác legal entity, hoặc PO/receipt/invoice link khác legal entity;
- dùng seller tax code để resolve legal entity hoặc buyer tax code để resolve supplier.

Reviewer không tự sửa blocker. Coordinator quyết định mở rộng contract hoặc tạo dependency task.

## 11. Build, test and evidence

Task Contract phải nói rõ:

- build command;
- test command/filter/project;
- database/provider;
- có/không migration validation;
- có/không runtime/manual check;
- required CI checks.

Evidence handoff gồm:

- exact commands;
- exit code/result;
- test count nếu runner cung cấp;
- skipped/not run và lý do;
- warning/error liên quan;
- branch/HEAD/status/diff cuối.

Không dùng “works”, “tested” hoặc “all green” nếu không có command/evidence tương ứng.

Level A documentation task có thể ghi rõ `NOT RUN — documentation-only`; đây không phải `TASK PASS`.

## 12. Migration governance

Mọi migration là Level C.

Trước khi tạo migration:

- Task Contract phải cho phép migration và snapshot;
- xác định database provider và baseline;
- khảo sát dữ liệu hiện hữu, unique violations và nullable/backfill;
- chốt downgrade/rollback hoặc forward-fix strategy;
- tách migration khỏi refactor không liên quan.

Validation tối thiểu theo contract:

- inspect generated migration và snapshot;
- verify indexes/FKs/nullability/defaults/precision;
- test upgrade trên database phù hợp;
- test application behavior và tenant isolation;
- không tự chạy migration lên database ngoài scope.

Không dùng `dotnet ef migrations add`, database update hoặc scaffolding trong task không cho phép.

## 13. GitHub CI and integration

- CI phải chạy trên đúng commit đã independent-reviewed.
- Nếu commit/reviewed SHA thay đổi sau review, review và CI evidence cũ không còn đủ; phải review lại commit mới và chạy required CI trên chính SHA đó.
- Required checks phải được ghi trong Task Contract.
- CI fail do source/task: quay lại implementation.
- CI fail do infrastructure: ghi evidence và Coordinator quyết định rerun/escalation; không tuyên bố `TASK PASS`.
- CI xanh không thay thế Independent Review; `INDEPENDENT REVIEW PASS` không thay thế required GitHub CI.
- Khi `INDEPENDENT REVIEW PASS` và required GitHub CI cùng đạt trên đúng `Reviewed commit SHA`, không có commit mới, Coordinator mới được kết luận `TASK PASS`.
- Coordinator chỉ khóa thứ tự/điều kiện và tuyên bố readiness; Implementation Agent, Independent Reviewer và Coordinator không merge. Người có thẩm quyền tích hợp thực hiện merge bên ngoài ba vai trò này.

## 14. Concurrent task conflict control

Coordinator duy trì file conflict matrix. Các file sau mặc định có độ xung đột cao:

- `AppDbContext` và entity configurations;
- DI registrations;
- shared entities/enums/DTOs;
- purchase/stock controllers và services;
- shared Razor views/JavaScript;
- test fixtures;
- migration model snapshot.

Hai task không nên đồng thời sửa cùng high-conflict file. Nếu bắt buộc:

- khóa thứ tự;
- task sau rebases/starts từ commit tích hợp đã duyệt;
- expected parent được cập nhật trong Task Contract mới, không tự thay đổi giữa task.

## 15. Templates

### 15.1 Task Contract

```text
TASK ID:
TASK:
ROLE:
LEVEL:
STATUS:
SOURCE:

MỤC TIÊU:
OBJECTIVE:
IN SCOPE:
NGHIỆP VỤ KHÓA:
OUT OF SCOPE:
NGOÀI PHẠM VI:

BRANCH:
BASE BRANCH:
BASE COMMIT:
EXPECTED PARENT:
WORKING BRANCH:
BRANCH LOCK:

MANDATORY READ FILES:
SEARCH TARGETS/CALL SITES:
ALLOWED MODIFIED FILES:
LOCKED FILES:
DEPENDENCIES:
CONCURRENT CONFLICTS:
RISKS:

BUSINESS RULES:
IMPLEMENTATION PLAN:
IMPLEMENTATION NOTES:

PRE-FLIGHT:
TEST PLAN:
EVIDENCE:
EVIDENCE REQUIRED:

COMMIT MESSAGE:
ROLLBACK:
PASS CRITERIA:
HANDOFF FORMAT:
```

### 15.2 Implementation Handoff

```text
<TASK ID> IMPLEMENTATION HANDOFF

1. Status
   READY FOR COORDINATOR REVIEW | BLOCKED
   Implementation Agent chỉ dùng một trong hai status trên.
   Không dùng INDEPENDENT REVIEW PASS hoặc TASK PASS trong Implementation Handoff.

2. Contract
   Level, objective, base commit, expected parent, mandatory read files, allowed files

3. Git Baseline
   Start/end branch, HEAD, status, task-created changes

4. Source Verification
   Files/methods/call sites read

5. Files Changed
   Exact path and purpose

6. Implementation Summary
   Behavior and important design choices

7. Validation
   Commands, results, not-run items

8. Scope Validation
   Allowed count, unexpected count

9. Risks and Unknowns

10. Commit
    Created? SHA? Proposed message? Waiting for COMMIT ALLOWED?

11. Rollback

12. Final Declaration
    No unauthorized files, migration, DB, branch, push or merge
```

### 15.3 Independent Review

```text
<TASK ID> INDEPENDENT REVIEW

1. Verdict
   INDEPENDENT REVIEW PASS | INDEPENDENT REVIEW FAIL
   This verdict applies only to Reviewed commit SHA.
   Overall task status: NOT DETERMINED BY REVIEWER.
   Final TASK PASS authority: Coordinator after required CI on the same SHA.

2. Reviewed Baseline
   Base branch:
   Base commit:
   Expected parent:
   Reviewed commit SHA:
   Reviewed commit parent:
   Commit parent verified:
   Reviewed diff range:
   Commit unchanged during review:
   Reviewer-created changes: None
   Push performed: No
   Merge performed: No

3. Scope
   Allowed/unexpected files

4. Findings
   Severity, file/method, evidence, impact, required action

5. Correctness/Security/Data
   Store tenant boundary, LegalEntity owner boundary, Warehouse/LegalEntity consistency,
   PO/receipt/invoice same-LegalEntity invariant, seller-vs-buyer tax-code resolution,
   authorization, audit, transaction, concurrency, idempotency, migration

6. Tests and CI
   Commands/evidence reviewed; missing coverage

7. Pre-existing/Out-of-scope
   Backlog references and Git history

8. Final Conditions
   Required actions after INDEPENDENT REVIEW FAIL, or immutable-SHA conditions preserved after INDEPENDENT REVIEW PASS
```

### 15.4 Rollback plan

```text
ROLLBACK

- Scope to revert:
- Data/schema impact:
- Safe revert command or forward-fix:
- Migration downgrade constraints:
- Feature flag/operational fallback:
- Verification after rollback:
- Owner/approval required:
```

## 16. Final checklist

Trước Implementation Handoff:

- branch và HEAD đúng;
- changed files nằm trong allowlist;
- không có unexpected generated/untracked files;
- diff đã đọc;
- tests/build/migration checks đúng contract;
- decisions/findings dài hạn đã cập nhật nếu task cho phép;
- không commit trước `COMMIT ALLOWED`;
- Implementation Agent không tuyên bố `INDEPENDENT REVIEW PASS` hoặc `TASK PASS`;
- không push nếu chưa được giao rõ;
- không merge trong mọi trường hợp.
