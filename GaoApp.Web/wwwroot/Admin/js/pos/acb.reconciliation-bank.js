(() => {
    'use strict';
    const filters = document.getElementById('acb-report-filter');
    const form = document.getElementById('acb-bank-lookup-form');
    const button = document.getElementById('acb-bank-lookup');
    const results = document.getElementById('acb-bank-results');
    if (!filters || !form || !button || !results) return;
    let abort;
    let requestId = 0;
    let query;

    function message(text, error = false) {
        results.replaceChildren();
        const block = document.createElement('p');
        block.className = error ? 'acb-bank-message acb-error' : 'acb-bank-message';
        block.setAttribute('role', error ? 'alert' : 'status');
        block.textContent = text;
        results.append(block);
    }

    function busy(value) {
        button.disabled = value;
        button.textContent = value ? 'Đang tra cứu ACB…' : 'Tra cứu ACB';
        results.setAttribute('aria-busy', String(value));
    }

    async function lookup(page, dates) {
        if (!filters.reportValidity()) return;
        const from = dates?.from ?? filters.elements.from.value;
        const to = dates?.to ?? filters.elements.to.value;
        const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (!token) { message('Phiên làm việc chưa sẵn sàng. Hãy tải lại trang.', true); return; }
        abort?.abort();
        abort = new AbortController();
        const controller = abort;
        const id = ++requestId;
        const timeout = setTimeout(() => controller.abort(), 50000);
        query = { from, to };
        busy(true);
        message(`Đang lấy giao dịch từ ACB (${from} → ${to})…`);
        try {
            const response = await fetch(form.action, {
                method: 'POST', credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
                headers: { 'RequestVerificationToken': token },
                body: new URLSearchParams({ from, to, page: String(page) })
            });
            if (id !== requestId) return;
            if (response.redirected || response.status === 401) throw new Error('Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.');
            if (response.status === 403) throw new Error('Bạn không có quyền tra cứu giao dịch ACB.');
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw new Error(error?.detail || 'Chưa lấy được dữ liệu ACB. Hãy tải lại trang và thử lại.');
            }
            if (response.headers.get('X-Acb-Data-Source') !== 'bank-lookup') throw new Error('Phản hồi tra cứu không hợp lệ. Hãy tải lại trang.');
            const html = await response.text();
            if (id === requestId) results.innerHTML = html;
        } catch (error) {
            if (id === requestId) message(error.name === 'AbortError' ? 'Tra cứu ACB quá thời gian chờ. Hãy thử lại.' : error.message, true);
        } finally {
            clearTimeout(timeout);
            if (id === requestId) busy(false);
        }
    }

    button.addEventListener('click', () => lookup(1));
    results.addEventListener('click', event => {
        const target = event.target.closest('[data-bank-page], [data-bank-jump]');
        if (!target || !results.contains(target) || button.disabled) return;
        const input = results.querySelector('[data-bank-page-input]');
        if (target.hasAttribute('data-bank-jump') && (!input || !input.reportValidity())) return;
        const page = Number(target.dataset.bankPage ?? input.value);
        if (Number.isSafeInteger(page) && page > 0) lookup(page, query);
    });
    results.addEventListener('keydown', event => {
        if (event.key === 'Enter' && event.target.matches('[data-bank-page-input]')) {
            event.preventDefault();
            results.querySelector('[data-bank-jump]')?.click();
        }
    });
    for (const field of [filters.elements.from, filters.elements.to]) {
        field.addEventListener('change', () => {
            ++requestId;
            abort?.abort();
            busy(false);
            query = undefined;
            message('Đã đổi khoảng ngày. Bấm “Tra cứu ACB” để lấy kết quả mới.');
        });
    }
})();
