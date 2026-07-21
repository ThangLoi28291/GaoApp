(function () {
    const apiBase = "/admin/api/inventory-adjustment-documents";

    // =====================================================
    // STATE PHÂN TRANG
    // page: trang hiện tại
    // pageSize: số dòng/trang, cho phép đổi bằng combobox
    // totalPages: tổng số trang do API trả về hoặc tự tính
    // =====================================================
    let page = 1;
    let pageSize = 20;
    let totalPages = 1;
    let currentPermissions = {};

    const els = {
        keyword: document.getElementById("filterKeyword"),
        status: document.getElementById("filterStatus"),
        adjustmentType: document.getElementById("filterAdjustmentType"),
        fromDate: document.getElementById("filterFromDate"),
        toDate: document.getElementById("filterToDate"),

        btnSearch: document.getElementById("btnAdjSearch"),
        btnReload: document.getElementById("btnAdjReload"),
        btnCreate: document.getElementById("btnCreateDocument"),

        tableBody: document.getElementById("adjTableBody"),
        mobileList: document.getElementById("adjMobileList"),

        pagingInfo: document.getElementById("adjPagingInfo"),
        pageSize: document.getElementById("adjPageSize"),
        pageNumbers: document.getElementById("adjPageNumbers"),
        btnPrev: document.getElementById("btnPrevPage"),
        btnNext: document.getElementById("btnNextPage"),

        statTotal: document.getElementById("statTotal"),
        statPending: document.getElementById("statPending"),
        statApproved: document.getElementById("statApproved"),
        statClosed: document.getElementById("statClosed")
    };

    document.addEventListener("DOMContentLoaded", init);

    async function init() {
        bindEvents();

        currentPermissions = await loadPermissions();

        // Ẩn/hiện nút tạo phiếu theo quyền.
        if (els.btnCreate) {
            els.btnCreate.classList.toggle("d-none", !currentPermissions.canCreate);
        }

        await loadData();
    }

    function bindEvents() {
        els.btnSearch?.addEventListener("click", function () {
            page = 1;
            loadData();
        });

        els.btnReload?.addEventListener("click", function () {
            loadData();
        });

        els.btnPrev?.addEventListener("click", function () {
            if (page <= 1) return;
            page--;
            loadData();
        });

        els.btnNext?.addEventListener("click", function () {
            if (page >= totalPages) return;
            page++;
            loadData();
        });

        // Đổi số dòng/trang thì quay về trang 1.
        els.pageSize?.addEventListener("change", function () {
            pageSize = Number(this.value || 20);
            page = 1;
            loadData();
        });

        // Enter trong ô tìm kiếm sẽ lọc.
        els.keyword?.addEventListener("keydown", function (e) {
            if (e.key === "Enter") {
                page = 1;
                loadData();
            }
        });
    }

    async function loadPermissions() {
        try {
            const res = await fetch(`${apiBase}/permissions`, {
                headers: { "Accept": "application/json" }
            });

            if (!res.ok) return {};
            return await res.json();
        } catch {
            return {};
        }
    }

    async function loadData() {
        setLoading();

        const params = new URLSearchParams();
        params.set("page", page);
        params.set("pageSize", pageSize);

        appendIfValue(params, "keyword", els.keyword?.value);
        appendIfValue(params, "status", els.status?.value);
        appendIfValue(params, "adjustmentType", els.adjustmentType?.value);
        appendIfValue(params, "fromDate", els.fromDate?.value);
        appendIfValue(params, "toDate", els.toDate?.value);

        try {
            const res = await fetch(`${apiBase}?${params.toString()}`, {
                headers: { "Accept": "application/json" }
            });

            if (!res.ok) {
                throw new Error("Không tải được danh sách phiếu.");
            }

            const data = await res.json();

            renderTable(data.items || []);
            renderMobile(data.items || []);
            renderPaging(data);
            renderStats(data.items || [], data.totalItems || 0);
        } catch (err) {
            renderError(err.message || "Có lỗi xảy ra.");
        }
    }

    function appendIfValue(params, key, value) {
        if (value !== undefined && value !== null && String(value).trim() !== "") {
            params.set(key, String(value).trim());
        }
    }

    function setLoading() {
        if (els.tableBody) {
            els.tableBody.innerHTML = `
                <tr>
                    <td colspan="8" class="text-center text-muted py-4">
                        Đang tải dữ liệu...
                    </td>
                </tr>`;
        }

        if (els.mobileList) {
            els.mobileList.innerHTML =
                `<div class="text-center text-muted py-4">Đang tải dữ liệu...</div>`;
        }
    }

    function renderTable(items) {
        if (!els.tableBody) return;

        if (!items.length) {
            els.tableBody.innerHTML = `
                <tr>
                    <td colspan="8" class="text-center text-muted py-4">
                        Chưa có phiếu điều chỉnh kho.
                    </td>
                </tr>`;
            return;
        }

        els.tableBody.innerHTML = items.map(x => `
            <tr>
                <td>
                    <a class="fw-bold" href="/admin/inventory-adjustment-documents/${x.id}">
                        ${escapeHtml(x.documentNo)}
                    </a>
                </td>
                <td>${formatDate(x.documentDate)}</td>
                <td>${escapeHtml(x.warehouseName || "-")}</td>
                <td>${renderType(x.adjustmentType, x.adjustmentTypeText)}</td>
                <td>${renderStatus(x.status, x.statusText)}</td>
                <td class="text-end">${x.lineCount || 0}</td>
                <td><div class="adj-note">${escapeHtml(x.note || "-")}</div></td>
                <td class="text-end">
                    ${renderActions(x)}
                </td>
            </tr>
        `).join("");
    }

    function renderMobile(items) {
        if (!els.mobileList) return;

        if (!items.length) {
            els.mobileList.innerHTML =
                `<div class="text-center text-muted py-4">Chưa có phiếu điều chỉnh kho.</div>`;
            return;
        }

        els.mobileList.innerHTML = items.map(x => `
            <div class="adj-mobile-card">
                <div class="d-flex justify-content-between gap-2">
                    <div>
                        <div class="adj-mobile-title">${escapeHtml(x.documentNo)}</div>
                        <div class="adj-mobile-meta">
                            ${formatDate(x.documentDate)} · ${escapeHtml(x.warehouseName || "-")}
                        </div>
                    </div>
                    <div>${renderStatus(x.status, x.statusText)}</div>
                </div>

                <div class="mt-2">
                    ${renderType(x.adjustmentType, x.adjustmentTypeText)}
                    <span class="text-muted ms-2">${x.lineCount || 0} dòng</span>
                </div>

                <div class="text-muted mt-2">${escapeHtml(x.note || "")}</div>

                <div class="d-flex gap-2 mt-3 flex-wrap">
                    ${renderActions(x, true)}
                </div>
            </div>
        `).join("");
    }

    function renderActions(x, mobile) {
        const btnSize = mobile ? "btn-sm" : "btn-sm";

        let html = `
            <a class="btn btn-outline-primary ${btnSize}"
               href="/admin/inventory-adjustment-documents/${x.id}">
                Xem
            </a>`;

        if (x.status === 0 && currentPermissions.canUpdate) {
            html += `
                <a class="btn btn-outline-secondary ${btnSize}"
                   href="/admin/inventory-adjustment-documents/${x.id}/edit">
                    Sửa
                </a>`;
        }

        return html;
    }

    // =====================================================
    // PHÂN TRANG
    // - Cập nhật tổng phiếu
    // - Disable nút Trước/Sau
    // - Render số trang dạng: 1 ... 4 5 [6] 7 8 ... 20
    // =====================================================
    function renderPaging(data) {
        const totalItems = data.totalItems || 0;

        totalPages = data.totalPages || Math.max(1, Math.ceil(totalItems / pageSize));
        page = data.page || page;

        if (page > totalPages) page = totalPages;
        if (page < 1) page = 1;

        if (els.pagingInfo) {
            els.pagingInfo.textContent =
                totalItems > 0
                    ? `${totalItems} phiếu · Trang ${page}/${totalPages}`
                    : "0 phiếu";
        }

        if (els.btnPrev) {
            els.btnPrev.disabled = page <= 1;
        }

        if (els.btnNext) {
            els.btnNext.disabled = page >= totalPages;
        }

        renderPageNumbers();
    }

    function renderPageNumbers() {
        if (!els.pageNumbers) return;

        const pages = [];
        const start = Math.max(1, page - 2);
        const end = Math.min(totalPages, page + 2);

        if (start > 1) {
            pages.push(1);
            if (start > 2) pages.push("...");
        }

        for (let i = start; i <= end; i++) {
            pages.push(i);
        }

        if (end < totalPages) {
            if (end < totalPages - 1) pages.push("...");
            pages.push(totalPages);
        }

        els.pageNumbers.innerHTML = pages.map(p => {
            if (p === "...") {
                return `<span class="adj-page-dot">...</span>`;
            }

            const activeClass = p === page ? "btn-primary" : "btn-outline-secondary";

            return `
                <button type="button"
                        class="btn btn-sm ${activeClass} js-page-number"
                        data-page="${p}">
                    ${p}
                </button>`;
        }).join("");

        document.querySelectorAll(".js-page-number").forEach(btn => {
            btn.addEventListener("click", function () {
                const nextPage = Number(this.dataset.page || 1);
                if (nextPage === page) return;

                page = nextPage;
                loadData();
            });
        });
    }

    function renderStats(items, totalItems) {
        const pending = items.filter(x => x.status === 1).length;
        const approved = items.filter(x => x.status === 2).length;
        const closed = items.filter(x => x.status === 3 || x.status === 4).length;

        if (els.statTotal) els.statTotal.textContent = totalItems;
        if (els.statPending) els.statPending.textContent = pending;
        if (els.statApproved) els.statApproved.textContent = approved;
        if (els.statClosed) els.statClosed.textContent = closed;
    }

    function renderType(type, text) {
        const label = escapeHtml(text || "");

        if (type === 30) {
            return `<span class="adj-type-in">+ ${label || "Điều chỉnh tăng"}</span>`;
        }

        if (type === 31) {
            return `<span class="adj-type-out">- ${label || "Điều chỉnh giảm"}</span>`;
        }

        return `<span>${label || "-"}</span>`;
    }

    function renderStatus(status, text) {
        const label = getStatusText(status, text);
        const cls = getStatusClass(status);

        return `<span class="adj-badge ${cls}">${label}</span>`;
    }

    function getStatusText(status, fallback) {
        switch (status) {
            case 0: return "Nháp";
            case 1: return "Chờ duyệt";
            case 2: return "Đã duyệt";
            case 3: return "Từ chối";
            case 4: return "Đã hủy";
            default: return fallback || "-";
        }
    }

    function getStatusClass(status) {
        switch (status) {
            case 0: return "adj-badge-draft";
            case 1: return "adj-badge-pending";
            case 2: return "adj-badge-approved";
            case 3: return "adj-badge-rejected";
            case 4: return "adj-badge-cancelled";
            default: return "adj-badge-draft";
        }
    }

    function renderError(message) {
        if (els.tableBody) {
            els.tableBody.innerHTML = `
                <tr>
                    <td colspan="8" class="text-center text-danger py-4">
                        ${escapeHtml(message)}
                    </td>
                </tr>`;
        }

        if (els.mobileList) {
            els.mobileList.innerHTML =
                `<div class="text-center text-danger py-4">${escapeHtml(message)}</div>`;
        }
    }

    function formatDate(value) {
        if (!value) return "-";

        const d = new Date(value);
        if (Number.isNaN(d.getTime())) return "-";

        return d.toLocaleDateString("vi-VN");
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }
})();