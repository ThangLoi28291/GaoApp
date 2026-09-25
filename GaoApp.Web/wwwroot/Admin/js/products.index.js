(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {
        const shell = document.querySelector("[data-product-shell]");
        if (!shell) return;

        const txtSearch = document.getElementById("txtSearch");
        const btnClearSearch = document.getElementById("btnClearSearch");
        const ddlCategory = document.getElementById("ddlCategory");
        const ddlLifecycle = document.getElementById("ddlLifecycle");
        const ddlPageSize = document.getElementById("ddlPageSize");
        const wrapper = document.getElementById("productTableWrapper");
        const totalCount = document.getElementById("productTotalCount");
        const posAllowedCount = document.getElementById("productPosAllowedCount");
        const notForPosCount = document.getElementById("productNotForPosCount");
        const inactiveCount = document.getElementById("productInactiveCount");
        const quickViewModalElement = document.getElementById("productQuickViewModal");
        const quickViewImage = document.getElementById("productQuickViewImage");
        const quickViewImageEmpty = document.getElementById("productQuickViewImageEmpty");
        const quickViewName = document.getElementById("productQuickViewName");
        const quickViewAlias = document.getElementById("productQuickViewAlias");
        const quickViewState = document.getElementById("productQuickViewState");
        const quickViewCategory = document.getElementById("productQuickViewCategory");
        const quickViewBrand = document.getElementById("productQuickViewBrand");
        const quickViewSupplier = document.getElementById("productQuickViewSupplier");
        const quickViewTax = document.getElementById("productQuickViewTax");
        const quickViewUnit = document.getElementById("productQuickViewUnit");
        const quickViewVariantCount = document.getElementById("productQuickViewVariantCount");
        const quickViewPrice = document.getElementById("productQuickViewPrice");
        const quickViewDetailLink = document.getElementById("productQuickViewDetailLink");

        if (
            !txtSearch ||
            !btnClearSearch ||
            !ddlCategory ||
            !ddlLifecycle ||
            !ddlPageSize ||
            !wrapper
        ) {
            return;
        }

        const indexUrl = shell.dataset.indexUrl;
        const searchUrl = shell.dataset.searchUrl;
        const toggleUrl = shell.dataset.toggleUrl;
        const deleteUrl = shell.dataset.deleteUrl;

        let typingTimer = null;
        let currentPage = Number.parseInt(shell.dataset.page || "1", 10) || 1;
        let loadController = null;
        let requestSequence = 0;
        let imageHoverPreview = null;

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
            const result = wrapper.querySelector("[data-product-result]");
            if (!result) return;

            if (totalCount) totalCount.textContent = result.dataset.totalProducts || "0";
            if (posAllowedCount) posAllowedCount.textContent = result.dataset.posAllowedProducts || "0";
            if (notForPosCount) notForPosCount.textContent = result.dataset.notForPosProducts || "0";
            if (inactiveCount) inactiveCount.textContent = result.dataset.inactiveProducts || "0";
        }

        function setQuickViewText(element, value) {
            if (element) element.textContent = value?.trim() || "—";
        }

        function getImageHoverPreview() {
            if (imageHoverPreview) return imageHoverPreview;

            const preview = document.createElement("div");
            preview.id = "productImageHoverPreview";
            preview.hidden = true;
            preview.setAttribute("aria-hidden", "true");
            Object.assign(preview.style, {
                position: "fixed",
                width: "280px",
                height: "280px",
                padding: "10px",
                background: "#fff",
                border: "1px solid rgba(34, 48, 62, 0.14)",
                borderRadius: "12px",
                boxShadow: "0 12px 36px rgba(34, 48, 62, 0.24)",
                pointerEvents: "none",
                zIndex: "1090"
            });

            const image = document.createElement("img");
            image.className = "w-100 h-100 rounded";
            image.style.objectFit = "contain";
            preview.appendChild(image);
            document.body.appendChild(preview);
            imageHoverPreview = preview;
            return preview;
        }

        function showImageHoverPreview(imageButton) {
            const productRow = imageButton.closest("[data-product-row]");
            const imageUrl = productRow?.dataset.quickImage?.trim() || "";
            if (!imageUrl) return;

            const preview = getImageHoverPreview();
            const previewImage = preview.querySelector("img");
            const rect = imageButton.getBoundingClientRect();
            const previewSize = 280;
            const viewportGap = 12;
            let left = rect.right + viewportGap;

            if (left + previewSize > window.innerWidth - viewportGap) {
                left = rect.left - previewSize - viewportGap;
            }

            const top = Math.min(
                Math.max(viewportGap, rect.top - ((previewSize - rect.height) / 2)),
                Math.max(viewportGap, window.innerHeight - previewSize - viewportGap));

            previewImage.src = imageUrl;
            previewImage.alt = "Ảnh phóng to sản phẩm " + (productRow.dataset.quickName || "");
            preview.style.left = Math.max(viewportGap, left) + "px";
            preview.style.top = top + "px";
            preview.hidden = false;
        }

        function hideImageHoverPreview() {
            if (imageHoverPreview) imageHoverPreview.hidden = true;
        }

        function showQuickView(source) {
            const productRow = source.closest("[data-product-row]");
            if (!productRow || !quickViewModalElement || !window.bootstrap?.Modal) return;

            hideImageHoverPreview();

            const data = productRow.dataset;
            setQuickViewText(quickViewName, data.quickName);
            setQuickViewText(quickViewAlias, data.quickAlias);
            setQuickViewText(quickViewCategory, data.quickCategory);
            setQuickViewText(quickViewBrand, data.quickBrand);
            setQuickViewText(quickViewSupplier, data.quickSupplier);
            setQuickViewText(quickViewTax, data.quickTax);
            setQuickViewText(quickViewUnit, data.quickUnit);
            setQuickViewText(quickViewVariantCount, data.quickVariants);
            setQuickViewText(quickViewPrice, data.quickPrice);
            setQuickViewText(quickViewState, data.quickState);

            if (quickViewState) {
                quickViewState.classList.remove("is-success", "is-warning", "is-neutral");
                quickViewState.classList.add(data.quickStateClass || "is-neutral");
            }

            const imageUrl = data.quickImage?.trim() || "";
            if (quickViewImage && quickViewImageEmpty) {
                quickViewImage.classList.toggle("d-none", !imageUrl);
                quickViewImageEmpty.classList.toggle("d-none", Boolean(imageUrl));

                if (imageUrl) {
                    quickViewImage.src = imageUrl;
                    quickViewImage.alt = "Ảnh sản phẩm " + (data.quickName || "");
                } else {
                    quickViewImage.removeAttribute("src");
                }
            }

            if (quickViewDetailLink) {
                quickViewDetailLink.href = data.quickDetailUrl || "#";
            }

            window.bootstrap.Modal.getOrCreateInstance(quickViewModalElement).show();
        }

        function buildParameters(page) {
            const parameters = new URLSearchParams();
            const search = txtSearch.value.trim();
            const categoryId = ddlCategory.value;
            const lifecycle = ddlLifecycle.value;
            const pageSize = ddlPageSize.value || "20";

            if (search) parameters.set("search", search);
            if (categoryId) parameters.set("categoryId", categoryId);
            if (lifecycle) parameters.set("lifecycle", lifecycle);
            if (page > 1) parameters.set("page", page.toString());
            if (pageSize !== "20") parameters.set("pageSize", pageSize);

            return parameters;
        }

        function syncHistory(page, mode) {
            if (mode === "none" || !indexUrl) return;

            const query = buildParameters(page).toString();
            const url = indexUrl + (query ? "?" + query : "");
            const state = { productList: true, page: page };

            if (mode === "push") {
                window.history.pushState(state, "", url);
            } else {
                window.history.replaceState(state, "", url);
            }
        }

        function applyLocationState() {
            const parameters = new URLSearchParams(window.location.search);
            txtSearch.value = parameters.get("search") || "";
            ddlCategory.value = parameters.get("categoryId") || "";
            ddlLifecycle.value = parameters.get("lifecycle") || "";
            ddlPageSize.value = parameters.get("pageSize") || "20";
            updateClearButtonVisibility();

            return Math.max(
                Number.parseInt(parameters.get("page") || "1", 10) || 1,
                1);
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
                    "X-Requested-With": "XMLHttpRequest",
                    "RequestVerificationToken": token
                },
                body: "id=" + encodeURIComponent(id)
            });

            const data = await readJson(response, fallbackMessage);
            if (!data.ok) {
                throw new Error(data.message || fallbackMessage);
            }

            return data;
        }

        async function loadPage(page, historyMode) {
            const requestedPage = Math.max(Number.parseInt(page || "1", 10) || 1, 1);
            const currentSequence = ++requestSequence;

            loadController?.abort();
            const requestController = new AbortController();
            loadController = requestController;

            const parameters = buildParameters(requestedPage);

            try {
                setResultsBusy(true);

                const query = parameters.toString();
                const response = await fetch(searchUrl + (query ? "?" + query : ""), {
                    method: "GET",
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    },
                    signal: requestController.signal
                });

                if (!response.ok) {
                    throw new Error("Không tải được danh sách sản phẩm.");
                }

                const html = await response.text();
                if (requestController.signal.aborted || currentSequence !== requestSequence) return;

                disposeTooltips();
                hideImageHoverPreview();
                wrapper.innerHTML = html;

                const result = wrapper.querySelector("[data-product-result]");
                const totalPages = Number.parseInt(result?.dataset.totalPages || "0", 10) || 0;

                if (totalPages > 0 && requestedPage > totalPages) {
                    return await loadPage(totalPages, "replace");
                }

                currentPage = requestedPage;
                shell.dataset.page = requestedPage.toString();
                syncHistory(requestedPage, historyMode || "replace");
                updateSummaryCounts();
                bindTableEvents();
            } catch (error) {
                if (error?.name !== "AbortError" && currentSequence === requestSequence) {
                    console.error(error);
                    notify("error", error?.message || "Có lỗi khi tải dữ liệu.");
                }
            } finally {
                if (currentSequence === requestSequence) {
                    loadController = null;
                    setResultsBusy(false);
                }
            }
        }

        function debounceLoad() {
            window.clearTimeout(typingTimer);
            typingTimer = window.setTimeout(function () {
                loadPage(1, "replace");
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

            if (!window.Swal?.fire) {
                notify("error", "Không tải được hộp thoại xác nhận. Vui lòng tải lại trang.");
                return;
            }

            try {
                setActionBusy(button, true);
                const data = await confirmWithSweetAlert(options, action);
                if (!data) return;

                notify("success", data.message || options.successMessage);
                await loadPage(currentPage, "none");
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
                    if (link.closest(".page-item")?.classList.contains("disabled")) return;

                    const page = Number.parseInt(link.dataset.page || "1", 10);
                    if (page > 0) loadPage(page, "push");
                });
            });
        }

        function bindToggleStatusEvents() {
            wrapper.querySelectorAll(".js-toggle-status").forEach(function (button) {
                button.addEventListener("click", function () {
                    const id = button.dataset.id;
                    if (!id) return;

                    const isActive = button.dataset.status === "true";
                    const name = button.dataset.name || "sản phẩm này";

                    runConfirmedAction(
                        button,
                        {
                            icon: isActive ? "warning" : "question",
                            title: isActive
                                ? "Ngừng hoạt động sản phẩm?"
                                : "Kích hoạt lại sản phẩm?",
                            text: "Bạn có chắc muốn "
                                + (isActive ? "ngừng hoạt động" : "kích hoạt lại")
                                + " “" + name + "”?",
                            confirmText: isActive ? "Ngừng hoạt động" : "Kích hoạt",
                            danger: isActive,
                            successMessage: "Đã cập nhật trạng thái sản phẩm.",
                            failureMessage: "Không cập nhật được trạng thái sản phẩm."
                        },
                        function () {
                            return postAction(
                                toggleUrl,
                                id,
                                "Không cập nhật được trạng thái sản phẩm.");
                        });
                });
            });
        }

        function bindDeleteEvents() {
            wrapper.querySelectorAll(".js-delete").forEach(function (button) {
                button.addEventListener("click", function () {
                    const id = button.dataset.id;
                    if (!id) return;

                    const name = button.dataset.name || "sản phẩm này";
                    runConfirmedAction(
                        button,
                        {
                            icon: "warning",
                            title: "Xóa sản phẩm?",
                            text: "Bạn có chắc muốn xóa “" + name + "”? Hành động này không thể hoàn tác trên giao diện.",
                            confirmText: "Xóa sản phẩm",
                            danger: true,
                            successMessage: "Đã xóa sản phẩm.",
                            failureMessage: "Không xóa được sản phẩm."
                        },
                        function () {
                            return postAction(deleteUrl, id, "Không xóa được sản phẩm.");
                        });
                });
            });
        }

        function bindQuickViewEvents() {
            wrapper.querySelectorAll(".js-quick-view").forEach(function (button) {
                button.addEventListener("click", function (event) {
                    event.preventDefault();
                    event.stopPropagation();
                    showQuickView(button);
                });
            });

            wrapper.querySelectorAll("[data-product-row]").forEach(function (productRow) {
                productRow.addEventListener("dblclick", function (event) {
                    if (event.target.closest("a, button, input, select, textarea")) return;
                    showQuickView(productRow);
                });

                productRow.addEventListener("keydown", function (event) {
                    if (event.key !== "Enter" || event.target !== productRow) return;
                    event.preventDefault();
                    showQuickView(productRow);
                });
            });

            wrapper.querySelectorAll(".js-product-image").forEach(function (imageButton) {
                imageButton.setAttribute("aria-haspopup", "dialog");
                imageButton.addEventListener("pointerenter", function () {
                    showImageHoverPreview(imageButton);
                });
                imageButton.addEventListener("pointerleave", hideImageHoverPreview);
                imageButton.addEventListener("focus", function () {
                    showImageHoverPreview(imageButton);
                });
                imageButton.addEventListener("blur", hideImageHoverPreview);
            });
        }

        function bindTableEvents() {
            initializeTooltips();
            bindPaginationEvents();
            bindToggleStatusEvents();
            bindDeleteEvents();
            bindQuickViewEvents();
        }

        txtSearch.addEventListener("input", function () {
            updateClearButtonVisibility();
            debounceLoad();
        });

        txtSearch.addEventListener("keydown", function (event) {
            if (event.key === "Enter") {
                event.preventDefault();
                window.clearTimeout(typingTimer);
                loadPage(1, "replace");
            }

            if (event.key === "Escape" && txtSearch.value) {
                event.preventDefault();
                txtSearch.value = "";
                updateClearButtonVisibility();
                loadPage(1, "push");
                txtSearch.focus();
            }
        });

        btnClearSearch.addEventListener("click", function () {
            txtSearch.value = "";
            updateClearButtonVisibility();
            loadPage(1, "push");
            txtSearch.focus();
        });

        ddlCategory.addEventListener("change", function () {
            loadPage(1, "push");
        });

        ddlLifecycle.addEventListener("change", function () {
            loadPage(1, "push");
        });

        ddlPageSize.addEventListener("change", function () {
            loadPage(1, "push");
        });

        window.addEventListener("popstate", function () {
            const page = applyLocationState();
            loadPage(page, "none");
        });

        updateClearButtonVisibility();
        updateSummaryCounts();
        bindTableEvents();
        syncHistory(currentPage, "replace");
    });
})();
