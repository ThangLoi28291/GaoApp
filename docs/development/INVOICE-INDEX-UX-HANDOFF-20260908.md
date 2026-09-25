# Invoice index UX handoff — 2026-09-08

Human requested the sales invoice list adopt the criteria used in earlier UI work. This is a bounded continuation using the existing GDS and Manual BMAD discovery → implementation → verification workflow. It does not resume the paused report task or claim independent review.

## Scope and changes

- `GaoApp.Web/Areas/Admin/Views/Invoice/Index.cshtml`: fluid GDS layout, Vietnamese filter labels, preserved display/sort enum values, page-size control, compact grouped table, labeled mobile cards, clear issuance/lock states, readable zero rows, quantity formatting without redundant zeros, contextual empty state, bounded pagination retaining all query parameters.
- `GaoApp.Web/wwwroot/Admin/css/invoice-index.css`: page-scoped styles, sticky desktop table heading, responsive cards, visible keyboard focus, reduced-motion support.
- `GaoApp.Tests/Ui/GaoAppDesignSystemSourceContractTests.cs`: added only `Invoice/Index.cshtml` to the existing opt-in rollout allowlist. The file already existed as an untracked worktree change; its other contents were preserved.
- This handoff.

Controller, queries, permissions, issuance APIs, data and schema are unchanged. HKD and buyer remain separate. Issuance and lock presentation uses the same existing DTO fields; no new status inference or page-only totals are presented as global statistics.

## Validation actually performed

- `dotnet build GaoApp.Web/GaoApp.Web.csproj --no-restore --verbosity quiet`: passed, 0 warnings, 0 errors.
- `dotnet test GaoApp.Tests/GaoApp.Tests.csproj --no-restore --filter 'FullyQualifiedName~GaoApp.Tests.Invoices|FullyQualifiedName~GaoAppDesignSystemSourceContractTests' --verbosity quiet`: 40 passed, 0 failed, 0 skipped. Existing unrelated analyzer warnings remain in the test project.
- Scoped `git diff --check`: passed.
- Browser verification used the actual compiled Razor view, shared layout and CSS in a temporary loopback-only fixture host, with synthetic invoice rows and a stub menu; it did not access the application database or Viettel.
- Visually inspected 1440px desktop, 800px tablet, 390px and 320px mobile. DOM checks confirmed no document horizontal overflow; after refinement, status badges fit within 320px cards.
- Submitted search/date/display/sort controls and verified selected values persisted. Navigated to page 100 with filter parameters intact; pagination remained bounded. Reset cleared keyword/display/sort. Detail link requested the expected invoice ID and returned HTTP 200 in the fixture host.
- Verified filtered empty state, fractional quantity, zero rows, issued/locked states, large amounts, long names and HTML-looking buyer text (rendered as text; no injected script elements).

Full authenticated UI flow against real data, full regression and independent review are not claimed. Temporary preview host/files are removed after verification. Restart the application to load the compiled Razor changes.

## Rollback

Revert only this task's invoice index view changes, remove its scoped CSS, and remove its single rollout allowlist entry. Preserve earlier parser/exchange-rate fixes and unrelated worktree changes. No commit or deployment was performed.
