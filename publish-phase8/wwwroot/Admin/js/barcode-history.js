(function () {
    if (!window.barcodeHistoryPage) return;

    const state = {
        page: window.barcodeHistoryPage.page || 1,
        pageSize: window.barcodeHistoryPage.pageSize || 20,
        totalPages: window.barcodeHistoryPage.totalPages || 1,
        loading: false,
        keyword: '',
        actionType: '',
        fromUtc: '',
        toUtc: ''
    };

    const listEl = document.getElementById('barcodeHistoryList');
    const loadingEl = document.getElementById('barcodeHistoryLoading');
    const endEl = document.getElementById('barcodeHistoryEnd');
    const summaryEl = document.getElementById('barcodeHistorySummary');
    const filterForm = document.getElementById('barcodeHistoryFilterForm');
    const resetBtn = document.getElementById('btnResetFilter');

    if (!listEl || !filterForm) return;

    function setLoading(isLoading) {
        state.loading = isLoading;
        loadingEl.classList.toggle('d-none', !isLoading);
    }

    function updateEndState() {
        const hasMore = state.page < state.totalPages;
        endEl.classList.toggle('d-none', hasMore);
    }

    function buildQuery(page) {
        const params = new URLSearchParams();
        params.set('Page', page);
        params.set('PageSize', state.pageSize);

        if (state.keyword) params.set('Keyword', state.keyword);
        if (state.actionType) params.set('ActionType', state.actionType);
        if (state.fromUtc) params.set('FromUtc', state.fromUtc);
        if (state.toUtc) params.set('ToUtc', state.toUtc);

        return params.toString();
    }

    function escapeHtml(text) {
        if (text === null || text === undefined) return '';
        return String(text)
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    function formatDate(value) {
        if (!value) return '';
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return value;
        return date.toLocaleString('vi-VN');
    }

    function renderItems(items) {
        return items.map(item => `
            <div class="barcode-history-item border-bottom py-3 px-3">
                <div class="d-flex justify-content-between gap-3">
                    <div class="flex-grow-1">
                        <div class="fw-semibold">
                            ${escapeHtml(item.productName || '')}
                            ${item.unitName ? `<span class="text-muted">- ${escapeHtml(item.unitName)}</span>` : ''}
                        </div>

                        <div class="small text-muted mt-1">
                            SKU: ${escapeHtml(item.variantSku || '-')}
                        </div>

                        <div class="mt-2">
                            <span class="badge bg-light text-dark border">${escapeHtml(item.actionType || '')}</span>
                        </div>

                        <div class="mt-2 small">
                            <div><strong>Mã cũ:</strong> ${escapeHtml(item.oldBarcode || '-')}</div>
                            <div><strong>Mã mới:</strong> ${escapeHtml(item.newBarcode || '-')}</div>
                        </div>

                        ${item.reason ? `
                            <div class="mt-2 small text-muted">
                                <strong>Lý do:</strong> ${escapeHtml(item.reason)}
                            </div>` : ''}

                        <div class="mt-2 small text-muted">
                            Người đổi: ${escapeHtml(item.changedByUserName || '-')}
                        </div>
                    </div>

                    <div class="text-muted small text-nowrap">
                        ${formatDate(item.changedAtUtc)}
                    </div>
                </div>
            </div>
        `).join('');
    }

    async function loadPage(page, append) {
        if (state.loading) return;

        setLoading(true);

        try {
            const url = `${window.barcodeHistoryPage.apiUrl}?${buildQuery(page)}`;
            const response = await fetch(url, {
                method: 'GET',
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });

            if (!response.ok) {
                throw new Error('Không thể tải lịch sử barcode.');
            }

            const json = await response.json();
            const data = json.data;

            state.page = data.page;
            state.pageSize = data.pageSize;
            state.totalPages = data.totalPages;

            summaryEl.textContent = `Tổng: ${data.totalItems}`;

            const html = renderItems(data.items || []);

            if (append) {
                listEl.insertAdjacentHTML('beforeend', html);
            } else {
                listEl.innerHTML = html || `<div class="text-center py-4 text-muted">Chưa có dữ liệu lịch sử barcode.</div>`;
            }

            updateEndState();
        } catch (error) {
            console.error(error);
        } finally {
            setLoading(false);
        }
    }

    function readFilter() {
        const formData = new FormData(filterForm);
        state.keyword = (formData.get('Keyword') || '').toString().trim();
        state.actionType = (formData.get('ActionType') || '').toString().trim();
        state.fromUtc = (formData.get('FromUtc') || '').toString().trim();
        state.toUtc = (formData.get('ToUtc') || '').toString().trim();
    }

    filterForm.addEventListener('submit', async function (e) {
        e.preventDefault();
        readFilter();
        await loadPage(1, false);
    });

    resetBtn.addEventListener('click', async function () {
        filterForm.reset();
        state.keyword = '';
        state.actionType = '';
        state.fromUtc = '';
        state.toUtc = '';
        await loadPage(1, false);
    });

    listEl.addEventListener('scroll', async function () {
        const nearBottom = listEl.scrollTop + listEl.clientHeight >= listEl.scrollHeight - 120;
        if (!nearBottom) return;
        if (state.loading) return;
        if (state.page >= state.totalPages) return;

        await loadPage(state.page + 1, true);
    });

    updateEndState();
})();