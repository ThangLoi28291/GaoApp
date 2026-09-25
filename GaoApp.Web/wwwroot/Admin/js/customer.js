(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {
        const shell = document.querySelector("[data-customer-shell]");
        if (!shell) return;

        const wrapper = document.getElementById("customerTableWrapper");
        const search = document.getElementById("txtSearch");
        const clearSearch = document.getElementById("btnClearSearch");
        const priceTier = document.getElementById("ddlPriceTier");
        const status = document.getElementById("ddlStatus");
        const debt = document.getElementById("ddlDebt");
        const pageSize = document.getElementById("ddlPageSize");
        const filterSheet = document.getElementById("customerFilterSheet");
        const filterOpen = document.getElementById("customerFilterOpen");
        const filterClose = document.getElementById("customerFilterClose");
        const filterOverlay = document.getElementById("customerFilterOverlay");
        if (!wrapper || !search || !clearSearch || !priceTier || !status || !debt || !pageSize) return;

        let customerSearchTimer = null;
        let customerRequestController = null;
        let customerRequestSequence = 0;
        let currentPage = Number.parseInt(shell.dataset.page || "1", 10) || 1;

        function token() {
            return document.querySelector('#customerAntiForgeryForm input[name="__RequestVerificationToken"]')?.value || "";
        }

        function notify(type, message) {
            if (window.GaoAppNotify && typeof window.GaoAppNotify[type] === "function") {
                window.GaoAppNotify[type](message);
            } else if (window.toastr && typeof window.toastr[type] === "function") {
                window.toastr[type](message);
            } else {
                console[type === "error" ? "error" : "log"](message);
            }
        }

        function escapeHtml(value) {
            const element = document.createElement("div");
            element.textContent = value == null || value === "" ? "—" : String(value);
            return element.innerHTML;
        }

        function updateKpis() {
            const result = wrapper.querySelector("[data-customer-result]");
            if (!result) return;
            document.getElementById("customerTotalCount").textContent = result.dataset.totalCustomers || "0";
            document.getElementById("customerActiveCount").textContent = result.dataset.activeCustomers || "0";
            document.getElementById("customerInactiveCount").textContent = result.dataset.inactiveCustomers || "0";
            document.getElementById("customerDebtEnabledCount").textContent = result.dataset.debtEnabledCustomers || "0";
        }

        async function loadPage(page) {
            const requestedPage = Math.max(1, Number.parseInt(page || "1", 10) || 1);
            customerRequestController?.abort();
            customerRequestController = new AbortController();
            const requestController = customerRequestController;
            const requestSequence = ++customerRequestSequence;
            const query = new URLSearchParams({
                searchString: search.value || "",
                priceTier: priceTier.value || "",
                status: status.value || "",
                haveDebt: debt.value || "",
                page: requestedPage.toString(),
                pageSize: pageSize.value || "20"
            });

            wrapper.setAttribute("aria-busy", "true");
            shell.classList.add("is-loading");
            try {
                const response = await fetch(shell.dataset.searchUrl + "?" + query.toString(), {
                    headers: { "X-Requested-With": "XMLHttpRequest" },
                    signal: requestController.signal
                });
                if (!response.ok) throw new Error("Không tải được danh sách khách hàng.");
                const html = await response.text();
                if (requestSequence !== customerRequestSequence || requestController.signal.aborted) return;
                wrapper.innerHTML = html;
                currentPage = requestedPage;
                shell.dataset.page = requestedPage.toString();
                updateKpis();
                bindResults();
            } catch (error) {
                if (error?.name !== "AbortError") notify("error", error?.message || "Có lỗi khi tải dữ liệu.");
            } finally {
                if (requestSequence === customerRequestSequence) {
                    wrapper.setAttribute("aria-busy", "false");
                    shell.classList.remove("is-loading");
                }
            }
        }

        async function showQuickView(id) {
            const response = await fetch(shell.dataset.quickViewUrl + "?id=" + encodeURIComponent(id), {
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            if (!response.ok) {
                notify("error", "Không tải được thông tin khách hàng.");
                return;
            }
            const item = await response.json();
            document.getElementById("customerQuickViewName").textContent = item.name || "Chi tiết khách hàng";
            document.getElementById("customerQuickViewCode").textContent = item.code || "Chưa có mã khách hàng";
            document.getElementById("customerQuickViewBody").innerHTML = [
                ["Điện thoại", item.phone], ["Email", item.email], ["Mã số thuế", item.taxCode],
                ["Nhóm giá", item.priceTier === "WHOLESALE" ? "Khách sỉ" : "Khách lẻ"],
                ["Công nợ", item.haveDebt ? "Được phép" : "Không được phép"],
                ["Trạng thái", item.isActive ? "Đang hoạt động" : "Ngừng hoạt động"],
                ["Địa chỉ", item.address], ["Ghi chú", item.note],
                ["Nhóm dữ liệu", item.customerGroup],
                ["Nguồn hồ sơ", item.isImportedFromOldSystem ? "Dữ liệu chuyển đổi" : "Tạo trong GaoApp"]
            ].map(function (entry) {
                return '<div class="customer-quick-view__item"><span>' + escapeHtml(entry[0]) + '</span><strong>' + escapeHtml(entry[1]) + '</strong></div>';
            }).join("");
            const editLink = document.getElementById("customerQuickViewEdit");
            if (editLink) editLink.href = "/Admin/Customer/Edit/" + encodeURIComponent(item.id);
            window.bootstrap.Modal.getOrCreateInstance(document.getElementById("customerQuickViewModal")).show();
        }

        async function toggleCustomer(button) {
            const active = button.dataset.status === "true";
            const name = button.dataset.name || "khách hàng này";
            if (!window.Swal?.fire) {
                notify("error", "Không thể mở hộp xác nhận. Vui lòng tải lại trang.");
                return;
            }
            let actionData = null;
            const result = await window.Swal.fire({
                icon: active ? "warning" : "question",
                title: active ? "Ngừng hoạt động khách hàng?" : "Kích hoạt khách hàng?",
                text: "Xác nhận " + (active ? "ngừng hoạt động" : "kích hoạt") + " “" + name + "”?",
                showCancelButton: true,
                confirmButtonText: active ? "Ngừng hoạt động" : "Kích hoạt",
                cancelButtonText: "Hủy",
                reverseButtons: true,
                buttonsStyling: false,
                customClass: {
                    popup: "gds-swal-popup", confirmButton: active ? "btn btn-warning" : "btn btn-primary",
                    cancelButton: "btn btn-label-secondary"
                },
                preConfirm: async function () {
                    const response = await fetch(shell.dataset.toggleUrl, {
                        method: "POST",
                        headers: { "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8", "X-Requested-With": "XMLHttpRequest" },
                        body: "id=" + encodeURIComponent(button.dataset.id) + "&__RequestVerificationToken=" + encodeURIComponent(token())
                    });
                    actionData = await response.json();
                    if (!response.ok || !actionData.success) {
                        window.Swal.showValidationMessage(actionData?.message || "Không cập nhật được trạng thái.");
                        return false;
                    }
                    return actionData;
                }
            });
            if (result.isConfirmed && actionData) {
                notify("success", actionData.message);
                await loadPage(currentPage);
            }
        }

        function bindResults() {
            wrapper.querySelectorAll(".js-customer-page").forEach(function (link) {
                link.addEventListener("click", function (event) {
                    event.preventDefault();
                    if (!link.closest(".page-item")?.classList.contains("disabled")) loadPage(link.dataset.page);
                });
            });
            wrapper.querySelectorAll(".js-customer-quick-view").forEach(function (button) {
                button.addEventListener("click", function (event) { event.stopPropagation(); showQuickView(button.dataset.id); });
            });
            wrapper.querySelectorAll(".js-customer-toggle").forEach(function (button) {
                button.addEventListener("click", function (event) { event.stopPropagation(); toggleCustomer(button); });
            });
            wrapper.querySelectorAll("[data-customer-quick-view]").forEach(function (row) {
                row.addEventListener("dblclick", function (event) { if (!event.target.closest("a,button")) showQuickView(row.dataset.id); });
                row.addEventListener("keydown", function (event) { if (event.key === "Enter") showQuickView(row.dataset.id); });
                if (row.classList.contains("customer-mobile-card")) {
                    row.addEventListener("click", function (event) { if (!event.target.closest("a,button")) showQuickView(row.dataset.id); });
                }
            });
        }

        function scheduleSearch() {
            window.clearTimeout(customerSearchTimer);
            customerSearchTimer = window.setTimeout(function () { loadPage(1); }, 350);
        }
        search.addEventListener("input", function () { clearSearch.classList.toggle("d-none", !search.value); scheduleSearch(); });
        clearSearch.addEventListener("click", function () { search.value = ""; clearSearch.classList.add("d-none"); loadPage(1); search.focus(); });
        [priceTier, status, debt, pageSize].forEach(function (control) { control.addEventListener("change", function () { loadPage(1); }); });
        function setFilterSheet(open) {
            filterSheet?.classList.toggle("is-open", open);
            filterOverlay?.classList.toggle("is-open", open);
            document.body.classList.toggle("customer-filter-sheet-open", open);
            filterOpen?.setAttribute("aria-expanded", open ? "true" : "false");
        }
        filterOpen?.addEventListener("click", function () { setFilterSheet(true); window.setTimeout(function () { search.focus(); }, 100); });
        filterClose?.addEventListener("click", function () { setFilterSheet(false); });
        filterOverlay?.addEventListener("click", function () { setFilterSheet(false); });
        document.addEventListener("keydown", function (event) { if (event.key === "Escape") setFilterSheet(false); });
        clearSearch.classList.toggle("d-none", !search.value);
        bindResults();
        updateKpis();
    });
})();
