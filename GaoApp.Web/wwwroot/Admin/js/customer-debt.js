(() => {
    'use strict';
    const root = document.getElementById('customerDebtPage');
    const form = document.getElementById('debtCollectForm');
    document.getElementById('debtPrint')?.addEventListener('click', () => window.print());
    if (!root) return;
    document.getElementById('debtReportFilters')?.addEventListener('submit', event => {
        const filters = event.currentTarget;
        if (filters.elements.from.value > filters.elements.to.value) {
            event.preventDefault();
            document.getElementById('debtReportError').textContent = 'Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.';
        }
    });
    const tabs = root.querySelectorAll('[data-debt-tab]');
    const panels = root.querySelectorAll('[data-debt-panel]');
    function selectSection(name) {
        tabs.forEach(tab => { const active = tab.dataset.debtTab === name; tab.classList.toggle('is-active', active); tab.setAttribute('aria-pressed', String(active)); });
        panels.forEach(panel => { panel.hidden = panel.dataset.debtPanel !== name; });
    }
    tabs.forEach(tab => tab.addEventListener('click', () => selectSection(tab.dataset.debtTab)));
    if (tabs.length) selectSection('orders');
    if (!form) return;
    const get = id => document.getElementById(id);
    const customerId = Number(root.dataset.customerId);
    const key = 'customer-debt:collection:' + location.host + ':' + customerId;
    let busy = false;
    function requestId() {
        if (crypto.randomUUID) return crypto.randomUUID();
        const bytes = crypto.getRandomValues(new Uint8Array(16));
        bytes[6] = (bytes[6] & 15) | 64;
        bytes[8] = (bytes[8] & 63) | 128;
        const hex = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }
    get('debtMethod').addEventListener('change', () => {
        const bank = get('debtMethod').value === '1';
        get('debtBank').disabled = !bank;
        get('debtBank').required = bank;
        get('debtReference').required = bank;
    });
    if (sessionStorage.getItem(key)) {
        const pending = JSON.parse(sessionStorage.getItem(key));
        get('debtAmount').value = pending.amount;
        get('debtMethod').value = pending.method;
        get('debtOrder').value = pending.orderId || '';
        get('debtBank').value = pending.storeBankAccountId || '';
        get('debtReference').value = pending.reference || '';
        get('debtNote').value = pending.note || '';
        form.noValidate = true; // The retained request, not these fields, is replayed.
        get('debtMessage').textContent = 'Có lần thu chưa rõ kết quả. Bấm xác nhận để đối soát lại đúng lần thu đó.';
    }
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy) return;
        busy = true;
        get('debtSubmit').disabled = true;
        try {
            const pending = sessionStorage.getItem(key);
            const body = pending ? JSON.parse(pending) : {
                clientRequestId: requestId(), customerId,
                orderId: Number(get('debtOrder').value) || null,
                amount: Number(get('debtAmount').value), method: Number(get('debtMethod').value),
                storeBankAccountId: get('debtMethod').value === '1' ? Number(get('debtBank').value) || null : null,
                reference: get('debtReference').value.trim() || null, note: get('debtNote').value.trim() || null
            };
            sessionStorage.setItem(key, JSON.stringify(body));
            const response = await fetch('/admin/customer-debt/collect', {
                method: 'POST', credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', RequestVerificationToken: root.querySelector('[name="__RequestVerificationToken"]').value },
                body: JSON.stringify(body)
            });
            const result = await response.json();
            if (!response.ok) {
                if ([400, 403, 422].includes(response.status)) sessionStorage.removeItem(key);
                throw new Error(result.message || result.detail || result.title || 'Không thể thu nợ. Kiểm tra ca đang mở và số dư.');
            }
            sessionStorage.removeItem(key);
            location.reload();
        } catch (error) { get('debtMessage').textContent = error.message; }
        finally { busy = false; get('debtSubmit').disabled = false; }
    });
})();
