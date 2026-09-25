(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {
        const shell = document.querySelector("[data-category-shell]");
        if (!shell) return;

        const txtSearch = document.getElementById("txtSearch");
        const btnClearSearch = document.getElementById("btnClearSearch");
        const ddlStatus = document.getElementById("ddlStatus");
        const ddlPageSize = document.getElementById("ddlPageSize");
        const wrapper = document.getElementById("categoryTableWrapper");
        const totalCount = document.getElementById("categoryTotalCount");
        const activeCount = document.getElementById("categoryActiveCount");
        const inactiveCount = document.getElementById("categoryInactiveCount");

        if (!txtSearch || !btnClearSearch || !ddlStatus || !ddlPageSize || !wrapper) {
            return;
        }

        const searchUrl = shell.dataset.searchUrl;
        const toggleUrl = shell.dataset.toggleUrl;
        const deleteUrl = shell.dataset.deleteUrl;

        let typingTimer = null;
        let currentPage = Number.parseInt(shell.dataset.page || "1", 10) || 1;
        let loadController = null;

        function getToken() {
            return document.querySelector(
                '#antiForgeryForm input[name="__RequestVerificationToken"]')?.value || "";
        }

        function notify(type, message) {
            const safeMessage = message || "Đã xử lý yêu cầu.";
            const notifier = window.GaoAppNotify;

            if (notifier && typeof notifier[type] === "function") {
                notifier[type](safeMessage);
                return;
            }

            if (window.toastr && typeof window.toastr[type] === "function") {
                window.toastr[type](safeMessage);
                return;
            }

            window.alert(safeMessage);
        }

        function setResultsBusy(value) {
            shell.classList.toggle("is-loading", value);
            wrapper.setAttribute("aria-busy", value ? "true" : "false");
        }

        function setActionBusy(button, value) {
            button.disabled = value;
            button.setAttribute("aria-busy", value ? "true" : "false");
        }

        function updateClearButtonVisibility() {
            btnClearSearch.classList.toggle(
                "d-none",
                txtSearch.value.trim().length === 0);
        }

        function disposeTooltips() {
            if (!window.bootstrap?.Tooltip) return;

            wrapper.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(function (element) {
                window.bootstrap.Tooltip.getInstance(element)?.dispose();
            });
        }

        function initializeTooltips() {
            if (!window.bootstrap?.Tooltip) return;

            wrapper.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(function (element) {
                window.bootstrap.Tooltip.getOrCreateInstance(element);
            });
        }

        function updateSummaryCounts() {
            const result = wrapper.querySelector("[data-category-result]");
            if (!result) return;

            if (totalCount) totalCount.textContent = result.dataset.totalCategories || "0";
            if (activeCount) activeCount.textContent = result.dataset.activeCategories || "0";
            if (inactiveCount) inactiveCount.textContent = result.dataset.inactiveCategories || "0";
        }

        async function readJson(response, fallbackMessage) {
            let data;

            try {
                data = await response.json();
            } catch {
                throw new Error(fallbackMessage);
            }

            if (!response.ok) {
                throw new Error(data?.message || fallbackMessage);
            }

            return data;
        }

        async function postAction(url, id, fallbackMessage) {
            const token = getToken();
            if (!token) {
                throw new Error("Thiếu AntiForgeryToken. Vui lòng tải lại trang.");
            }

            const response = await fetch(url, {
                method: "POST",
                headers: {
                    "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                    "X-Requested-With": "XMLHttpRequest"
                },
                body:
                    "id=" + encodeURIComponent(id)
                    + "&__RequestVerificationToken=" + encodeURIComponent(token)
            });

            const data = await readJson(response, fallbackMessage);
            if (!data.success) {
                throw new Error(data.message || fallbackMessage);
            }

            return data;
        }

        async function loadPage(page) {
            const requestedPage = Math.max(Number.parseInt(page || "1", 10) || 1, 1);

            loadController?.abort();
            const requestController = new AbortController();
            loadController = requestController;

            const parameters = new URLSearchParams({
                searchString: txtSearch.value || "",
                status: ddlStatus.value || "",
                page: requestedPage.toString(),
                pageSize: ddlPageSize.value || "20"
            });

            try {
                setResultsBusy(true);

                const response = await fetch(searchUrl + "?" + parameters.toString(), {
                    method: "GET",
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    },
                    signal: requestController.signal
                });

                if (!response.ok) {
                    throw new Error("Không tải được danh sách danh mục.");
                }

                const html = await response.text();
                if (requestController.signal.aborted) return;

                disposeTooltips();
                wrapper.innerHTML = html;
                currentPage = requestedPage;
                shell.dataset.page = requestedPage.toString();
                updateSummaryCounts();
                bindTableEvents();
            } catch (error) {
                if (error?.name !== "AbortError") {
                    console.error(error);
                    notify("error", error?.message || "Có lỗi khi tải dữ liệu.");
                }
            } finally {
                if (loadController === requestController) {
                    loadController = null;
                    setResultsBusy(false);
                }
            }
        }

        function debounceLoad() {
            window.clearTimeout(typingTimer);
            typingTimer = window.setTimeout(function () {
                loadPage(1);
            }, 300);
        }

        async function confirmWithSweetAlert(options, action) {
            let actionResult = null;
            const result = await window.Swal.fire({
                icon: options.icon,
                title: options.title,
                text: options.text,
                showCancelButton: true,
                confirmButtonText: options.confirmText,
                cancelButtonText: "Hủy",
                reverseButtons: true,
                buttonsStyling: false,
                showLoaderOnConfirm: true,
                customClass: {
                    popup: "gds-swal-popup",
                    title: "gds-swal-title",
                    htmlContainer: "gds-swal-copy",
                    actions: "gds-swal-actions",
                    confirmButton: options.danger
                        ? "btn btn-danger gds-swal-confirm"
                        : "btn btn-primary gds-swal-confirm",
                    cancelButton: "btn btn-label-secondary gds-swal-cancel"
                },
                preConfirm: async function () {
                    try {
                        actionResult = await action();
                        return actionResult;
                    } catch (error) {
                        window.Swal.showValidationMessage(
                            error?.message || "Không thể xử lý yêu cầu.");
                        return false;
                    }
                },
                allowOutsideClick: function () {
                    return !window.Swal.isLoading();
                }
            });

            return result.isConfirmed ? actionResult : null;
        }

        async function runConfirmedAction(button, options, action) {
            window.bootstrap?.Tooltip?.getInstance(button)?.dispose();

            try {
                setActionBusy(button, true);
                let data = null;

                if (window.Swal?.fire) {
                    data = await confirmWithSweetAlert(options, action);
                } else if (window.confirm(options.text)) {
                    data = await action();
                }

                if (!data) return;

                notify("success", data.message || options.successMessage);
                await loadPage(currentPage);
            } catch (error) {
                console.error(error);
                notify("error", error?.message || options.failureMessage);
            } finally {
                setActionBusy(button, false);
            }
        }

        function bindPaginationEvents() {
            wrapper.querySelectorAll(".js-page-link").forEach(function (link) {
                link.addEventListener("click", function (event) {
                    event.preventDefault();

                    if (link.closest(".page-item")?.classList.contains("disabled")) {
                        return;
                    }

                    const page = Number.parseInt(link.dataset.page || "1", 10);
                    if (page > 0) loadPage(page);
                });
            });
        }

        function bindToggleStatusEvents() {
            wrapper.querySelectorAll(".js-toggle-status").forEach(function (button) {
                button.addEventListener("click", function () {
                    const id = button.dataset.id;
                    if (!id) return;

                    const isActive = button.dataset.status === "true";
                    const name = button.dataset.name || "danh mục này";
                    const verb = isActive ? "ngừng hoạt động" : "kích hoạt lại";

                    runConfirmedAction(
                        button,
                        {
                            icon: isActive ? "warning" : "question",
                            title: isActive
                                ? "Ngừng hoạt động danh mục?"
                                : "Kích hoạt lại danh mục?",
                            text: "Bạn có chắc muốn " + verb + " “" + name + "”?",
                            confirmText: isActive ? "Ngừng hoạt động" : "Kích hoạt",
                            danger: isActive,
                            successMessage: "Đã cập nhật trạng thái danh mục.",
                            failureMessage: "Không cập nhật được trạng thái danh mục."
                        },
                        function () {
                            return postAction(
                                toggleUrl,
                                id,
                                "Không cập nhật được trạng thái danh mục.");
                        });
                });
            });
        }

        function bindDeleteEvents() {
            wrapper.querySelectorAll(".js-delete").forEach(function (button) {
                button.addEventListener("click", function () {
                    const id = button.dataset.id;
                    if (!id) return;

                    const name = button.dataset.name || "danh mục này";
                    runConfirmedAction(
                        button,
                        {
                            icon: "warning",
                            title: "Xóa danh mục?",
                            text: "Bạn có chắc muốn xóa “" + name + "”? Hành động này không thể hoàn tác trên giao diện.",
                            confirmText: "Xóa danh mục",
                            danger: true,
                            successMessage: "Đã xóa danh mục.",
                            failureMessage: "Không xóa được danh mục."
                        },
                        function () {
                            return postAction(
                                deleteUrl,
                                id,
                                "Không xóa được danh mục.");
                        });
                });
            });
        }

        function bindTableEvents() {
            initializeTooltips();
            bindPaginationEvents();
            bindToggleStatusEvents();
            bindDeleteEvents();
        }

        txtSearch.addEventListener("input", function () {
            updateClearButtonVisibility();
            debounceLoad();
        });

        btnClearSearch.addEventListener("click", function () {
            txtSearch.value = "";
            updateClearButtonVisibility();
            loadPage(1);
            txtSearch.focus();
        });

        ddlStatus.addEventListener("change", function () {
            loadPage(1);
        });

        ddlPageSize.addEventListener("change", function () {
            loadPage(1);
        });

        updateClearButtonVisibility();
        updateSummaryCounts();
        bindTableEvents();
    });
})();
