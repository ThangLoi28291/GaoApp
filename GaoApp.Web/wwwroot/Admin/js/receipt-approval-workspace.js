(function (root, factory) {
    'use strict';
    const model = factory();
    if (typeof module === 'object' && module.exports) module.exports = model;
    if (!root?.document) return;
    const document = root.document;
    const byId = id => document.getElementById(id);
    const checked = id => byId(id)?.checked === true;
    const number = input => input && input.value.trim() !== '' ? Number(input.value) : NaN;
    const money = value => new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(value) + ' đ';
    const rows = () => [...document.querySelectorAll('#commercialApprovalWorkbench .commercial-line')];
    let workbench, state, activeFilter = 'all', baseline, lastIssueKey, bound = false, renderSignature;

    function collect() {
        const hasVat = checked('commercialHasVat');
        const lineModels = rows().map(row => {
            const price = row.querySelector('.commercial-unit-price');
            const quantity = row.querySelector('.commercial-quantity');
            const tax = row.querySelector('.commercial-tax');
            const oldPrice = row.querySelector('.commercial-last-price')?.dataset.value;
            return {
                id: row.dataset.lineId, lineNo: row.dataset.lineNo || row.dataset.lineNumber,
                name: row.dataset.productName || `Dòng ${row.dataset.lineNo}`,
                price: number(price), priceText: price?.value || '', quantity: quantity ? number(quantity) : Number(row.dataset.quantity),
                factor: Number(row.dataset.factor), savingQuantity: quantity?.dataset.saving === 'true',
                exactPrice: Number(row.dataset.exactUnitPrice), exactAmount: Number(row.dataset.exactBeforeVat),
                taxId: Number(tax?.value), taxRate: Number(tax?.selectedOptions[0]?.dataset.rate || 0),
                allocation: number(row.querySelector('.commercial-freight-allocation')),
                previousPrice: oldPrice === '' || oldPrice == null ? null : Number(oldPrice),
                catalog: row.dataset.catalogReview === 'true', loss: row.classList.contains('sd-price-loss'),
                attention: row.classList.contains('sd-price-attention')
            };
        });
        return {
            readOnly: workbench.dataset.readonly === 'true', lines: lineModels, hasVat,
            supplierId: Number(byId('commercialSupplierId')?.value),
            supplierState: root.GaoAppPurchaseReceiptApproval?.getState?.() || {},
            draftState: root.GaoReceiptPriceDrafts?.getState?.() || {},
            pendingOutside: workbench.dataset.hasPendingOutside === 'true',
            unresolved: Number(workbench.dataset.unresolvedCount || (workbench.dataset.hasUnresolvedProvisional === 'true' ? 1 : 0)),
            hasCatalog: workbench.dataset.hasCatalogReview === 'true',
            hasRevisionRequest: workbench.dataset.hasRevisionRequest === 'true',
            overdelivery: !!byId('acceptOverdelivery'),
            hasFreight: checked('commercialHasFreight'), freight: number(byId('commercialFreightTotal')),
            freightPayee: byId('commercialFreightPayee')?.value.trim(),
            capitalizeFreight: checked('capitalizeFreightInInventoryCost'), includeVat: checked('includeVatInInventoryCost'),
            merchandisePaid: workbench.dataset.readonly === 'true' && workbench.dataset.merchandisePaid === 'true',
            freightPaid: workbench.dataset.readonly === 'true' && workbench.dataset.freightPaid === 'true',
            priceReviewUnavailable: root.GaoReceiptPriceReviews?.getState?.().unavailable === true
        };
    }
    function settingsFingerprint() {
        return JSON.stringify([...workbench.querySelectorAll('[data-workbench-panel="settlement"] input, [data-workbench-panel="freight"] input, .commercial-tax, .commercial-freight-allocation')]
            .filter(x => x.id !== 'commercialSupplierId').map(x => [x.id || `${x.closest('[data-line-id]')?.dataset.lineId}:${x.className}`, x.type === 'checkbox' ? x.checked : x.value]));
    }
    function init() {
        const next = byId('commercialApprovalWorkbench');
        if (!next?.classList.contains('sd-review-workspace')) return;
        const changed = workbench !== next;
        workbench = next;
        if (baseline == null) baseline = settingsFingerprint();
        if (changed) { renderSignature = undefined; lastIssueKey = null; }
        const target = byId('receiptReviewOtherActions');
        if (target) for (const toolbar of document.querySelectorAll('[data-receipt-review-actions-source] #receiptDocumentActions, [data-receipt-review-actions-source] [data-label-receipt]')) target.append(toolbar);
        const summary = workbench.querySelector('.sd-review-financial-disclosure');
        if (summary && !summary.dataset.workspaceBound) {
            summary.dataset.workspaceBound = 'true';
            const desktop = root.matchMedia('(min-width: 1600px)');
            const sync = () => { if (desktop.matches) summary.open = true; };
            sync(); desktop.addEventListener?.('change', sync);
        }
        if (!bound) {
            bound = true;
            document.addEventListener('input', event => {
                if (!workbench?.contains(event.target)) return;
                if (event.target.id === 'commercialLineSearch') applyFilter(); else refresh();
            });
            document.addEventListener('change', event => { if (workbench?.contains(event.target)) refresh(); });
            document.addEventListener('click', event => {
                const more = document.querySelector('.sd-review-more');
                if (more?.open && (!more.contains(event.target) || event.target.closest?.('#receiptReviewOtherActions button, #receiptReviewOtherActions a'))) more.open = false;
                if (event.target.closest?.('#commercialNextIssue')) nextIssue();
                const issue = event.target.closest?.('[data-review-issue-key]');
                if (issue) revealIssue(issue.dataset.reviewIssueKey);
            });
            document.addEventListener('keydown', event => {
                const more = event.target.closest?.('.sd-review-more');
                if (event.key === 'Escape' && more?.open) { more.open = false; more.querySelector('summary')?.focus(); }
            });
            for (const name of ['receipt-pricing-inputs-changed', 'receipt-price-drafts-state-changed', 'receipt-price-reviews-changed', 'receipt-approval-operation-changed'])
                document.addEventListener(name, refresh);
            root.addEventListener('beforeunload', event => {
                if (workbench?.isConnected && workbench.dataset.readonly !== 'true' && settingsFingerprint() !== baseline && !root.GaoReceiptApprovalWorkspace.leaving) {
                    event.preventDefault(); event.returnValue = '';
                }
            });
        }
        refresh();
        if (changed) applyFilter();
        if (changed && state?.blocking.length && byId('receiptApprovalIssuesDisclosure')) byId('receiptApprovalIssuesDisclosure').open = true;
    }
    function refresh() {
        if (!workbench?.isConnected) { if (byId('commercialApprovalWorkbench')) init(); return; }
        const snapshot = collect();
        state = model.evaluate(snapshot);
        state.filter = activeFilter;
        state.search = byId('commercialLineSearch')?.value || '';
        state.totalCount = snapshot.lines.length;
        const issuesByLine = new Map();
        for (const issue of state.issues) if (issue.lineId) {
            if (!issuesByLine.has(issue.lineId)) issuesByLine.set(issue.lineId, []);
            issuesByLine.get(issue.lineId).push(issue);
        }
        for (const row of rows()) {
            const lineIssues = issuesByLine.get(row.dataset.lineId) || [];
            row.dataset.reviewBlocking = String(lineIssues.some(x => x.severity === 'blocking'));
            row.dataset.reviewWarning = String(lineIssues.some(x => x.severity !== 'blocking'));
            row.dataset.priceMissing = String(lineIssues.some(x => x.key === `price-${row.dataset.lineId}`));
            row.classList.toggle('sd-review-line-blocking', row.dataset.reviewBlocking === 'true');
            for (const [selector, key] of [['.commercial-unit-price', 'price'], ['.commercial-tax', 'tax'], ['.commercial-freight-allocation', 'allocation']]) {
                const input = row.querySelector(selector), invalid = lineIssues.some(x => x.key === `${key}-${row.dataset.lineId}`);
                input?.setAttribute('aria-invalid', String(invalid));
            }
        }
        text('receiptApprovalBlockingCount', state.counts.blocking);
        text('receiptApprovalAcknowledgementCount', state.counts.acknowledgements);
        text('receiptApprovalWarningCount', state.counts.warnings);
        const title = snapshot.readOnly ? 'Phiếu đã duyệt và ghi sổ' : state.blocking.length ? `Còn ${state.blocking.length} mục cần hoàn tất trước khi duyệt` : state.acknowledgements.length ? 'Đủ điều kiện · Có nội dung cần xác nhận khi duyệt' : 'Đủ điều kiện duyệt phiếu';
        text('receiptApprovalReadinessTitle', title);
        byId('receiptApprovalReadiness')?.classList.toggle('has-blockers', state.blocking.length > 0);
        text('managerApprovalBlockerSummary', state.blocking.length ? title : 'Kiểm tra thông tin và xác nhận để ghi sổ.');
        renderIssues();
        const next = byId('commercialNextIssue');
        if (next) next.disabled = state.issues.length === 0;
        const approve = byId('commercialApproveButton');
        if (approve) approve.disabled = snapshot.readOnly || state.blocking.length > 0;
        byId('managerApprovalBlockerSummary')?.classList.toggle('is-blocked', state.blocking.length > 0);
        renderFinance(snapshot);
        const footer = document.querySelector('.sd-manager-save-state');
        if (footer && !byId('receiptApprovalSettingsState')) {
            const status = document.createElement('small'); status.id = 'receiptApprovalSettingsState'; status.className = 'd-block'; footer.append(status);
        }
        text('receiptApprovalSettingsState', settingsFingerprint() !== baseline ? 'Thuế hoặc vận chuyển đã đổi · áp dụng khi duyệt' : 'Thuế và vận chuyển · áp dụng khi duyệt');
        for (const [key, field] of [['receiptReviewSupplier', 'CurrentSupplierText'], ['receiptReviewWarehouse', 'CurrentWarehouseText'], ['receiptReviewLegalEntity', 'CurrentLegalEntityText']])
            if (byId(field)) text(key, byId(field).value || (key === 'receiptReviewSupplier' ? 'Chưa chọn nhà cung cấp' : 'Chưa chọn'));
        updateFilterCounts();
        // Never hide/move the field under the keyboard while its validation/save state changes.
        // Explicit search/filter actions apply the latest state; rows keep the server's stable order.
    }
    function renderIssues() {
        const container = byId('receiptApprovalIssues');
        if (!container) return;
        const signature = JSON.stringify(state.issues);
        if (renderSignature === signature) return;
        renderSignature = signature;
        const fragment = document.createDocumentFragment();
        for (const issue of state.issues) {
            const li = document.createElement('li'), button = document.createElement('button');
            button.type = 'button'; button.className = `sd-review-issue is-${issue.severity}`;
            button.dataset.reviewIssueKey = issue.key; button.dataset.reviewSeverity = issue.severity;
            const label = document.createElement('strong'), detail = document.createElement('span'), category = document.createElement('span');
            label.textContent = issue.title; detail.textContent = issue.detail;
            category.className = 'sd-review-issue-kind';
            category.textContent = { blocking: 'Bắt buộc', acknowledgement: 'Cần xác nhận', warning: 'Tham khảo' }[issue.severity];
            button.append(label, category, detail); li.append(button); fragment.append(li);
        }
        if (!state.issues.length) {
            const li = document.createElement('li'); li.textContent = 'Không còn điều kiện bắt buộc cần xử lý.'; fragment.append(li);
        }
        container.replaceChildren(fragment);
    }
    function renderFinance(snapshot) {
        const f = state.financial;
        const freightAmountLabel = Number.isFinite(snapshot.freight) && snapshot.freight > 0 ? money(f.freight) : 'Chưa nhập tổng phí';
        text('commercialFreightHeading', 'Phí vận chuyển: ' + (snapshot.hasFreight ? freightAmountLabel : 'Không phát sinh'));
        text('commercialFreightDescription', snapshot.hasFreight
            ? (snapshot.capitalizeFreight ? 'Đưa vào giá vốn' : 'Ghi nhận riêng') + ' · ' + (snapshot.readOnly ? 'Thanh toán theo sổ Thu/chi' : 'Tạo khoản phải trả khi duyệt')
            : 'Bật khi phiếu có phát sinh phí vận chuyển.');
        text('commercialFreightTabTotal', freightAmountLabel);
        const freightTab = byId('commercialFreightTabTotal');
        if (freightTab) freightTab.hidden = !snapshot.hasFreight;
        for (const [id, value] of Object.entries({ commercialOutstandingMerchandise: f.outstandingMerchandise, commercialOutstandingFreight: f.outstandingFreight,
            commercialSubtotal: f.subtotal, commercialVatTotal: f.vat, commercialMerchandiseTotal: f.merchandise,
            commercialFreightSummary: f.freight, commercialLandedTotal: f.landed,
            commercialApprovalModalMerchandiseTotal: f.merchandise, approvalModalLanded: f.landed,
            approvalModalOutstandingMerchandise: f.outstandingMerchandise, approvalModalOutstandingFreight: f.outstandingFreight })) text(id, money(value));
        text('approvalModalWarehouse', byId('CurrentWarehouseText')?.value || 'Kho nhận trên phiếu');
        text('commercialMerchandisePaymentState', snapshot.readOnly ? (snapshot.merchandisePaid ? 'Đã thanh toán theo sổ công nợ' : 'Xem số dư và lịch sử thanh toán tại Thu/chi') : 'Tạo công nợ nhà cung cấp khi duyệt');
        text('commercialFreightPaymentState', !snapshot.hasFreight ? 'Không phát sinh vận chuyển' : snapshot.readOnly ? (snapshot.freightPaid ? 'Đã thanh toán theo sổ công nợ' : 'Xem số dư và lịch sử thanh toán tại Thu/chi') : 'Tạo khoản phải trả vận chuyển khi duyệt');
    }
    function updateFilterCounts() {
        for (const button of workbench.querySelectorAll('[data-commercial-filter]')) {
            const count = button.querySelector('[data-commercial-filter-count]');
            if (count) count.textContent = String(rows().filter(row => matchesFilter(row, button.dataset.commercialFilter)).length);
        }
    }
    function matchesFilter(row, filter) {
        return filter === 'all' || filter === 'revision' && row.dataset.revisionChanged === 'true' ||
            filter === 'catalog' && row.dataset.catalogReview === 'true' || filter === 'price' && row.dataset.priceMissing === 'true' ||
            filter === 'exception' && row.dataset.exception === 'true' || filter === 'blocking' && row.dataset.reviewBlocking === 'true' ||
            filter === 'warning' && row.dataset.reviewWarning === 'true';
    }
    function applyFilter() {
        if (!workbench || !state) return;
        const query = model.normalize(byId('commercialLineSearch')?.value || '');
        let visible = 0;
        for (const row of rows()) {
            const show = matchesFilter(row, activeFilter) && query.split(/\s+/).every(token => model.normalize(row.dataset.search || row.textContent).includes(token));
            row.classList.toggle('d-none', !show); row.hidden = !show; if (show) visible++;
        }
        state.filter = activeFilter; state.search = byId('commercialLineSearch')?.value || '';
        state.visibleCount = visible;
        for (const button of workbench.querySelectorAll('[data-commercial-filter]')) {
            const selected = button.dataset.commercialFilter === activeFilter;
            button.classList.toggle('active', selected); button.setAttribute('aria-pressed', String(selected));
        }
        text('commercialVisibleCount', `${visible} / ${rows().length} dòng`);
        const empty = byId('commercialFilterEmpty'); if (empty) empty.hidden = visible > 0;
        text('commercialFilterEmptyMessage', 'Không có dòng phù hợp. Đổi bộ lọc hoặc xóa từ khóa để xem thêm.');
    }
    function setFilter(value, resetSearch = false) { activeFilter = value || 'all'; if (resetSearch && byId('commercialLineSearch')) byId('commercialLineSearch').value = ''; refresh(); applyFilter(); }
    function revealIssue(keyOrElement) {
        refresh();
        const issue = typeof keyOrElement === 'string' ? state?.issues.find(x => x.key === keyOrElement) : null;
        let target = issue ? issue.lineId ? rows().find(row => row.dataset.lineId === issue.lineId)?.querySelector(issue.selector) : byId(issue.targetId) : keyOrElement;
        if (!target && issue?.lineId) target = rows().find(row => row.dataset.lineId === issue.lineId);
        if (!target) return false;
        lastIssueKey = issue?.key || null;
        if (target.closest('.commercial-line')) {
            activeFilter = 'all'; if (byId('commercialLineSearch')) byId('commercialLineSearch').value = ''; applyFilter();
        }
        if (target.tagName === 'DETAILS') target.open = true;
        for (let parent = target.parentElement; parent; parent = parent.parentElement) if (parent.tagName === 'DETAILS') parent.open = true;
        const tab = target.closest('[data-workbench-panel]')?.dataset.workbenchPanel;
        if (tab) root.activateStockDocumentWorkbenchTab?.(tab);
        const modal = target.closest('.modal');
        if (modal && !modal.classList.contains('show')) { root.GaoAppPurchaseReceiptApproval?.open(); return true; }
        target.scrollIntoView({ behavior: root.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'center' });
        if (!target.matches('input,select,textarea,button,a,[tabindex]')) target.setAttribute('tabindex', '-1');
        if (target.id === 'commercialSupplierId' && root.jQuery?.fn?.select2) root.jQuery(target).select2('open');
        else { target.focus({ preventScroll: true }); if (target.matches('input[type="number"]')) target.select?.(); }
        return true;
    }
    function nextIssue() {
        refresh();
        const items = state.issues, index = items.findIndex(x => x.key === lastIssueKey);
        if (items.length) revealIssue(items[(index + 1) % items.length].key);
    }
    function text(id, value) { const element = byId(id); if (element && element.textContent !== String(value)) element.textContent = value; }
    root.GaoReceiptApprovalWorkspace = { init, refresh, revealIssue, nextIssue, setFilter, getState: () => {
        refresh(); if (state) state.visibleCount = rows().filter(x => !x.hidden && !x.classList.contains('d-none')).length; return state;
    }, leaving: false };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => { init(); applyFilter(); }); else { init(); applyFilter(); }
})(typeof window === 'undefined' ? null : window, function () {
    'use strict';
    const round = n => {
        const parts = String(Math.abs(n)).split('e');
        return Math.sign(n) * Number(String(Math.round(Number(parts[0] + 'e' + (Number(parts[1] || 0) + 2)))) + 'e-2');
    };
    const finite = n => Number.isFinite(n) ? n : 0;
    const normalize = text => String(text || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase().trim();
    function financial(s) {
        let subtotal = 0, vat = 0, merchandise = 0;
        for (const line of s.lines) {
            const price = round(finite(line.price));
            const before = line.priceText !== '' && price === line.exactPrice && Number.isFinite(line.exactAmount) ? line.exactAmount : round(finite(line.quantity) * price);
            const tax = s.hasVat ? round(before * finite(line.taxRate) / 100) : 0;
            subtotal = round(subtotal + before); vat = round(vat + tax); merchandise = round(merchandise + before + tax);
        }
        const freight = s.hasFreight ? round(finite(s.freight)) : 0;
        return { subtotal, vat, merchandise, freight, landed: round(subtotal + (s.hasVat && s.includeVat ? vat : 0) + (s.hasFreight && s.capitalizeFreight ? freight : 0)),
            outstandingMerchandise: merchandise, outstandingFreight: freight };
    }
    function evaluate(s) {
        const issues = [];
        const add = (key, severity, title, detail, targetId, line, selector) => issues.push({ key, severity, title, detail: detail || '', targetId, lineId: line?.id == null ? undefined : String(line.id), selector });
        if (!s.readOnly) {
            if (s.hasRevisionRequest) add('revision-request', 'blocking', 'Giải quyết yêu cầu xin sửa trước khi duyệt', 'Trả phiếu để nhân viên sửa hoặc từ chối yêu cầu có lý do.', 'receiptRevisionRequest');
            if (s.pendingOutside) add('pending-outside', 'blocking', 'Xử lý hàng ngoài đơn đặt hàng', 'Chấp nhận hoặc loại từng dòng trước khi duyệt.', 'managerExceptionQueue');
            if (s.unresolved > 0) add('provisional', 'blocking', `${s.unresolved} sản phẩm chưa đối chiếu danh mục`, 'Ghép hoặc tạo sản phẩm theo quyền được cấp.', 'receiptIntakePending');
            if (s.hasCatalog) add('catalog', 'blocking', 'Hoàn thiện sản phẩm tạm', 'Cần tên sản phẩm hợp lệ trong danh mục.', 'managerExceptionQueue');
            if (!s.lines.length) add('no-lines', 'blocking', 'Phiếu chưa có hàng thực nhận', 'Bổ sung hàng trước khi duyệt.', 'commercialApprovalWorkbench');
            for (const line of s.lines) {
                const label = `Dòng ${line.lineNo}: ${line.name}`;
                if (!Number.isFinite(line.quantity) || !Number.isFinite(line.factor) || !(line.quantity > 0) || !(line.factor > 0) || line.savingQuantity) add(`quantity-${line.id}`, 'blocking', line.savingQuantity ? 'Đang lưu số lượng thực nhận' : 'Kiểm tra số lượng và quy đổi', label, null, line, '.commercial-quantity');
                if (!Number.isFinite(line.price) || line.price <= 0 || line.price > 99999999999999.99)
                    add(`price-${line.id}`, 'blocking', 'Kiểm tra giá nhập chưa VAT', label + ' · Giá phải lớn hơn 0.', null, line, '.commercial-unit-price');
                if (s.hasVat && !(line.taxId > 0)) add(`tax-${line.id}`, 'blocking', 'Chọn thuế suất VAT', label + ' · Kể cả thuế suất 0% phải chọn từ cấu hình.', null, line, '.commercial-tax');
                if (s.hasFreight && s.capitalizeFreight && (!Number.isFinite(line.allocation) || line.allocation < 0 || line.allocation > 99999999999999.99)) add(`allocation-${line.id}`, 'blocking', 'Phí phân bổ không hợp lệ', label + ' · Nhập từ 0 đến 99.999.999.999.999,99 đ.', null, line, '.commercial-freight-allocation');
                if (line.previousPrice > 0 && line.price > 0 && round(line.previousPrice) !== round(line.price)) add(`variance-${line.id}`, 'acknowledgement', 'Giá nhập khác lần nhập gần nhất', label + ' · Xác nhận chênh lệch trong bước duyệt.', null, line, '.commercial-unit-price');
                if (line.loss) add(`loss-${line.id}`, 'warning', 'Có giá bán thấp hơn giá vốn', label + ' · Kiểm tra giá bán nếu cần, không chặn nhập kho.', null, line, '.js-receipt-selling-prices');
                else if (line.attention) add(`selling-${line.id}`, 'warning', 'Nên xem lại giá bán', label + ' · Giá bán đã áp dụng có hiệu lực ngay.', null, line, '.js-receipt-selling-prices');
            }
            if (!(s.supplierId > 0)) add('supplier', 'blocking', 'Chọn nhà cung cấp', 'Bắt buộc để ghi nhận công nợ khi duyệt.', 'commercialSupplierId');
            if (s.supplierState?.supplierSaving || s.supplierState?.supplierSaveFailed) add('supplier-save', 'blocking', s.supplierState.supplierSaving ? 'Đang lưu nhà cung cấp' : 'Nhà cung cấp chưa lưu thành công', 'Đợi lưu xong hoặc chọn lại nhà cung cấp.', 'commercialSupplierId');
            if (s.draftState?.error) add('price-save', 'blocking', 'Giá nhập chưa lưu thành công', s.draftState.error, 'receiptPriceDraftSave');
            if (s.hasFreight) {
                if (!Number.isFinite(s.freight) || round(s.freight) <= 0 || round(s.freight) > 99999999999999.99) add('freight-total', 'blocking', 'Nhập tổng phí vận chuyển hợp lệ', 'Tổng phí phải lớn hơn 0 và không vượt 99.999.999.999.999,99 đ.', 'commercialFreightTotal');
                if (!s.freightPayee) add('freight-payee', 'blocking', 'Nhập bên nhận phí vận chuyển', 'Bắt buộc để ghi nhận thanh toán hoặc khoản phải trả.', 'commercialFreightPayee');
                if (s.capitalizeFreight && round(s.lines.reduce((sum, line) => sum + round(finite(line.allocation)), 0) - round(finite(s.freight))) !== 0)
                    add('freight-allocation', 'blocking', 'Phân bổ đủ phí vận chuyển', 'Tổng phân bổ phải bằng tổng phí; dùng Tự phân bổ hoặc chỉnh từng dòng.', 'commercialAutoAllocate');
            }
            if (s.overdelivery) add('overdelivery', 'acknowledgement', 'Xác nhận số lượng vượt đơn đặt hàng', 'Kiểm tra và chấp nhận phần nhận vượt.', 'acceptOverdelivery');
            if (s.priceReviewUnavailable) add('selling-unavailable', 'warning', 'Chưa tải được đánh giá giá bán', 'Tải lại để kiểm tra; không chặn duyệt phiếu.', 'refreshPriceReviews');
        }
        const blocking = issues.filter(x => x.severity === 'blocking'), acknowledgements = issues.filter(x => x.severity === 'acknowledgement'), warnings = issues.filter(x => x.severity === 'warning');
        // Keep priority stable without reordering goods rows.
        return { issues: [...blocking, ...acknowledgements, ...warnings], blocking, acknowledgements, warnings,
            counts: { blocking: blocking.length, acknowledgements: acknowledgements.length, warnings: warnings.length }, financial: financial(s) };
    }
    return { evaluate, financial, normalize };
});
