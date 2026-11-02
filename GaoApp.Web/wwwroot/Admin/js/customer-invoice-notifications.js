(() => {
    "use strict";
    const root = document.getElementById("invoiceNotifications"); if (!root) return;
    const get = id => document.getElementById(id), base = root.dataset.base;
    const csrf = root.querySelector('input[name="__RequestVerificationToken"]').value;
    const money = value => new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 2 }).format(value || 0) + " ₫";
    const time = value => value ? new Date(value).toLocaleString("vi-VN") : "Chưa có";
    const states = { Pending: "Chờ xử lý", Dispatching: "Đang xử lý", Accepted: "ZaloMarketing đã nhận · chờ kết quả", Sent: "Đã gửi · chưa xác nhận khách đọc",
        Blocked: "Bị chặn", Failed: "Thất bại", Unknown: "Chưa rõ kết quả", NeedsReview: "Chưa rõ kết quả · cần kiểm tra", PartiallySent: "Đã gửi một phần · cần kiểm tra", Cancelled: "Đã dừng" };
    const kinds = { Receipt: "Phiếu mua hàng", OfficialPdf: "Hóa đơn · PDF", OfficialZipXml: "Hóa đơn · ZIP", OfficialXml: "XML gốc trong gói chính thức", OfficialLink: "Xem hóa đơn qua liên kết", CorrectionPdf: "Điều chỉnh / thay thế · PDF", CorrectionZipXml: "Điều chỉnh / thay thế · ZIP", CorrectionXml: "Điều chỉnh / thay thế · XML gốc", CorrectionLink: "Xem hóa đơn điều chỉnh / thay thế qua liên kết" };
    const sourceNames = { Order: "Đơn mua", Invoice: "Hóa đơn" }, partKinds = { Text: "Tin chữ", Image: "Ảnh", File: "Tệp" };
    const sourceStates = { Completed: "Hoàn tất", Issued: "Đã phát hành", PdfDownloaded: "Đã phát hành", ZipDownloaded: "Đã phát hành", EmailSent: "Đã phát hành", Cancelled: "Đã hủy", LocalDraft: "Bản nháp", ReadyToIssue: "Chưa phát hành" };
    const paymentStates = { Unpaid: "Chưa thanh toán", PartiallyPaid: "Thanh toán một phần", Paid: "Đã thanh toán", Refunded: "Đã hoàn tiền" };
    const partStates = { Pending: "Chưa gửi", Dispatching: "Đang gửi", Sent: "Đã gửi", FailedBeforeSend: "Chưa gửi được", Uncertain: "Chưa rõ kết quả", Cancelled: "Đã dừng" };
    const node = (tag, text, className) => { const element = document.createElement(tag); if (text !== undefined) element.textContent = text; if (className) element.className = className; return element; };
    const notice = message => { get("inNotice").textContent = message || ""; };
    let customer = null, evidence = null, thread = null, selectedBinding = null, selectedSource = null, selectedPreview = null, bindings = [], sources = [];
    let customerGeneration = 0, threadGeneration = 0, sourceGeneration = 0, page = 0, busy = false, loadingOutbox = false, selectedDetail = null;
    let verifyRequest = crypto.randomUUID(), sendRequest = crypto.randomUUID(), sendSignature = null, acceptedSignature = null;
    async function api(path, body) {
        const response = await fetch(base + path, { method: body === undefined ? "GET" : "POST", credentials: "same-origin", cache: "no-store",
            headers: body === undefined ? {} : { "Content-Type": "application/json", RequestVerificationToken: csrf },
            body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(20000) });
        let data; try { data = await response.json(); } catch { throw new Error("Chưa đọc được phản hồi. Tải lại hàng chờ để kiểm tra trước khi gửi lại."); }
        if (!response.ok) { const error = new Error(data.message || "Chưa thực hiện được. Tải lại dữ liệu và kiểm tra quyền của tài khoản."); error.status = response.status; error.code = data.code; throw error; }
        return data;
    }
    const empty = (target, text) => target.replaceChildren(node("p", text, "in-empty"));
    function badge(state) { return node("span", states[state] || state, "in-badge " + (state === "Sent" ? "good" : ["Blocked", "Failed", "Unknown", "NeedsReview", "PartiallySent"].includes(state) ? "warn" : "")); }
    function commandBusy(value) { busy = value; root.setAttribute("aria-busy", String(value)); root.querySelectorAll("button,input,select,textarea").forEach(control => { control.disabled = value; }); if (!value) { renderBindings(); renderSources(); updateActions(); } }
    function signature() { return JSON.stringify([customer?.id, selectedBinding?.id, selectedBinding?.version, selectedPreview?.sourceType, selectedPreview?.sourceId, selectedPreview?.kind, selectedPreview?.sourceVersion]); }
    function updateActions() {
        get("inVerify").disabled = busy || !customer || !evidence?.verified || !get("inMethod").value || get("inReference").value.trim().length < 8 || !get("inAttested").checked;
        get("inSend").disabled = busy || root.dataset.reply !== "true" || root.dataset.enabled !== "true" || !selectedBinding || !selectedPreview?.canQueue || !selectedPreview?.sourceVersion || !get("inSendAttested").checked || acceptedSignature === signature();
        get("inSend").textContent = acceptedSignature === signature() ? "Đã tiếp nhận lượt gửi này" : "Kiểm tra & tạo lượt gửi";
        get("inReloadPreview").disabled = busy || !selectedSource;
        const review = get("inSendReview"); review.hidden = !selectedPreview;
        if (!review.hidden) review.replaceChildren(node("strong", customer.name), node("p", `${selectedPreview.label} · ${selectedPreview.code || selectedPreview.sourceId}`),
            node("p", `${selectedPreview.legalOwner} · ${money(selectedPreview.total)} · ${sourceStates[selectedPreview.status] || selectedPreview.status}`),
            node("p", selectedBinding ? `Kết nối ${selectedBinding.connectionId} · Hội thoại ${selectedBinding.conversationId}` : "Chọn liên kết người nhận đã xác minh để gửi."),
            node("p", selectedPreview.blockedReason || "Đã đọc phiên bản nguồn để xác nhận. Nếu chứng từ thay đổi sau bước này, hệ thống sẽ từ chối lượt gửi và yêu cầu kiểm tra lại."));
    }
    function clearEvidence() { evidence = null; thread = null; threadGeneration++; get("inEvidence").hidden = true; get("inAttested").checked = false; verifyRequest = crypto.randomUUID(); updateActions(); }
    async function chooseCustomer(row) {
        if (busy) return;
        customer = row; const generation = ++customerGeneration; sourceGeneration++; const initialSources = sourceGeneration; clearEvidence(); selectedBinding = null; selectedSource = null; selectedPreview = null; bindings = []; sources = []; get("inSendAttested").checked = false; get("inSourceQuery").value = "";
        get("inCustomerSelected").hidden = false; get("inCustomerSelected").replaceChildren(node("strong", row.name), node("p", `${row.code || "Hồ sơ"} · ${row.phoneHint || "Chưa có số"}`));
        empty(get("inBindings"), "Đang đọc liên kết…"); empty(get("inSources"), "Đang đọc nguồn chứng từ…"); updateActions();
        try {
            const [bindingData, sourceData] = await Promise.all([api(`/customers/${row.id}/bindings`), api(`/customers/${row.id}/sources`)]);
            if (generation !== customerGeneration) return;
            bindings = bindingData.items; if (initialSources === sourceGeneration) sources = sourceData.items; renderBindings(); renderSources();
        } catch (error) { if (generation === customerGeneration) notice(error.message); }
    }
    async function loadBindings() {
        if (!customer) return; const id = customer.id, generation = customerGeneration;
        const data = await api(`/customers/${id}/bindings`); if (generation !== customerGeneration) return;
        bindings = data.items; if (selectedBinding) selectedBinding = bindings.find(row => row.id === selectedBinding.id && row.version === selectedBinding.version && row.current && !row.revokedAtUtc) || null;
        renderBindings(); updateActions();
    }
    function renderBindings() {
        const target = get("inBindings"); target.replaceChildren();
        if (!bindings.length) { empty(target, customer ? "Chưa có liên kết đã xác minh." : "Chọn hồ sơ khách để xem liên kết."); return; }
        for (const row of bindings) {
            const article = node("article", undefined, "in-binding"), content = node("div"), actions = node("div");
            content.append(node("strong", row.connectionId), node("p", `Hội thoại ${row.conversationId}`), node("p", `Xác minh ${time(row.verifiedAtUtc)} · Phiên bản ${row.version}`));
            if (row.blockedReason) content.append(node("p", row.blockedReason, "in-reason"));
            if (!row.revokedAtUtc && row.current) {
                const select = node("button", selectedBinding?.id === row.id ? "Đã chọn để gửi" : "Chọn để gửi", "btn btn-sm btn-outline-primary"); select.type = "button"; select.disabled = busy;
                select.addEventListener("click", () => { selectedBinding = row; get("inSendAttested").checked = false; renderBindings(); updateActions(); }); actions.append(select);
            }
            if (!row.revokedAtUtc) {
                const revoke = node("button", "Thu hồi", "btn btn-sm btn-outline-secondary"); revoke.type = "button"; revoke.disabled = busy;
                revoke.addEventListener("click", async () => {
                    const reason = prompt("Ghi lý do thu hồi liên kết (8–240 ký tự). Tin đã gửi không thể thu hồi tại đây."); if (reason === null) return;
                    if (reason.trim().length < 8 || reason.length > 240) return notice("Lý do thu hồi cần từ 8 đến 240 ký tự.");
                    commandBusy(true); notice("");
                    try { await api(`/bindings/${row.id}/revoke`, { version: row.version, reason }); await loadBindings(); await loadOutbox(); notice("Đã thu hồi liên kết. Các lượt chưa gửi sẽ được kiểm tra lại."); }
                    catch (error) { notice(error.message); } finally { commandBusy(false); }
                }); actions.append(revoke);
            }
            article.append(content, actions); target.append(article);
        }
    }
    function renderSources() {
        const target = get("inSources"); target.replaceChildren();
        if (!sources.length) { empty(target, !customer ? "Chọn hồ sơ khách để xem nguồn chứng từ." : get("inSourceQuery").value.trim() ? "Không tìm thấy chứng từ trùng mã trong hồ sơ đang chọn." : "Chưa có chứng từ gắn với hồ sơ trong 10 đơn gần nhất."); return; }
        for (const row of sources) {
            const label = node("label", undefined, "in-source"), radio = document.createElement("input"), content = node("div");
            radio.type = "radio"; radio.name = "invoiceSource"; radio.disabled = busy || !row.canQueue;
            radio.checked = selectedSource?.sourceType === row.sourceType && selectedSource?.sourceId === row.sourceId && selectedSource?.kind === row.kind;
            radio.addEventListener("change", () => chooseSource(row));
            content.append(node("strong", `${row.label} · ${row.code || row.sourceId}`), node("p", `${row.legalOwner} · ${money(row.total)}`), node("p", `${time(row.at)} · ${sourceStates[row.status] || row.status}`));
            if (row.blockedReason) content.append(node("p", row.blockedReason, "in-reason")); label.append(radio, content); target.append(label);
        }
    }
    async function loadSources(query) {
        if (!customer) return notice("Chọn hồ sơ khách trước khi tìm chứng từ.");
        const id = customer.id, generation = ++sourceGeneration; selectedSource = null; selectedPreview = null; get("inSendAttested").checked = false; updateActions();
        empty(get("inSources"), "Đang đọc chứng từ thuộc hồ sơ khách…");
        const path = `/customers/${id}/sources` + (query ? `/lookup?query=${encodeURIComponent(query)}` : "");
        try { const data = await api(path); if (generation !== sourceGeneration || customer?.id !== id) return; sources = data.items; renderSources(); }
        catch (error) { if (generation === sourceGeneration) { sources = []; renderSources(); notice(error.message); } }
    }
    async function chooseSource(row) {
        selectedSource = row; selectedPreview = null; const generation = ++sourceGeneration, customerId = customer.id;
        get("inSendAttested").checked = false; updateActions();
        const review = get("inSendReview"); review.hidden = false; review.replaceChildren(node("p", "Đang đọc phiên bản chứng từ hiện tại để bạn xác nhận…"));
        try {
            const preview = await api(`/customers/${customerId}/source-preview?sourceType=${encodeURIComponent(row.sourceType)}&sourceId=${row.sourceId}&kind=${encodeURIComponent(row.kind)}`);
            if (generation !== sourceGeneration || customer?.id !== customerId) return;
            selectedPreview = preview; updateActions(); if (!preview.canQueue) notice(preview.blockedReason || "Chứng từ hiện chưa đủ điều kiện gửi.");
        } catch (error) { if (generation === sourceGeneration) { selectedSource = null; renderSources(); updateActions(); notice(error.message); } }
    }
    async function loadThreads() {
        clearEvidence(); const connection = get("inConnection").value, query = get("inThreadQuery").value.trim(), generation = threadGeneration;
        if (!connection) return empty(get("inThreads"), "Chọn một kết nối Zalo.");
        empty(get("inThreads"), "Đang đọc hội thoại đã đồng bộ…");
        const data = await api(`/conversations?connection=${encodeURIComponent(connection)}&query=${encodeURIComponent(query)}`);
        if (generation !== threadGeneration || connection !== get("inConnection").value) return;
        const target = get("inThreads"); target.replaceChildren();
        if (!data.items.length) return empty(target, "Chưa có hội thoại cá nhân phù hợp. Nhờ khách nhắn mới và đồng bộ hộp thư trước khi xác minh.");
        for (const row of data.items) {
            const button = node("button", undefined, "in-choice"), content = node("span"); button.type = "button";
            content.append(node("strong", row.displayName), node("small", row.incomingSequence > 0 ? `Tin nhận #${row.incomingSequence} · ${time(row.incomingAt)}` : `Cập nhật ${time(row.incomingAt)} · Chọn để kiểm tra tin nhận mới nhất`)); button.append(content, node("span", "Kiểm tra SDK"));
            button.addEventListener("click", async () => {
                clearEvidence(); thread = row; const check = threadGeneration; button.disabled = true; notice("");
                try {
                    const result = await api(`/conversations/${row.conversationId}/evidence?connection=${encodeURIComponent(connection)}&sequence=${row.incomingSequence}`);
                    if (check !== threadGeneration || connection !== get("inConnection").value) return;
                    evidence = result; get("inEvidence").hidden = false;
                    get("inEvidence").replaceChildren(node("strong", result.verified ? "Đã kiểm tra nguồn SDK" : "Chưa đủ bằng chứng"), node("p", result.displayName),
                        node("p", `Tin nhận mới nhất #${result.incomingSequence} · ${time(result.observedAtUtc)}`), node("p", result.blockedReason || "Bằng chứng nguồn xác định đúng hội thoại và tài khoản; nhân viên vẫn phải đối chiếu danh tính khách bên dưới."));
                    updateActions();
                } catch (error) { notice(error.message); } finally { button.disabled = false; }
            }); target.append(button);
        }
    }
    async function loadOutbox() {
        if (loadingOutbox) return; loadingOutbox = true;
        try {
            const data = await api(`/outbox?page=${page}&state=${encodeURIComponent(get("inState").value)}`), target = get("inOutbox"); target.replaceChildren();
            if (!data.items.length) empty(target, "Chưa có lượt gửi trong bộ lọc này.");
            for (const row of data.items) {
                const article = node("article", undefined, "in-outbox-row"), content = node("div"), title = node("strong", `${row.code || row.sourceId} · ${kinds[row.kind] || row.kind}`);
                title.append(badge(row.state)); content.append(title, node("p", `${sourceNames[row.sourceType] || row.sourceType} #${row.sourceId} · Hồ sơ ${row.customerId || "chưa gắn"}`), node("p", `Tạo ${time(row.createdAtUtc)} · ${row.attempts} lần xử lý`));
                if (row.reason) content.append(node("p", row.reason, "in-reason"));
                const button = node("button", "Xem từng phần", "btn btn-sm btn-outline-primary"); button.type = "button";
                button.addEventListener("click", async () => { try { selectedDetail = row.id; await loadDetail(row.id); } catch (error) { notice(error.message); } });
                article.append(content, button); target.append(article);
            }
            get("inPage").textContent = `Trang ${page + 1}`; get("inPrevious").disabled = busy || page === 0; get("inNext").disabled = busy || data.items.length < 30;
        } finally { loadingOutbox = false; }
    }
    async function loadDetail(id) {
        const data = await api(`/outbox/${id}`); if (selectedDetail !== id) return;
        const target = get("inDetail"); target.hidden = false; target.replaceChildren(node("h4", `Chi tiết ${data.item.code || data.item.sourceId}`), badge(data.item.state));
        const fields = node("dl");
        for (const [label, value] of [["Nguồn", `${sourceNames[data.item.sourceType] || data.item.sourceType} #${data.item.sourceId}`], ["Tổng tiền", money(data.total)], ["Đã thanh toán", money(data.paidTotal)],
            ["Tiền thừa", money(data.changeDue)], ["Còn phải trả", money(data.balanceDue)], ["Điểm tại lúc chốt", data.currentPoints === null ? "Chưa có nguồn" : String(data.currentPoints)],
            ["Trạng thái nguồn", sourceStates[data.status] || data.status], ["Thanh toán", paymentStates[data.paymentStatus] || data.paymentStatus], ["Job ZaloMarketing", data.item.zaloJobId || "Chưa tiếp nhận"]]) {
            const item = node("div"); item.append(node("dt", label), node("dd", value)); fields.append(item);
        }
        target.append(fields); if (data.item.reason) target.append(node("p", data.item.reason, "in-reason"));
        if (data.binding) target.append(node("p", `Người nhận đã chốt: ${data.binding.connectionId} · ${data.binding.conversationId}`));
        if (!data.parts.length) target.append(node("p", "Chưa có bằng chứng kết quả từng phần.", "in-empty"));
        for (const part of data.parts) {
            const article = node("article", undefined, "in-part"), heading = node("div");
            heading.append(node("strong", `${part.index}. ${partKinds[part.kind] || part.kind}${part.fileName ? " · " + part.fileName : ""}`), node("span", partStates[part.state] || "Chưa rõ kết quả")); article.append(heading);
            if (part.text) article.append(node("pre", part.text));
            if (part.byteLength) article.append(node("small", `${part.byteLength} byte`));
            if (part.receiptId) article.append(node("small", `Mã xác nhận phần: ${part.receiptId}`));
            if (part.reason) article.append(node("p", part.reason, "in-reason")); target.append(article);
        }
    }
    get("inCustomerSearch").addEventListener("submit", async event => {
        event.preventDefault(); notice(""); try {
            const data = await api(`/customers?query=${encodeURIComponent(get("inCustomerQuery").value.trim())}`), target = get("inCustomers"); target.replaceChildren();
            if (!data.items.length) return empty(target, "Không có hồ sơ phù hợp trong cửa hàng. Kiểm tra đúng mã hoặc số đã lưu.");
            data.items.forEach(row => { const button = node("button", undefined, "in-choice"), content = node("span"); button.type = "button";
                content.append(node("strong", row.name), node("small", `${row.code || "Hồ sơ"} · ${row.phoneHint || "Chưa có số"}`)); button.append(content, node("span", "Chọn")); button.addEventListener("click", () => chooseCustomer(row)); target.append(button); });
        } catch (error) { notice(error.message); }
    });
    get("inThreadSearch").addEventListener("submit", event => { event.preventDefault(); loadThreads().catch(error => notice(error.message)); });
    get("inConnection").addEventListener("change", () => { clearEvidence(); empty(get("inThreads"), "Bấm Tải hội thoại để kiểm tra kết nối vừa chọn."); });
    get("inVerifyForm").addEventListener("input", () => { verifyRequest = crypto.randomUUID(); updateActions(); });
    get("inVerifyForm").addEventListener("submit", async event => {
        event.preventDefault(); if (get("inVerify").disabled) return;
        if (!confirm(`Lưu liên kết giữa hồ sơ ${customer.name} và hội thoại ${evidence.displayName} đã đối chiếu?`)) return;
        const body = { requestId: verifyRequest, customerId: customer.id, connectionId: get("inConnection").value, conversationId: evidence.conversationId,
            incomingSequence: evidence.incomingSequence, method: get("inMethod").value, evidenceReference: get("inReference").value.trim(), confirmed: get("inAttested").checked };
        commandBusy(true); notice(""); try { await api("/bindings", body); await loadBindings(); notice("Đã lưu người nhận đã xác minh. Chọn liên kết và đúng chứng từ trước khi tạo lượt gửi."); }
        catch (error) { notice(error.message + " Mã yêu cầu giữ nguyên; kiểm tra liên kết trước khi thử lại."); } finally { commandBusy(false); }
    });
    get("inSendAttested").addEventListener("change", updateActions);
    get("inSourceSearch").addEventListener("submit", event => { event.preventDefault(); loadSources(get("inSourceQuery").value.trim()); });
    get("inRecentSources").addEventListener("click", () => { get("inSourceQuery").value = ""; loadSources(""); });
    get("inReloadPreview").addEventListener("click", () => { if (selectedSource && !busy) chooseSource(selectedSource); });
    get("inRefreshBindings").addEventListener("click", () => loadBindings().catch(error => notice(error.message)));
    get("inSend").addEventListener("click", async () => {
        if (get("inSend").disabled) return;
        if (!confirm(`Tạo lượt gửi ${selectedPreview.label} ${selectedPreview.code || selectedPreview.sourceId}, tổng ${money(selectedPreview.total)}, tới người nhận đã xác minh của ${customer.name}?`)) return;
        const current = signature(); if (sendSignature !== current) { sendSignature = current; sendRequest = crypto.randomUUID(); }
        const body = { requestId: sendRequest, customerId: customer.id, sourceType: selectedPreview.sourceType, sourceId: selectedPreview.sourceId, kind: selectedPreview.kind,
            bindingId: selectedBinding.id, bindingVersion: selectedBinding.version, expectedSourceVersion: selectedPreview.sourceVersion, confirmed: get("inSendAttested").checked };
        commandBusy(true); notice(""); try {
            const data = await api("/outbox", body); acceptedSignature = current; selectedDetail = data.id;
            await loadOutbox(); await loadDetail(data.id); notice("Đã tiếp nhận vào hàng chờ. Kiểm tra kết quả từng phần; không tạo lại lượt gửi này.");
        } catch (error) {
            if (error.status === 409) { get("inSendAttested").checked = false; if (selectedPreview) selectedPreview = { ...selectedPreview, canQueue: false, blockedReason: "Đọc lại chứng từ đã chọn, kiểm tra người nhận rồi xác nhận phiên bản mới trước khi gửi." }; }
            notice(error.message + " Mã yêu cầu giữ nguyên; tải lại hàng chờ để đối chiếu trước khi thử lại.");
        } finally { commandBusy(false); }
    });
    get("inRefreshOutbox").addEventListener("click", () => loadOutbox().catch(error => notice(error.message)));
    get("inState").addEventListener("change", () => { page = 0; loadOutbox().catch(error => notice(error.message)); });
    get("inPrevious").addEventListener("click", () => { page = Math.max(0, page - 1); loadOutbox().catch(error => notice(error.message)); });
    get("inNext").addEventListener("click", () => { page++; loadOutbox().catch(error => notice(error.message)); });
    async function initialize() {
        try { const data = await api("/connections"), select = get("inConnection"); select.replaceChildren(node("option", "Chọn kết nối Zalo")); select.options[0].value = "";
            data.items.forEach(row => { const option = node("option", `${row.id} · ${row.accountHint}${row.enabled ? "" : " · đang tắt"}`); option.value = row.id; option.disabled = !row.enabled; select.append(option); });
            if (!data.items.length) empty(get("inThreads"), "Chưa cấu hình kết nối Zalo cho cửa hàng."); await loadOutbox();
        } catch (error) { notice(error.message); } updateActions();
    }
    initialize(); setInterval(() => { if (!document.hidden && !busy) loadOutbox().then(() => selectedDetail ? loadDetail(selectedDetail) : undefined).catch(() => {}); }, 20000);
})();
