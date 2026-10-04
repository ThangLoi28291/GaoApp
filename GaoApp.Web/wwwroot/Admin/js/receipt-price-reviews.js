(function () {
    'use strict';
    let records = [], requestVersion = 0, timer;
    const summary = document.getElementById('priceReviewCounts');
    if (!summary) return;
    const checked = id => document.getElementById(id)?.checked === true;
    const number = input => input?.value !== '' ? Number(input?.value) : NaN;
    function currentCost(row) {
        if (!row?.classList.contains('commercial-line')) return null;
        const price = number(row.querySelector('.commercial-unit-price')), factor = Number(row.dataset.factor);
        const quantity = Number(row.dataset.quantity);
        const rate = Number(row.querySelector('.commercial-tax')?.selectedOptions[0]?.dataset.rate || 0);
        const tax = checked('commercialHasVat') && checked('includeVatInInventoryCost') ? price * rate / 100 : 0;
        const freight = checked('commercialHasFreight') && checked('capitalizeFreightInInventoryCost') && quantity > 0
            ? (number(row.querySelector('.commercial-freight-allocation')) || 0) / quantity : 0;
        return factor > 0 ? (price + tax + freight) / factor : NaN;
    }
    function badge(parent, text, color) {
        const span = document.createElement('span'); span.className = `badge bg-label-${color} me-1 mb-1`;
        span.textContent = text; parent.append(span);
    }
    function render() {
        let needs = 0, changed = 0, reviewedCount = 0, pending = 0;
        for (const record of records) {
            const placeholders = [...document.querySelectorAll(`[data-price-review-line-id="${record.lineId}"]`)];
            const cost = currentCost(placeholders[0]?.closest('tr'));
            const local = cost !== null && record.currentBaseCost !== null;
            const reviewed = local ? record.catalogCurrent && record.reviewedBaseCost != null && Number.isFinite(cost) && Math.abs(cost - record.reviewedBaseCost) < 0.0002 : record.reviewed;
            const differentCost = local && (!Number.isFinite(cost) || Math.abs(cost - record.currentBaseCost) >= 0.0002);
            const row = placeholders[0]?.closest('tr');
            const previous = Number(row?.querySelector('.commercial-last-price')?.dataset.value || 0);
            const moved = previous > 0 ? number(row.querySelector('.commercial-unit-price')) !== previous : placeholders[0]?.dataset.priceMoved === 'true';
            const attention = !reviewed && (record.needsAttention || differentCost || moved);
            const belowCost = record.belowCost && !differentCost;
            if (attention || belowCost) needs++;
            if (record.hasChanges) changed++;
            if (reviewed) reviewedCount++; else if (!attention && !belowCost) pending++;
            for (const placeholder of placeholders) {
                const row = placeholder.closest('tr');
                row?.classList.remove('sd-price-attention', 'sd-price-loss', 'sd-price-reviewed', 'sd-price-pending');
                row?.classList.add(belowCost ? 'sd-price-loss' : attention ? 'sd-price-attention' : reviewed ? 'sd-price-reviewed' : 'sd-price-pending');
                placeholder.replaceChildren();
                if (record.hasChanges) badge(placeholder, 'Đã cập nhật giá', 'success');
                if (belowCost) badge(placeholder, 'Có giá bán dưới vốn', 'danger');
                if (attention) badge(placeholder, 'Cần xem lại giá', 'warning');
                else if (reviewed) badge(placeholder, record.hasChanges ? 'Đã đánh giá' : 'Đã kiểm tra, giữ giá', 'success');
                else badge(placeholder, 'Chưa đánh giá', 'secondary');
                if (record.reviewedAtUtc) {
                    const stamp = document.createElement('small'); stamp.className = 'd-block text-muted';
                    const utc = record.reviewedAtUtc.endsWith('Z') ? record.reviewedAtUtc : record.reviewedAtUtc + 'Z';
                    stamp.textContent = `${record.reviewedBy || 'Hệ thống'} · ${new Date(utc).toLocaleString('vi-VN')}`;
                    placeholder.append(stamp);
                }
            }
        }
        summary.replaceChildren();
        badge(summary, `Cần xem lại: ${needs}`, needs ? 'warning' : 'secondary');
        badge(summary, `Đã cập nhật giá: ${changed}`, 'success');
        badge(summary, `Đã đánh giá: ${reviewedCount}`, 'primary');
        badge(summary, `Chưa đánh giá: ${pending}`, 'secondary');
    }
    async function refresh() {
        const version = ++requestVersion;
        try {
            const response = await fetch(`/admin/api/stock-documents/${window.stockDocumentPage.documentId}/selling-price-reviews`, { cache: 'no-store', headers: { Accept: 'application/json' } });
            const json = await response.json();
            const data = json?.data ?? json;
            if (!response.ok || !Array.isArray(data.lines)) throw new Error('Không tải được đánh giá giá bán. Bấm Tải lại đánh giá.');
            if (version !== requestVersion) return;
            records = data.lines; render();
        } catch (error) {
            if (version !== requestVersion) return;
            summary.textContent = 'Chưa xác minh được đánh giá mới nhất. Bấm Tải lại đánh giá.';
            document.querySelectorAll('[data-price-review-line-id]').forEach(x => {
                x.textContent = 'Chưa tải được đánh giá';
                x.closest('tr')?.classList.remove('sd-price-attention', 'sd-price-loss', 'sd-price-reviewed');
            });
            records = [];
        }
    }
    window.GaoReceiptPriceReviews = { refresh };
    document.getElementById('refreshPriceReviews')?.addEventListener('click', refresh);
    document.addEventListener('receipt-pricing-inputs-changed', () => { if (records.length) render(); });
    new MutationObserver(changes => {
        if (changes.some(change => [...change.addedNodes].some(node => node.nodeType === 1 &&
            (node.matches?.('[data-price-review-line-id]') || node.querySelector?.('[data-price-review-line-id]'))))) {
            clearTimeout(timer); timer = setTimeout(refresh, 150);
        }
    }).observe(document.body, { childList: true, subtree: true });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', refresh); else refresh();
})();
