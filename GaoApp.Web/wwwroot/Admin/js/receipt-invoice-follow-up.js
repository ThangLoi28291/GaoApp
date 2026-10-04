(() => {
    'use strict';
    const states = {
        Waiting: ['Chờ hóa đơn', 'warning'], NotExpected: ['Không chờ hóa đơn', 'secondary'],
        NeedsReview: ['Cần kiểm tra hóa đơn', 'warning'], Complete: ['Đủ hóa đơn', 'success'],
        Reviewed: ['Đã kiểm tra hóa đơn', 'success'],
        Unclassified: ['Chưa phân loại hóa đơn', 'secondary']
    };
    let current = null, generation = 0;
    const id = () => Number(window.stockDocumentPage?.documentId || 0);
    async function api(suffix = '', body) {
        const response = await fetch(`/admin/api/stock-documents/${id()}/invoice-follow-up${suffix}`, {
            method: body ? 'POST' : 'GET', cache: 'no-store', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', RequestVerificationToken: document.querySelector('[name="__RequestVerificationToken"]')?.value || '' },
            body: body ? JSON.stringify(body) : undefined
        });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data.message || 'Không tải được tình trạng hóa đơn. Vui lòng thử lại.');
        return data;
    }
    function render(model) {
        current = model;
        const [label, color] = states[model.state] || states.Unclassified;
        const badge = document.getElementById('receiptInvoiceFollowUpBadge');
        if (badge) {
            badge.textContent = label + (model.state === 'Waiting' && model.isConfirmed ? ` · ${model.waitingDays} ngày` : '');
            badge.className = `badge bg-label-${color}`;
        }
        const end = document.getElementById('btnEndInvoiceWaiting');
        if (end) end.hidden = !(model.isConfirmed && model.state === 'Waiting');
        const review = document.getElementById('btnReviewInvoice');
        if (review) review.hidden = !(model.isConfirmed && model.state === 'NeedsReview' && model.evidenceFingerprint);
        const details = document.getElementById('receiptInvoiceReviewDetails');
        if (details) {
            details.hidden = model.state !== 'Reviewed';
            details.textContent = model.state === 'Reviewed'
                ? `${model.reviewedBy || 'Quản lý'} · ${new Date(model.reviewedAtUtc).toLocaleString('vi-VN')} · ${model.reviewReason || ''}` : '';
        }
        const choices = document.getElementById('approvalInvoiceChoices');
        const linked = document.getElementById('approvalInvoiceLinked');
        if (choices) choices.hidden = model.hasLinkedInvoice;
        if (linked) {
            linked.hidden = !model.hasLinkedInvoice;
            linked.textContent = `Phiếu đã gắn XML · ${label}. XML chỉ hỗ trợ lưu và đối chiếu; tồn hóa đơn ghi theo các dòng nhập khi quản lý xác nhận.`;
        }
    }
    async function refresh() {
        if (!id() || !document.getElementById('receiptInvoiceFollowUp')) return;
        const version = ++generation;
        try {
            const model = await api();
            if (version === generation) render(model);
            return model;
        } catch (error) {
            if (version === generation) {
                current = null;
                const badge = document.getElementById('receiptInvoiceFollowUpBadge');
                if (badge) badge.textContent = error.message;
            }
        }
    }
    async function approvalChoice() {
        const model = await refresh();
        if (!model) throw new Error('Chưa kiểm tra được hóa đơn. Vui lòng thử duyệt lại.');
        if (model.hasLinkedInvoice) return true;
        const selected = document.querySelector('input[name="approvalInvoiceExpectation"]:checked');
        if (!selected) throw new Error('Chọn chờ nhà cung cấp gửi hóa đơn hoặc hoàn tất không chờ hóa đơn.');
        return selected.value === 'waiting';
    }
    function badgeHtml(item) {
        if (Number(item.status) !== 3) return '';
        const [label, color] = states[item.invoiceFollowUp] || states.Unclassified;
        const days = item.invoiceFollowUp === 'Waiting' ? ` · ${Math.max(0, Number(item.invoiceWaitingDays) || 0)} ngày` : '';
        return `<div class="mt-1"><span class="badge bg-label-${color}">${label}${days}</span></div>`;
    }
    window.GaoReceiptInvoiceFollowUp = { refresh, approvalChoice, badgeHtml };
    document.addEventListener('DOMContentLoaded', () => {
        refresh();
        document.getElementById('approvalInvoiceChoices')?.addEventListener('change', () => {
            const message = document.getElementById('approveMessage');
            if (message?.textContent.startsWith('Chọn chờ nhà cung cấp')) message.textContent = '';
        });
        document.getElementById('approveModal')?.addEventListener('show.bs.modal', refresh);
        const reviewModal = document.getElementById('reviewInvoiceModal');
        let reviewSnapshot = null;
        document.getElementById('btnReviewInvoice')?.addEventListener('click', () => {
            if (!current || current.state !== 'NeedsReview') return;
            reviewSnapshot = { ...current };
            const money = value => Number(value || 0).toLocaleString('vi-VN') + ' đ';
            document.getElementById('reviewInvoiceSummary').textContent =
                `Tổng phiếu nhập: ${money(current.receiptGoodsTotal)}\nTổng XML: ${money(current.xmlPaymentAmount)}\nChênh lệch: ${money(current.receiptGoodsTotal - current.xmlPaymentAmount)}\nDòng XML chưa nhận diện: ${current.unmatchedDetailCount}`;
            document.getElementById('reviewInvoiceReason').value = '';
            document.getElementById('reviewInvoiceMessage').textContent = '';
            bootstrap.Modal.getOrCreateInstance(reviewModal).show();
        });
        document.getElementById('btnConfirmReviewInvoice')?.addEventListener('click', async function () {
            const message = document.getElementById('reviewInvoiceMessage');
            const reason = document.getElementById('reviewInvoiceReason').value.trim();
            if (!reason) { message.textContent = 'Vui lòng ghi kết quả kiểm tra hóa đơn.'; return; }
            if (!reviewSnapshot) return;
            this.disabled = true;
            try {
                const { rowVersion, mapId, evidenceFingerprint } = reviewSnapshot;
                render(await api('/review', { rowVersion, mapId, evidenceFingerprint, reason }));
                bootstrap.Modal.getInstance(reviewModal).hide();
                window.stockDocumentRowVersion?.update(current.rowVersion);
            } catch (error) { message.textContent = error.message; }
            finally { this.disabled = false; }
        });
        const modal = document.getElementById('endInvoiceWaitingModal');
        document.getElementById('btnEndInvoiceWaiting')?.addEventListener('click', () => {
            document.getElementById('endInvoiceWaitingReason').value = '';
            document.getElementById('endInvoiceWaitingMessage').textContent = '';
            bootstrap.Modal.getOrCreateInstance(modal).show();
        });
        document.getElementById('btnConfirmEndInvoiceWaiting')?.addEventListener('click', async function () {
            const message = document.getElementById('endInvoiceWaitingMessage');
            const reason = document.getElementById('endInvoiceWaitingReason').value.trim();
            if (!reason) { message.textContent = 'Vui lòng nhập ghi chú ngắn.'; return; }
            this.disabled = true;
            try {
                if (!current) throw new Error('Vui lòng tải lại tình trạng hóa đơn.');
                render(await api('/end-waiting', { rowVersion: current.rowVersion, reason }));
                window.stockDocumentRowVersion?.update(current.rowVersion);
                bootstrap.Modal.getInstance(modal).hide();
                window.location.reload();
            } catch (error) { message.textContent = error.message; }
            finally { this.disabled = false; }
        });
    });
})();
