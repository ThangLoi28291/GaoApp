(() => {
    'use strict';
    const rowInput = '.js-receiving-qty,.js-inline-line-qty,[data-intake-quantity]';
    const quantityInput = rowInput + ',#riQuantity,#popupQuantity,#popupQuickQty,#quickEditQty,#editLineQuantity,#provisionalQuantity';
    const containers = [], queue = [];
    let selected = '', busy = false, failed = false, focusRevision = 0;
    const key = input => input.dataset.intakeQuantity ? `intake:${input.dataset.intakeQuantity}` : `line:${input.dataset.lineId}`;
    const inputs = () => containers.flatMap(x => [...x.element.querySelectorAll(rowInput)]);
    const selectedInput = () => inputs().find(input => key(input) === selected);
    const nextQuantity = (input, delta) => Math.max(1, Math.round((Number(input.value || 0) + delta) * 1000) / 1000);
    function render() {
        if (failed && inputs().every(x => Number(x.value) === Number(x.defaultValue))) {
            failed = false; containers.forEach(x => x.message.textContent = '');
        }
        for (const group of containers) {
            const editable = [...group.element.querySelectorAll(rowInput)].filter(x => !x.readOnly);
            group.toolbar.hidden = !editable.length;
            for (const input of editable) {
                const row = input.closest('tr');
                row.tabIndex = 0;
                row.classList.toggle('rq-selected', key(input) === selected);
                if (key(input) !== selected) row.classList.remove('sd-line-row-editing');
            }
            const input = editable.find(x => key(x) === selected);
            const label = input?.closest('tr').querySelector('.wrd-line-name,.sd-product-name,strong,.fw-semibold');
            group.name.textContent = input ? label?.textContent.trim() || 'Đã chọn sản phẩm' : 'Chọn dòng sản phẩm';
            group.minus.disabled = !input || Number(input.value) <= 1;
            group.plus.disabled = !input;
        }
    }
    function select(input, focus) {
        if (!input || input.readOnly || (input.disabled && !busy)) return;
        selected = key(input); render();
        if (focus) {
            window.jQuery?.('#quickLookupInput').select2('close');
            input.closest('tr').focus({preventScroll:true});
        }
    }
    function enqueue(delta) {
        const input = selectedInput();
        if (!input || input.readOnly || (!busy && (input.disabled || window.ReceiptIntake?.isSaving()))) return;
        queue.push({key:selected,delta});
        if (!busy) void flush();
    }
    async function flush() {
        busy = true; failed = false;
        try {
            while (queue.length) {
                const operation = queue.shift();
                const input = inputs().find(x => key(x) === operation.key);
                if (!input || input.readOnly || input.disabled) throw new Error('Dòng hàng vừa thay đổi. Hãy chọn lại sản phẩm.');
                const next = nextQuantity(input, operation.delta);
                if (next === Number(input.value)) continue;
                const revision = focusRevision;
                input.dataset.receiptStepSaving = '1';
                input.value = String(next);
                if (document.activeElement === input) input.closest('tr').focus({preventScroll:true});
                let saved;
                try {
                    if (input.dataset.intakeQuantity) saved = await window.ReceiptIntake.saveQuantity(input);
                    else if (input.classList.contains('js-receiving-qty')) saved = await window.saveReceivingQty(input);
                    else saved = await window.saveInlineLineQuantity(input);
                } finally { delete input.dataset.receiptStepSaving; }
                if (saved === false) throw new Error('Chưa lưu được số lượng. Kiểm tra dòng hàng và thử lại.');
                render();
                // Refreshed widgets can claim focus. Restore the row unless the user deliberately moved elsewhere.
                if (revision === focusRevision && selected === operation.key && !document.querySelector('.modal.show')) {
                    window.jQuery?.('#quickLookupInput').select2('close');
                    selectedInput()?.closest('tr').focus({preventScroll:true});
                }
            }
            containers.forEach(x => x.message.textContent = '');
        } catch (error) {
            queue.length = 0; failed = true;
            containers.forEach(x => x.message.textContent = error.message || 'Không lưu được số lượng.');
        } finally { busy = false; render(); document.dispatchEvent(new Event('receipt-quantity:idle')); }
    }
    function start() {
        for (const id of ['wrdLinesContainer','stockDocumentLinesContainer']) {
            const element = document.getElementById(id); if (!element) continue;
            const toolbar = document.createElement('div'); toolbar.className = 'rq-toolbar';
            toolbar.innerHTML = '<div class="rq-hint"><strong></strong><small>Phím + / −: tăng / giảm 1</small></div><div class="rq-buttons" role="group" aria-label="Điều chỉnh số lượng dòng đã chọn"><button type="button" class="btn btn-sm btn-outline-secondary" data-quantity-step="-1" aria-label="Giảm 1 đơn vị">−</button><button type="button" class="btn btn-sm btn-outline-primary" data-quantity-step="1" aria-label="Tăng 1 đơn vị">+</button></div><span class="rq-message text-danger" role="status"></span>';
            element.before(toolbar);
            const group = {element,toolbar,name:toolbar.querySelector('strong'),minus:toolbar.querySelector('[data-quantity-step="-1"]'),plus:toolbar.querySelector('[data-quantity-step="1"]'),message:toolbar.querySelector('.rq-message')};
            containers.push(group);
            toolbar.addEventListener('pointerdown',event=>{if(event.target.closest('button'))event.preventDefault();});
            toolbar.addEventListener('click',event=>{const button=event.target.closest('[data-quantity-step]');if(button)enqueue(Number(button.dataset.quantityStep));});
            element.addEventListener('click',event=>{
                if (event.target.closest('button,a,select,textarea,input,[contenteditable="true"]')) return;
                select(event.target.closest('tr')?.querySelector(rowInput),true);
            });
            element.addEventListener('focusin',event=>{
                if (event.target.matches(rowInput)) select(event.target,false);
                else if (event.target.matches('tr')) select(event.target.querySelector(rowInput),false);
            });
            new MutationObserver(render).observe(element,{childList:true,subtree:true});
        }
        if (!containers.length) return;
        document.addEventListener('pointerdown',event=>{if(!event.target.closest('.rq-toolbar'))focusRevision++;},true);
        document.addEventListener('keydown',event=>{
            if (event.key === 'Tab') focusRevision++;
            if (event.ctrlKey || event.altKey || event.metaKey || event.isComposing) return;
            const field = event.target.closest('input,textarea,select,[contenteditable="true"]');
            if (field?.matches(quantityInput) && !field.disabled && !field.readOnly && ['ArrowUp','ArrowDown'].includes(event.key)) {
                event.preventDefault(); event.stopImmediatePropagation();
                field.value = String(nextQuantity(field,event.key === 'ArrowUp' ? 1:-1));
                field.dispatchEvent(new Event('input',{bubbles:true}));
                render(); return;
            }
            if (document.querySelector('.modal.show,.select2-container--open')) return;
            const delta = event.key === '+' || event.code === 'NumpadAdd' ? 1 : event.key === '-' || event.code === 'NumpadSubtract' ? -1 : 0;
            if (!delta || (field && !field.matches(rowInput))) return;
            if (field) select(field,false);
            if (!selectedInput()) return;
            event.preventDefault(); event.stopImmediatePropagation(); enqueue(delta);
        },true);
        render();
    }
    window.ReceiptQuantityControls = {isBusy:()=>busy,hasFailed:()=>failed,
        step:(input,delta)=>{
            if (![1,-1].includes(delta) || !inputs().includes(input) || input.readOnly || input.disabled) return;
            select(input,false); enqueue(delta);
        }};
    if(document.readyState === 'loading') document.addEventListener('DOMContentLoaded',start); else start();
})();
