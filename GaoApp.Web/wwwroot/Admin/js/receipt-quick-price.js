(() => {
    'use strict';
    let active;
    const find = (panel, name) => panel.querySelector(`[data-qprice-${name}]`);
    const decimal = value => {
        const match = String(value ?? '').trim().match(/^(\d+)(?:\.(\d{1,9}))?$/);
        if (!match || match[1].length > 30) return null;
        return { n: BigInt(match[1] + (match[2] || '')), d: 10n ** BigInt((match[2] || '').length) };
    };
    function moneyCents(value) {
        let text = String(value ?? '').trim().replace(/\s/g, '');
        if (text.includes(',')) {
            if (!/^\d+(?:\.\d{3})*(?:,\d{1,2})?$/.test(text)) return null;
            text = text.replace(/\./g, '').replace(',', '.');
        } else if (/^\d{1,3}(?:\.\d{3})+$/.test(text)) text = text.replace(/\./g, '');
        if (!/^\d+(?:\.\d{1,2})?$/.test(text)) return null;
        const valueParts = decimal(text);
        return valueParts ? valueParts.n * 100n / valueParts.d : null;
    }
    const round = (n, d) => n / d + (2n * (n % d) >= d ? 1n : 0n);
    function decimalText(n, scale) {
        const digits = n.toString().padStart(scale + 1, '0');
        if (!scale) return digits;
        const fraction = digits.slice(-scale).replace(/0+$/, '');
        return digits.slice(0, -scale) + (fraction ? '.' + fraction : '');
    }
    function formatted(n, scale = 2) {
        const divisor = 10n ** BigInt(scale);
        const whole = (n / divisor).toString().replace(/\B(?=(\d{3})+(?!\d))/g, '.');
        const fraction = (n % divisor).toString().padStart(scale, '0').replace(/0+$/, '');
        return whole + (fraction ? ',' + fraction : '');
    }
    const quantityText = (n, d) => formatted(round(n * 1_000_000_000n, d), 9);
    function unitFactor(select) {
        const option = select?.options[select.selectedIndex], factor = decimal(option?.dataset.factor);
        return factor?.n > 0n ? factor : null;
    }
    function editable(row, input) {
        return !!row?.isConnected && !!input?.isConnected && !input.disabled && !input.readOnly &&
            row.closest('#commercialApprovalWorkbench')?.dataset.readonly !== 'true' &&
            window.stockDocumentPage?.canApproveCommercial === true;
    }
    function syncLauncher(row) {
        const button = row?.querySelector('[data-qprice-open]'); if (!button) return;
        button.disabled = !row.querySelector('[data-qprice-panel]') || !editable(row, row.querySelector('.commercial-unit-price'));
    }
    function initLaunchers() {
        document.querySelectorAll('#commercialApprovalWorkbench .commercial-line').forEach(syncLauncher);
        new MutationObserver(records => {
            for (const record of records) {
                if (record.type === 'attributes') {
                    if (record.target.matches('.commercial-unit-price')) syncLauncher(record.target.closest('.commercial-line'));
                    continue;
                }
                for (const node of record.addedNodes) {
                    if (node.nodeType !== 1) continue;
                    if (node.matches('#commercialApprovalWorkbench, .commercial-line')) {
                        if (node.matches('.commercial-line')) syncLauncher(node);
                        node.querySelectorAll('.commercial-line').forEach(syncLauncher);
                    } else if (node.querySelector('#commercialApprovalWorkbench, .commercial-line')) {
                        node.querySelectorAll('.commercial-line').forEach(syncLauncher);
                    }
                }
            }
        }).observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['disabled', 'readonly'] });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initLaunchers, { once: true });
    else initLaunchers();
    function displayError(panel, message) {
        const error = find(panel, 'error'); error.textContent = message; error.hidden = !message;
        find(panel, 'results').hidden = !!message;
        find(panel, 'approve').disabled = !!message;
    }
    function calculate(panel) {
        const buy = decimal(find(panel, 'buy').value), giftEnabled = find(panel, 'has-gift').checked;
        const gift = giftEnabled ? decimal(find(panel, 'gift').value) : { n: 0n, d: 1n };
        const paid = moneyCents(find(panel, 'money').value), buyFactor = unitFactor(find(panel, 'buy-unit'));
        const giftFactor = giftEnabled ? unitFactor(find(panel, 'gift-unit')) : { n: 1n, d: 1n };
        const receiptFactor = decimal(panel.dataset.qpriceFactor);
        if (!buy || buy.n <= 0n) throw new Error('Nhập số lượng mua lớn hơn 0, tối đa 9 số lẻ.');
        if (!gift) throw new Error('Số lượng tặng phải từ 0 trở lên, tối đa 9 số lẻ.');
        if (!buyFactor || !giftFactor || !receiptFactor || receiptFactor.n <= 0n) throw new Error('Chọn đơn vị có quy đổi hợp lệ của sản phẩm này.');
        if (paid === null || paid <= 0n || paid > 9_999_999_999_999_999n) throw new Error('Nhập tổng tiền trước VAT lớn hơn 0, tối đa 2 số lẻ và không vượt giới hạn giá nhập.');
        const buyN = buy.n * buyFactor.n, buyD = buy.d * buyFactor.d;
        const giftN = gift.n * giftFactor.n, giftD = gift.d * giftFactor.d;
        const totalN = buyN * giftD + giftN * buyD, totalD = buyD * giftD;
        const price = round(paid * totalD * receiptFactor.n, totalN * receiptFactor.d);
        if (price <= 0n || price > 9_999_999_999_999_999n) throw new Error('Giá tính được nằm ngoài giới hạn giá nhập. Kiểm tra lại số lượng, đơn vị và tổng tiền.');
        const basePrice = round(paid * totalD, totalN);
        return { price, basePrice, totalN, totalD, receiptFactor };
    }
    function render(panel) {
        find(panel, 'gift-fields').hidden = !find(panel, 'has-gift').checked;
        try {
            const value = calculate(panel), receiptName = panel.dataset.qpriceUnitName || 'đơn vị thực nhận';
            const baseName = panel.dataset.qpriceBaseUnitName || 'đơn vị gốc';
            find(panel, 'total').textContent = `${quantityText(value.totalN * value.receiptFactor.d, value.totalD * value.receiptFactor.n)} ${receiptName}` +
                (value.receiptFactor.n !== value.receiptFactor.d ? ` · ${quantityText(value.totalN, value.totalD)} ${baseName}` : '');
            find(panel, 'result').textContent = `${formatted(value.price)} đ/${receiptName}`;
            find(panel, 'base').textContent = `${formatted(value.basePrice)} đ/${baseName}`;
            find(panel, 'base').hidden = value.receiptFactor.n === value.receiptFactor.d;
            displayError(panel, '');
        } catch (error) { displayError(panel, error.message); }
    }
    function close(focus = true) {
        if (!active) return;
        const previous = active; active = null; previous.panel.hidden = true;
        previous.button.setAttribute('aria-expanded', 'false');
        if (focus && previous.button.isConnected) previous.button.focus();
    }
    function open(button) {
        const row = button.closest('.commercial-line'), panel = row?.querySelector('[data-qprice-panel]');
        const input = row?.querySelector('.commercial-unit-price');
        if (!panel || !editable(row, input)) return;
        if (active?.panel === panel) { close(); return; }
        close(false);
        active = { row, panel, input, button, workbench: row.closest('#commercialApprovalWorkbench'),
            lineId: row.dataset.lineId, quantity: row.dataset.quantity, factor: row.dataset.factor,
            unitId: panel.dataset.qpriceUnitId, receiptFactor: panel.dataset.qpriceFactor,
            price: input.value };
        panel.hidden = false; button.setAttribute('aria-expanded', 'true'); render(panel);
        find(panel, 'buy').focus(); find(panel, 'buy').select();
    }
    function approve(panel) {
        if (!active || active.panel !== panel) return;
        const state = active;
        if (!editable(state.row, state.input) || state.workbench !== document.getElementById('commercialApprovalWorkbench') ||
            state.row.querySelector('.commercial-unit-price') !== state.input || state.row.dataset.lineId !== state.lineId ||
            state.row.dataset.quantity !== state.quantity || state.row.dataset.factor !== state.factor ||
            panel.dataset.qpriceUnitId !== state.unitId || panel.dataset.qpriceFactor !== state.receiptFactor ||
            state.input.value !== state.price) {
            displayError(panel, 'Dòng hàng hoặc giá nhập đã thay đổi. Đóng Tính nhanh, kiểm tra dòng hiện tại rồi mở lại trước khi điền giá.');
            return;
        }
        let value;
        try { value = calculate(panel); } catch (error) { displayError(panel, error.message); return; }
        state.input.value = decimalText(value.price, 2);
        // Reuse the manual input's totals, validation and price-draft autosave pipeline.
        state.input.dispatchEvent(new Event('input', { bubbles: true }));
        close(false); state.input.focus(); state.input.select();
    }
    document.addEventListener('click', event => {
        const openButton = event.target.closest('[data-qprice-open]');
        if (openButton) { event.preventDefault(); open(openButton); return; }
        const panel = event.target.closest('[data-qprice-panel]'); if (!panel) return;
        if (event.target.closest('[data-qprice-cancel], [data-qprice-close]')) { event.preventDefault(); close(); }
        else if (event.target.closest('[data-qprice-approve]')) { event.preventDefault(); approve(panel); }
    });
    for (const eventName of ['input', 'change']) document.addEventListener(eventName, event => {
        const panel = event.target.closest('[data-qprice-panel]');
        if (panel && active?.panel === panel) render(panel);
    });
    document.addEventListener('focusin', event => {
        if (event.target.closest('[data-qprice-panel]') && event.target.tagName === 'INPUT' && event.target.type !== 'checkbox') event.target.select();
    });
    document.addEventListener('focusout', event => {
        const panel = event.target.closest('[data-qprice-panel]');
        if (panel && event.target.matches('[data-qprice-money]')) {
            const value = moneyCents(event.target.value); if (value !== null) event.target.value = formatted(value);
        }
    });
    document.addEventListener('keydown', event => {
        const panel = event.target.closest('[data-qprice-panel]'); if (!panel || event.isComposing) return;
        if (event.key === 'Escape') { event.preventDefault(); event.stopImmediatePropagation(); close(); }
        else if (event.key === 'Enter') {
            event.preventDefault(); event.stopImmediatePropagation();
            if (event.target.closest('[data-qprice-cancel], [data-qprice-close]')) close();
            else approve(panel);
        }
    }, true);
})();
