/* Phone presentation only. The existing form, validation, CSRF and server action own saving. */
(() => {
    'use strict';
    const root = document.querySelector('[data-menu-mobile-form]');
    if (!root || root.dataset.mobileFormReady) return;
    root.dataset.mobileFormReady = 'true';
    const phone = matchMedia('(max-width: 767.98px)'), form = root.querySelector('form');
    const review = document.querySelector('[data-menu-review]');
    if (!form || !review || typeof review.showModal !== 'function') return;
    let approved = false, committing = false, submitter = null, disabledSubmitButtons = [];
    const security = form.querySelector('[data-security-confirmation]');
    const marker = document.createComment('Original security confirmation position');
    if (security) security.before(marker);
    const parent = form.elements.namedItem('ParentId');
    const extraParents = document.querySelector('[data-mobile-parent-options]');
    const iconSelect = form.elements.namedItem('Icon');
    let phoneOptionsInitialized = false, phoneIconInitialized = false, parentTouched = false, iconTouched = false;
    parent?.addEventListener('change', () => { parentTouched = true; });
    iconSelect?.addEventListener('change', () => { iconTouched = true; });
    function arrange() {
        if (phone.matches) {
            const mainColumn = form.querySelector('.row.g-4 > .col-xl-8');
            if (security && mainColumn && security.parentElement !== mainColumn) mainColumn.append(security);
            if (parent && extraParents) {
                const existing = new Set(Array.from(parent.options, option => option.value));
                const priorSelection = parent.value;
                JSON.parse(extraParents.textContent).forEach(option => {
                    if (existing.has(String(option.id))) return;
                    const added = new Option(option.title, String(option.id)); added.dataset.phoneParent = 'true'; parent.add(added);
                });
                const initialParent = root.dataset.menuCurrentParent;
                parent.value = !phoneOptionsInitialized && !parentTouched && initialParent && Array.from(parent.options).some(option => option.value === initialParent) ? initialParent : priorSelection;
                phoneOptionsInitialized = true;
            }
            if (iconSelect) {
                const currentIcon = root.dataset.menuCurrentIcon;
                if (currentIcon && !Array.from(iconSelect.options).some(option => option.value === currentIcon)) {
                    const option = new Option('Biểu tượng hiện tại', currentIcon); option.dataset.phoneIcon = 'true'; iconSelect.add(option);
                    if (!phoneIconInitialized && !iconTouched) iconSelect.value = currentIcon;
                }
                phoneIconInitialized = true;
            }
        } else {
            if (security && marker.parentNode) marker.after(security);
            // Keep a deep selection made on the phone when resizing, rather than silently changing its parent.
            if (parent) Array.from(parent.options).filter(option => option.dataset.phoneParent && !option.selected).forEach(option => option.remove());
            if (iconSelect) Array.from(iconSelect.options).filter(option => option.dataset.phoneIcon && !option.selected).forEach(option => option.remove());
            if (review.open) review.close();
        }
    }
    const value = name => form.elements.namedItem(name)?.value?.trim() || '';
    const label = name => {
        const select = form.elements.namedItem(name);
        return select?.selectedOptions?.[0]?.textContent.trim() || '';
    };
    function addSummary(name, text) {
        const row = document.createElement('div'), term = document.createElement('dt'), description = document.createElement('dd');
        term.textContent = name; description.textContent = text; row.append(term, description);
        review.querySelector('[data-menu-review-summary]').append(row);
    }
    form.addEventListener('submit', event => {
        if (!phone.matches) return;
        if (committing) { event.preventDefault(); return; }
        if (approved) {
            queueMicrotask(() => {
                if (event.defaultPrevented) return;
                committing = true;
                disabledSubmitButtons = Array.from(form.querySelectorAll('[type="submit"]')).filter(button => !button.disabled);
                disabledSubmitButtons.forEach(button => { button.disabled = true; });
            });
            return;
        }
        event.preventDefault();
        if (!form.reportValidity() || (window.jQuery?.fn?.valid && !window.jQuery(form).valid())) return;
        submitter = event.submitter;
        review.querySelector('[data-menu-review-summary]').replaceChildren();
        review.querySelector('[data-menu-review-intro]').textContent = root.dataset.menuOperation === 'create'
            ? 'Menu này sẽ được tạo trong cửa hàng hiện tại.' : 'Kiểm tra cấu hình menu trước khi cập nhật cửa hàng hiện tại.';
        addSummary('Tên menu', value('Title'));
        addSummary('Vị trí', value('ParentId') ? label('ParentId') : 'Menu gốc');
        addSummary('Đích đến', value('Url') || (value('Controller') ? [value('Area') || 'Admin', value('Controller'), value('Action') || 'Index'].join(' / ') : 'Nhóm chứa menu'));
        addSummary('Thứ tự', value('SortOrder') || '0');
        addSummary('Biểu tượng', value('Icon') ? label('Icon') : 'Theo biểu tượng mặc định');
        addSummary('Hiển thị', form.querySelector('input[type="checkbox"][name="IsActive"]')?.checked ? 'Đang bật' : 'Đã tắt');
        addSummary('Quyền cần có', value('PermissionCode') ? label('PermissionCode') : 'Không yêu cầu quyền để hiện menu');
        // Password/reason fields are never copied, serialized or persisted by this review.
        review.showModal();
    }, true);
    review.querySelector('[data-menu-review-cancel]').addEventListener('click', () => review.close());
    review.querySelector('[data-menu-review-confirm]').addEventListener('click', () => {
        if (!phone.matches || committing) return;
        review.close(); approved = true;
        try { form.requestSubmit(submitter?.isConnected ? submitter : undefined); }
        finally { approved = false; }
    });
    phone.addEventListener('change', arrange);
    window.addEventListener('pageshow', event => {
        if (!event.persisted) return;
        committing = false; approved = false;
        disabledSubmitButtons.forEach(button => { button.disabled = false; }); disabledSubmitButtons = [];
    });
    arrange();
})();
