let auditPage = 1;
const auditPageSize = 20;
let auditTotalPages = 1;
let auditDetailModal = null;

document.addEventListener('DOMContentLoaded', function () {
    const modalEl = document.getElementById('auditLogDetailModal');
    if (modalEl) {
        auditDetailModal = new bootstrap.Modal(modalEl);
    }

    document.getElementById('btnSearch')?.addEventListener('click', function () {
        auditPage = 1;
        loadAuditLogs();
    });

    document.getElementById('btnPrev')?.addEventListener('click', function () {
        if (auditPage > 1) {
            auditPage--;
            loadAuditLogs();
        }
    });

    document.getElementById('btnNext')?.addEventListener('click', function () {
        if (auditPage < auditTotalPages) {
            auditPage++;
            loadAuditLogs();
        }
    });

    loadAuditLogs();
});

async function loadAuditLogs() {
    const query = new URLSearchParams({
        page: auditPage,
        pageSize: auditPageSize
    });

    const fromUtc = document.getElementById('fromUtc')?.value;
    const toUtc = document.getElementById('toUtc')?.value;
    const actorUserId = document.getElementById('actorUserId')?.value;
    const entityName = document.getElementById('entityName')?.value;
    const entityId = document.getElementById('entityId')?.value;
    const keyword = document.getElementById('keyword')?.value;

    if (fromUtc) query.append('fromUtc', new Date(fromUtc).toISOString());
    if (toUtc) query.append('toUtc', new Date(toUtc).toISOString());
    if (actorUserId) query.append('actorUserId', actorUserId);
    if (entityName) query.append('entityName', entityName);
    if (entityId) query.append('entityId', entityId);
    if (keyword) query.append('keyword', keyword);

    const response = await fetch(`/admin/api/audit-logs?${query.toString()}`);
    const result = await response.json();

    renderAuditTable(result.items || []);
    auditTotalPages = result.totalPages || 1;
    document.getElementById('pagingInfo').textContent = `Trang ${result.page || 1} / ${auditTotalPages}`;
}

function renderAuditTable(items) {
    const tbody = document.querySelector('#auditLogTable tbody');
    if (!tbody) return;

    tbody.innerHTML = '';

    if (!items.length) {
        tbody.innerHTML = `<tr><td colspan="9" class="text-center text-muted">Không có dữ liệu</td></tr>`;
        return;
    }

    for (const item of items) {
        const tr = document.createElement('tr');

        tr.innerHTML = `
            <td>${escapeHtml(item.createdAtUtc || '')}</td>
            <td>${escapeHtml(item.actorUserName || '')}</td>
            <td>${escapeHtml(item.module || '')}</td>
            <td>${escapeHtml(item.actionType || '')}</td>
            <td>${escapeHtml(item.entityName || '')}</td>
            <td>${escapeHtml(item.entityId || '')}</td>
            <td>${escapeHtml(item.summary || '')}</td>
            <td>${item.isSuccess ? '<span class="badge bg-success">OK</span>' : '<span class="badge bg-danger">Fail</span>'}</td>
            <td><button class="btn btn-sm btn-outline-primary" data-id="${item.id}">Xem</button></td>
        `;

        tr.querySelector('button')?.addEventListener('click', function () {
            loadAuditDetail(item.id);
        });

        tbody.appendChild(tr);
    }
}

async function loadAuditDetail(id) {
    const response = await fetch(`/admin/api/audit-logs/${id}`);
    if (!response.ok) {
        alert('Không tải được chi tiết log.');
        return;
    }

    const item = await response.json();

    setText('detailCreatedAt', item.createdAtUtc || '');
    setText('detailUser', item.actorUserName || '');
    setText('detailModule', item.module || '');
    setText('detailAction', item.actionType || '');
    setText('detailEntity', `${item.entityName || ''} #${item.entityId || ''}`);
    setText('detailSummary', item.summary || '');
    setText('detailPath', item.path || '');
    setText('detailIp', item.ipAddress || '');
    setText('detailTraceId', item.traceId || '');
    setPre('detailChanged', item.changedColumnsJson || '');
    setPre('detailOld', item.oldValuesJson || '');
    setPre('detailNew', item.newValuesJson || '');
    setPre('detailError', item.errorMessage || '');

    auditDetailModal?.show();
}

function setText(id, value) {
    const el = document.getElementById(id);
    if (el) el.textContent = value;
}

function setPre(id, value) {
    const el = document.getElementById(id);
    if (el) el.textContent = value;
}

function escapeHtml(text) {
    if (text == null) return '';
    return String(text)
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');
}