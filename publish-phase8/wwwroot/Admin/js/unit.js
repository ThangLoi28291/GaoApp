(function () {

    // =========================
    // 0) Helpers
    // =========================
    function qs(sel) { return document.querySelector(sel); }

    function toastOk(msg) {
        if (typeof ToastSuccess === "function") ToastSuccess(msg);
        else if (typeof toastr !== "undefined") toastr.success(msg);
        else alert(msg);
    }

    function toastErr(msg) {
        if (typeof ToastError === "function") ToastError(msg);
        else if (typeof toastr !== "undefined") toastr.error(msg);
        else alert(msg);
    }

    function getAntiForgeryToken() {
        const ip = qs('#antiForgeryForm input[name="__RequestVerificationToken"]');
        return ip ? ip.value : "";
    }

    async function postForm(url, data) {
        const token = getAntiForgeryToken();

        const body = new URLSearchParams();
        // ✅ quan trọng: token phải nằm trong body với đúng key này
        body.append("__RequestVerificationToken", token);

        Object.keys(data || {}).forEach(k => body.append(k, data[k]));

        const res = await fetch(url, {
            method: "POST",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8"
            },
            body: body.toString(),
            cache: "no-store"
        });

        if (!res.ok) {
            const txt = await res.text().catch(() => "");
            throw new Error("HTTP " + res.status + " " + txt);
        }

        return await res.json();
    }


    // =========================
    // 1) Toggle Active
    // =========================
    document.addEventListener("click", async function (e) {
        const btn = e.target.closest(".js-toggle");
        if (!btn) return;

        const id = btn.dataset.id || btn.getAttribute("data-id");
        if (!id) return;

        try {
            const rs = await postForm("/Admin/Unit/ToggleActive", { id });

            if (rs.success) toastOk(rs.message || "OK");
            else toastErr(rs.message || "Có lỗi");

            if (rs.success) {
                const wasInactive = btn.classList.contains("btn-secondary");
                btn.classList.toggle("btn-success");
                btn.classList.toggle("btn-secondary");
                btn.textContent = wasInactive ? "Đang dùng" : "Ngưng";
            }
        } catch (err) {
            console.error(err);
            toastErr("Toggle lỗi. Mở F12 > Network/Console để xem chi tiết.");
        }
    });

    // =========================
    // 2) Delete with SweetAlert2
    // =========================
    document.addEventListener("click", function (e) {
        const btn = e.target.closest(".js-delete");
        if (!btn) return;

        const id = btn.dataset.id;
        const name = btn.dataset.name || "";

        if (typeof Swal === "undefined") {
            toastErr("Chưa nhúng SweetAlert2 (Swal is undefined).");
            return;
        }

        Swal.fire({
            title: "Xác nhận xóa?",
            html: `Bạn có chắc muốn xóa <b>${name}</b> không?`,
            icon: "warning",
            showCancelButton: true,
            confirmButtonText: "Xóa",
            cancelButtonText: "Hủy",
            confirmButtonColor: "#d33",
            reverseButtons: true
        }).then(async (result) => {
            if (!result.isConfirmed) return;

            try {
                const rs = await postForm("/Admin/Unit/Delete", { id });

                if (rs.success) toastOk(rs.message || "OK");
                else toastErr(rs.message || "Có lỗi");

                if (rs.success) {
                    const row = btn.closest("tr");
                    if (row) row.remove();
                }
            } catch (err) {
                console.error(err);
                toastErr("Không xóa được. Mở F12 > Network/Console để xem chi tiết.");
            }
        });
    });

    // =========================
    // 3) Live Search (debounce 300ms)
    // =========================
    let _unitSearchTimer = null;

    document.addEventListener("input", function (e) {
        const ip = e.target.closest("#unitSearch");
        if (!ip) return;

        clearTimeout(_unitSearchTimer);
        _unitSearchTimer = setTimeout(async () => {
            const v = (ip.value || "").trim();
            const url = "/Admin/Unit/Table?search=" + encodeURIComponent(v);

            try {
                const html = await fetch(url, { cache: "no-store" }).then(r => r.text());
                const list = qs("#unitList");
                if (list) list.innerHTML = html;

                const newUrl = "/Admin/Unit/Index" + (v ? ("?search=" + encodeURIComponent(v)) : "");
                window.history.replaceState({}, "", newUrl);
            } catch (err) {
                console.error(err);
                toastErr("Search lỗi. Kiểm tra action /Admin/Unit/Table.");
            }
        }, 300);
    });

})();
