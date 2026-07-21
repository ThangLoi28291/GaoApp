(function () {

    function qs(sel) { return document.querySelector(sel); }

    /* ================= TOKEN ================= */
    function getToken() {
        const ip = qs('#antiForgeryForm input[name="__RequestVerificationToken"]');
        return ip ? ip.value : "";
    }

    async function postForm(url, data) {
        const body = new URLSearchParams();
        body.append("__RequestVerificationToken", getToken());
        Object.keys(data || {}).forEach(k => body.append(k, data[k]));

        const res = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8" },
            body: body.toString(),
            cache: "no-store"
        });

        return await res.json();
    }

    /* ================= LOAD TABLE ================= */
    function loadCategory(page) {
        const search = qs("#txtCategorySearch")?.value || "";
        const pageSize = qs("#ddlPageSize")?.value || 20;

        const url = "/Admin/Category/Table"
            + "?searchString=" + encodeURIComponent(search)
            + "&page=" + (page || 1)
            + "&pageSize=" + pageSize;

        fetch(url, { cache: "no-store" })
            .then(r => r.text())
            .then(html => {
                const wrapper = qs("#categoryTableWrapper");
                if (!wrapper) return;

                wrapper.innerHTML = html;
                wirePaging();
            });
    }

    /* ================= PAGING ================= */
    function wirePaging() {
        qs("#categoryTableWrapper")
            ?.querySelectorAll(".js-page-link")
            .forEach(a => {
                a.onclick = function (e) {
                    e.preventDefault();
                    const page = this.dataset.page;
                    if (page) loadCategory(parseInt(page));
                };
            });
    }

    /* ================= SEARCH ================= */
    let typingTimer = null;
    document.addEventListener("input", function (e) {
        if (!e.target.matches("#txtCategorySearch")) return;

        clearTimeout(typingTimer);
        typingTimer = setTimeout(() => loadCategory(1), 300);
    });

    /* ================= PAGE SIZE ================= */
    document.addEventListener("change", function (e) {
        if (!e.target.matches("#ddlPageSize")) return;
        loadCategory(1);
    });

    /* ================= TOGGLE ================= */
    document.addEventListener("click", async function (e) {
        const btn = e.target.closest(".js-toggle");
        if (!btn) return;

        const rs = await postForm("/Admin/Category/ChangeStatus", { id: btn.dataset.id });
        if (rs.success) {
            btn.classList.toggle("btn-success");
            btn.classList.toggle("btn-secondary");
            btn.textContent = btn.classList.contains("btn-success") ? "Đang dùng" : "Ngưng";
        }
    });

    /* ================= DELETE ================= */
    document.addEventListener("click", function (e) {
        const btn = e.target.closest(".js-delete");
        if (!btn) return;

        Swal.fire({
            title: "Xác nhận xóa?",
            text: btn.dataset.name,
            icon: "warning",
            showCancelButton: true,
            confirmButtonText: "Xóa"
        }).then(async r => {
            if (!r.isConfirmed) return;

            const rs = await postForm("/Admin/Category/Delete", { id: btn.dataset.id });
            if (rs.success) btn.closest("tr")?.remove();
        });
    });

    /* ================= INIT ================= */
    document.addEventListener("DOMContentLoaded", wirePaging);

})();
