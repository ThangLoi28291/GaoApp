(function () {
    'use strict';
    const root = document.querySelector('[data-employee-account]');
    if (!root || !window.bootstrap?.Offcanvas) return;
    const control = root.querySelector('[data-account-control]');
    const trigger = document.getElementById('employeeAccountTrigger');
    const menu = document.getElementById('employeeAccountMenu');
    const drawer = document.getElementById('employeeAccountDrawer');
    const title = document.getElementById('employeeAccountTitle');
    const form = root.querySelector('[data-account-password-form]');
    const save = root.querySelector('[data-account-save]');
    const status = root.querySelector('[data-account-status]');
    const login = root.querySelector('[data-account-login]');
    const dialog = bootstrap.Offcanvas.getOrCreateInstance(drawer);
    let submitting = false;
    let signedOut = false;
    let parentDrawer = null;
    let activePanel = 'profile';

    function closeMenu(restoreFocus) {
        menu.hidden = true;
        if (menu.parentElement !== root) root.append(menu);
        trigger.setAttribute('aria-expanded', 'false');
        if (restoreFocus) trigger.focus();
    }
    function mount() {
        closeMenu(false);
        const target = document.querySelector('[data-account-toolbar-slot]');
        if (!target) return;
        target.append(control);
        control.hidden = false;
    }
    function positionMenu() {
        if (menu.hidden) return;
        const rect = trigger.getBoundingClientRect();
        const width = document.documentElement.clientWidth;
        const height = window.innerHeight;
        if (rect.bottom <= 0 || rect.top >= height) { closeMenu(false); return; }
        menu.style.left = Math.max(12, Math.min(rect.right - menu.offsetWidth, width - menu.offsetWidth - 12)) + 'px';
        const anchorBottom = trigger.closest('header')?.getBoundingClientRect().bottom ?? rect.bottom;
        const top = Math.min(anchorBottom + 6, height - menu.offsetHeight - 12);
        menu.style.top = Math.max(12, top) + 'px';
    }
    function openMenu(focusItem) {
        // Keep the popup within the POS navigation drawer's focus trap when opened there.
        (control.closest('.offcanvas') || root).append(menu);
        menu.hidden = false;
        trigger.setAttribute('aria-expanded', 'true');
        positionMenu();
        if (focusItem) menu.querySelector('[role="menuitem"]').focus();
    }
    trigger.addEventListener('click', () => menu.hidden ? openMenu(false) : closeMenu(false));
    trigger.addEventListener('keydown', event => {
        if (event.key === 'Escape' && !menu.hidden) {
            event.preventDefault(); event.stopPropagation(); closeMenu(true); return;
        }
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault(); openMenu(true);
            if (event.key === 'ArrowUp') Array.from(menu.querySelectorAll('[role="menuitem"]')).at(-1).focus();
        }
    });
    menu.addEventListener('keydown', event => {
        if (event.key === 'Escape') {
            event.preventDefault(); event.stopPropagation(); closeMenu(true); return;
        }
        const items = Array.from(menu.querySelectorAll('[role="menuitem"]'));
        const index = items.indexOf(document.activeElement);
        let next;
        if (event.key === 'ArrowDown') next = (index + 1) % items.length;
        if (event.key === 'ArrowUp') next = (index - 1 + items.length) % items.length;
        if (event.key === 'Home') next = 0;
        if (event.key === 'End') next = items.length - 1;
        if (next !== undefined) { event.preventDefault(); items[next].focus(); }
    });
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && !menu.hidden) { event.preventDefault(); closeMenu(true); }
    });
    document.addEventListener('click', event => {
        if (!menu.contains(event.target) && !control.contains(event.target)) closeMenu(false);
    });
    document.addEventListener('focusin', event => {
        if (!menu.contains(event.target) && !control.contains(event.target)) closeMenu(false);
    });
    window.addEventListener('resize', positionMenu);
    window.addEventListener('scroll', positionMenu, true);

    function clearPasswordForm() {
        form.reset();
        form.querySelectorAll('[data-account-error]').forEach(node => { node.textContent = ''; });
        form.querySelectorAll('input').forEach(input => { input.removeAttribute('aria-invalid'); input.setCustomValidity(''); });
        form.querySelectorAll('[data-password-toggle]').forEach(button => {
            document.getElementById(button.dataset.passwordToggle).type = 'password';
            button.setAttribute('aria-pressed', 'false');
            button.setAttribute('aria-label', button.getAttribute('aria-label').replace(/^Ẩn/, 'Hiện'));
            button.querySelector('i').className = 'bx bx-show';
        });
        status.hidden = true;
        status.classList.remove('is-success');
        login.hidden = true;
    }
    function panel(name) {
        if (submitting || signedOut) return;
        closeMenu(false);
        activePanel = name;
        const labels = { profile: 'Hồ sơ của tôi', password: 'Đổi mật khẩu', help: 'Trợ giúp' };
        title.textContent = labels[name];
        root.querySelectorAll('[data-account-section]').forEach(section => { section.hidden = section.dataset.accountSection !== name; });
        clearPasswordForm();
        if (drawer.classList.contains('show')) {
            (name === 'password' ? form.elements.CurrentPassword : drawer.querySelector('.btn-close')).focus();
            return;
        }
        const parent = control.closest('.offcanvas.show');
        if (parent) {
            parentDrawer = bootstrap.Offcanvas.getInstance(parent);
            parent.addEventListener('hidden.bs.offcanvas', () => dialog.show(), { once: true });
            parentDrawer.hide();
        } else dialog.show();
    }
    root.querySelectorAll('[data-account-panel]').forEach(button => button.addEventListener('click', () => panel(button.dataset.accountPanel)));
    drawer.addEventListener('shown.bs.offcanvas', () => {
        if (activePanel === 'password') form.elements.CurrentPassword.focus();
    });
    drawer.addEventListener('hide.bs.offcanvas', event => { if (submitting) event.preventDefault(); });
    drawer.addEventListener('hidden.bs.offcanvas', () => {
        clearPasswordForm();
        if (parentDrawer) { parentDrawer.show(); parentDrawer = null; }
        else trigger.focus();
    });
    form.querySelectorAll('[data-password-toggle]').forEach(button => {
        button.addEventListener('click', () => {
            const input = document.getElementById(button.dataset.passwordToggle);
            const visible = input.type === 'password';
            input.type = visible ? 'text' : 'password';
            button.setAttribute('aria-pressed', String(visible));
            button.setAttribute('aria-label', button.getAttribute('aria-label').replace(/^(Hiện|Ẩn)/, visible ? 'Ẩn' : 'Hiện'));
            button.querySelector('i').className = visible ? 'bx bx-hide' : 'bx bx-show';
        });
    });
    function fieldError(name, message) {
        const input = form.elements[name];
        const error = form.querySelector('[data-account-error="' + name + '"]');
        if (!input || !error) return;
        input.setAttribute('aria-invalid', 'true');
        error.textContent = message;
    }
    form.addEventListener('input', event => {
        if (!event.target.name) return;
        event.target.removeAttribute('aria-invalid');
        const error = form.querySelector('[data-account-error="' + event.target.name + '"]');
        if (error) error.textContent = '';
    });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (submitting || signedOut) return;
        status.hidden = true;
        form.querySelectorAll('[data-account-error]').forEach(node => { node.textContent = ''; });
        form.querySelectorAll('[aria-invalid]').forEach(node => node.removeAttribute('aria-invalid'));
        if (form.elements.NewPassword.value !== form.elements.ConfirmPassword.value) {
            fieldError('ConfirmPassword', 'Mật khẩu xác nhận chưa khớp.');
            form.elements.ConfirmPassword.focus(); return;
        }
        if (!form.reportValidity()) return;
        submitting = true;
        save.disabled = true;
        save.textContent = 'Đang đổi mật khẩu…';
        form.setAttribute('aria-busy', 'true');
        const deadline = new AbortController();
        const timeout = window.setTimeout(() => deadline.abort(), 20000);
        try {
            const response = await fetch(form.action, {
                method: 'POST', body: new FormData(form), credentials: 'same-origin', signal: deadline.signal,
                headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
            });
            const data = response.headers.get('content-type')?.includes('application/json') ? await response.json() : null;
            if (!response.ok) {
                const fields = data?.errors || {};
                Object.entries(fields).forEach(([name, messages]) => fieldError(name, Array.isArray(messages) ? messages[0] : messages));
                status.textContent = data?.message || (response.status === 401 || response.status === 403
                    ? 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.' : 'Không thể đổi mật khẩu. Vui lòng tải lại trang rồi thử lại.');
                status.hidden = false;
                if (response.status === 401 || response.status === 403 || response.status === 409) login.hidden = false;
                form.querySelector('[aria-invalid="true"]')?.focus();
                return;
            }
            if (!data?.redirectUrl) throw new Error('Unexpected response');
            signedOut = true;
            form.reset();
            status.textContent = 'Đổi mật khẩu thành công. Đang chuyển đến trang đăng nhập…';
            status.hidden = false;
            status.classList.add('is-success');
            login.hidden = false;
            // The destination is supplied by our own controller; only navigate within this origin.
            const destination = new URL(data.redirectUrl, window.location.origin);
            window.location.assign(destination.origin === window.location.origin ? destination.href : login.href);
        } catch {
            status.textContent = 'Chưa nhận được kết quả. Kiểm tra kết nối; nếu mật khẩu đã đổi, hãy đăng nhập lại bằng mật khẩu mới.';
            status.hidden = false;
            login.hidden = false;
        } finally {
            window.clearTimeout(timeout);
            submitting = false;
            save.disabled = signedOut;
            save.textContent = 'Đổi mật khẩu';
            form.removeAttribute('aria-busy');
        }
    });
    window.addEventListener('pagehide', () => { form.reset(); closeMenu(false); });
    window.addEventListener('pageshow', event => { if (event.persisted) window.location.reload(); });
    mount();
})();
