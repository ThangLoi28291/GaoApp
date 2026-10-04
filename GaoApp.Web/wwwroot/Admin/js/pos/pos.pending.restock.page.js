(() => {
    'use strict';
    let submitting = false;
    document.addEventListener('click', async event => {
        const button = event.target.closest('.js-complete-restock');
        if (!button || button.disabled || submitting) return;
        submitting = true;
        button.disabled = true;
        const label = button.textContent;
        button.textContent = 'Đang nhập kho…';
        const message = document.getElementById('pendingRestockMessage');
        try {
            const response = await fetch(`/admin/pos/returns/${Number(button.dataset.returnId)}/complete-restock`, {
                method: 'POST', headers: {Accept: 'application/json', RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''}
            });
            const data = await response.json();
            if (!response.ok || data.success === false) throw new Error([data.message || 'Chưa hoàn tất nhập kho.', data.actionHint].filter(Boolean).join(' '));
            button.closest('.card[data-return-id]').remove();
            const remaining = document.querySelectorAll('.card[data-return-id]').length;
            const count = document.getElementById('pendingRestockCount');
            if (count) count.textContent = `${remaining} phiếu`;
            message.className = 'alert alert-success';
            message.textContent = data.message;
            message.hidden = false;
            if (!document.querySelector('.js-complete-restock')) message.textContent += ' Không còn phiếu đang chờ trong danh sách này.';
        } catch (error) {
            message.className = 'alert alert-warning';
            message.textContent = error.message || 'Chưa hoàn tất nhập kho. Tải lại để kiểm tra.';
            message.hidden = false;
            button.disabled = false;
            button.textContent = label;
        } finally { submitting = false; }
    });
})();
