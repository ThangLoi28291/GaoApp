(() => {
    'use strict';

    const root = document.getElementById('customerDepositPage');
    if (!root) return;

    const token = root.querySelector('[name="__RequestVerificationToken"]')?.value || '';
    const money = value => `${Number(value || 0).toLocaleString('vi-VN')} đ`;
    const uuid = () => {
        if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
        const bytes = crypto.getRandomValues(new Uint8Array(16));
        bytes[6] = bytes[6] & 15 | 64; bytes[8] = bytes[8] & 63 | 128;
        const hex = Array.from(bytes, item => item.toString(16).padStart(2, '0')).join('');
        return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;
    };

    root.querySelectorAll('[data-deposit-tab]').forEach(button => {
        button.addEventListener('click', () => {
            const name = button.dataset.depositTab;
            root.querySelectorAll('[data-deposit-tab]').forEach(item => {
                const active = item === button;
                item.classList.toggle('is-active', active);
                item.setAttribute('aria-selected', String(active));
            });
            root.querySelectorAll('[data-deposit-panel]').forEach(panel => {
                panel.hidden = panel.dataset.depositPanel !== name;
            });
        });
    });

    document.getElementById('depositPrint')?.addEventListener('click', () => window.print());

    const customerSearch = document.getElementById('depositCustomerSearch');
    const customerSuggestions = document.getElementById('depositCustomerSuggestions');
    const customerCombobox = customerSearch?.closest('[role="combobox"]');
    let customerSearchTimer = 0;
    let customerSearchRequest = null;
    let customerSearchSequence = 0;
    let customerResults = [];
    let activeCustomerResult = -1;

    const escapeHtml = value => String(value ?? '').replace(/[&<>'"]/g, character => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;'
    }[character]));

    function setCustomerSuggestions(html, open = true) {
        if (!customerSuggestions) return;
        customerSuggestions.innerHTML = html;
        customerSuggestions.hidden = !open;
        customerCombobox?.setAttribute('aria-expanded', String(open));
    }

    function closeCustomerSuggestions() {
        if (!customerSuggestions) return;
        customerSuggestions.hidden = true;
        customerCombobox?.setAttribute('aria-expanded', 'false');
        customerSearch?.removeAttribute('aria-activedescendant');
        activeCustomerResult = -1;
    }

    function renderCustomerState(icon, title, detail, className = '') {
        setCustomerSuggestions(`<div class="deposit-suggestion-state ${className}"><i class="bx ${icon}"></i><strong>${escapeHtml(title)}</strong><span>${escapeHtml(detail)}</span></div>`);
    }

    function renderCustomerResults(items) {
        customerResults = Array.isArray(items) ? items : [];
        activeCustomerResult = -1;
        customerSearch?.removeAttribute('aria-activedescendant');
        if (!customerResults.length) {
            renderCustomerState('bx-user-x', 'Không tìm thấy khách hàng', 'Thử lại bằng tên hoặc số điện thoại khác.');
            return;
        }
        setCustomerSuggestions(customerResults.map((item, index) => {
            const customerId = Number(item.customerId || item.id || 0);
            const name = String(item.name || 'Khách hàng');
            const phone = String(item.phone || 'Chưa có số điện thoại');
            const address = String(item.address || '');
            const initial = name.trim().charAt(0).toUpperCase() || 'K';
            return `<button type="button" id="depositCustomerOption${index}" class="deposit-customer-suggestion" role="option" aria-selected="false" data-customer-result data-customer-id="${customerId}">
                <span class="deposit-avatar">${escapeHtml(initial)}</span>
                <span class="deposit-customer__copy"><strong>${escapeHtml(name)}</strong><small>${escapeHtml(phone)}${address ? ` · ${escapeHtml(address)}` : ''}</small></span>
                <i class="bx bx-chevron-right"></i>
            </button>`;
        }).join(''));
    }

    function setActiveCustomerResult(index) {
        const options = Array.from(customerSuggestions?.querySelectorAll('[data-customer-result]') || []);
        if (!options.length) return;
        activeCustomerResult = (index + options.length) % options.length;
        options.forEach((option, optionIndex) => {
            const active = optionIndex === activeCustomerResult;
            option.classList.toggle('is-active', active);
            option.setAttribute('aria-selected', String(active));
        });
        const selected = options[activeCustomerResult];
        customerSearch?.setAttribute('aria-activedescendant', selected.id);
        selected.scrollIntoView({ block: 'nearest' });
    }

    function chooseCustomer(customerId) {
        if (!customerId) return;
        const url = new URL(location.href);
        url.search = '';
        url.searchParams.set('customerId', String(customerId));
        location.assign(url.toString());
    }

    async function searchDepositCustomers() {
        if (!customerSearch) return;
        const keyword = customerSearch.value.trim();
        if (keyword.length < 2) {
            customerSearchRequest?.abort();
            customerSearchRequest = null;
            customerResults = [];
            renderCustomerState('bx-keyboard', 'Nhập ít nhất 2 ký tự', 'Tìm theo tên hoặc số điện thoại khách hàng.');
            return;
        }

        customerSearchRequest?.abort();
        const controller = new AbortController();
        customerSearchRequest = controller;
        const sequence = ++customerSearchSequence;
        customerCombobox?.classList.add('is-loading');
        renderCustomerState('bx-loader-alt', `Đang tìm “${keyword}”`, 'Vui lòng chờ trong giây lát.', 'is-loading');
        try {
            const response = await fetch(`/admin/customer-deposit/customers/search?keyword=${encodeURIComponent(keyword)}&take=12`, {
                credentials: 'same-origin', signal: controller.signal, headers: { Accept: 'application/json' }
            });
            if (!response.ok) throw new Error('Không thể tìm khách hàng lúc này.');
            const items = await response.json();
            if (sequence === customerSearchSequence) renderCustomerResults(items);
        } catch (error) {
            if (error.name !== 'AbortError' && sequence === customerSearchSequence) {
                renderCustomerState('bx-error-circle', 'Tìm kiếm chưa thành công', error.message, 'is-error');
            }
        } finally {
            if (sequence === customerSearchSequence) {
                customerCombobox?.classList.remove('is-loading');
                customerSearchRequest = null;
            }
        }
    }

    if (customerSearch && customerSuggestions) {
        customerSearch.addEventListener('focus', () => {
            if (customerSearch.value.trim().length < 2) {
                renderCustomerState('bx-keyboard', 'Nhập ít nhất 2 ký tự', 'Tìm theo tên hoặc số điện thoại khách hàng.');
            } else if (customerSuggestions.innerHTML) {
                customerSuggestions.hidden = false;
                customerCombobox?.setAttribute('aria-expanded', 'true');
            }
        });
        customerSearch.addEventListener('input', () => {
            window.clearTimeout(customerSearchTimer);
            customerSearchTimer = window.setTimeout(searchDepositCustomers, 250);
        });
        customerSearch.addEventListener('keydown', event => {
            const options = customerSuggestions.querySelectorAll('[data-customer-result]');
            if (event.key === 'ArrowDown' && options.length) {
                event.preventDefault(); setActiveCustomerResult(activeCustomerResult + 1);
            } else if (event.key === 'ArrowUp' && options.length) {
                event.preventDefault(); setActiveCustomerResult(activeCustomerResult - 1);
            } else if (event.key === 'Enter') {
                event.preventDefault();
                const selected = options[activeCustomerResult];
                if (selected) chooseCustomer(Number(selected.dataset.customerId));
                else { window.clearTimeout(customerSearchTimer); void searchDepositCustomers(); }
            } else if (event.key === 'Escape') {
                closeCustomerSuggestions();
            }
        });
        customerSuggestions.addEventListener('click', event => {
            const option = event.target.closest('[data-customer-result]');
            if (option) chooseCustomer(Number(option.dataset.customerId));
        });
        document.addEventListener('pointerdown', event => {
            if (!event.target.closest('.deposit-customer-searchbox')) closeCustomerSuggestions();
        });
        document.addEventListener('keydown', event => {
            if (event.key === 'F3') {
                event.preventDefault(); customerSearch.focus(); customerSearch.select();
            }
        });
    }

    const receiveForm = document.getElementById('depositReceive');
    let receiveIntentId = uuid();
    let qrSignature = '';
    let qrPromise = null;

    function hideDepositDisplay() {
        return fetch('/admin/customer-deposit/display/hide', {
            method: 'POST', credentials: 'same-origin',
            headers: { RequestVerificationToken: token }
        }).catch(() => {});
    }

    function resetReceiveQr(notifyDisplay) {
        if (!receiveForm) return;
        const hadQr = !!receiveForm.elements.reference.value;
        receiveForm.elements.reference.value = '';
        qrSignature = '';
        const box = document.getElementById('depositQrPreview');
        if (box) box.hidden = true;
        const content = box?.querySelector('.deposit-qr-preview__content');
        const loading = box?.querySelector('.deposit-qr-preview__loading');
        const qrButton = document.getElementById('depositCreateQr');
        if (content) content.hidden = true;
        if (loading) loading.hidden = false;
        if (qrButton && !qrPromise) qrButton.querySelector('span').textContent = 'Tạo mã QR để khách quét';
        if (hadQr && notifyDisplay) void hideDepositDisplay();
    }

    function setReceiveMethodPresentation() {
        if (!receiveForm) return;
        const method = receiveForm.elements.method.value;
        receiveForm.querySelectorAll('.deposit-methods label').forEach(label => {
            label.classList.toggle('is-selected', label.querySelector('input')?.checked === true);
        });
        const qrBox = document.getElementById('depositQrPreview');
        const qrButton = document.getElementById('depositCreateQr');
        if (qrButton) {
            qrButton.hidden = method !== '1';
            const amount = Number(receiveForm.elements.amount.value);
            qrButton.disabled = !Number.isInteger(amount) || amount <= 0 || !!qrPromise;
        }
        if (qrBox) qrBox.hidden = method !== '1' || !receiveForm.elements.reference.value;
        if (method !== '1') resetReceiveQr(true);
    }

    async function createReceiveQr() {
        if (!receiveForm || receiveForm.elements.method.value !== '1') return null;
        const amount = Number(receiveForm.elements.amount.value);
        const customerId = Number(root.dataset.customerId);
        if (!Number.isInteger(amount) || amount <= 0 || !customerId) {
            throw new Error('Nhập số tiền cọc hợp lệ để tạo mã QR.');
        }
        const signature = `${receiveIntentId}:${customerId}:${amount}`;
        if (qrSignature === signature && receiveForm.elements.reference.value) return null;
        if (qrPromise && qrSignature === signature) return qrPromise;

        qrSignature = signature;
        const box = document.getElementById('depositQrPreview');
        const loading = box?.querySelector('.deposit-qr-preview__loading');
        const content = box?.querySelector('.deposit-qr-preview__content');
        const qrButton = document.getElementById('depositCreateQr');
        if (box) box.hidden = false;
        if (loading) loading.hidden = false;
        if (content) content.hidden = true;
        if (qrButton) { qrButton.disabled = true; qrButton.querySelector('span').textContent = 'Đang tạo mã QR...'; }

        qrPromise = fetch('/admin/customer-deposit/receive-qr', {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
            body: JSON.stringify({ clientRequestId: receiveIntentId, customerId, amount })
        }).then(async response => {
            const data = await response.json();
            if (!response.ok) throw new Error(data.message || data.detail || data.title || 'Không thể tạo mã QR đặt cọc.');
            receiveForm.elements.reference.value = data.content;
            document.getElementById('depositQrImage').src = data.qrDataUrl;
            document.getElementById('depositQrAmount').textContent = money(data.amount);
            document.getElementById('depositQrBank').textContent = `${data.bankName} · ${data.accountNumber} · ${data.accountName}`;
            document.getElementById('depositQrContent').textContent = data.content;
            if (loading) loading.hidden = true;
            if (content) content.hidden = false;
            return data;
        }).catch(error => {
            qrSignature = '';
            receiveForm.elements.reference.value = '';
            if (loading) loading.hidden = true;
            const message = receiveForm.querySelector('.deposit-message');
            if (message) message.textContent = error.message;
            throw error;
        }).finally(() => {
            qrPromise = null;
            if (qrButton) {
                qrButton.disabled = false;
                qrButton.querySelector('span').textContent = receiveForm.elements.reference.value ? 'Mã QR đã sẵn sàng' : 'Tạo mã QR để khách quét';
            }
        });
        return qrPromise;
    }

    if (receiveForm) {
        receiveForm.querySelectorAll('[name="method"]').forEach(radio => radio.addEventListener('change', () => {
            setReceiveMethodPresentation();
        }));
        receiveForm.elements.amount.addEventListener('input', () => {
            resetReceiveQr(true);
            setReceiveMethodPresentation();
        });
        document.getElementById('depositCreateQr')?.addEventListener('click', () => createReceiveQr().catch(() => {}));
        receiveForm.addEventListener('keydown', event => {
            if (event.ctrlKey && event.key === 'Enter') {
                event.preventDefault();
                receiveForm.requestSubmit();
            }
        });
        setReceiveMethodPresentation();
    }

    root.querySelectorAll('form[data-action]').forEach(form => {
        const action = form.dataset.action;
        const key = `deposit:${location.host}:${root.dataset.customerId}:${action}`;
        const message = form.querySelector('.deposit-message');
        const button = form.querySelector('button[type="submit"]');
        const pending = sessionStorage.getItem(key);
        if (pending) {
            form.noValidate = true;
            message.textContent = 'Có giao dịch chưa rõ kết quả. Xác nhận lại để hệ thống đối soát đúng lần giao dịch trước.';
        }

        if (action === 'refund') {
            const method = form.elements.method;
            const syncReference = () => { form.elements.reference.required = method.value === '1'; };
            method.addEventListener('change', syncReference);
            syncReference();
        }

        let busy = false;
        form.addEventListener('submit', async event => {
            event.preventDefault();
            if (busy) return;
            busy = true;
            button.disabled = true;
            message.textContent = '';
            try {
                const saved = sessionStorage.getItem(key);
                if (!saved && action === 'receive' && form.elements.method.value === '1') {
                    if (!form.elements.reference.value) throw new Error('Bấm “Tạo mã QR để khách quét” trước khi xác nhận nhận cọc.');
                }
                const fields = Object.fromEntries(new FormData(form));
                const body = saved ? JSON.parse(saved) : {
                    clientRequestId: action === 'receive' ? receiveIntentId : uuid(),
                    customerId: Number(root.dataset.customerId),
                    depositId: Number(fields.depositId) || undefined,
                    amount: Number(fields.amount),
                    method: Number(fields.method),
                    purpose: fields.purpose,
                    expectedDeliveryDate: fields.expectedDeliveryDate || null,
                    reference: fields.reference || null,
                    note: fields.note || null
                };
                sessionStorage.setItem(key, JSON.stringify(body));
                const response = await fetch(`/admin/customer-deposit/${action}`, {
                    method: 'POST', credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
                    body: JSON.stringify(body)
                });
                const data = await response.json();
                if (!response.ok) {
                    if ([400, 403, 422].includes(response.status)) {
                        sessionStorage.removeItem(key);
                        form.noValidate = false;
                    }
                    throw new Error(data.message || data.detail || data.title || 'Không thể xử lý tiền cọc.');
                }
                sessionStorage.removeItem(key);
                location.reload();
            } catch (error) {
                message.textContent = error.message;
            } finally {
                busy = false;
                button.disabled = false;
            }
        });
    });
})();
