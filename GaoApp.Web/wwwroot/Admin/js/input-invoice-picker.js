(function () {
    'use strict';

    let modalInstance = null;
    let candidates = [];
    let selected = null;
    let previewObjectUrl = null;
    let selecting = false;
    let relinkExpectedInputInvoiceHeadId = null;
    let associationContext = null;

    document.addEventListener('DOMContentLoaded', function () {
        const modal = document.getElementById('inputInvoicePickerModal');
        const open = document.getElementById('btnOpenInputInvoicePicker');
        if (!modal || !open || !window.bootstrap) return;

        modalInstance = bootstrap.Modal.getOrCreateInstance(modal);
        open.addEventListener('click', function () {
            relinkExpectedInputInvoiceHeadId = null;
            openPicker();
        });
        document.getElementById('inputInvoicePickerFilters')?.addEventListener('submit', function (event) {
            event.preventDefault();
            browse();
        });
        document.getElementById('btnSelectInputInvoice')?.addEventListener('click', selectCandidate);
        document.getElementById('inputInvoicePickerCandidates')?.addEventListener('keydown', moveCandidateFocus);
        modal.addEventListener('hidden.bs.modal', releasePreview);
        window.openInputInvoicePickerForRelink = function (expectedInputInvoiceHeadId) {
            relinkExpectedInputInvoiceHeadId = Number(expectedInputInvoiceHeadId || 0) || null;
            openPicker();
        };
    });

    async function openPicker() {
        const now = new Date();
        const year = document.getElementById('inputInvoicePickerYear');
        const month = document.getElementById('inputInvoicePickerMonth');
        if (year && !year.value) year.value = String(now.getFullYear());
        if (month && !month.value) month.value = String(now.getMonth() + 1);
        modalInstance.show();
        await browse();
        (document.querySelector('#inputInvoicePickerCandidates [aria-selected="true"]')
            || document.querySelector('#inputInvoicePickerCandidates [role="option"]'))?.focus();
    }

    async function browse() {
        const receiptId = getReceiptId();
        if (!receiptId) return announce('Không xác định được phiếu nhập.', true);

        clearSelection();
        setCandidateContent('<div class="text-muted">Đang tải thư viện hóa đơn...</div>');
        const query = new URLSearchParams();
        new FormData(document.getElementById('inputInvoicePickerFilters')).forEach(function (value, key) {
            const text = String(value || '').trim();
            if (text) query.set(key, text);
        });

        try {
            const response = await fetch(
                `/admin/api/stock-documents/${receiptId}/input-invoices/picker/candidates?${query}`,
                { credentials: 'same-origin', cache: 'no-store' });
            const data = await readJson(response);
            if (!response.ok) throw new Error(data?.message || 'Không tải được thư viện hóa đơn.');

            candidates = data.candidates || [];
            associationContext = data.context || {};
            renderContext(associationContext);
            renderCandidates();
            const linkedIndex = candidates.findIndex(function (candidate) {
                return candidate.linkedCurrentReceipt;
            });
            if (linkedIndex >= 0) {
                await choose(linkedIndex);
            } else {
                announce(`Tìm thấy ${candidates.length} hóa đơn.`, false);
            }
        } catch (error) {
            candidates = [];
            setCandidateContent(`<div class="text-danger">${escapeHtml(error.message)}</div>`);
            announce(error.message, true);
        }
    }

    function renderContext(context) {
        const text = `${context.supplierCode || ''} · ${context.supplierName || '-'} · MST ${context.supplierTaxCode || '-'}`;
        setText('inputInvoicePickerSupplierContext', text);
        setText('inputInvoicePickerOwnerContext',
            `Chủ thể phiếu: ${context.receiptOwnerLegalEntityName || 'Chưa xác định'} · ` +
            'MST người mua phải phân giải đúng duy nhất chủ thể này.');
        setText('inputInvoiceSupplierTaxCode', context.supplierTaxCode || '-');
        const relinkMode = Boolean(relinkExpectedInputInvoiceHeadId);
        const title = document.getElementById('inputInvoicePickerTitle');
        if (title) title.textContent = relinkMode ? 'Thay hóa đơn liên kết' : 'Chọn hóa đơn';
        document.getElementById('inputInvoiceRelinkReasonBox')?.classList.toggle('d-none', !relinkMode);
        const reason = document.getElementById('inputInvoiceRelinkReason');
        if (reason && !relinkMode) reason.value = '';
    }

    function renderCandidates() {
        if (!candidates.length) {
            setCandidateContent('<div class="text-muted">Không tìm thấy hóa đơn trong phạm vi lọc.</div>');
            return;
        }

        setCandidateContent(candidates.map(function (candidate, index) {
            const ownerStatus = buyerOwnerLabel(candidate);
            const status = candidate.linkedCurrentReceipt
                ? 'Đã chọn'
                : candidate.linkedOtherReceiptCount > 0
                    ? `Đã liên kết ${candidate.linkedOtherReceiptCount} phiếu khác`
                    : candidate.selectionAllowed
                        ? 'Chưa liên kết'
                        : ownerStatus;
            return `<button type="button" class="input-invoice-candidate"
                        role="option" aria-selected="false" data-index="${index}"
                        title="${escapeHtml(status)}">
                    <span>${formatDate(candidate.invoiceDate)}</span>
                    <span>${escapeHtml(candidate.invoiceSeries || '-')}</span>
                    <span>${escapeHtml(candidate.invoiceNumber || '-')}</span>
                    <span>${escapeHtml(status)}</span>
                </button>`;
        }).join(''));

        document.querySelectorAll('.input-invoice-candidate').forEach(function (button) {
            button.addEventListener('click', function () {
                choose(Number(button.dataset.index));
            });
        });
    }

    function buyerOwnerLabel(candidate) {
        if (candidate.buyerOwnerMatchesReceipt) return 'Đúng pháp nhân';
        switch (candidate.buyerOwnerResolutionStatus) {
            case 'MissingBuyerTaxCode': return 'Thiếu MST bên mua';
            case 'NotFound': return 'Không tìm thấy pháp nhân';
            case 'Ambiguous': return 'MST bên mua bị trùng';
            case 'Resolved': return 'Sai pháp nhân';
            default: return candidate.selectionBlockMessage || 'Không thể liên kết';
        }
    }

    async function choose(index) {
        selected = candidates[index] || null;
        document.querySelectorAll('.input-invoice-candidate').forEach(function (button) {
            button.setAttribute('aria-selected', String(Number(button.dataset.index) === index));
        });
        const select = document.getElementById('btnSelectInputInvoice');
        if (select) {
            select.disabled = !selected?.selectionAllowed;
            select.textContent = relinkExpectedInputInvoiceHeadId ? 'Xác nhận thay hóa đơn' : 'Xác nhận liên kết';
        }
        if (!selected) return;
        setText('inputInvoicePickerConfirmation', selected.linkedCurrentReceipt
            ? 'Đây là hóa đơn đang liên kết với phiếu.'
            : `Sẽ ${relinkExpectedInputInvoiceHeadId ? 'thay bằng' : 'liên kết'} hóa đơn ${selected.invoiceSeries || '-'} · ${selected.invoiceNumber || '-'}.`);
        announce(`Đã chọn ${selected.invoiceSeries || '-'} · ${selected.invoiceNumber || '-'}.`, false);
        await preview(selected);
    }

    async function preview(candidate) {
        releasePreview();
        const box = document.getElementById('inputInvoicePickerPreview');
        if (!box) return;
        box.innerHTML = '<div class="text-muted p-4">Đang tải bản xem trước...</div>';
        const receiptId = getReceiptId();
        const key = encodeURIComponent(candidate.documentKey);
        try {
            if (candidate.hasPdf) {
                const response = await fetch(
                    `/admin/api/stock-documents/${receiptId}/input-invoices/picker/pdf?documentKey=${key}`,
                    { credentials: 'same-origin' });
                if (response.ok) {
                    previewObjectUrl = URL.createObjectURL(await response.blob());
                    box.innerHTML = `<iframe title="Bản xem trước PDF hóa đơn" src="${previewObjectUrl}"></iframe>`;
                    return;
                }
                if (!candidate.hasXml)
                    throw new Error(await responseMessage(response, 'Không mở được PDF.'));
                announce('Không mở được PDF. Đang hiển thị nội dung dựng từ XML.', false);
            }
            if (candidate.hasXml) {
                const response = await fetch(
                    `/admin/api/stock-documents/${receiptId}/input-invoices/picker/xml?documentKey=${key}`,
                    { credentials: 'same-origin' });
                if (!response.ok) throw new Error(await responseMessage(response, 'Không dựng được bản xem trước XML.'));
                box.innerHTML = await response.text();
                return;
            }
            box.innerHTML = '<div class="text-muted p-4">Hóa đơn không có PDF hoặc XML để xem trước.</div>';
        } catch (error) {
            box.innerHTML = `<div class="alert alert-danger m-4">${escapeHtml(error.message)}</div>`;
            announce(error.message, true);
        }
    }

    async function selectCandidate() {
        if (!selected?.selectionAllowed || selecting) return;
        const button = document.getElementById('btnSelectInputInvoice');
        const receiptId = getReceiptId();
        const expectedCurrentInputInvoiceHeadId = relinkExpectedInputInvoiceHeadId
            || Number(associationContext?.currentInputInvoiceHeadId || 0)
            || null;
        const isRelink = Boolean(expectedCurrentInputInvoiceHeadId &&
            (relinkExpectedInputInvoiceHeadId || selected.requiresRelinkReason));
        const reasonInput = document.getElementById('inputInvoiceRelinkReason');
        const reason = String(reasonInput?.value || '').trim();
        if (isRelink && !reason) {
            announce('Vui lòng nhập lý do thay hóa đơn.', true);
            reasonInput?.focus();
            return;
        }
        selecting = true;
        try {
            if (button) button.disabled = true;
            announce(isRelink ? 'Đang thay hóa đơn...' : 'Đang liên kết hóa đơn...', false);
            const response = await fetch(
                `/admin/api/stock-documents/${receiptId}/input-invoices/${isRelink ? 'relink' : 'picker/select'}`, {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(isRelink
                        ? {
                            documentKey: selected.documentKey,
                            expectedCurrentInputInvoiceHeadId: expectedCurrentInputInvoiceHeadId,
                            reason: reason
                        }
                        : { documentKey: selected.documentKey })
                });
            const data = await readJson(response);
            if (!response.ok) {
                if (response.status === 409 && data?.code === 'AssociationChanged')
                    throw new Error(data.message || 'Liên kết đã thay đổi. Vui lòng tải lại và thử lại.');
                throw new Error(data?.message || 'Không thể liên kết hóa đơn.');
            }
            const successMessage = data?.message || 'Đã liên kết hóa đơn.';
            setPageMessage(successMessage, false);
            modalInstance.hide();
            if (typeof window.loadInputInvoicesForStockDocument === 'function') {
                try {
                    await window.loadInputInvoicesForStockDocument();
                } catch {
                    setPageMessage(`${successMessage} Không thể làm mới danh sách; vui lòng tải lại trang.`, true);
                }
            }
            relinkExpectedInputInvoiceHeadId = null;
        } catch (error) {
            announce(error.message, true);
            setPageMessage(error.message, true);
        } finally {
            selecting = false;
            if (button) button.disabled = !selected?.selectionAllowed;
        }
    }

    function moveCandidateFocus(event) {
        if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
        const options = Array.from(document.querySelectorAll('.input-invoice-candidate'));
        const current = options.indexOf(document.activeElement);
        if (current < 0) return;
        event.preventDefault();
        const next = event.key === 'ArrowDown'
            ? Math.min(options.length - 1, current + 1)
            : Math.max(0, current - 1);
        options[next].focus();
        options[next].click();
    }

    function clearSelection() {
        selected = null;
        releasePreview();
        const button = document.getElementById('btnSelectInputInvoice');
        if (button) button.disabled = true;
        const preview = document.getElementById('inputInvoicePickerPreview');
        if (preview) preview.innerHTML = '<div class="text-muted p-4">Chọn một hóa đơn để xem trước.</div>';
        setText('inputInvoicePickerConfirmation', 'Hãy kiểm tra thông tin và bản xem trước trước khi xác nhận.');
    }

    function releasePreview() {
        if (previewObjectUrl) URL.revokeObjectURL(previewObjectUrl);
        previewObjectUrl = null;
    }

    function getReceiptId() {
        return Number(document.getElementById('StockDocumentId')?.value || 0);
    }

    function setCandidateContent(html) {
        const box = document.getElementById('inputInvoicePickerCandidates');
        if (box) box.innerHTML = html;
    }

    function setText(id, value) {
        const node = document.getElementById(id);
        if (node) node.textContent = value;
    }

    function announce(message, isError) {
        const node = document.getElementById('inputInvoicePickerLiveRegion');
        if (!node) return;
        node.textContent = message || '';
        node.className = `me-auto small ${isError ? 'text-danger' : 'text-muted'}`;
    }

    function setPageMessage(message, isError) {
        const node = document.getElementById('inputInvoicePickerPageMessage');
        if (!node) return;
        node.textContent = message || '';
        node.className = `small px-4 pb-3 ${isError ? 'text-danger' : 'text-success'}`;
    }

    async function readJson(response) {
        const text = await response.text();
        if (!text) return null;
        try { return JSON.parse(text); } catch { return { message: text }; }
    }

    async function responseMessage(response, fallback) {
        const data = await readJson(response);
        return data?.message || fallback;
    }

    function formatDate(value) {
        if (!value) return '-';
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? '-' : date.toLocaleDateString('vi-VN');
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;').replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;').replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }
}());
