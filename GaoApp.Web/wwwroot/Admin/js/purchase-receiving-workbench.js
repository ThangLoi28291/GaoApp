(() => {
  "use strict";
  const root = document.getElementById("receivingWorkbench");
  if (!root) return;

  const id = Number(root.dataset.documentId);
  const tokenInput = document.querySelector('#rw-antiforgery input[name="__RequestVerificationToken"]');
  const storageKey = `gaoapp:receiving-lease:${id}`;
  let leaseToken = root.dataset.leaseToken || sessionStorage.getItem(storageKey) || null;
  let rowVersion = root.dataset.rowVersion;
  let state = null;
  let busy = false;
  let cameraStream = null;
  let cameraControls = null;
  let cameraDecoderPromise = null;
  let cameraGeneration = 0;
  let cameraTorchEnabled = false;
  if (leaseToken) sessionStorage.setItem(storageKey, leaseToken);

  const message = document.getElementById("rwMessage");
  const saveState = document.getElementById("rwSaveState");
  const receiveLookup = document.getElementById("rwReceiveLookup");
  const receiveQuantity = document.getElementById("rwReceiveQuantity");
  const resolvedItem = document.getElementById("rwResolvedItem");
  const $lookup = window.jQuery && receiveLookup ? $(receiveLookup) : null;
  let selectedLookupItem = null;
  let resolvingLookup = false;
  const canApproveOutside = root.dataset.canApproveOutside === "true";
  const escapeHtml = value => String(value ?? "").replace(/[&<>'"]/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;",'"':"&quot;"}[c]));
  const quantity = value => new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 3 }).format(value || 0);
  const commandId = () => crypto.randomUUID();

  function setSave(text, saving = false) {
    saveState.innerHTML = `<i class="bx ${saving ? "bx-loader-alt bx-spin" : "bx-cloud"}"></i><span>${escapeHtml(text)}</span>`;
  }
  function showError(text) {
    message.className = "alert alert-danger";
    message.textContent = text;
    message.focus();
  }
  function clearError() { message.className = "alert d-none"; message.textContent = ""; }
  document.addEventListener("receipt-barcode:selected", event => selectLookupItem(event.detail));
  function showUnknown(term, searchField) {
    message.className = "alert alert-warning";
    message.innerHTML = `<div class="fw-semibold">Không tìm thấy sản phẩm</div>
      <div class="small mb-2">Có thể tìm lại hoặc ghi nhận nguyên trạng để Manager xử lý danh mục sau.</div>
      <div class="d-flex flex-wrap gap-2">
        <button type="button" class="btn btn-sm btn-outline-secondary" id="rwRetryUnknown">Tìm lại</button>
        <button type="button" class="btn btn-sm btn-primary" id="rwCaptureUnknown">Ghi nhận hàng mới</button>
      </div>`;
    document.getElementById("rwRetryUnknown")?.addEventListener("click", () => {
      clearError();
      searchField.focus();
      searchField.select();
    });
    document.getElementById("rwCaptureUnknown")?.addEventListener("click", () => {
      clearError();
      document.dispatchEvent(new CustomEvent("provisional:capture-request", {
        detail: { rawInput: term }
      }));
    });
  }

  async function post(action, body) {
    if (busy) throw new Error("Một thao tác khác đang được lưu.");
    busy = true; clearError(); setSave("Đang tự lưu…", true);
    try {
      const response = await fetch(`/admin/purchase-receiving/${id}/${action}`, {
        method: "POST",
        credentials: "same-origin",
        headers: { "Content-Type": "application/json", "RequestVerificationToken": tokenInput?.value || "" },
        body: JSON.stringify(body)
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.message || "Không thể lưu thao tác nhận hàng.");
      applyState(payload);
      setSave("Đã tự lưu");
      return payload;
    } finally { busy = false; }
  }

  function leaseBody(extra = {}) { return { leaseToken, rowVersion, ...extra }; }
  function applyState(payload) {
    state = payload;
    rowVersion = payload.rowVersion;
    root.dataset.rowVersion = rowVersion || "";
    if (payload.leaseToken) {
      leaseToken = payload.leaseToken;
      root.dataset.leaseToken = leaseToken;
      sessionStorage.setItem(storageKey, leaseToken);
    }
    renderProgress(payload.progress || []);
    renderComponents(payload.components || []);
    renderSummary(payload);
    document.getElementById("rwUndo").disabled = !payload.canUndo || payload.sessionState !== 1;
    document.getElementById("rwReview").disabled =
      (!payload.components?.length && !payload.unresolvedProvisionalCount) || payload.sessionState !== 1;
  }

  document.addEventListener("provisional:state", event => {
    const detail = event.detail || {};
    if (Number(detail.stockDocumentId) !== id) return;
    rowVersion = detail.documentRowVersion || rowVersion;
    root.dataset.rowVersion = rowVersion || "";
    if (state) state.unresolvedProvisionalCount = Number(detail.unresolvedCount || 0);
    const review = document.getElementById("rwReview");
    if (review) {
      const componentCount = state?.components?.length ?? document.querySelectorAll("#rwComponents .rw-component").length;
      review.disabled = (componentCount === 0 && !detail.unresolvedCount) || root.dataset.editable !== "true";
    }
    if (state) renderSummary(state);
  });
  document.addEventListener("provisional:saved", focusUnifiedSearch);
  function renderProgress(items) {
    document.getElementById("rwProgress").innerHTML = items.map(item => {
      const receivedBase = Number(item.confirmedBaseQuantity || 0) + Number(item.currentReceiptBaseQuantity || 0);
      const orderedBase = Number(item.orderedBaseQuantity || 0);
      const percent = orderedBase <= 0 ? 0 : Math.min(100, Math.round(receivedBase / orderedBase * 100));
      const over = item.projectedOverdeliveryBaseQuantity > 0
        ? `<div class="text-danger small mt-2"><i class="bx bx-error"></i> Dự kiến vượt ${quantity(item.projectedOverdeliveryBaseQuantity)} ${escapeHtml(item.baseUnitName)} — Manager phải xác nhận khi duyệt.</div>` : "";
      return `<article class="rw-progress-card"><div class="rw-progress-heading"><div><strong>${escapeHtml(item.productName)}</strong><span class="rw-order-unit">${quantity(item.orderedQuantity)} ${escapeHtml(item.orderedUnitName)}</span></div><b>${percent}%</b></div><div class="progress my-2" role="progressbar" aria-label="Tiến độ nhận ${escapeHtml(item.productName)}" aria-valuenow="${percent}" aria-valuemin="0" aria-valuemax="100"><div class="progress-bar" style="width:${percent}%"></div></div><div class="rw-metrics"><span><small>Đã nhập kho</small><strong>${quantity(item.confirmedBaseQuantity)} ${escapeHtml(item.baseUnitName)}</strong></span><span><small>Đợt này</small><strong>${quantity(item.currentReceiptBaseQuantity)} ${escapeHtml(item.baseUnitName)}</strong></span><span><small>Còn chờ</small><strong>${quantity(item.remainingBaseQuantity)} ${escapeHtml(item.baseUnitName)}</strong></span></div>${over}</article>`;
    }).join("");
  }
  function renderComponents(items) {
    const editable = state ? state.sessionState === 1 : root.dataset.editable === "true";
    document.getElementById("rwComponents").innerHTML = items.map(line => {
      const outside = line.allocationKind === 2;
      const badge = outside ? `<span class="badge bg-label-warning">Ngoài PO · ${["N/A","Chờ xử lý","Đã chấp nhận","Đã từ chối"][line.outsideStatus] || "Chờ xử lý"}</span>` : "";
      const managerControls = outside && canApproveOutside && line.outsideStatus === 1
        ? '<div class="d-flex gap-2 mt-2"><button type="button" class="btn btn-sm btn-outline-success rw-outside-accept">Chấp nhận</button><button type="button" class="btn btn-sm btn-outline-danger rw-outside-reject">Từ chối</button></div>' : "";
      const controls = editable ? `<div class="d-flex align-items-center gap-2"><input class="form-control form-control-sm rw-edit" type="number" min="0.001" step="0.001" value="${line.quantity}" aria-label="Sửa số lượng ${escapeHtml(line.productName)}"/><button class="btn btn-icon btn-sm btn-outline-danger rw-remove" type="button" aria-label="Xóa ${escapeHtml(line.productName)}"><i class="bx bx-trash"></i></button></div>` : managerControls;
      const physical = `${quantity(line.quantity)} ${escapeHtml(line.unitName)}`;
      const received = line.isBaseUnit
        ? physical
        : `${physical} <span class="text-muted">= ${quantity(line.baseQuantity)} ${escapeHtml(line.baseUnitName)}</span>`;
      return `<article class="rw-component ${outside ? "is-outside" : ""}" data-line-id="${line.id}"><div class="flex-grow-1"><strong>${escapeHtml(line.productName)}</strong><div>${received}</div>${badge}</div>${controls}</article>`;
    }).join("");
    const oldEmpty = document.getElementById("rwEmpty");
    if (oldEmpty) oldEmpty.remove();
    if (!items.length) document.getElementById("rwComponents").insertAdjacentHTML("afterend", '<div id="rwEmpty" class="rw-empty">Chưa có hàng. Hãy quét barcode hoặc tìm sản phẩm.</div>');
    const poCount = items.filter(item => item.allocationKind === 1).length;
    const outsideCount = items.filter(item => item.allocationKind === 2).length;
    document.getElementById("rwFinalSummary").innerHTML = `<div><span>Dòng thuộc PO</span><strong>${poCount}</strong></div><div><span>Ngoài PO</span><strong class="${outsideCount ? "text-warning" : ""}">${outsideCount}</strong></div>`;
  }

  function renderSummary(payload) {
    const components = payload.components || [];
    const progress = payload.progress || [];
    const poCount = components.filter(item => item.allocationKind === 1).length;
    const outsideCount = components.filter(item => item.allocationKind === 2).length;
    const completed = progress.filter(item => Number(item.remainingBaseQuantity || 0) <= 0).length;
    const unresolved = Number(payload.unresolvedProvisionalCount || 0);
    const poElement = document.getElementById("rwSummaryPoLines");
    const completedElement = document.getElementById("rwSummaryCompleted");
    const exceptionsElement = document.getElementById("rwSummaryExceptions");
    if (poElement) poElement.textContent = String(poCount);
    if (completedElement) completedElement.textContent = `${completed}/${progress.length}`;
    if (exceptionsElement) exceptionsElement.textContent = String(outsideCount + unresolved);
  }

  function parseLookupItem(item) {
    const parts = String(item?.text || "").split(" | ").map(value => value.trim()).filter(Boolean);
    const part = prefix => parts.find(value => value.toLocaleUpperCase("vi-VN").startsWith(prefix));
    const factorText = part("X")?.slice(1);
    return {
      productName: parts[0] || "Sản phẩm",
      sku: part("SKU:")?.slice(4).trim() || "",
      unitName: item?.unitName || part("ĐV:")?.slice(3).trim() || "Đơn vị",
      baseUnitName: item?.baseUnitName || "",
      isBaseUnit: item?.isBaseUnit === true,
      factor: Number(item?.factor ?? factorText ?? 1),
      barcode: item?.barcode || part("BC:")?.slice(3).trim() || ""
    };
  }

  function isPurchaseOrderItem(item) {
    const variantId = Number(item?.productVariantId);
    return !!variantId && !!state?.progress?.some(progress => Number(progress.productVariantId) === variantId);
  }

  function renderLookupResult(item) {
    if (item.loading) return escapeHtml(item.text);
    const display = parseLookupItem(item);
    const poBadge = isPurchaseOrderItem(item)
      ? '<span class="badge bg-label-success rw-lookup-po">Trong PO</span>'
      : '<span class="badge bg-label-warning rw-lookup-outside">Ngoài PO</span>';
    const sku = display.sku ? `<span>SKU: ${escapeHtml(display.sku)}</span>` : "";
    const barcodeText = display.barcode
      ? `<span class="rw-lookup-barcode">BC ${escapeHtml(display.barcode)}</span>` : "";
    return `<div class="rw-lookup-result"><div class="rw-lookup-name">${escapeHtml(display.productName)}</div><div class="rw-lookup-meta">${sku}${poBadge}</div><div class="rw-lookup-badges"><span class="badge bg-label-primary rw-lookup-unit">${escapeHtml(display.unitName)}</span><span class="badge bg-label-secondary rw-lookup-factor">×${quantity(display.factor)}</span>${barcodeText}</div></div>`;
  }

  function renderLookupSelection(item) {
    if (!item?.id) return escapeHtml(item?.text || receiveLookup?.dataset.placeholder || "");
    const display = parseLookupItem(item);
    return `<span class="rw-lookup-selection"><strong>${escapeHtml(display.productName)}</strong><span>${escapeHtml(display.unitName)} ×${quantity(display.factor)}</span></span>`;
  }

  function processLookupResults(payload) {
    const results = [...(payload?.results || [])];
    results.sort((left, right) => {
      const poOrder = Number(isPurchaseOrderItem(right)) - Number(isPurchaseOrderItem(left));
      if (poOrder) return poOrder;
      return parseLookupItem(left).productName.localeCompare(
        parseLookupItem(right).productName, "vi", { sensitivity: "base" });
    });
    return { results };
  }

  function renderResolvedItem(item) {
    const display = parseLookupItem(item);
    const poBadge = isPurchaseOrderItem(item)
      ? '<span class="badge bg-label-success">Trong PO</span>'
      : '<span class="badge bg-label-warning">Ngoài PO</span>';
    const sku = display.sku ? `<span>SKU: ${escapeHtml(display.sku)}</span>` : "";
    const barcodeText = display.barcode ? `<span>BC: ${escapeHtml(display.barcode)}</span>` : "";
    const equivalent = display.isBaseUnit
      ? ""
      : `<div class="rw-resolved-equivalent">Tương đương: 1 ${escapeHtml(display.unitName)} = ${quantity(display.factor)} ${escapeHtml(display.baseUnitName)}</div>`;
    resolvedItem.innerHTML = `<div><strong>${escapeHtml(display.productName)}</strong><div class="rw-resolved-meta">${sku}${barcodeText}</div></div><div class="rw-resolved-badges">${poBadge}<span class="badge bg-label-primary">${escapeHtml(display.unitName)}</span><span class="badge bg-label-secondary">×${quantity(display.factor)}</span></div>${equivalent}`;
    resolvedItem.classList.remove("d-none");
  }

  function selectLookupItem(item) {
    selectedLookupItem = item;
    renderResolvedItem(item);
    receiveQuantity.value = "1";
    window.setTimeout(() => {
      receiveQuantity.focus();
      receiveQuantity.select();
    }, 0);
  }

  function clearLookupSelection() {
    selectedLookupItem = null;
    receiveQuantity.value = "1";
    resolvedItem.classList.add("d-none");
    resolvedItem.innerHTML = "";
    if ($lookup) $lookup.val(null).trigger("change");
  }

  function focusUnifiedSearch() {
    if (root.dataset.editable !== "true" || receiveLookup?.disabled) return;
    if ($lookup?.data("select2")) {
      $lookup.select2("open");
      window.setTimeout(() => {
        const field = document.querySelector(".select2-container--open .select2-search__field");
        field?.focus();
        field?.select();
      }, 0);
      return;
    }
    receiveLookup?.focus();
  }

  function chooseAllocation(item, candidates) {
    const modalElement = document.getElementById("rwAllocationModal");
    const choices = document.getElementById("rwAllocationChoices");
    const description = document.getElementById("rwAllocationDescription");
    const outside = document.getElementById("rwAllocationOutside");
    const display = parseLookupItem(item);
    if (!modalElement || !choices || !description || !outside) return Promise.resolve(undefined);

    description.innerHTML = `Mã vừa quét là <strong>${escapeHtml(display.productName)}</strong> — <strong>${escapeHtml(display.unitName)}</strong>. Chọn dòng đặt hàng cần đối chiếu.`;
    choices.innerHTML = candidates.map(candidate => `
      <button type="button" class="rw-allocation-choice" data-po-line-id="${candidate.purchaseOrderLineId}">
        <span class="rw-allocation-choice-icon"><i class="bx bx-package"></i></span>
        <span><strong>${escapeHtml(candidate.productName)}</strong><small>Đơn vị đặt: ${quantity(candidate.orderedQuantity)} ${escapeHtml(candidate.orderedUnitName)}</small></span>
        <span class="rw-allocation-remaining"><small>Còn chờ</small><strong>${quantity(candidate.remainingBaseQuantity)} ${escapeHtml(candidate.baseUnitName)}</strong></span>
        <i class="bx bx-chevron-right"></i>
      </button>`).join("");

    return new Promise(resolve => {
      const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
      let settled = false;
      const finish = value => {
        if (settled) return;
        settled = true;
        resolve(value);
        modal.hide();
      };
      const onChoice = event => {
        const button = event.target.closest("[data-po-line-id]");
        if (button) finish(Number(button.dataset.poLineId));
      };
      const onOutside = () => finish(null);
      const onHidden = () => {
        choices.removeEventListener("click", onChoice);
        outside.removeEventListener("click", onOutside);
        modalElement.removeEventListener("hidden.bs.modal", onHidden);
        if (!settled) { settled = true; resolve(undefined); }
      };
      choices.addEventListener("click", onChoice);
      outside.addEventListener("click", onOutside);
      modalElement.addEventListener("hidden.bs.modal", onHidden);
      modal.show();
    });
  }

  async function resolveAllocationChoice(item) {
    const variantId = Number(item?.productVariantId);
    if (!variantId || !state?.progress?.length) return null;
    const candidates = state.progress.filter(progress =>
      Number(progress.productVariantId) === variantId);
    if (!candidates.length) return null;
    const conversionId = Number(item?.productUnitConversionId || item?.id);
    const exact = candidates.filter(progress =>
      Number(progress.productUnitConversionId) === conversionId);
    if (exact.length === 1) return Number(exact[0].purchaseOrderLineId);
    if (candidates.length === 1) return Number(candidates[0].purchaseOrderLineId);
    return chooseAllocation(item, candidates);
  }

  async function resolveUniqueLookup(term, searchField) {
    if (resolvingLookup) return;
    resolvingLookup = true;
    try {
      const separator = root.dataset.lookupUrl.includes("?") ? "&" : "?";
      const response = await fetch(`${root.dataset.lookupUrl}${separator}term=${encodeURIComponent(term)}`, {
        credentials: "same-origin"
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.message || "Không thể tìm sản phẩm.");
      const results = processLookupResults(payload).results;
      if (results.length === 1) {
        const item = results[0];
        $lookup.find(`option[value="${item.id}"]`).remove();
        $lookup.append(new Option(item.text, item.id, true, true)).trigger("change");
        $lookup.select2("close");
        selectLookupItem(item);
      } else if (!results.length) {
        if (window.ReceiptBarcodeProposals?.open(term, selectLookupItem)) return;
        if (document.getElementById("btnCaptureProvisional"))
          showUnknown(term, searchField);
        else {
          showError("Không tìm thấy barcode, SKU hoặc sản phẩm phù hợp.");
          searchField.focus();
          searchField.select();
        }
      }
    } catch (error) {
      showError(error.message);
      searchField.focus();
      searchField.select();
    } finally {
      resolvingLookup = false;
    }
  }

  function bindUnifiedSearchKeyboard() {
    const searchField = document.querySelector(".select2-container--open .select2-search__field");
    if (!searchField || searchField.dataset.rwKeyboardBound === "true") return;
    searchField.dataset.rwKeyboardBound = "true";
    searchField.setAttribute("aria-label", "Quét barcode hoặc tìm sản phẩm");
    searchField.addEventListener("keydown", async event => {
      if (event.key !== "Enter") return;
      const highlighted = document.querySelector(".select2-results__option--highlighted[aria-selected]");
      if (highlighted) return;
      const term = searchField.value.trim();
      if (!term) return;
      event.preventDefault();
      event.stopPropagation();
      await resolveUniqueLookup(term, searchField);
    }, true);
  }

  async function addSelectedQuantity() {
    const conversionId = Number(selectedLookupItem?.id);
    const receiveValue = Number(receiveQuantity.value);
    if (!conversionId) return showError("Hãy quét hoặc chọn sản phẩm và đơn vị nhận.");
    if (!Number.isFinite(receiveValue) || receiveValue <= 0)
      return showError("Số lượng nhận phải lớn hơn 0.");
    try {
      const purchaseOrderLineId = await resolveAllocationChoice(selectedLookupItem);
      if (purchaseOrderLineId === undefined) return;
      const action = receiveValue === 1 ? "add" : "bulk";
      await post(action, leaseBody({
        commandId: commandId(),
        productUnitConversionId: conversionId,
        purchaseOrderLineId,
        quantity: receiveValue
      }));
      clearLookupSelection();
      focusUnifiedSearch();
    } catch (error) {
      showError(error.message);
      receiveQuantity.focus();
      receiveQuantity.select();
    }
  }

  document.getElementById("rwReceiveAdd")?.addEventListener("click", addSelectedQuantity);
  receiveQuantity?.addEventListener("keydown", async event => {
    if (event.key === "Enter") {
      event.preventDefault();
      await addSelectedQuantity();
    }
  });
  document.getElementById("rwComponents")?.addEventListener("click", async event => {
    const outsideButton = event.target.closest(".rw-outside-accept,.rw-outside-reject");
    if (outsideButton) {
      const lineId = Number(outsideButton.closest("[data-line-id]").dataset.lineId);
      const accept = outsideButton.classList.contains("rw-outside-accept");
      try { await post(`outside/${lineId}`, { rowVersion, accept }); }
      catch (error) { showError(error.message); }
      return;
    }
    const button = event.target.closest(".rw-remove");
    if (!button) return;
    const lineId = Number(button.closest("[data-line-id]").dataset.lineId);
    try { await post(`lines/${lineId}/remove`, leaseBody({ commandId: commandId() })); }
    catch (error) { showError(error.message); }
  });
  document.getElementById("rwComponents")?.addEventListener("change", async event => {
    if (!event.target.matches(".rw-edit")) return;
    const lineId = Number(event.target.closest("[data-line-id]").dataset.lineId);
    try { await post(`lines/${lineId}/edit`, leaseBody({ commandId: commandId(), quantity: Number(event.target.value) })); }
    catch (error) { showError(error.message); }
  });
  document.getElementById("rwUndo")?.addEventListener("click", async () => {
    try { await post("undo", leaseBody({ commandId: commandId() })); }
    catch (error) { showError(error.message); }
  });
  document.getElementById("rwFinish")?.addEventListener("click", async () => {
    try {
      await post("finish", leaseBody());
      sessionStorage.removeItem(storageKey);
      window.location.assign(`/admin/stock-documents/${id}`);
    } catch (error) { showError(error.message); bootstrap.Modal.getInstance(document.getElementById("rwFinishModal"))?.hide(); }
  });

  function cameraMessage(text, error = false) {
    const element = document.getElementById("rwCameraMessage");
    if (!element) return;
    element.textContent = text;
    element.dataset.error = String(error);
  }

  function stopCamera() {
    cameraGeneration++;
    cameraControls?.stop();
    cameraControls = null;
    cameraStream?.getTracks().forEach(track => track.stop());
    cameraStream = null;
    const video = document.getElementById("rwCameraVideo");
    if (video) video.srcObject = null;
    const torch = document.getElementById("rwCameraTorch");
    if (torch) {
      torch.hidden = true;
      torch.textContent = "Bật đèn";
      torch.setAttribute("aria-pressed", "false");
    }
    cameraTorchEnabled = false;
  }

  function loadCameraDecoder() {
    if (window.ZXingBrowser) return Promise.resolve(window.ZXingBrowser);
    if (!cameraDecoderPromise) cameraDecoderPromise = new Promise((resolve, reject) => {
      const script = document.createElement("script");
      script.src = root.dataset.decoderUrl;
      script.async = true;
      script.onload = () => window.ZXingBrowser
        ? resolve(window.ZXingBrowser)
        : reject(new Error("Không khởi tạo được bộ quét barcode."));
      script.onerror = () => {
        script.remove();
        cameraDecoderPromise = null;
        reject(new Error("Không tải được bộ quét barcode."));
      };
      document.head.append(script);
    });
    return cameraDecoderPromise;
  }

  async function lookupScannedCode(code) {
    const separator = root.dataset.lookupUrl.includes("?") ? "&" : "?";
    const response = await fetch(`${root.dataset.lookupUrl}${separator}term=${encodeURIComponent(code)}`, {
      credentials: "same-origin",
      cache: "no-store"
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(payload.message || "Không thể tìm sản phẩm theo mã vừa quét.");
    const results = processLookupResults(payload).results;
    const normalized = code.toLocaleUpperCase("vi-VN");
    const exact = results.filter(item => [item.barcode, item.sku].some(value =>
      value && String(value).toLocaleUpperCase("vi-VN") === normalized));
    if (exact.length !== 1) {
      if (!exact.length) throw new Error(`Không tìm thấy sản phẩm có mã ${code}. Hãy tìm thủ công hoặc ghi nhận hàng mới.`);
      throw new Error(`Mã ${code} đang khớp nhiều đơn vị. Hãy chọn thủ công để tránh nhận sai.`);
    }
    const item = exact[0];
    if ($lookup) {
      $lookup.find(`option[value="${item.id}"]`).remove();
      $lookup.append(new Option(item.text, item.id, true, true)).trigger("change");
    }
    selectLookupItem(item);
    return item;
  }

  async function startCamera() {
    stopCamera();
    const turn = cameraGeneration;
    const retry = document.getElementById("rwCameraRetry");
    if (retry) retry.disabled = true;
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
      cameraMessage("Camera cần HTTPS và trình duyệt hỗ trợ. Bạn vẫn có thể đóng cửa sổ để tìm mã thủ công.", true);
      if (retry) retry.disabled = false;
      return;
    }
    cameraMessage("Đang mở camera sau. Cho phép truy cập camera khi thiết bị hỏi.");
    try {
      const media = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: "environment" }, width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: false
      });
      if (turn !== cameraGeneration) { media.getTracks().forEach(track => track.stop()); return; }
      cameraStream = media;
      const ZXing = await loadCameraDecoder();
      if (turn !== cameraGeneration) return;
      const track = media.getVideoTracks()[0];
      const torch = document.getElementById("rwCameraTorch");
      if (torch) torch.hidden = !track?.getCapabilities?.().torch;
      const reader = new ZXing.BrowserMultiFormatOneDReader(undefined, {
        delayBetweenScanAttempts: 180,
        delayBetweenScanSuccess: 600
      });
      const controls = await reader.decodeFromStream(media, document.getElementById("rwCameraVideo"), async (result, error, current) => {
        if (!result || turn !== cameraGeneration) return;
        current.stop();
        const code = result.getText().trim();
        stopCamera();
        cameraMessage(`Đã nhận mã ${code}. Đang đối chiếu sản phẩm…`);
        try {
          await lookupScannedCode(code);
          cameraMessage(`Đã chọn mã ${code}. Nhập số lượng rồi xác nhận nhận hàng.`);
          window.setTimeout(() => bootstrap.Modal.getInstance(document.getElementById("rwCameraModal"))?.hide(), 350);
        } catch (lookupError) {
          cameraMessage(lookupError.message, true);
        }
      });
      if (turn !== cameraGeneration) controls.stop();
      else {
        cameraControls = controls;
        cameraMessage("Đưa mã vạch vào giữa khung. Camera sẽ dừng sau khi đọc được một mã.");
      }
    } catch (error) {
      if (turn !== cameraGeneration) return;
      stopCamera();
      const details = {
        NotAllowedError: "Chưa được cấp quyền camera. Hãy cho phép camera rồi bấm Quét lại.",
        NotFoundError: "Không tìm thấy camera trên thiết bị này.",
        NotReadableError: "Camera đang bận. Hãy đóng ứng dụng khác đang dùng camera rồi thử lại."
      };
      cameraMessage(details[error.name] || error.message || "Không mở được camera.", true);
    } finally {
      if (retry) retry.disabled = false;
    }
  }

  const cameraModalElement = document.getElementById("rwCameraModal");
  document.getElementById("rwOpenCamera")?.addEventListener("click", () => {
    bootstrap.Modal.getOrCreateInstance(cameraModalElement).show();
  });
  cameraModalElement?.addEventListener("shown.bs.modal", startCamera);
  cameraModalElement?.addEventListener("hidden.bs.modal", stopCamera);
  document.getElementById("rwCameraRetry")?.addEventListener("click", startCamera);
  document.getElementById("rwCameraTorch")?.addEventListener("click", async () => {
    const track = cameraStream?.getVideoTracks()[0];
    if (!track) return;
    try {
      await track.applyConstraints({ advanced: [{ torch: !cameraTorchEnabled }] });
      cameraTorchEnabled = !cameraTorchEnabled;
      const button = document.getElementById("rwCameraTorch");
      button.textContent = cameraTorchEnabled ? "Tắt đèn" : "Bật đèn";
      button.setAttribute("aria-pressed", String(cameraTorchEnabled));
    } catch {
      cameraMessage("Camera này không bật được đèn. Hãy đưa mã đến nơi sáng hơn.", true);
    }
  });
  document.addEventListener("visibilitychange", () => {
    if (document.hidden && cameraStream) {
      stopCamera();
      cameraMessage("Camera đã dừng khi trang chuyển nền. Bấm Quét lại để tiếp tục.");
    }
  });
  window.addEventListener("pagehide", stopCamera);

  if ($lookup && $.fn.select2) {
    $lookup.select2({
      theme: "bootstrap-5",
      width: "100%",
      minimumInputLength: 1,
      placeholder: receiveLookup.dataset.placeholder,
      templateResult: renderLookupResult,
      templateSelection: renderLookupSelection,
      escapeMarkup: markup => markup,
      ajax: {
        url: root.dataset.lookupUrl,
        dataType: "json",
        delay: 250,
        data: params => ({ term: params.term }),
        processResults: processLookupResults
      }
    });
    $lookup.on("select2:select", event => selectLookupItem(event.params.data));
    $lookup.on("select2:open", bindUnifiedSearchKeyboard);
  }

  async function acquireIfNeeded() {
    if (root.dataset.editable !== "true") return;
    try {
      const payload = await post("acquire", leaseBody());
      applyState(payload);
      focusUnifiedSearch();
    } catch (error) { showError(error.message); document.querySelectorAll("#receivingWorkbench input,#receivingWorkbench button,#receivingWorkbench select").forEach(x => x.disabled = true); }
  }
  acquireIfNeeded();
  window.setInterval(async () => {
    if (!leaseToken || document.hidden || root.dataset.editable !== "true") return;
    try { await post("heartbeat", leaseBody()); }
    catch (error) { showError(error.message); }
  }, 30000);
})();
