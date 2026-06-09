(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {
        const shell = document.querySelector(".av-shell");
        if (!shell) return;

        const ddlAttribute = document.getElementById("ddlAttribute");
        const txtSearch = document.getElementById("txtSearch");
        const btnClearSearch = document.getElementById("btnClearSearch");
        const ddlPageSize = document.getElementById("ddlPageSize");
        const btnReload = document.getElementById("btnReload");
        const wrapper = document.getElementById("attributeValueTableWrapper");
        const ddlStatus = document.getElementById("ddlStatus");

        const searchUrl = shell.dataset.searchUrl;
        const toggleUrl = shell.dataset.toggleUrl;
        const deleteUrl = shell.dataset.deleteUrl;

        let typingTimer = null;
        let deleteId = 0;
        let currentPage = 1;
        let isLoading = false;

        function getToken() {
            const el = document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]');
            return el ? el.value : "";
        }

        function toast(type, message) {
            if (window.toastr) {
                toastr[type](message);
                return;
            }

            alert(message);
        }

        function setLoading(value) {
            isLoading = value;
            shell.classList.toggle("is-loading", value);

            if (btnReload) {
                btnReload.disabled = value;
                btnReload.innerHTML = value
                    ? '<span class="spinner-border spinner-border-sm"></span>'
                    : '<i class="bx bx-refresh"></i>';
            }
        }

        function updateClearButtonVisibility() {
            const hasText = (txtSearch.value || "").trim().length > 0;
            btnClearSearch.classList.toggle("d-none", !hasText);
        }

        async function loadPage(page) {
            if (isLoading) return;

            currentPage = page || 1;

            const attributeId = ddlAttribute.value || "";
            const search = txtSearch.value || "";
            const pageSize = ddlPageSize.value || "20";
            const status = ddlStatus ? ddlStatus.value : "";

            const url = searchUrl
                + "?attributeId=" + encodeURIComponent(attributeId)
                + "&status=" + encodeURIComponent(status)
                + "&search=" + encodeURIComponent(search)
                + "&page=" + encodeURIComponent(currentPage)
                + "&pageSize=" + encodeURIComponent(pageSize);

            try {
                setLoading(true);

                const response = await fetch(url, {
                    method: "GET",
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    }
                });

                if (!response.ok) {
                    toast("error", "Không tải được danh sách.");
                    return;
                }

                const html = await response.text();
                wrapper.innerHTML = html;
                bindTableEvents();
            } catch (error) {
                console.error(error);
                toast("error", "Có lỗi khi tải dữ liệu.");
            } finally {
                setLoading(false);
            }
        }

        function debounceLoad() {
            clearTimeout(typingTimer);
            typingTimer = setTimeout(function () {
                loadPage(1);
            }, 300);
        }

        function bindPaginationEvents() {
            wrapper.querySelectorAll(".js-page-link").forEach(function (a) {
                a.addEventListener("click", function (e) {
                    e.preventDefault();

                    if (this.closest(".page-item")?.classList.contains("disabled")) {
                        return;
                    }

                    const page = parseInt(this.dataset.page || "1");
                    if (page > 0) loadPage(page);
                });
            });
        }

        function bindToggleStatusEvents() {
            wrapper.querySelectorAll(".js-toggle-status").forEach(function (btn) {
                btn.addEventListener("click", async function () {
                    const id = this.dataset.id;
                    if (!id) return;

                    const token = getToken();
                    if (!token) {
                        toast("error", "Thiếu AntiForgeryToken.");
                        return;
                    }

                    try {
                        this.disabled = true;

                        const response = await fetch(toggleUrl, {
                            method: "POST",
                            headers: {
                                "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                                "X-Requested-With": "XMLHttpRequest"
                            },
                            body:
                                "id=" + encodeURIComponent(id)
                                + "&__RequestVerificationToken=" + encodeURIComponent(token)
                        });

                        const data = await response.json();

                        toast(data.success ? "success" : "error", data.message || "Đã xử lý.");

                        if (data.success) {
                            loadPage(currentPage);
                        }
                    } catch (error) {
                        console.error(error);
                        toast("error", "Không cập nhật được trạng thái.");
                    } finally {
                        this.disabled = false;
                    }
                });
            });
        }

        function bindDeleteEvents() {
            wrapper.querySelectorAll(".js-delete").forEach(function (btn) {
                btn.addEventListener("click", function () {
                    deleteId = parseInt(this.dataset.id || "0");
                    document.getElementById("deleteName").textContent = this.dataset.name || "";

                    const modalEl = document.getElementById("deleteModal");
                    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
                    modal.show();
                });
            });
        }

        function bindTableEvents() {
            bindPaginationEvents();
            bindToggleStatusEvents();
            bindDeleteEvents();
        }

        document.getElementById("btnConfirmDelete")?.addEventListener("click", async function () {
            if (!deleteId) return;

            const token = getToken();
            if (!token) {
                toast("error", "Thiếu AntiForgeryToken.");
                return;
            }

            try {
                this.disabled = true;
                this.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Đang xóa';

                const response = await fetch(deleteUrl, {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                        "X-Requested-With": "XMLHttpRequest"
                    },
                    body:
                        "id=" + encodeURIComponent(deleteId)
                        + "&__RequestVerificationToken=" + encodeURIComponent(token)
                });

                const data = await response.json();

                toast(data.success ? "success" : "error", data.message || "Đã xử lý.");

                if (!data.success) return;

                const modalEl = document.getElementById("deleteModal");
                bootstrap.Modal.getInstance(modalEl)?.hide();

                deleteId = 0;
                loadPage(currentPage);
            } catch (error) {
                console.error(error);
                toast("error", "Không xóa được giá trị.");
            } finally {
                this.disabled = false;
                this.innerHTML = "Xóa";
            }
        });

        ddlAttribute?.addEventListener("change", function () {
            loadPage(1);
        });

        txtSearch?.addEventListener("input", function () {
            updateClearButtonVisibility();
            debounceLoad();
        });

        btnClearSearch?.addEventListener("click", function () {
            txtSearch.value = "";
            updateClearButtonVisibility();
            loadPage(1);
            txtSearch.focus();
        });

        ddlPageSize?.addEventListener("change", function () {
            loadPage(1);
        });

        btnReload?.addEventListener("click", function () {
            loadPage(currentPage);
        });
        ddlStatus?.addEventListener("change", function () {
            loadPage(1);
        });

        updateClearButtonVisibility();
        bindTableEvents();
    });
})();