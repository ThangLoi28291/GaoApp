(function () {
    "use strict";
    const root = document.querySelector("[data-pin-editor]");
    if (!root || root.dataset.editorReady) return;
    root.dataset.editorReady = "true";
    const find = selector => root.querySelector(selector);
    const selectedList = find("[data-pin-selected]"), choiceList = find("[data-pin-choices]");
    const save = find("[data-pin-save]"), reload = find("[data-pin-reload]"), subject = find("[data-pin-subject]");
    const feedback = find("[data-pin-feedback]"), preview = find("[data-pin-preview]"), previewStatus = find("[data-pin-preview-status]");
    let workspace, selected = [], choices = new Map(), busy = false, stale = false, previewSequence = 0, previewRequest = null;
    const phone = matchMedia('(max-width: 767.98px)'), emptyText = find('[data-pin-empty]').textContent;
    let defaults = [], previewingDefaults = false;
    try { defaults = JSON.parse(document.querySelector('[data-mobile-pin-defaults]')?.textContent || '[]'); } catch (_) { /* No synthetic choices if navigation metadata is unavailable. */ }
    if (!Array.isArray(defaults)) defaults = [];
    const normalize = value => value.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLocaleLowerCase("vi").replace(/đ/g, "d").trim();
    const element = (tag, text, className) => {
        const node = document.createElement(tag);
        if (text !== undefined) node.textContent = text;
        if (className) node.className = className;
        return node;
    };
    function message(text, error = false) {
        feedback.textContent = text; feedback.hidden = !text; feedback.dataset.error = String(error);
        feedback.setAttribute("role", error ? "alert" : "status");
    }
    async function request(url, options = {}) {
        const response = await fetch(url, { credentials: "same-origin", cache: "no-store", ...options,
            headers: { Accept: "application/json", ...options.headers } });
        const data = (response.headers.get("content-type") || "").includes("application/json") ? await response.json() : null;
        if (!response.ok || response.redirected || !data) {
            const error = new Error(data?.message || (response.status === 403 ? "Bạn không có quyền chỉnh mục ghim." : "Không tải được cấu hình. Kiểm tra kết nối hoặc đăng nhập lại rồi thử lại."));
            error.status = response.status; throw error;
        }
        return data;
    }
    function isDirty() { return JSON.stringify(selected) !== JSON.stringify(workspace.menuIds); }
    function updateControls() {
        const invalid = selected.some(id => !choices.has(id));
        save.disabled = busy || stale || invalid || !isDirty(); reload.disabled = busy;
        subject.disabled = busy; find("[data-pin-search]").disabled = busy;
        choiceList.querySelectorAll("input").forEach(input => {
            const checked = selected.includes(Number(input.value));
            input.checked = checked; input.disabled = busy || (!checked && selected.length >= 4);
        });
        selectedList.querySelectorAll("button").forEach(button => {
            const index = selected.indexOf(Number(button.dataset.id));
            button.disabled = busy || (button.dataset.action === "up" && index === 0) || (button.dataset.action === "down" && index === selected.length - 1);
        });
        root.setAttribute("aria-busy", String(busy));
    }
    function renderSelected() {
        selectedList.replaceChildren();
        selected.forEach((id, index) => {
            const item = choices.get(id), title = item?.title || "Mục không còn khả dụng";
            const li = element("li");
            li.append(element("span", String(index + 1).padStart(2, "0"), "gao-pin-position"), element("span", title, "gao-pin-selected-title"));
            [["up", "Lên", "bx-chevron-up"], ["down", "Xuống", "bx-chevron-down"], ["remove", "Bỏ ghim", "bx-x"]].forEach(([action, label, icon]) => {
                const button = element("button"); button.type = "button";
                button.dataset.action = action; button.dataset.id = String(id); button.title = label;
                button.setAttribute("aria-label", label + " " + title);
                const i = element("i", undefined, "bx " + icon); i.setAttribute("aria-hidden", "true");
                button.append(i); li.append(button);
            });
            selectedList.append(li);
        });
        find("[data-pin-empty]").hidden = selected.length > 0;
        find("[data-pin-count]").textContent = selected.length + "/4";
        updateControls(); updatePreview();
    }
    function filterChoices() {
        const query = normalize(find("[data-pin-search]").value); let count = 0;
        choiceList.querySelectorAll("label").forEach(label => {
            label.hidden = !normalize(label.dataset.path).includes(query); if (!label.hidden) count++;
        });
        find("[data-pin-no-choices]").hidden = count !== 0;
    }
    function accept(data) {
        workspace = data; selected = [...data.menuIds]; choices = new Map(data.choices.map(item => [item.id, item])); stale = false;
        choiceList.replaceChildren();
        data.choices.forEach(item => {
            const label = element("label", undefined, "gao-pin-choice"), input = element("input"), text = element("span");
            input.type = "checkbox"; input.value = String(item.id); label.dataset.path = item.path;
            input.setAttribute("aria-label", "Ghim " + item.title); input.setAttribute("aria-describedby", "pin-limit-help");
            text.append(element("strong", item.title), element("small", item.path)); label.append(input, text); choiceList.append(label);
        });
        const previousSubject = subject.value;
        subject.replaceChildren(new Option("Tài khoản của tôi", ""));
        data.subjects.forEach(item => subject.add(new Option((item.type === "role" ? "Nhóm · " : "Nhân viên · ") + item.name + " · " + item.detail, item.type + ":" + item.id)));
        if (Array.from(subject.options).some(option => option.value === previousSubject)) subject.value = previousSubject;
        filterChoices(); renderSelected();
        if (selected.some(id => !choices.has(id))) message("Có mục đã bị ẩn, tắt hoặc không còn quyền. Bỏ mục không còn khả dụng trước khi lưu.", true);
    }
    function renderPreview(ids) {
        preview.replaceChildren();
        ids.forEach(id => {
            const item = choices.get(id); if (!item) return;
            const tile = element("span"), icon = element("i", undefined, "icon-base " + item.icon);
            const label = previewingDefaults ? defaults.find(tab => tab.id === id)?.label || item.title : item.title;
            tile.title = item.title; icon.setAttribute("aria-hidden", "true"); tile.append(icon, element("small", label)); preview.append(tile);
        });
        const menu = element("span"), icon = element("i", undefined, "icon-base bx bx-menu");
        icon.setAttribute("aria-hidden", "true"); menu.append(icon, element("small", "Menu")); preview.append(menu);
    }
    async function updatePreview() {
        const sequence = ++previewSequence;
        previewRequest?.abort(); previewRequest = null;
        previewingDefaults = phone.matches && selected.length === 0;
        const ids = previewingDefaults ? defaults.filter(tab => choices.has(tab.id)).map(tab => tab.id) : selected.filter(id => choices.has(id));
        find('[data-pin-empty]').textContent = phone.matches
            ? 'Chưa ghim riêng. Thanh dưới ưu tiên Tổng quan, Đơn hàng, Nhập hàng và Bán hàng khi có trong menu và được cấp quyền.'
            : emptyText;
        if (!subject.value) {
            renderPreview(ids); previewStatus.textContent = previewingDefaults
                ? 'Các phần ưu tiên lấy từ menu hiện có; không tự tạo hay lưu mục ghim.'
                : 'Hiển thị theo menu và quyền của tài khoản hiện tại.'; return;
        }
        // Never show a previous subject's shortcuts while a new preview is loading or fails.
        renderPreview([]); previewStatus.textContent = "Đang kiểm tra quyền và cấu hình ẩn/hiện…";
        const [type, id] = subject.value.split(":"), url = new URL(root.dataset.previewUrl, location.origin);
        url.searchParams.set("type", type); url.searchParams.set("id", id); ids.forEach(value => url.searchParams.append("menuIds", value));
        const controller = new AbortController(); previewRequest = controller;
        try {
            const data = await request(url, { signal: controller.signal });
            if (sequence !== previewSequence) return;
            renderPreview(data.visibleIds);
            previewStatus.textContent = data.visibleIds.length + "/" + ids.length + " mục hiển thị. Mục thiếu quyền, ngừng hoạt động hoặc đang ẩn được bỏ qua.";
        } catch (error) { if (sequence === previewSequence && error.name !== "AbortError") previewStatus.textContent = "Chưa kiểm tra được đối tượng này. Hãy chọn lại để thử."; }
        finally { if (previewRequest === controller) previewRequest = null; }
    }
    choiceList.addEventListener("change", event => {
        const input = event.target; if (!(input instanceof HTMLInputElement) || busy) return;
        const id = Number(input.value);
        if (input.checked && selected.length < 4) selected.push(id); else selected = selected.filter(value => value !== id);
        renderSelected();
    });
    selectedList.addEventListener("click", event => {
        const button = event.target.closest("button[data-action]"); if (!button || busy) return;
        const id = Number(button.dataset.id), index = selected.indexOf(id), action = button.dataset.action;
        if (index < 0) return;
        if (action === "remove") selected.splice(index, 1);
        else {
            const next = index + (action === "up" ? -1 : 1);
            if (next < 0 || next >= selected.length) return;
            [selected[index], selected[next]] = [selected[next], selected[index]];
        }
        renderSelected();
        const focusId = action === "remove" ? selected[Math.min(index, selected.length - 1)] : id;
        const target = selectedList.querySelector('button[data-id="' + focusId + '"][data-action="remove"]');
        (target || find("[data-pin-search]")).focus();
    });
    find("[data-pin-search]").addEventListener("input", filterChoices);
    subject.addEventListener("change", updatePreview);
    phone.addEventListener('change', () => { if (workspace) updatePreview(); });
    save.addEventListener("click", async () => {
        if (save.disabled) return;
        busy = true; message(""); updateControls(); save.textContent = "Đang lưu…";
        try {
            const data = await request(root.dataset.saveUrl, { method: "POST", headers: { "Content-Type": "application/json",
                RequestVerificationToken: find('input[name="__RequestVerificationToken"]').value }, body: JSON.stringify({ version: workspace.version, menuIds: selected }) });
            accept(data); document.dispatchEvent(new CustomEvent("gao:menu-pins-saved", { detail: { menuIds: data.menuIds } }));
            message("Đã lưu mục ghim cho cửa hàng. Mỗi người vẫn nhìn thấy theo quyền và cấu hình menu của họ.");
        } catch (error) {
            stale = error.status === 409; message(error.message + (error.status ? "" : " Lựa chọn của bạn vẫn được giữ; tải lại để kiểm tra trạng thái đã lưu."), true);
        } finally { busy = false; save.textContent = "Lưu mục ghim"; updateControls(); }
    });
    reload.addEventListener("click", async () => {
        if (busy) return;
        busy = true; message(""); updateControls();
        try { accept(await request(root.dataset.workspaceUrl)); message("Đã tải cấu hình mới nhất của cửa hàng."); }
        catch (error) { message(error.message, true); }
        finally { busy = false; updateControls(); }
    });
    window.addEventListener("beforeunload", event => { if (workspace && isDirty()) { event.preventDefault(); event.returnValue = ""; } });
    try { accept(JSON.parse(find("[data-pin-workspace]").textContent)); }
    catch (_) { stale = true; message("Không đọc được cấu hình. Bấm Tải lại để thử lại.", true); }
})();
