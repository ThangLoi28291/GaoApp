(function () {
    'use strict';

    const byId = function (id) { return document.getElementById(id); };
    const createModal = function (id) {
        const element = byId(id);
        return element && window.bootstrap ? window.bootstrap.Modal.getOrCreateInstance(element) : null;
    };

    const helpModal = createModal('purchaseRequestHelpModal');
    const genericConfirmModal = createModal('genericConfirmModal');
    const genericConfirmMessage = byId('genericConfirmMessage');
    const genericConfirmButton = byId('genericConfirmButton');
    let pendingGenericForm = null;
    const genericBypass = new WeakSet();

    document.querySelectorAll('form[data-confirm]').forEach(function (form) {
        form.addEventListener('submit', function (event) {
            if (genericBypass.has(form)) {
                genericBypass.delete(form);
                return;
            }

            event.preventDefault();
            pendingGenericForm = form;
            if (genericConfirmMessage) genericConfirmMessage.textContent = form.dataset.confirm || 'Bạn có chắc muốn tiếp tục?';
            if (genericConfirmModal) genericConfirmModal.show();
        });
    });

    if (genericConfirmButton) {
        genericConfirmButton.addEventListener('click', function () {
            if (!pendingGenericForm) return;
            const form = pendingGenericForm;
            pendingGenericForm = null;
            genericBypass.add(form);
            if (genericConfirmModal) genericConfirmModal.hide();
            form.requestSubmit();
        });
    }

    const form = byId('approveRequestForm');
    const returnModal = createModal('returnRequestModal');
    const rejectModal = createModal('rejectRequestModal');

    document.addEventListener('keydown', function (event) {
        if (event.key === 'F1') {
            event.preventDefault();
            if (helpModal) helpModal.show();
            return;
        }

        if (!form || !event.altKey || event.ctrlKey || event.metaKey) return;
        if (event.key === 'Enter') {
            event.preventDefault();
            form.requestSubmit();
        } else if (event.key.toLowerCase() === 'r') {
            event.preventDefault();
            if (returnModal) returnModal.show();
        } else if (event.key.toLowerCase() === 'x') {
            event.preventDefault();
            if (rejectModal) rejectModal.show();
        }
    });

    if (!form) return;

    const rows = Array.from(form.querySelectorAll('.pr-approval-row'));
    const note = byId('approvalNote');
    const noteShell = byId('approvalNoteShell');
    const noteToggle = byId('approvalNoteToggle');
    const notePanel = byId('approvalNotePanel');
    const noteRequiredMark = byId('approvalNoteRequiredMark');
    const noteToggleHint = byId('approvalNoteToggleHint');
    const noteHelp = byId('approvalNoteHelp');
    const noteCount = byId('approvalNoteCount');
    const resetButton = byId('resetApprovalQuantities');
    const approveButton = byId('approveRequestButton');
    const confirmApproveButton = byId('confirmApproveButton');
    const approvalModal = createModal('approveConfirmModal');
    const viDisplay = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 });
    const viInput = new Intl.NumberFormat('vi-VN', { useGrouping: false, maximumFractionDigits: 3 });
    let finalSubmitting = false;
    let currentSummary = null;

    const summaryElements = {
        total: byId('approvalTotalCount'),
        unchanged: byId('approvalUnchangedCount'),
        increased: byId('approvalIncreasedCount'),
        decreased: byId('approvalDecreasedCount'),
        removed: byId('approvalRemovedCount')
    };

    const confirmElements = {
        unchanged: byId('confirmUnchangedCount'),
        increased: byId('confirmIncreasedCount'),
        decreased: byId('confirmDecreasedCount'),
        removed: byId('confirmRemovedCount'),
        description: byId('approveConfirmDescription')
    };

    rows.forEach(function (row) {
        const input = row.querySelector('.pr-approval-input');
        input.addEventListener('input', function () {
            syncRow(row, false);
            updateSummary();
        });
        input.addEventListener('blur', function () {
            const value = syncRow(row, true);
            if (value !== null) input.value = viInput.format(value);
            updateSummary();
        });
    });

    if (note) {
        note.addEventListener('input', function () {
            note.classList.remove('is-invalid');
            if (noteCount) noteCount.textContent = String(note.value.length);
        });
    }

    if (noteToggle) {
        noteToggle.addEventListener('click', function () {
            if (note && note.required && !notePanel.hidden) {
                note.focus();
                return;
            }
            setNotePanel(notePanel.hidden);
        });
    }

    if (resetButton) {
        resetButton.addEventListener('click', function () {
            rows.forEach(function (row) {
                const input = row.querySelector('.pr-approval-input');
                input.value = viInput.format(Number(row.dataset.requested));
                syncRow(row, false);
            });
            clearFormError();
            updateSummary();
            const firstInput = rows.length ? rows[0].querySelector('.pr-approval-input') : null;
            if (firstInput) firstInput.focus();
        });
    }

    form.addEventListener('submit', function (event) {
        if (finalSubmitting) {
            if (approveButton) approveButton.disabled = true;
            if (confirmApproveButton) confirmApproveButton.disabled = true;
            return;
        }

        event.preventDefault();
        clearFormError();
        let invalidInput = null;

        rows.forEach(function (row) {
            if (syncRow(row, true) === null && !invalidInput) invalidInput = row.querySelector('.pr-approval-input');
        });

        const summary = updateSummary();
        if (invalidInput) {
            showFormError('Kiểm tra lại số lượng duyệt đang nhập chưa hợp lệ.');
            invalidInput.focus();
            return;
        }

        if (summary.positiveCount === 0) {
            showFormError('Phải duyệt ít nhất một mặt hàng có số lượng lớn hơn 0. Nếu không mua mặt hàng nào, hãy dùng thao tác Từ chối.');
            const firstInput = rows.length ? rows[0].querySelector('.pr-approval-input') : null;
            if (firstInput) firstInput.focus();
            return;
        }

        if (summary.changedCount > 0 && note && !note.value.trim()) {
            note.classList.add('is-invalid');
            setNotePanel(true);
            showFormError('Nhập lý do vì có số lượng duyệt khác yêu cầu ban đầu.');
            note.focus();
            return;
        }

        populateConfirmation(summary);
        currentSummary = summary;
        if (approvalModal) approvalModal.show();
    });

    if (confirmApproveButton) {
        confirmApproveButton.addEventListener('click', function () {
            if (!currentSummary || currentSummary.positiveCount === 0) return;
            finalSubmitting = true;
            if (approvalModal) approvalModal.hide();
            form.requestSubmit();
        });
    }

    rows.forEach(function (row) { syncRow(row, false); });
    if (noteCount && note) noteCount.textContent = String(note.value.length);
    updateSummary();

    function setNotePanel(open) {
        if (!notePanel || !noteToggle) return;
        notePanel.hidden = !open;
        noteToggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    }

    function syncRow(row, showInvalid) {
        const input = row.querySelector('.pr-approval-input');
        const hidden = row.querySelector('.pr-approval-value');
        const value = parseQuantity(input.value);
        const valid = value !== null && value >= 0 && value <= 999999999999;
        input.classList.toggle('is-invalid', showInvalid && !valid);

        if (!valid) {
            hidden.value = '';
            setRowState(row, null, null);
            return null;
        }

        const rounded = Math.round((value + Number.EPSILON) * 1000) / 1000;
        const requested = Number(row.dataset.requested);
        hidden.value = String(rounded);
        setRowState(row, rounded, rounded - requested);
        return rounded;
    }

    function setRowState(row, approved, delta) {
        const badge = row.querySelector('.pr-delta-badge');
        row.classList.remove('is-unchanged', 'is-increased', 'is-decreased', 'is-removed');
        badge.className = 'pr-delta-badge';

        if (delta === null || !Number.isFinite(delta)) {
            badge.classList.add('is-invalid');
            badge.textContent = 'Chưa hợp lệ';
            return;
        }

        if (approved === 0) {
            row.classList.add('is-removed');
            badge.classList.add('is-removed');
            badge.textContent = 'Không mua';
        } else if (Math.abs(delta) < 0.0005) {
            row.classList.add('is-unchanged');
            badge.classList.add('is-unchanged');
            badge.textContent = 'Không đổi';
        } else if (delta > 0) {
            row.classList.add('is-increased');
            badge.classList.add('is-increased');
            badge.textContent = '+' + viDisplay.format(delta);
        } else {
            row.classList.add('is-decreased');
            badge.classList.add('is-decreased');
            badge.textContent = '−' + viDisplay.format(Math.abs(delta));
        }
    }

    function updateSummary() {
        const summary = {
            totalCount: rows.length,
            unchangedCount: 0,
            increasedCount: 0,
            decreasedCount: 0,
            removedCount: 0,
            changedCount: 0,
            positiveCount: 0,
            invalidCount: 0
        };

        rows.forEach(function (row) {
            const raw = row.querySelector('.pr-approval-value').value;
            if (raw === '') {
                summary.invalidCount++;
                return;
            }

            const approved = Number(raw);
            const requested = Number(row.dataset.requested);
            const delta = approved - requested;
            if (approved > 0) summary.positiveCount++;

            if (approved === 0) {
                summary.removedCount++;
                if (Math.abs(delta) >= 0.0005) summary.changedCount++;
            } else if (Math.abs(delta) < 0.0005) {
                summary.unchangedCount++;
            } else if (delta > 0) {
                summary.increasedCount++;
                summary.changedCount++;
            } else {
                summary.decreasedCount++;
                summary.changedCount++;
            }
        });

        setText(summaryElements.total, summary.totalCount);
        setText(summaryElements.unchanged, summary.unchangedCount);
        setText(summaryElements.increased, summary.increasedCount);
        setText(summaryElements.decreased, summary.decreasedCount);
        setText(summaryElements.removed, summary.removedCount);

        const changed = summary.changedCount > 0;
        if (note) note.required = changed;
        if (noteRequiredMark) noteRequiredMark.classList.toggle('d-none', !changed);
        if (noteShell) noteShell.classList.toggle('is-required', changed);
        if (noteToggleHint) noteToggleHint.textContent = changed ? 'Bắt buộc vì số lượng đã thay đổi' : 'Không bắt buộc khi giữ nguyên số lượng';
        if (noteHelp) noteHelp.textContent = changed
            ? 'Bắt buộc nhập lý do khi tăng, giảm hoặc không mua một mặt hàng.'
            : 'Ghi chú được lưu trong lịch sử xử lý.';
        if (changed) setNotePanel(true);

        currentSummary = summary;
        return summary;
    }

    function populateConfirmation(summary) {
        setText(confirmElements.unchanged, summary.unchangedCount);
        setText(confirmElements.increased, summary.increasedCount);
        setText(confirmElements.decreased, summary.decreasedCount);
        setText(confirmElements.removed, summary.removedCount);

        if (!confirmElements.description) return;
        if (summary.changedCount === 0) {
            confirmElements.description.textContent = 'Toàn bộ số lượng được giữ nguyên theo yêu cầu của nhân viên.';
        } else {
            confirmElements.description.textContent = summary.changedCount + ' mặt hàng đã được điều chỉnh. Lý do sẽ được lưu cùng lịch sử duyệt.';
        }
    }

    function showFormError(message) {
        let alert = byId('approvalFormError');
        if (!alert) {
            alert = document.createElement('div');
            alert.id = 'approvalFormError';
            alert.className = 'alert alert-danger d-flex align-items-center gap-2 mx-3 mt-3 mb-0';
            alert.setAttribute('role', 'alert');
            alert.innerHTML = '<i class="bx bx-error-circle fs-5"></i><span></span>';
            const noteSection = byId('approvalNoteShell');
            if (noteSection) form.insertBefore(alert, noteSection);
            else form.prepend(alert);
        }
        alert.querySelector('span').textContent = message;
        alert.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    function clearFormError() {
        const alert = byId('approvalFormError');
        if (alert) alert.remove();
    }

    function setText(element, value) {
        if (element) element.textContent = String(value);
    }

    function parseQuantity(raw) {
        let text = String(raw || '').trim().replace(/[\s\u00a0]/g, '').replace(/^\+/, '');
        if (!text || !/^\d[\d.,]*$/.test(text)) return null;

        const hasComma = text.includes(',');
        const hasDot = text.includes('.');
        let canonical = text;
        if (hasComma && hasDot) {
            const decimalSeparator = text.lastIndexOf(',') > text.lastIndexOf('.') ? ',' : '.';
            const groupingSeparator = decimalSeparator === ',' ? '.' : ',';
            if (text.split(decimalSeparator).length !== 2) return null;
            const parts = text.split(decimalSeparator);
            const groups = parts[0].split(groupingSeparator);
            if (groups.length > 1 && (groups[0].length < 1 || groups[0].length > 3 || groups.slice(1).some(function (group) { return group.length !== 3; }))) return null;
            canonical = groups.join('') + '.' + parts[1];
        } else if (hasComma || hasDot) {
            const separator = hasComma ? ',' : '.';
            if (text.split(separator).length !== 2) return null;
            canonical = text.replace(separator, '.');
        }

        const decimalPart = canonical.includes('.') ? canonical.split('.')[1] : '';
        if (decimalPart.length > 3) return null;
        const value = Number(canonical);
        return Number.isFinite(value) ? value : null;
    }
})();
