(() => {
  "use strict";
  const panel = document.getElementById("provisionalReceivingPanel");
  if (!panel) return;

  const documentId = Number(panel.dataset.documentId);
  const mode = panel.dataset.mode;
  const itemsRoot = document.getElementById("provisionalReceivingItems");
  const message = document.getElementById("provisionalReceivingMessage");
  const api = `/admin/api/stock-documents/${documentId}/provisional-items`;
  let state = null;
  let editItemId = null;
  let options = null;

  const escapeHtml = value => String(value ?? "").replace(/[&<>'"]/g,
    char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" }[char]));
  const quantity = value => new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 3 }).format(value || 0);
  const commandId = () => crypto.randomUUID();
  const token = () => document.querySelector(
    '#provisionalReceivingAntiforgery input[name="__RequestVerificationToken"], #rw-antiforgery input[name="__RequestVerificationToken"]')?.value || "";
  const workbench = () => document.getElementById("receivingWorkbench");
  const currentRowVersion = () => workbench()?.dataset.rowVersion ||
    window.stockDocumentPage?.rowVersion || panel.dataset.documentRowVersion || "";
  const currentLease = () => workbench()?.dataset.leaseToken || null;

  function classifyUnknownInput(rawInput) {
    const normalized = String(rawInput || "").trim();
    const barcodeLike = /^\d{8,14}$/.test(normalized);
    return {
      barcodeLike,
      name: barcodeLike ? "" : normalized,
      rawBarcode: barcodeLike ? normalized : ""
    };
  }

  function setMessage(text, danger = false) {
    message.textContent = text || "";
    message.className = `small mt-2 ${danger ? "text-danger" : "text-success"}`;
    if (danger) message.focus?.();
  }

  async function request(path = "", method = "GET", body = null) {
    const response = await fetch(`${api}${path}`, {
      method,
      credentials: "same-origin",
      headers: {
        "Content-Type": "application/json",
        "RequestVerificationToken": token()
      },
      body: body ? JSON.stringify(body) : null
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(payload.message || "Không thể lưu mặt hàng mới.");
    return payload;
  }

  function mutation(item, extra = {}) {
    return {
      commandId: commandId(),
      leaseToken: currentLease(),
      documentRowVersion: currentRowVersion(),
      ...(item ? { itemRowVersion: item.rowVersion } : {}),
      ...extra
    };
  }

  function applyState(payload) {
    state = payload;
    const documentRowVersion = payload.documentRowVersion || "";
    if (window.stockDocumentRowVersion) {
      if (!window.stockDocumentRowVersion.update(documentRowVersion))
        throw new Error("Máy chủ không trả về phiên bản phiếu hợp lệ. Vui lòng tải lại.");
    } else panel.dataset.documentRowVersion = documentRowVersion;
    const rw = workbench();
    if (rw) rw.dataset.rowVersion = documentRowVersion || rw.dataset.rowVersion;
    if (!window.stockDocumentRowVersion && window.stockDocumentPage)
      window.stockDocumentPage.rowVersion = documentRowVersion;
    document.dispatchEvent(new CustomEvent("provisional:state", { detail: payload }));
    render();
  }

  function statusBadge(item) {
    if (item.status === 1) return '<span class="badge bg-label-success">Đã liên kết danh mục</span>';
    if (item.status === 2) return '<span class="badge bg-label-secondary">Đã loại khỏi phiếu</span>';
    return '<span class="badge bg-label-warning">Chờ Manager xử lý</span>';
  }

  function warehouseActions(item) {
    if (item.status !== 0) return "";
    return `<div class="provisional-actions">
      <button type="button" class="btn btn-sm btn-outline-primary provisional-increment" data-id="${item.id}">+1</button>
      <button type="button" class="btn btn-sm btn-outline-secondary provisional-edit" data-id="${item.id}">Sửa</button>
      <button type="button" class="btn btn-sm btn-outline-danger provisional-remove" data-id="${item.id}">Loại</button>
    </div>`;
  }

  function managerActions(item) {
    if (item.status !== 0) return "";
    return `<div class="provisional-actions">
      <button type="button" class="btn btn-sm btn-primary provisional-link" data-id="${item.id}">Liên kết sản phẩm có sẵn</button>
      <button type="button" class="btn btn-sm btn-outline-primary provisional-create" data-id="${item.id}">Tạo sản phẩm mới</button>
      <button type="button" class="btn btn-sm btn-outline-danger provisional-remove" data-id="${item.id}">Loại khỏi phiếu</button>
    </div>`;
  }

  function render() {
    const unified = !!document.getElementById('receiptIntake');
    const items = (state?.items || []).filter(item => item.status === 0 && (!unified || !item.proposedFactor));
    if (unified) panel.hidden = items.length === 0;
    itemsRoot.innerHTML = items.length ? items.map(item => `
      <article class="provisional-item ${item.status === 0 ? "is-unresolved" : ""}" data-id="${item.id}">
        <div class="provisional-item-main">
          <strong>${escapeHtml(item.name)}</strong>
          <div>${quantity(item.quantity)} ${escapeHtml(item.unitName || "chưa rõ đơn vị")}</div>
          <div class="small text-muted">${item.rawBarcode ? `Barcode: ${escapeHtml(item.rawBarcode)}` : "Không có barcode"}${item.note ? ` · ${escapeHtml(item.note)}` : ""}</div>
          ${statusBadge(item)}
        </div>
        ${mode === "manager" ? managerActions(item) : warehouseActions(item)}
      </article>`).join("") : '<div class="provisional-empty">Chưa ghi nhận hàng ngoài danh mục.</div>';
  }

  function ensureCaptureModal() {
    if (document.getElementById("provisionalCaptureModal")) return;
    document.body.insertAdjacentHTML("beforeend", `
      <div class="modal fade" id="provisionalCaptureModal" tabindex="-1" aria-labelledby="provisionalCaptureTitle" aria-hidden="true">
        <div class="modal-dialog modal-lg modal-dialog-centered modal-fullscreen-sm-down"><div class="modal-content">
          <div class="modal-header"><h5 id="provisionalCaptureTitle" class="modal-title">Ghi nhận hàng mới</h5><button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Đóng"></button></div>
          <div class="modal-body">
            <div class="mb-3"><label for="provisionalName" class="form-label">Tên hàng <span class="text-danger">*</span></label><input id="provisionalName" class="form-control" maxlength="200" autocomplete="off"></div>
            <div class="mb-3"><label for="provisionalBarcode" class="form-label">Barcode thực tế</label><input id="provisionalBarcode" class="form-control" maxlength="256" autocomplete="off"><div class="form-text">Chỉ Manager mới có thể ghi barcode vào danh mục.</div></div>
            <div class="row g-3"><div class="col-sm-7"><label for="provisionalUnit" class="form-label">Đơn vị</label><select id="provisionalUnit" class="form-select"><option value="">Chưa rõ / nhập bên cạnh</option></select></div><div class="col-sm-5"><label for="provisionalUnitName" class="form-label">Tên đơn vị ghi nhận</label><input id="provisionalUnitName" class="form-control" maxlength="100"></div></div>
            <div class="mt-3"><label for="provisionalQuantity" class="form-label">Số lượng <span class="text-danger">*</span></label><input id="provisionalQuantity" class="form-control" type="number" min="1" step="1" value="1" inputmode="decimal"></div>
            <div class="mt-3"><label for="provisionalNote" class="form-label">Ghi chú</label><textarea id="provisionalNote" class="form-control" maxlength="500" rows="2"></textarea></div>
          </div>
          <div class="modal-footer"><button type="button" class="btn btn-label-secondary" data-bs-dismiss="modal">Hủy</button><button type="button" class="btn btn-primary" id="btnSaveProvisional">Ghi nhận &amp; tự lưu</button></div>
        </div></div>
      </div>`);
  }

  function ensureManagerModal() {
    if (document.getElementById("provisionalResolveModal")) return;
    document.body.insertAdjacentHTML("beforeend", `
      <div class="modal fade" id="provisionalResolveModal" tabindex="-1" aria-labelledby="provisionalResolveTitle" aria-hidden="true">
        <div class="modal-dialog modal-lg modal-dialog-centered"><div class="modal-content">
          <div class="modal-header"><h5 id="provisionalResolveTitle" class="modal-title">Xử lý sản phẩm chưa có trong danh mục</h5><button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Đóng"></button></div>
          <div class="modal-body">
            <div id="provisionalResolveEvidence" class="alert alert-warning"></div>
            <div id="provisionalLinkFields">
              <label for="provisionalCandidate" class="form-label">Sản phẩm và đơn vị quy đổi</label>
              <select id="provisionalCandidate" class="form-select" style="width:100%"></select>
            </div>
            <div id="provisionalCreateFields" class="row g-3 d-none">
              <div class="col-12"><label for="provisionalProductName" class="form-label">Tên sản phẩm</label><input id="provisionalProductName" class="form-control" maxlength="200"></div>
              <div class="col-md-6"><label for="provisionalCategory" class="form-label">Danh mục <span class="text-danger">*</span></label><select id="provisionalCategory" class="form-select"></select></div>
              <div class="col-md-6"><label for="provisionalCreateUnit" class="form-label">Đơn vị gốc</label><select id="provisionalCreateUnit" class="form-select"></select></div>
              <div class="col-12"><label for="provisionalNewUnit" class="form-label">Hoặc tạo đơn vị mới</label><input id="provisionalNewUnit" class="form-control" maxlength="200"><div class="form-text">Yêu cầu quyền Catalog.Unit.Create.</div></div>
            </div>
            <div class="form-check mt-3"><input id="provisionalRememberBarcode" class="form-check-input" type="checkbox"><label class="form-check-label" for="provisionalRememberBarcode">Ghi nhớ barcode này cho sản phẩm/đơn vị đã chọn</label><div id="provisionalBarcodeHelp" class="form-text"></div></div>
          </div>
          <div class="modal-footer"><button type="button" class="btn btn-label-secondary" data-bs-dismiss="modal">Hủy</button><button type="button" class="btn btn-primary" id="btnResolveProvisional">Xác nhận</button></div>
        </div></div>
      </div>`);
  }

  async function loadOptions(manager = false) {
    if (options && manager) return options;
    const path = manager ? "/options" : "/capture-options";
    options = await request(path);
    const units = options.units || [];
    const capture = document.getElementById("provisionalUnit");
    if (capture) capture.innerHTML = '<option value="">Chưa rõ / nhập bên cạnh</option>' + units.map(x => `<option value="${x.id}">${escapeHtml(x.text)}${x.code ? ` (${escapeHtml(x.code)})` : ""}</option>`).join("");
    const createUnit = document.getElementById("provisionalCreateUnit");
    if (createUnit) createUnit.innerHTML = '<option value="">Chọn đơn vị hoặc tạo mới</option>' + units.map(x => `<option value="${x.id}">${escapeHtml(x.text)}</option>`).join("");
    const category = document.getElementById("provisionalCategory");
    if (category) category.innerHTML = '<option value="">Chọn danh mục</option>' + (options.categories || []).map(x => `<option value="${x.id}">${escapeHtml(x.text)}</option>`).join("");
    return options;
  }

  function itemById(id) { return state?.items?.find(item => Number(item.id) === Number(id)); }

  async function saveCapture() {
    const item = editItemId ? itemById(editItemId) : null;
    const unitId = Number(document.getElementById("provisionalUnit").value) || null;
    const payload = mutation(item, {
      name: document.getElementById("provisionalName").value,
      rawBarcode: item ? undefined : document.getElementById("provisionalBarcode").value,
      unitId,
      unitName: unitId ? null : document.getElementById("provisionalUnitName").value,
      quantity: Number(document.getElementById("provisionalQuantity").value),
      note: document.getElementById("provisionalNote").value
    });
    if (item) delete payload.rawBarcode;
    const result = await request(item ? `/${item.id}/edit` : "", "POST", payload);
    applyState(result);
    bootstrap.Modal.getOrCreateInstance(document.getElementById("provisionalCaptureModal")).hide();
    setMessage(item ? "Đã cập nhật và tự lưu." : "Đã ghi nhận hàng mới và tự lưu.");
    document.dispatchEvent(new CustomEvent("provisional:saved", { detail: result }));
  }

  async function openCapture(item = null, prefill = {}) {
    editItemId = item?.id || null;
    await loadOptions(false);
    document.getElementById("provisionalName").value = item?.name || prefill.name || "";
    document.getElementById("provisionalBarcode").value = item?.rawBarcode || prefill.rawBarcode || "";
    document.getElementById("provisionalBarcode").disabled = !!item;
    document.getElementById("provisionalUnit").value = item?.unitId || "";
    document.getElementById("provisionalUnitName").value = item?.unitId ? "" : item?.unitName || "";
    document.getElementById("provisionalQuantity").value = item?.quantity || 1;
    document.getElementById("provisionalNote").value = item?.note || "";
    bootstrap.Modal.getOrCreateInstance(document.getElementById("provisionalCaptureModal")).show();
    window.setTimeout(() => document.getElementById("provisionalName").focus(), 150);
  }

  async function openResolve(item, create) {
    ensureManagerModal();
    await loadOptions(true);
    panel.dataset.resolveItemId = item.id;
    panel.dataset.resolveMode = create ? "create" : "link";
    document.getElementById("provisionalResolveEvidence").textContent = `${item.name} · ${quantity(item.quantity)} ${item.unitName || "chưa rõ đơn vị"}${item.rawBarcode ? ` · Barcode ${item.rawBarcode}` : ""}`;
    document.getElementById("provisionalLinkFields").classList.toggle("d-none", create);
    document.getElementById("provisionalCreateFields").classList.toggle("d-none", !create);
    document.getElementById("provisionalProductName").value = item.name;
    document.getElementById("provisionalCreateUnit").value = item.unitId || "";
    document.getElementById("provisionalNewUnit").value = item.unitId ? "" : item.unitName || "";
    const remember = document.getElementById("provisionalRememberBarcode");
    const canRegister = !!item.rawBarcode && item.rawBarcode.trim().length <= 64;
    remember.checked = false;
    remember.disabled = !canRegister;
    document.getElementById("provisionalBarcodeHelp").textContent = !item.rawBarcode
      ? "Không có barcode để ghi nhớ."
      : canRegister ? "Chỉ ghi vào danh mục khi bạn chủ động chọn." : "Barcode dài hơn 64 ký tự chỉ được giữ làm bằng chứng, không đăng ký.";

    const candidate = document.getElementById("provisionalCandidate");
    if (window.jQuery && $.fn.select2 && !$(candidate).data("select2")) {
      $(candidate).select2({
        theme: "bootstrap-5",
        dropdownParent: $("#provisionalResolveModal"),
        minimumInputLength: 1,
        ajax: {
          url: `${api}/candidates`, dataType: "json", delay: 250,
          data: params => ({ term: params.term || item.name }),
          processResults: payload => payload
        }
      });
    }
    bootstrap.Modal.getOrCreateInstance(document.getElementById("provisionalResolveModal")).show();
  }

  async function resolveCurrent() {
    const item = itemById(panel.dataset.resolveItemId);
    if (!item) return;
    const create = panel.dataset.resolveMode === "create";
    const rememberRawBarcode = document.getElementById("provisionalRememberBarcode").checked;
    let path;
    let payload;
    if (create) {
      path = `/${item.id}/quick-create`;
      payload = mutation(item, {
        categoryId: Number(document.getElementById("provisionalCategory").value),
        unitId: Number(document.getElementById("provisionalCreateUnit").value) || null,
        newUnitName: document.getElementById("provisionalNewUnit").value,
        productName: document.getElementById("provisionalProductName").value,
        rememberRawBarcode
      });
    } else {
      const selected = window.jQuery ? $("#provisionalCandidate").select2("data")[0] : null;
      if (!selected) throw new Error("Vui lòng chọn sản phẩm và đơn vị quy đổi.");
      path = `/${item.id}/link`;
      payload = mutation(item, {
        productVariantId: Number(selected.productVariantId),
        productUnitConversionId: Number(selected.productUnitConversionId || selected.id),
        rememberRawBarcode
      });
    }
    const result = await request(path, "POST", payload);
    applyState(result);
    const resolved = result.items?.find(candidate => Number(candidate.id) === Number(item.id));
    if (rememberRawBarcode && resolved && !resolved.rawBarcodeRemembered)
      window.alert("Đã liên kết sản phẩm nhưng barcode có lịch sử cũ nên không được ghi nhớ lại.");
    setMessage("Đã xử lý sản phẩm. Đang tải lại phiếu…");
    window.location.reload();
  }

  itemsRoot.addEventListener("click", async event => {
    const button = event.target.closest("button[data-id]");
    if (!button) return;
    const item = itemById(button.dataset.id);
    if (!item) return;
    try {
      if (button.classList.contains("provisional-increment"))
        applyState(await request(`/${item.id}/increment`, "POST", mutation(item, { quantity: 1 })));
      else if (button.classList.contains("provisional-edit")) await openCapture(item);
      else if (button.classList.contains("provisional-remove")) {
        if (!window.confirm(`Loại '${item.name}' khỏi phiếu? Bằng chứng audit vẫn được giữ.`)) return;
        const result = await request(`/${item.id}/remove`, "POST", mutation(item));
        applyState(result);
        if (mode === "manager") window.location.reload();
      } else if (button.classList.contains("provisional-link")) await openResolve(item, false);
      else if (button.classList.contains("provisional-create")) await openResolve(item, true);
    } catch (error) { setMessage(error.message, true); }
  });

  if (mode === "warehouse") {
    ensureCaptureModal();
    document.getElementById("btnCaptureProvisional")?.addEventListener("click", event => {
      event.preventDefault();
      openCapture().catch(error => setMessage(error.message, true));
    });
    document.getElementById("btnSaveProvisional")?.addEventListener("click", () =>
      saveCapture().catch(error => setMessage(error.message, true)));
    document.getElementById("provisionalQuantity")?.addEventListener("keydown", event => {
      if (event.key !== "Enter") return;
      event.preventDefault();
      saveCapture().catch(error => setMessage(error.message, true));
    });
    document.addEventListener("provisional:capture-request", event => {
      openCapture(null, classifyUnknownInput(event.detail?.rawInput || ""))
        .catch(error => setMessage(error.message, true));
    });
  } else {
    ensureManagerModal();
    document.getElementById("btnResolveProvisional")?.addEventListener("click", () =>
      resolveCurrent().catch(error => setMessage(error.message, true)));
  }

  request().then(applyState).catch(error => setMessage(error.message, true));
})();
