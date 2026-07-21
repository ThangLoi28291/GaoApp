(function () {
    const apiUrl = "/admin/api/warehouses";
    const page = document.querySelector(".warehouse-page");
    const permissions = {
        canCreate: page?.dataset.canCreate === "true",
        canUpdate: page?.dataset.canUpdate === "true"
    };

    let warehouses = [];
    let legalEntities = [];
    let modal;

    document.addEventListener("DOMContentLoaded", function () {
        modal = new bootstrap.Modal(document.getElementById("warehouseModal"));

        document.getElementById("btnCreateWarehouse")?.addEventListener("click", openCreateModal);
        document.getElementById("warehouseForm")?.addEventListener("submit", saveWarehouse);

        loadLegalEntities().then(loadWarehouses);
    });

    async function loadLegalEntities() {
        try {
            const response = await fetch(`${apiUrl}/legal-entity-options`, {
                headers: { "Accept": "application/json" },
                cache: "no-store"
            });
            if (!response.ok) {
                throw new Error(await readErrorMessage(response, "Không tải được danh sách HKD."));
            }
            const data = await response.json();
            legalEntities = Array.isArray(data) ? data.map(x => ({
                id: Number(x.id ?? x.Id),
                code: x.code ?? x.Code ?? "",
                name: x.name ?? x.Name ?? "",
                isDefaultForPurchase: toBool(x.isDefaultForPurchase ?? x.IsDefaultForPurchase)
            })) : [];
        } catch (error) {
            legalEntities = [];
            showToastOrAlert(error.message);
        }
    }

    async function loadWarehouses() {
        const tbody = document.querySelector("#warehouseTable tbody");

        tbody.innerHTML = `
            <tr>
                <td colspan="9" class="text-center text-muted py-4">
                    Đang tải dữ liệu...
                </td>
            </tr>`;

        try {
            const response = await fetch(apiUrl, {
                method: "GET",
                headers: { "Accept": "application/json" },
                cache: "no-store"
            });

            if (!response.ok) {
                throw new Error(await readErrorMessage(response, "Không tải được danh sách kho."));
            }

            const data = await response.json();
            warehouses = Array.isArray(data) ? data.map(normalizeWarehouse) : [];

            renderTable();
            renderStatistics();
        } catch (error) {
            tbody.innerHTML = `
                <tr>
                    <td colspan="9" class="text-center text-danger py-4">
                        ${escapeHtml(error.message)}
                    </td>
                </tr>`;
        }
    }

    function renderTable() {
        const tbody = document.querySelector("#warehouseTable tbody");

        if (!warehouses.length) {
            tbody.innerHTML = `
                <tr>
                    <td colspan="9" class="text-center text-muted py-4">
                        Chưa có kho nào.
                    </td>
                </tr>`;
            return;
        }

        tbody.innerHTML = warehouses.map(x => `
            <tr>
              <td>
    <div class="warehouse-code">
        ${escapeHtml(x.code)}
    </div>
</td>

               <td>
    <div class="warehouse-name">
        ${escapeHtml(x.name)}
    </div>
                    ${x.isDefault ? `<div class="warehouse-default-badge">
    <i class="bx bx-star"></i>
    Kho mặc định
</div>` : ``}
                </td>

                <td>
                    <div class="fw-semibold">${escapeHtml(x.legalEntityName || "")}</div>
                </td>

                <td>${escapeHtml(x.location || "")}</td>

                <td class="text-center">
                    <div class="form-check form-switch d-flex justify-content-center">
                        <input class="form-check-input"
                               type="checkbox"
                               ${x.isDefault ? "checked" : ""}
                               ${!x.isActive || !permissions.canUpdate ? "disabled" : ""}
                               onchange="WarehousePage.confirmSetDefault(${x.id}, this)" />
                    </div>
                </td>

                <td class="text-center">
                    <div class="form-check form-switch d-flex justify-content-center">
                        <input class="form-check-input"
                               type="checkbox"
                               ${x.allowNegativeInventory ? "checked" : ""}
                               ${!permissions.canUpdate ? "disabled" : ""}
                               onchange="WarehousePage.confirmToggleNegative(${x.id}, this.checked, this)" />
                    </div>
                </td>

                <td class="text-center">
                    <div class="form-check form-switch d-flex justify-content-center">
                        <input class="form-check-input"
                               type="checkbox"
                               ${x.isActive ? "checked" : ""}
                               ${!permissions.canUpdate ? "disabled" : ""}
                               onchange="WarehousePage.confirmToggleActive(${x.id}, this.checked, this)" />
                    </div>
                </td>

                <td>${escapeHtml(x.note || "")}</td>

                <td class="text-end">
                    ${permissions.canUpdate ? `<button type="button"
                            class="btn btn-sm btn-outline-primary"
                            onclick="WarehousePage.openEditModal(${x.id})">
                        Sửa
                    </button>` : ``}
                </td>
            </tr>
        `).join("");
    }
    function renderStatistics() {

        document.getElementById("totalWarehouseCount").innerText =
            warehouses.length;

        document.getElementById("activeWarehouseCount").innerText =
            warehouses.filter(x => x.isActive).length;

        document.getElementById("negativeWarehouseCount").innerText =
            warehouses.filter(x => x.allowNegativeInventory).length;

        const defaultWarehouse =
            warehouses.find(x => x.isDefault);

        document.getElementById("defaultWarehouseName").innerText =
            defaultWarehouse?.name || "--";
    }
    function openCreateModal() {
        if (!permissions.canCreate) return;
        clearError();

        document.getElementById("warehouseModalTitle").innerText = "Thêm kho";
        document.getElementById("warehouseId").value = "0";

        const codeInput = document.getElementById("warehouseCode");
        if (codeInput) {
            codeInput.value = "Tự động sinh";
            codeInput.readOnly = true;
            codeInput.classList.add("bg-light");
        }

        document.getElementById("warehouseName").value = "";
        document.getElementById("warehouseLocation").value = "";
        document.getElementById("warehouseNote").value = "";
        document.getElementById("warehouseIsDefault").checked = false;
        document.getElementById("warehouseIsActive").checked = true;
        document.getElementById("warehouseAllowNegativeInventory").checked = false;

        renderLegalEntityOptions(null);

        document.getElementById("activeWrapper")?.classList.add("d-none");

        modal.show();

        setTimeout(() => {
            document.getElementById("warehouseName").focus();
        }, 300);
    }

    function openEditModal(id) {
        if (!permissions.canUpdate) return;
        clearError();

        const item = findWarehouse(id);
        if (!item) {
            showToastOrAlert("Không tìm thấy kho.");
            return;
        }

        document.getElementById("warehouseModalTitle").innerText = "Cập nhật kho";
        document.getElementById("warehouseId").value = item.id;

        const codeInput = document.getElementById("warehouseCode");
        if (codeInput) {
            codeInput.value = item.code || "";
            codeInput.readOnly = true;
            codeInput.classList.add("bg-light");
        }

        document.getElementById("warehouseName").value = item.name || "";
        document.getElementById("warehouseLocation").value = item.location || "";
        document.getElementById("warehouseNote").value = item.note || "";
        document.getElementById("warehouseIsDefault").checked = item.isDefault;
        document.getElementById("warehouseIsActive").checked = item.isActive;
        document.getElementById("warehouseAllowNegativeInventory").checked = item.allowNegativeInventory;

        renderLegalEntityOptions(item.legalEntityId);

        document.getElementById("activeWrapper")?.classList.remove("d-none");

        modal.show();

        setTimeout(() => {
            document.getElementById("warehouseName").focus();
        }, 300);
    }

    async function saveWarehouse(e) {
        e.preventDefault();
        clearError();

        const id = parseInt(document.getElementById("warehouseId").value || "0");

        const payload = {
            id: id,
            name: document.getElementById("warehouseName").value.trim(),
            location: document.getElementById("warehouseLocation").value.trim(),
            note: document.getElementById("warehouseNote").value.trim(),
            isDefault: document.getElementById("warehouseIsDefault").checked,
            isActive: document.getElementById("warehouseIsActive").checked,
            allowNegativeInventory: document.getElementById("warehouseAllowNegativeInventory").checked
        };

        if (id > 0) {
            payload.code = document.getElementById("warehouseCode")?.value?.trim() || "";
        } else {
            payload.legalEntityId = Number(document.getElementById("warehouseLegalEntityId")?.value || 0) || null;
        }

        if (!payload.name) {
            showError("Tên kho không được để trống.");
            return;
        }

        if (id === 0 && legalEntities.length > 1 && !payload.legalEntityId) {
            showError("Vui lòng chọn HKD sở hữu kho.");
            return;
        }

        const btn = document.getElementById("btnSaveWarehouse");
        btn.disabled = true;
        btn.innerText = "Đang lưu...";

        try {
            const response = await fetch(apiUrl, {
                method: id > 0 ? "PUT" : "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json"
                },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                throw new Error(await readErrorMessage(response, "Lưu kho thất bại."));
            }

            modal.hide();
            await loadWarehouses();
        } catch (error) {
            showError(error.message);
        } finally {
            btn.disabled = false;
            btn.innerText = "Lưu kho";
        }
    }

    async function confirmToggleNegative(id, value, checkbox) {
        const item = findWarehouse(id);

        const ok = await confirmBox({
            title: value ? "Bật cho phép âm kho?" : "Tắt cho phép âm kho?",
            message: value
                ? `Kho "${item?.name || ""}" sẽ cho phép POS bán khi thiếu tồn.`
                : `Kho "${item?.name || ""}" sẽ chặn bán nếu thiếu tồn.`,
            confirmText: value ? "Bật âm kho" : "Tắt âm kho",
            danger: value
        });

        if (!ok) {
            checkbox.checked = !value;
            return;
        }

        await updateWarehouseByPut(id, { allowNegativeInventory: value }, checkbox, !value);
    }

    async function confirmToggleActive(id, value, checkbox) {
        const item = findWarehouse(id);

        if (item?.isDefault && value === false) {
            checkbox.checked = true;
            showToastOrAlert("Không thể ngưng sử dụng kho mặc định.");
            return;
        }

        const ok = await confirmBox({
            title: value ? "Kích hoạt kho?" : "Ngưng sử dụng kho?",
            message: value
                ? `Kho "${item?.name || ""}" sẽ được đưa vào sử dụng lại.`
                : `Kho "${item?.name || ""}" sẽ bị ngưng sử dụng.`,
            confirmText: value ? "Kích hoạt" : "Ngưng sử dụng",
            danger: !value
        });

        if (!ok) {
            checkbox.checked = !value;
            return;
        }

        await updateWarehouseByPut(id, { isActive: value }, checkbox, !value);
    }

    async function confirmSetDefault(id, checkbox) {
        const item = findWarehouse(id);

        if (!item) {
            checkbox.checked = false;
            showToastOrAlert("Không tìm thấy kho.");
            return;
        }

        if (item.isDefault) {
            checkbox.checked = true;
            return;
        }

        if (!item.isActive) {
            checkbox.checked = false;
            showToastOrAlert("Không thể đặt kho đang ngưng sử dụng làm mặc định.");
            return;
        }

        const ok = await confirmBox({
            title: "Đặt làm kho mặc định?",
            message: `Kho "${item.name}" sẽ trở thành kho mặc định. Kho mặc định cũ sẽ tự bỏ.`,
            confirmText: "Đặt mặc định",
            danger: false
        });

        if (!ok) {
            checkbox.checked = false;
            return;
        }

        await updateWarehouseByPut(id, { isDefault: true }, checkbox, false);
    }

    async function updateWarehouseByPut(id, changes, checkbox, rollbackValue) {
        const item = findWarehouse(id);

        if (!item) {
            checkbox.checked = rollbackValue;
            showToastOrAlert("Không tìm thấy kho.");
            return;
        }

        const payload = {
            id: item.id,
            code: item.code,
            name: item.name,
            location: item.location || "",
            note: item.note || "",
            isDefault: item.isDefault,
            isActive: item.isActive,
            allowNegativeInventory: item.allowNegativeInventory,
            ...changes
        };

        try {
            checkbox.disabled = true;

            const response = await fetch(apiUrl, {
                method: "PUT",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json"
                },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                throw new Error(await readErrorMessage(response, "Cập nhật thất bại."));
            }

            const updated = normalizeWarehouse(await response.json());

            if (changes.isDefault === true) {
                warehouses = warehouses.map(x => ({
                    ...x,
                    isDefault: x.id === id ? true : false
                }));
            }

            warehouses = warehouses.map(x =>
                x.id === id
                    ? { ...x, ...updated }
                    : x
            );

            renderTable();
        } catch (error) {
            checkbox.checked = rollbackValue;
            showToastOrAlert(error.message);
            await loadWarehouses();
        } finally {
            checkbox.disabled = false;
        }
    }

    async function confirmBox(options) {
        if (window.Swal) {
            const result = await Swal.fire({
                title: options.title,
                text: options.message,
                icon: options.danger ? "warning" : "question",
                showCancelButton: true,
                confirmButtonText: options.confirmText || "Đồng ý",
                cancelButtonText: "Hủy",
                confirmButtonColor: options.danger ? "#dc3545" : "#696cff"
            });

            return result.isConfirmed;
        }

        return confirm(`${options.title}\n\n${options.message}`);
    }

    async function readErrorMessage(response, fallback) {
        try {
            const data = await response.json();
            return data.message || data.title || data.errorMessage || data.detail || fallback;
        } catch {
            return fallback;
        }
    }

    function normalizeWarehouse(x) {
        return {
            id: Number(x.id ?? x.Id),
            legalEntityId: Number(x.legalEntityId ?? x.LegalEntityId ?? 0),
            legalEntityName: x.legalEntityName ?? x.LegalEntityName ?? "",
            code: x.code ?? x.Code ?? "",
            name: x.name ?? x.Name ?? "",
            location: x.location ?? x.Location ?? "",
            note: x.note ?? x.Note ?? "",
            isDefault: toBool(x.isDefault ?? x.IsDefault),
            isActive: toBool(x.isActive ?? x.IsActive),
            allowNegativeInventory: toBool(x.allowNegativeInventory ?? x.AllowNegativeInventory)
        };
    }

    function renderLegalEntityOptions(selectedId) {
        const select = document.getElementById("warehouseLegalEntityId");
        const help = document.getElementById("warehouseLegalEntityHelp");
        if (!select) return;

        select.innerHTML = `<option value="">-- Chọn HKD --</option>` + legalEntities.map(x =>
            `<option value="${x.id}">${escapeHtml(x.code)} - ${escapeHtml(x.name)}${x.isDefaultForPurchase ? " (nhập mặc định)" : ""}</option>`
        ).join("");

        if (selectedId) {
            select.value = String(selectedId);
            select.disabled = true;
            if (help) help.innerText = "Không thể đổi HKD của kho đã tạo để bảo toàn lịch sử tồn.";
            return;
        }

        select.disabled = legalEntities.length <= 1;
        const preferred = legalEntities.find(x => x.isDefaultForPurchase) || legalEntities[0];
        select.value = preferred ? String(preferred.id) : "";
        if (help) {
            help.innerText = legalEntities.length <= 1
                ? "Kho sẽ thuộc HKD đang hoạt động duy nhất."
                : "Chọn đúng HKD sở hữu tồn kho ngay khi tạo.";
        }
    }

    function findWarehouse(id) {
        return warehouses.find(x => Number(x.id) === Number(id));
    }

    function toBool(value) {
        return value === true || value === "true" || value === 1 || value === "1";
    }

    function showToastOrAlert(message) {
        if (window.toastr) {
            toastr.error(message);
            return;
        }

        alert(message);
    }

    function showError(message) {
        const box = document.getElementById("warehouseError");
        box.innerText = message;
        box.classList.remove("d-none");
    }

    function clearError() {
        const box = document.getElementById("warehouseError");
        box.innerText = "";
        box.classList.add("d-none");
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    window.WarehousePage = {
        openEditModal,
        confirmToggleNegative,
        confirmToggleActive,
        confirmSetDefault
    };
})();
