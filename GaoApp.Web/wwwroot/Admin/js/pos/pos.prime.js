/* GaoApp POS V3-P4 — presentation adapter only.
 * Existing Pos* modules own every business action and financial value.
 * Same nodes are moved, never copied. A failed/missing adapter leaves P2 fallback.
 */
window.PosPrime = (function () {
    'use strict';

    let instance = null;

    function mount() {
        if (instance) return instance;
        const root = document.querySelector('.pos-cockpit-v2.pos-prime');
        const layout = root?.closest('.pos-prime-layout');
        if (!root || !layout) return null;
        if (!window.bootstrap?.Offcanvas || !window.bootstrap?.Modal) {
            console.warn('POS Prime: Bootstrap unavailable; keeping fallback layout.');
            return null;
        }

        const byId = (id) => document.getElementById(id);
        const elements = {
            table: byId('currentDraftBody')?.closest('table'),
            context: byId('posPrimeContextContent'),
            contextHost: byId('posPrimeContextSheetHost'),
            contextLauncher: byId('posPrimeContextLauncher'),
            summary: byId('posPrimeSummaryContent'),
            moneyHost: byId('posPrimeMoneySheetHost'),
            moneyActions: byId('posPrimeMoneyActions'),
            moneyLauncher: byId('posPrimeMoneyLauncher'),
            actions: byId('posPrimeActionContent'),
            dockActions: byId('posPrimeDockActions'),
            secondary: root.querySelector('.pos-cockpit-secondary-actions'),
            customer: byId('customerInfoBox'),
            balance: byId('sumBalance'),
            customerName: byId('posPrimeCustomerName'),
            benefits: byId('posPrimeCustomerBenefits'),
            dockBalance: byId('posPrimeDockBalance'),
            payment: byId('paymentModal'),
            addPayment: byId('btnAddPayment')
        };
        if (Object.values(elements).some((element) => !element)) {
            console.error('POS Prime: a required presentation host is missing; keeping fallback.');
            return null;
        }
        const paymentFooter = elements.payment.querySelector('.pos-payment-workspace__footer');
        const holdModal = byId('holdModal');
        const holdNote = byId('txtHoldNote');
        const confirmHold = byId('btnConfirmHold');
        if (!paymentFooter || !holdModal || !holdNote || !confirmHold) return null;

        const tabletQuery = window.matchMedia('(max-width: 1199.98px)');
        const phoneQuery = window.matchMedia('(max-width: 767.98px)');
        const cardQuery = window.matchMedia('(max-width: 991.98px)');
        const originalTableClass = elements.table.classList.contains('pos-line-table');
        const sheets = [
            { key: 'scanHistory', node: byId('posScanHistory'), launcher: byId('posScanHistoryOpen') },
            { key: 'context', node: byId('posPrimeContextSheet'), launcher: elements.contextLauncher },
            { key: 'money', node: byId('posPrimeMoneySheet'), launcher: elements.moneyLauncher },
            { key: 'admin', node: byId('posPrimeAdminDrawer'), launcher: root.querySelector('[data-bs-target="#posPrimeAdminDrawer"]') }
        ];
        if (sheets.some((sheet) => !sheet.node)) return null;

        let activeSheet = null;
        let mode = '';
        let alive = true;
        let suspended = false;
        let frame = 0;
        let mirrorFrame = 0;
        let focusRedirecting = false;
        let returnLease = null;
        let replaying = false;
        let recoveryFrame = 0;
        let interactionVersion = 0;
        let heldFrame = 0;
        let heldDecoration = null;
        const modalOpeners = new WeakMap();
        const closingModals = new Set();
        const heldList = byId('heldList');
        const removers = [];
        const observers = [];
        const markers = new Map();
        const createdInstances = new Set();
        const submitHost = document.createElement('div');
        submitHost.className = 'pos-prime-payment-submit-host';
        paymentFooter.insertBefore(submitHost, paymentFooter.firstChild);
        const holdHint = document.createElement('div');
        holdHint.id = 'posPrimeHoldShortcutHint';
        holdHint.className = 'pos-prime-hold-shortcut-hint';
        holdHint.textContent = 'Ctrl + Enter để xác nhận';
        holdNote.insertAdjacentElement('afterend', holdHint);

        [elements.context, elements.summary, elements.actions, elements.secondary, elements.addPayment]
            .forEach((node) => {
                const marker = document.createComment('Prime P3 return position');
                node.parentNode.insertBefore(marker, node);
                markers.set(node, marker);
            });

        const desiredMode = () => phoneQuery.matches ? 'phone' : tabletQuery.matches ? 'tablet' : 'desktop';
        const visible = (node) => !!node?.isConnected && node.getClientRects().length > 0;
        const enabled = (node) => !!node?.isConnected && !node.disabled
            && !node.closest('[inert], fieldset[disabled], [aria-disabled="true"], .disabled');
        const businessModalOpen = () => closingModals.size > 0 || !!layout.querySelector('.modal.show, .modal.showing');
        const inputLike = (node) => !!node?.matches?.('input, textarea, select, [contenteditable="true"]');

        function focus(node) {
            if (!visible(node) || !enabled(node)) return;
            try { node.focus({ preventScroll: true }); } catch { node.focus(); }
        }

        function place(node, host) {
            if (node.parentNode === host) return;
            // appendChild moves the original node, preserving listeners and values.
            host.appendChild(node);
        }

        function restore(node) {
            const marker = markers.get(node);
            if (marker?.parentNode && marker.nextSibling !== node) {
                marker.parentNode.insertBefore(node, marker.nextSibling);
            }
        }

        function canvas(sheet) {
            let control = window.bootstrap.Offcanvas.getInstance(sheet.node);
            if (!control) {
                control = new window.bootstrap.Offcanvas(sheet.node, { backdrop: true, scroll: false, keyboard: true });
                // Admin Drawer is shared; never dispose its external instance.
                if (sheet.key !== 'admin') createdInstances.add(control);
            }
            return control;
        }

        function setExpanded(sheet, value) {
            sheet.launcher?.setAttribute('aria-expanded', value ? 'true' : 'false');
        }

        function closeSheet(afterClose, restoreFocus) {
            if (!activeSheet) {
                if (afterClose) afterClose();
                return;
            }
            const sheet = activeSheet;
            sheet.afterClose = afterClose || null;
            sheet.restoreFocus = restoreFocus !== false;
            sheet.closeRequested = true;
            if (sheet.phase === 'opening' || sheet.phase === 'closing') return;
            canvas(sheet).hide();
        }

        function openSheet(key, launcher, preferredFocus) {
            const sheet = sheets.find((item) => item.key === key);
            if (!sheet || !alive || suspended || businessModalOpen()) return;
            if ((key === 'context' && !tabletQuery.matches) || (key === 'money' && !phoneQuery.matches)) return;
            if (activeSheet === sheet) return;
            if (activeSheet) {
                closeSheet(() => openSheet(key, launcher, preferredFocus), false);
                return;
            }
            sheet.returnTarget = launcher || sheet.launcher;
            sheet.preferredFocus = preferredFocus || null;
            returnLease = null;
            canvas(sheet).show(sheet.returnTarget);
        }

        function refreshMode() {
            if (!alive || suspended) return;
            // pos.css forces .pos-line-table active rows to table-row with priority.
            // This class is CSS-only in the pinned Pos* owners. Keep the SAME table,
            // tbody IDs and is-active-line state; use an isolated presentation class
            // for cards instead of fighting legacy rules or changing business JS.
            elements.table.classList.toggle('pos-prime-mobile-table', cardQuery.matches);
            elements.table.classList.toggle('pos-line-table', !cardQuery.matches && originalTableClass);
            const nextMode = desiredMode();
            if (nextMode === mode) return;
            const currentFocus = document.activeElement;
            if (activeSheet && activeSheet.key !== 'admin') {
                // Finish the Bootstrap transition before moving the focus-trap contents.
                closeSheet(() => applyMode(desiredMode(), currentFocus), false);
                return;
            }
            applyMode(nextMode, currentFocus);
        }

        function applyMode(nextMode, previousFocus) {
            const contextWasFocused = elements.context.contains(previousFocus);
            mode = nextMode;
            returnLease = null;
            if (mode === 'desktop') {
                restore(elements.context);
            } else {
                place(elements.context, elements.contextHost);
            }

            if (mode === 'phone') {
                place(elements.summary, elements.moneyHost);
                place(elements.actions, elements.dockActions);
                place(elements.secondary, elements.moneyActions);
                // type=button: moving it does not change form-submit semantics.
                // Direct listeners, disabled/busy state and original text stay intact.
                place(elements.addPayment, submitHost);
            } else {
                restore(elements.secondary);
                restore(elements.summary);
                restore(elements.actions);
                restore(elements.addPayment);
            }
            root.setAttribute('data-prime-mode', mode);
            layout.setAttribute('data-prime-mode', mode);
            syncMirrors();
            scheduleGeometry();
            if (contextWasFocused && mode !== 'desktop' && !businessModalOpen() && inputLike(previousFocus)) {
                openSheet('context', elements.contextLauncher, previousFocus);
            } else if (visible(previousFocus) && document.activeElement !== previousFocus) {
                focus(previousFocus);
            } else if (!businessModalOpen() && previousFocus && !visible(previousFocus)) {
                // A launcher/sheet control has become hidden on this breakpoint.
                focus(mode === 'desktop' ? byId('txtBarcode') : elements.contextLauncher);
            }
        }

        function text(node) { return (node?.textContent || '').replace(/\s+/g, ' ').trim(); }
        function setText(node, value) { if (node.textContent !== value) node.textContent = value; }
        function syncMirrors() {
            if (!alive) return;
            const name = text(elements.customer.querySelector('.pos-customer-identity__name, .pos-customer-name, .pos-customer-empty__name')) || 'Khách lẻ';
            const points = text(elements.customer.querySelector('.pos-customer-benefit-card--points .pos-customer-benefit-card__value'));
            const vouchers = text(elements.customer.querySelector('.pos-customer-benefit-card--voucher .pos-customer-benefit-card__value'));
            setText(elements.customerName, name);
            setText(elements.benefits, [points ? points + ' điểm' : '', vouchers ? vouchers + ' voucher' : '', 'Công cụ đơn'].filter(Boolean).join(' · '));
            // Read-only display of the canonical balance. No monetary arithmetic.
            setText(elements.dockBalance, text(elements.balance) || '—');
            elements.contextLauncher.title = name + ' · ' + text(elements.benefits);
        }

        function restoreHeldDecoration() {
            if (!heldDecoration) return;
            const { button, title, titleText, attributes } = heldDecoration;
            if (button.isConnected) {
                for (const [name, value] of Object.entries(attributes)) {
                    if (value === null) button.removeAttribute(name);
                    else button.setAttribute(name, value);
                }
                if (title?.isConnected) setText(title, titleText);
            }
            heldDecoration = null;
        }

        function syncHeldPresentation() {
            heldFrame = 0;
            if (!alive || suspended || !heldList) return;
            // renderHeldList replaces the launcher. Re-resolve it every time;
            // retain its existing click/F6/data-bs-target owners, not a clone.
            const button = heldList.querySelector('#btnOpenHeldOrders');
            const countText = text(button?.querySelector('.pos-held-summary-launcher__total'));
            if (!button || !/^\d+\s+đơn$/.test(countText)) return;
            const title = button.querySelector('.pos-held-summary-launcher__title');
            if (heldDecoration?.button !== button) {
                restoreHeldDecoration();
                heldDecoration = {
                    button, title, titleText: text(title),
                    attributes: Object.fromEntries(['aria-label', 'title', 'aria-keyshortcuts', 'data-prime-held']
                        .map((name) => [name, button.getAttribute(name)]))
                };
            }
            if (title) setText(title, 'Đơn giữ');
            const label = 'Mở danh sách đơn giữ · ' + countText + ' · F6';
            button.setAttribute('aria-label', label);
            button.setAttribute('title', label);
            button.setAttribute('aria-keyshortcuts', 'F6');
            button.setAttribute('data-prime-held', /^0\s+đơn$/.test(countText) ? 'empty' : 'pending');
        }

        function scheduleHeldPresentation() {
            if (!heldFrame && alive && !suspended) heldFrame = window.requestAnimationFrame(syncHeldPresentation);
        }

        function cancelFocusRecovery() {
            window.cancelAnimationFrame(recoveryFrame);
            recoveryFrame = 0;
        }

        function queueFocusRecovery(surface, opener) {
            cancelFocusRecovery();
            const version = interactionVersion;
            // Two one-shot frames allow Bootstrap and owner close handlers to
            // finish. Never poll or fight a newly opened surface/user input.
            recoveryFrame = window.requestAnimationFrame(() => {
                recoveryFrame = window.requestAnimationFrame(() => {
                    recoveryFrame = 0;
                    if (!alive || suspended || version !== interactionVersion
                        || document.visibilityState === 'hidden' || !document.hasFocus()
                        || activeSheet || businessModalOpen()
                        || document.querySelector('.offcanvas.show, .offcanvas.showing, .modal.show, .swal2-container')) return;
                    const current = document.activeElement;
                    if (inputLike(current) && current !== byId('txtBarcode') && visible(current)) {
                        // Preserve this input also against a delayed owner return
                        // timer. A genuine user key/pointer clears the lease.
                        returnLease = current;
                        return;
                    }
                    const neutral = !current || current === document.body || current === document.documentElement
                        || surface?.contains(current) || !visible(current) || current === opener
                        || current === byId('txtBarcode');
                    if (!neutral) return;
                    if (phoneQuery.matches) {
                        // Do not focus/blur an owner input on phones. Retain a
                        // non-input return target to prevent delayed owner scan
                        // focus from summoning the software keyboard.
                        const target = visible(opener) && enabled(opener) && !inputLike(opener) ? opener
                            : visible(elements.contextLauncher) ? elements.contextLauncher : elements.moneyLauncher;
                        returnLease = target;
                        focus(target);
                    } else {
                        returnLease = null;
                        focus(byId('txtBarcode'));
                    }
                });
            });
        }

        function onModalHide(event) {
            if (!layout.contains(event.target) || !event.target.matches('.modal')) return;
            closingModals.add(event.target);
            cancelFocusRecovery();
        }

        function onModalHidden(event) {
            if (!layout.contains(event.target) || !event.target.matches('.modal')) return;
            closingModals.delete(event.target);
            const opener = modalOpeners.get(event.target);
            modalOpeners.delete(event.target);
            if (phoneQuery.matches && !businessModalOpen() && !activeSheet) {
                returnLease = visible(opener) && !inputLike(opener) ? opener : elements.contextLauncher;
            }
            queueFocusRecovery(event.target, opener);
            scheduleGeometry();
        }

        function observeUtilities() {
            if (heldList) {
                const observer = new MutationObserver(scheduleHeldPresentation);
                observer.observe(heldList, { childList: true, subtree: true, characterData: true });
                observers.push(observer);
            }
            syncHeldPresentation();
        }

        function cancelUtilityFrames() {
            cancelFocusRecovery();
            window.cancelAnimationFrame(heldFrame);
            heldFrame = 0;
        }

        function scheduleMirrors() {
            if (mirrorFrame || suspended || !alive) return;
            mirrorFrame = window.requestAnimationFrame(() => { mirrorFrame = 0; syncMirrors(); });
        }

        function updateGeometry() {
            frame = 0;
            if (!alive || suspended) return;
            const viewport = window.visualViewport;
            const visibleHeight = viewport?.height || window.innerHeight;
            layout.setAttribute('data-prime-room', visibleHeight < 480 ? 'short' : 'normal');
            // Breakpoints use matchMedia (layout viewport), not pinch-zoom width.
            // Leave pinch-zoom to the browser; never disable zoom or zoom the page.
            if (!viewport || Math.abs(viewport.scale - 1) > 0.02) {
                ['--pos-prime-visible-height', '--pos-prime-visible-top', '--pos-prime-visible-bottom']
                    .forEach((property) => layout.style.removeProperty(property));
                return;
            }
            layout.style.setProperty('--pos-prime-visible-height', Math.floor(viewport.height) + 'px');
            layout.style.setProperty('--pos-prime-visible-top', Math.max(0, viewport.offsetTop) + 'px');
            layout.style.setProperty('--pos-prime-visible-bottom', Math.max(0, window.innerHeight - viewport.height - viewport.offsetTop) + 'px');
            const focused = document.activeElement;
            if (mode === 'phone' && inputLike(focused)) {
                // Scroll only the current overlay body, never the POS page or another form.
                const body = focused.closest('.pos-prime-sheet .offcanvas-body, #paymentModal .modal-body');
                if (body && visible(body)) {
                    const fieldRect = focused.getBoundingClientRect();
                    const bodyRect = body.getBoundingClientRect();
                    if (fieldRect.bottom > bodyRect.bottom - 12) body.scrollTop += fieldRect.bottom - bodyRect.bottom + 12;
                    else if (fieldRect.top < bodyRect.top + 12) body.scrollTop -= bodyRect.top - fieldRect.top + 12;
                }
            }
        }

        function scheduleGeometry() {
            if (!frame && !suspended && alive) frame = window.requestAnimationFrame(updateGeometry);
        }

        function onKeydown(event) {
            if (!activeSheet) return;
            if (event.key === 'Escape') {
                event.preventDefault();
                event.stopImmediatePropagation();
                closeSheet(null, true);
                return;
            }
            if (event.key === 'Tab') {
                // Wrap at canvas boundaries before Tab can move to browser chrome.
                // Bootstrap still owns focus trapping and canvas dismissal.
                const targets = Array.from(activeSheet.node.querySelectorAll(
                    'a[href], button, input, select, textarea, [tabindex], [contenteditable="true"]'
                )).filter((node) => visible(node) && enabled(node) && node.tabIndex >= 0);
                const first = targets[0];
                const last = targets[targets.length - 1];
                if (!first || !activeSheet.node.contains(event.target)
                    || event.target === activeSheet.node
                    || (event.shiftKey && event.target === first)
                    || (!event.shiftKey && event.target === last)) {
                    event.preventDefault();
                    event.stopImmediatePropagation();
                    focus(event.shiftKey ? last || activeSheet.node : first || activeSheet.node);
                }
                return;
            }
            const businessShortcut = /^F(?:[1-9]|1[0-2])$/.test(event.key)
                || (event.ctrlKey && event.key === 'Delete');
            if (businessShortcut || !activeSheet.node.contains(event.target)) {
                // Keep native Ctrl+Delete text editing, but never let it cancel the sale.
                if (!(event.ctrlKey && event.key === 'Delete' && inputLike(event.target))) {
                    event.preventDefault();
                }
                event.stopImmediatePropagation();
                return;
            }
            // Keep native button/link activation, but not global Enter -> barcode.
            // Tab and text editing still reach Bootstrap and the existing field owner.
            if (event.key === 'Enter' && !inputLike(event.target)) event.stopImmediatePropagation();
        }

        function onFocusin(event) {
            if (focusRedirecting) return;
            const sheet = activeSheet;
            if (sheet && !businessModalOpen()) {
                if (sheet.node.contains(event.target)) { sheet.lastFocus = event.target; return; }
                focusRedirecting = true;
                focus(visible(sheet.lastFocus) ? sheet.lastFocus : sheet.node);
                focusRedirecting = false;
            } else if (returnLease && event.target === byId('txtBarcode') && !businessModalOpen()) {
                // Ignore an old owner timer only after a Prime sheet closes directly.
                // A real click/key clears the lease before user-directed focus occurs.
                focusRedirecting = true;
                focus(returnLease);
                focusRedirecting = false;
            }
            scheduleGeometry();
        }

        function clearReturnLease(event) {
            if (event.isTrusted) {
                returnLease = null;
                interactionVersion++;
                cancelFocusRecovery();
            }
        }

        function resolveSurfaceLauncher(button) {
            const action = button.getAttribute('data-customer-action');
            if (['open', 'reward', 'use-voucher'].includes(action)) {
                return () => elements.customer.querySelector('[data-customer-action="' + action + '"]');
            }
            if (button.hasAttribute('data-pos-command-prefix')) {
                const prefix = button.getAttribute('data-pos-command-prefix');
                return () => Array.from(elements.context.querySelectorAll('[data-pos-command-prefix]'))
                    .find((candidate) => candidate.getAttribute('data-pos-command-prefix') === prefix);
            }
            // Both existing order launchers open an owner-managed modal first.
            if (['btnHoldCart', 'btnCancelCart'].includes(button.id)) return () => byId(button.id);
            return null;
        }

        function onClick(event) {
            if (!(event.target instanceof Element)) return;
            const opener = event.target.closest('[data-prime-open]');
            if (opener && root.contains(opener)) {
                event.preventDefault();
                event.stopImmediatePropagation();
                if (enabled(opener)) openSheet(opener.getAttribute('data-prime-open'), opener);
                return;
            }
            if (!activeSheet || replaying || activeSheet.key === 'admin') return;
            const button = event.target.closest('button, a');
            if (!button || !activeSheet.node.contains(button)) return;
            const resolve = resolveSurfaceLauncher(button);
            if (!resolve) return; // Never replay Save, Clear or any mutation on close/resize.
            event.preventDefault();
            event.stopImmediatePropagation();
            if (!enabled(button) || activeSheet.closeRequested) return;
            closeSheet(() => {
                // The customer renderer may replace the old launcher while closing.
                const current = resolve();
                if (!enabled(current)) return;
                replaying = true;
                try { current.click(); } finally { replaying = false; }
            }, false);
        }

        function onModalShow(event) {
            if (!layout.contains(event.target) || !event.target.matches('.modal')) return;
            returnLease = null;
            cancelFocusRecovery();
            closingModals.delete(event.target);
            modalOpeners.set(event.target, event.relatedTarget || document.activeElement);
            if (activeSheet) {
                // A programmatic owner surface also waits for the current canvas.
                // Only defer presentation, never repeat the business handler.
                event.preventDefault();
                const modalNode = event.target;
                const relatedTarget = event.relatedTarget;
                closeSheet(() => {
                    if (modalNode.isConnected) window.bootstrap.Modal.getOrCreateInstance(modalNode).show(relatedTarget);
                }, false);
            }
            scheduleGeometry();
        }

        function onModalShown(event) {
            if (!layout.contains(event.target) || !event.target.matches('.modal')) return;
            scheduleGeometry();
            if (event.target === holdModal) focus(holdNote);
        }

        function onHoldNoteKeydown(event) {
            if (event.key !== 'Enter' || !event.ctrlKey || event.shiftKey || event.altKey || event.metaKey) return;
            if (!holdModal.classList.contains('show') || !enabled(confirmHold)) return;
            event.preventDefault();
            event.stopImmediatePropagation();
            if (event.repeat) return;
            confirmHold.click();
        }

        function listen(target, type, handler, options) {
            target.addEventListener(type, handler, options);
            removers.push(() => target.removeEventListener(type, handler, options));
        }

        function installListeners() {
            listen(window, 'keydown', onKeydown, true);
            listen(window, 'focusin', onFocusin, true);
            listen(window, 'pointerdown', clearReturnLease, true);
            listen(window, 'keydown', clearReturnLease, true);
            listen(document, 'click', onClick, true);
            listen(document, 'show.bs.modal', onModalShow, true);
            listen(document, 'shown.bs.modal', onModalShown);
            listen(document, 'hide.bs.modal', onModalHide, true);
            listen(holdNote, 'keydown', onHoldNoteKeydown);
            listen(document, 'hidden.bs.modal', onModalHidden);
            listen(document, 'visibilitychange', () => {
                if (document.visibilityState === 'hidden') cancelUtilityFrames();
            });
            listen(window, 'resize', () => { refreshMode(); scheduleGeometry(); });
            if (window.visualViewport) {
                listen(window.visualViewport, 'resize', scheduleGeometry);
                listen(window.visualViewport, 'scroll', scheduleGeometry);
            }
            [tabletQuery, phoneQuery, cardQuery].forEach((query) => {
                if (query.addEventListener) listen(query, 'change', refreshMode);
                else { query.addListener(refreshMode); removers.push(() => query.removeListener(refreshMode)); }
            });
            sheets.forEach((sheet) => {
                listen(sheet.node, 'show.bs.offcanvas', (event) => {
                    if (businessModalOpen()) { event.preventDefault(); return; }
                    if (activeSheet && activeSheet !== sheet) {
                        event.preventDefault();
                        closeSheet(() => canvas(sheet).show(event.relatedTarget), false);
                        return;
                    }
                    activeSheet = sheet;
                    cancelFocusRecovery();
                    sheet.phase = 'opening';
                    sheet.closeRequested = false;
                    sheet.restoreFocus = true;
                    sheet.returnTarget = event.relatedTarget || sheet.returnTarget || sheet.launcher;
                    sheet.lastFocus = null;
                    returnLease = null;
                    setExpanded(sheet, true);
                });
                listen(sheet.node, 'shown.bs.offcanvas', () => {
                    sheet.phase = 'open';
                    if (sheet.closeRequested) { canvas(sheet).hide(); return; }
                    focus(sheet.preferredFocus);
                    sheet.preferredFocus = null;
                    scheduleGeometry();
                });
                listen(sheet.node, 'hide.bs.offcanvas', () => { sheet.phase = 'closing'; });
                listen(sheet.node, 'hidden.bs.offcanvas', () => {
                    sheet.phase = 'closed';
                    setExpanded(sheet, false);
                    if (activeSheet === sheet) activeSheet = null;
                    const callback = sheet.afterClose;
                    sheet.afterClose = null;
                    sheet.closeRequested = false;
                    if (callback) callback();
                    else if (sheet.restoreFocus && !businessModalOpen()) {
                        const target = visible(sheet.returnTarget) ? sheet.returnTarget
                            : mode === 'desktop' ? byId('txtBarcode') : elements.contextLauncher;
                        returnLease = phoneQuery.matches ? target : null;
                        focus(target);
                        queueFocusRecovery(sheet.node, target);
                    }
                    refreshMode();
                });
            });
            observeMirrors();
            observeUtilities();
        }

        function observeMirrors() {
            [elements.customer, elements.summary].forEach((node) => {
                const observer = new MutationObserver(scheduleMirrors);
                observer.observe(node, { childList: true, subtree: true, characterData: true });
                observers.push(observer);
            });
        }

        function detachListeners() {
            cancelUtilityFrames();
            while (removers.length) removers.pop()();
            while (observers.length) observers.pop().disconnect();
            window.cancelAnimationFrame(frame);
            window.cancelAnimationFrame(mirrorFrame);
            frame = mirrorFrame = 0;
        }

        function suspend() {
            suspended = true;
            cancelUtilityFrames();
            while (observers.length) observers.pop().disconnect();
            window.cancelAnimationFrame(frame);
            window.cancelAnimationFrame(mirrorFrame);
            frame = mirrorFrame = 0;
            // BFCache freezes the document. Retain listener registration order:
            // removing/re-adding capture guards would put them AFTER PosKeyboard.
            // A real destroy still removes every listener in detachListeners().
        }

        function resume() {
            if (!alive || !suspended) return;
            suspended = false;
            observeMirrors();
            observeUtilities();
            // Bootstrap/document state is preserved by BFCache. Do not re-init POS.
            if (activeSheet) {
                activeSheet.phase = activeSheet.node.classList.contains('show') ? 'open' : 'closed';
                if (activeSheet.phase === 'closed') activeSheet = null;
            }
            refreshMode();
            syncMirrors();
            scheduleGeometry();
        }

        function disposeNow() {
            if (!alive) return;
            alive = false;
            detachListeners();
            window.removeEventListener('pagehide', onPageHide);
            window.removeEventListener('pageshow', onPageShow);
            elements.table.classList.remove('pos-prime-mobile-table');
            elements.table.classList.toggle('pos-line-table', originalTableClass);
            restore(elements.addPayment);
            restore(elements.secondary);
            restore(elements.actions);
            restore(elements.summary);
            restore(elements.context);
            markers.forEach((marker) => marker.remove());
            submitHost.remove();
            holdHint.remove();
            restoreHeldDecoration();
            closingModals.clear();
            createdInstances.forEach((control) => control.dispose());
            root.removeAttribute('data-prime-ui');
            root.removeAttribute('data-prime-mode');
            layout.removeAttribute('data-prime-ui');
            layout.removeAttribute('data-prime-mode');
            layout.removeAttribute('data-prime-room');
            ['--pos-prime-visible-height', '--pos-prime-visible-top', '--pos-prime-visible-bottom']
                .forEach((property) => layout.style.removeProperty(property));
            instance = null;
        }

        function destroy() {
            if (!alive) return;
            if (suspended) resume();
            if (activeSheet) closeSheet(disposeNow, false);
            else disposeNow();
        }

        function onPageHide(event) {
            if (event.persisted) suspend();
            else destroy();
        }
        function onPageShow(event) { if (event.persisted) resume(); }

        try {
            installListeners();
            root.setAttribute('data-prime-ui', 'ready');
            layout.setAttribute('data-prime-ui', 'ready');
            refreshMode();
            syncMirrors();
            scheduleGeometry();
            window.addEventListener('pagehide', onPageHide);
            window.addEventListener('pageshow', onPageShow);
            instance = { destroy };
            return instance;
        } catch (error) {
            disposeNow();
            console.error('POS Prime: adapter stopped; fallback layout restored.', error);
            return null;
        }
    }

    return { mount, destroy: function () { instance?.destroy(); } };
})();
