/* Interaction fixture for the mobile redesign. No HTTP calls, storage, tenant data or business writes. */
(function () {
    "use strict";
    const root = document.querySelector("[data-mobile-v2]");
    if (!root || root.dataset.mobileReady) return;
    root.dataset.mobileReady = "true";
    const data = JSON.parse(root.querySelector("#m2-fixture").textContent);
    const content = root.querySelector("#m2-content");
    const $ = selector => root.querySelector(selector);
    const all = selector => Array.from(root.querySelectorAll(selector));
    const money = value => new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 0 }).format(value) + " ₫";
    const esc = value => String(value).replace(/[&<>"']/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[character]));
    const normalize = value => String(value).normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLocaleLowerCase("vi").replace(/đ/g, "d");
    const icon = id => '<svg class="m2-icon" aria-hidden="true"><use href="#' + esc(id) + '"></use></svg>';
    const statusLabels = { pending: ["Chờ thanh toán", "warning"], paid: ["Đã thanh toán", "success"], shipping: ["Đang giao", "info"], completed: ["Hoàn tất", "success"] };
    const state = { screen: "home", filter: "all", sort: "recent", category: "all", cart: new Map(), receiving: data.receiving.map(line => ({ ...line })), receivingId: null, scroll: {}, toastTimer: null };
    const systemMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

    function badge(status) {
        const [label, tone] = statusLabels[status] || [status, "neutral"];
        return '<span class="m2-status m2-status--' + tone + '">' + esc(label) + '</span>';
    }
    function orderMarkup(order) {
        return '<button type="button" class="m2-order" data-m2-order="' + esc(order.id) + '" aria-label="Xem đơn ' + esc(order.id) + ', ' + esc(order.customer) + '">'
            + '<span class="m2-avatar" aria-hidden="true">' + esc(order.initials) + '</span>'
            + '<span class="m2-order-name">' + esc(order.customer) + '</span><strong class="m2-order-amount">' + money(order.amount) + '</strong>'
            + '<span class="m2-order-meta">#' + esc(order.id.replace("DH-", "")) + ' · ' + esc(order.time) + '</span>' + badge(order.status) + '</button>';
    }
    function renderOrders() {
        const query = normalize($("#m2-order-search").value.trim());
        const orders = data.orders.filter(order => {
            const statusMatches = state.filter === "all" || (state.filter === "unpaid" ? !order.paid : state.filter === "paid" ? order.paid : order.status === state.filter);
            return statusMatches && normalize(order.id + " " + order.customer).includes(query);
        }).sort((a, b) => state.sort === "amount" ? b.amount - a.amount : b.time.localeCompare(a.time));
        $("[data-m2-orders]").innerHTML = orders.map(orderMarkup).join("");
        $("[data-m2-orders]").hidden = orders.length === 0;
        $("[data-m2-order-empty]").hidden = orders.length > 0;
        $("[data-m2-order-count]").textContent = orders.length + " đơn hàng";
        all("[data-m2-filter]").forEach(button => button.setAttribute("aria-pressed", String(button.dataset.m2Filter === state.filter)));
        $("[data-m2-filter-dot]").hidden = state.filter === "all" && state.sort === "recent";
        $("[data-m2-filter-form]").elements.namedItem("status").value = state.filter;
        $("#m2-sort").value = state.sort;
    }
    function go(screen, keyboard = false) {
        const target = all("[data-m2-screen]").find(item => item.dataset.m2Screen === screen);
        if (!target) return;
        state.scroll[state.screen] = content.scrollTop;
        state.screen = screen;
        root.dataset.screen = screen;
        all("[data-m2-screen]").forEach(item => { item.hidden = item !== target; });
        all("[data-m2-nav]").forEach(item => {
            if (item.dataset.m2Nav === screen) item.setAttribute("aria-current", "page");
            else item.removeAttribute("aria-current");
        });
        updateCart();
        content.scrollTop = state.scroll[screen] || 0;
        if (keyboard) target.querySelector("h1").focus({ preventScroll: true });
    }
    function toast(message) {
        clearTimeout(state.toastTimer);
        $("[data-m2-toast-text]").textContent = message;
        $("[data-m2-toast]").hidden = false;
        state.toastTimer = setTimeout(() => { $("[data-m2-toast]").hidden = true; }, 2600);
    }
    function open(dialog, opener) { return window.GaoCommerce.openDialog(dialog, opener); }
    function closeThen(dialog, callback) {
        dialog.addEventListener("close", () => queueMicrotask(callback), { once: true });
        dialog.close();
    }
    function showOrder(id, opener) {
        const order = data.orders.find(item => item.id === id);
        if (!order) return;
        $("[data-m2-detail-id]").textContent = order.id;
        $("[data-m2-detail-status]").innerHTML = badge(order.status);
        $("[data-m2-detail-amount]").textContent = money(order.amount);
        $("[data-m2-detail-customer]").textContent = order.customer;
        $("[data-m2-detail-time]").textContent = order.time + " · 25/10/2026";
        $("[data-m2-detail-lines]").textContent = order.lines + " dòng hàng";
        $("[data-m2-detail-payment]").textContent = order.paid ? "Đã thanh toán" : "Còn chờ thu " + money(order.amount);
        open($("#m2-order-detail"), opener);
    }
    function renderReceiving() {
        const completed = state.receiving.filter(line => line.checked).length;
        $("[data-m2-receiving-short]").textContent = "Đã kiểm " + completed + "/" + state.receiving.length + " dòng";
        $("[data-m2-receiving-progress]").textContent = completed + "/" + state.receiving.length + " dòng";
        $("[data-m2-progress]").value = completed;
        $("[data-m2-progress]").textContent = completed + "/" + state.receiving.length;
        $("[data-m2-receiving-lines]").innerHTML = state.receiving.map(line => {
            const discrepancy = line.checked && line.quantity !== line.expected;
            const label = !line.checked ? "Chưa kiểm" : discrepancy ? "Chênh lệch" : "Đã kiểm";
            const tone = !line.checked ? "neutral" : discrepancy ? "warning" : "success";
            return '<button type="button" class="m2-receiving-line" data-m2-line="' + line.id + '" aria-label="Kiểm ' + esc(line.name) + ', ' + esc(line.pack) + '">'
                + '<span class="m2-task-icon ' + (line.checked ? 'm2-tone-teal' : 'm2-tone-neutral') + '">' + icon(line.checked ? "m2-check" : "m2-box") + '</span>'
                + '<span class="m2-receiving-copy"><strong>' + esc(line.name) + '</strong><small>' + esc(line.pack) + ' · Dự kiến ' + line.expected + '</small></span>'
                + '<span class="m2-receiving-quantity"><strong>' + line.quantity + ' / ' + line.expected + '</strong><span class="m2-status m2-status--' + tone + '">' + label + '</span></span></button>';
        }).join("");
    }
    function showReceiving(id, opener) {
        const line = state.receiving.find(item => item.id === Number(id));
        if (!line) return;
        state.receivingId = line.id;
        $("[data-m2-receive-product]").textContent = line.name;
        $("[data-m2-receive-expected]").textContent = "Quy cách: " + line.pack + " · Dự kiến " + line.expected;
        $("#m2-receive-quantity").value = line.quantity;
        $("#m2-receive-quantity").removeAttribute("aria-invalid");
        $("#m2-receive-error").hidden = true;
        open($("#m2-receive-line"), opener);
    }
    function renderProducts() {
        const query = normalize($("#m2-product-search").value.trim());
        const products = data.products.filter(product => (state.category === "all" || state.category === product.category) && normalize(product.name + " " + product.barcode).includes(query));
        $("[data-m2-products]").innerHTML = products.map(product => '<button type="button" class="m2-product" data-m2-add="' + product.id + '" aria-label="Thêm ' + esc(product.name) + ', ' + esc(product.pack) + ', ' + money(product.price) + '">'
            + '<span class="m2-product-art" data-color="' + esc(product.color) + '"><svg class="m2-pack-svg" aria-hidden="true"><use href="#m2-rice"></use></svg><span class="m2-product-mark" aria-hidden="true">' + esc(product.mark) + '</span></span>'
            + '<span class="m2-product-count" data-m2-product-count hidden></span><span class="m2-product-info"><strong>' + esc(product.name) + '</strong><small>' + esc(product.pack) + ' · Còn ' + product.stock + '</small><span class="m2-product-price-row"><span>' + money(product.price) + '</span><span class="m2-product-add" aria-hidden="true">' + icon("m2-plus") + '</span></span></span></button>').join("");
        $("[data-m2-product-empty]").hidden = products.length > 0;
        all("[data-m2-category]").forEach(item => item.setAttribute("aria-pressed", String(item.dataset.m2Category === state.category)));
        updateCart();
    }
    function cartLines() { return data.products.filter(product => state.cart.has(product.id)).map(product => ({ ...product, quantity: state.cart.get(product.id) })); }
    function cartTotal() { return cartLines().reduce((sum, product) => sum + product.price * product.quantity, 0); }
    function updateCart() {
        const lines = cartLines();
        const total = cartTotal();
        $("[data-m2-cart-dock]").hidden = state.screen !== "pos" || lines.length === 0;
        $("[data-m2-cart-lines]").textContent = lines.length + " dòng hàng";
        $("[data-m2-cart-total]").textContent = money(total);
        $("[data-m2-cart-sheet-total]").textContent = money(total);
        $("[data-m2-cart-empty]").hidden = lines.length > 0;
        $("[data-m2-open-payment]").disabled = lines.length === 0;
        all("[data-m2-add]").forEach(button => {
            const quantity = state.cart.get(Number(button.dataset.m2Add)) || 0;
            const count = button.querySelector("[data-m2-product-count]");
            count.hidden = !quantity; count.textContent = String(quantity);
        });
    }
    function renderCart(focusProduct, focusStep) {
        $("[data-m2-cart-items]").innerHTML = cartLines().map(product => '<article class="m2-cart-item"><div><h3>' + esc(product.name) + '</h3><p>' + esc(product.pack) + ' · ' + money(product.price) + '</p></div><strong>' + money(product.price * product.quantity) + '</strong>'
            + '<div class="m2-stepper"><button type="button" data-m2-cart-step="-1" data-product="' + product.id + '" aria-label="Giảm ' + esc(product.name) + '">' + icon("m2-minus") + '</button><span aria-label="Số lượng ' + product.quantity + '">' + product.quantity + '</span><button type="button" data-m2-cart-step="1" data-product="' + product.id + '" aria-label="Tăng ' + esc(product.name) + '">' + icon("m2-plus") + '</button></div></article>').join("");
        updateCart();
        if (focusProduct) {
            const next = $("[data-m2-cart-step='" + focusStep + "'][data-product='" + focusProduct + "']");
            (next || $("#m2-cart [data-gao-dialog-close]")).focus({ preventScroll: true });
        }
    }
    function changeCart(id, change, notify = false) {
        const product = data.products.find(item => item.id === Number(id));
        if (!product) return;
        const quantity = (state.cart.get(product.id) || 0) + change;
        if (quantity > product.stock) { toast("Đã đạt số lượng tối đa trong dữ liệu mẫu."); return; }
        if (quantity <= 0) state.cart.delete(product.id); else state.cart.set(product.id, quantity);
        updateCart();
        if (notify) toast("Đã thêm " + product.name + " vào giỏ mẫu");
    }
    function resetOrders(filter = "all") {
        state.filter = filter; state.sort = "recent"; state.scroll.orders = 0;
        $("#m2-order-search").value = "";
        renderOrders();
    }
    function reflectPaymentChange() {
        const value = $("#m2-cash").value;
        const amount = Number(value);
        const valid = value !== "" && Number.isSafeInteger(amount) && amount >= cartTotal();
        $("[data-m2-change]").textContent = valid ? money(amount - cartTotal()) : "—";
        if ($("#m2-cash").hasAttribute("aria-invalid")) {
            $("#m2-cash").setAttribute("aria-invalid", String(!valid)); $("#m2-cash-error").hidden = valid;
        }
        $("[data-m2-payment-result]").hidden = true;
        return valid;
    }

    $("#m2-order-search").addEventListener("input", renderOrders);
    $("#m2-product-search").addEventListener("input", renderProducts);
    $("#m2-cash").addEventListener("input", reflectPaymentChange);
    root.addEventListener("click", function (event) {
        if (!(event.target instanceof Element)) return;
        const button = event.target.closest("button");
        if (!button) return;
        const keyboard = event.detail === 0;
        if (button.dataset.m2Go) go(button.dataset.m2Go, keyboard);
        if (button.hasAttribute("data-m2-search")) { go("orders"); $("#m2-order-search").focus(); }
        if (button.hasAttribute("data-m2-unpaid-orders")) { resetOrders("unpaid"); go("orders", keyboard); }
        if (button.hasAttribute("data-m2-all-orders")) { resetOrders(); go("orders", keyboard); }
        if (button.dataset.m2Filter) { state.filter = button.dataset.m2Filter; renderOrders(); }
        if (button.hasAttribute("data-m2-reset-orders")) { resetOrders(); $("#m2-order-search").focus(); }
        if (button.dataset.m2Order) showOrder(button.dataset.m2Order, button);
        if (button.dataset.m2Line) showReceiving(button.dataset.m2Line, button);
        if (button.dataset.m2Category) { state.category = button.dataset.m2Category; renderProducts(); }
        if (button.dataset.m2Add) changeCart(Number(button.dataset.m2Add), 1, true);
        if (button.dataset.m2CartStep) {
            changeCart(Number(button.dataset.product), Number(button.dataset.m2CartStep));
            renderCart(button.dataset.product, button.dataset.m2CartStep);
        }
        if (button.dataset.gaoDialogOpen === "m2-cart") renderCart();
        if (button.hasAttribute("data-m2-clear-filter")) { $("[data-m2-filter-form]").elements.namedItem("status").value = "all"; $("#m2-sort").value = "recent"; }
        if (button.hasAttribute("data-m2-open-payment")) {
            closeThen($("#m2-cart"), () => {
                $("[data-m2-payment-total]").textContent = money(cartTotal());
                $("#m2-cash").value = ""; $("#m2-cash").removeAttribute("aria-invalid"); $("#m2-cash-error").hidden = true;
                $("[data-m2-change]").textContent = "—"; $("[data-m2-payment-result]").hidden = true;
                open($("#m2-payment"), $("[data-m2-cart-dock] button"));
            });
        }
        if (button.dataset.m2NotificationGo) {
            closeThen($("#m2-notifications"), () => {
                if (button.dataset.m2NotificationGo === "orders") resetOrders("unpaid");
                go(button.dataset.m2NotificationGo, true);
            });
        }
    });
    $("[data-m2-filter-form]").addEventListener("submit", function (event) {
        event.preventDefault(); state.filter = this.elements.namedItem("status").value; state.sort = $("#m2-sort").value;
        renderOrders(); this.closest("dialog").close();
    });
    $("[data-m2-receive-form]").addEventListener("submit", function (event) {
        event.preventDefault();
        const input = $("#m2-receive-quantity");
        const value = input.valueAsNumber;
        const valid = Number.isInteger(value) && value >= 0 && value <= 9999;
        input.setAttribute("aria-invalid", String(!valid)); $("#m2-receive-error").hidden = valid;
        if (!valid) { input.focus(); return; }
        const line = state.receiving.find(item => item.id === state.receivingId);
        if (!line) return;
        line.quantity = value; line.checked = true;
        closeThen(this.closest("dialog"), () => {
            renderReceiving();
            $("[data-m2-line='" + line.id + "']").focus({ preventScroll: true });
            toast("Đã kiểm " + line.name + " trong bản mẫu");
        });
    });
    $("[data-m2-scan-form]").addEventListener("submit", function (event) {
        event.preventDefault();
        const product = data.products.find(item => item.barcode === $("#m2-barcode").value.trim());
        $("[data-m2-scan-error]").hidden = !!product;
        if (!product) { $("#m2-barcode").focus(); return; }
        closeThen(this.closest("dialog"), () => {
            go("pos"); state.category = "all"; $("#m2-product-search").value = product.name; renderProducts();
            toast("Đã tìm thấy " + product.name);
        });
    });
    $("[data-m2-payment-form]").addEventListener("submit", function (event) {
        event.preventDefault();
        const valid = cartTotal() > 0 && reflectPaymentChange();
        $("#m2-cash").setAttribute("aria-invalid", String(!valid)); $("#m2-cash-error").hidden = valid;
        if (!valid) { $("#m2-cash").focus(); return; }
        const submit = this.querySelector("button[type='submit']");
        if (submit.disabled) return;
        submit.disabled = true; submit.setAttribute("aria-busy", "true"); submit.textContent = "Đang kiểm tra…";
        setTimeout(() => {
            submit.disabled = false; submit.removeAttribute("aria-busy"); submit.textContent = "Kiểm tra thanh toán mẫu";
            $("[data-m2-payment-result]").textContent = "Thông tin mẫu hợp lệ. Chưa thu tiền, chưa tạo đơn hàng.";
            $("[data-m2-payment-result]").hidden = false;
            $("[data-m2-payment-result]").scrollIntoView({ block: "nearest" });
        }, 650);
    });
    const motionControl = $("[data-m2-reduce-motion]");
    function reflectMotion() {
        if (systemMotion.matches) { motionControl.checked = true; motionControl.disabled = true; }
        else motionControl.disabled = false;
        root.dataset.m2Motion = motionControl.checked || systemMotion.matches ? "off" : "on";
    }
    motionControl.addEventListener("change", reflectMotion);
    systemMotion.addEventListener("change", reflectMotion);
    reflectMotion();
    function viewport() {
        if (window.visualViewport) root.style.setProperty("--m2-viewport", window.visualViewport.height + "px");
    }
    if (window.visualViewport) window.visualViewport.addEventListener("resize", viewport);
    viewport();
    $("[data-m2-recent]").innerHTML = data.orders.slice(0, 2).map(orderMarkup).join("");
    renderOrders(); renderReceiving(); renderProducts(); go("home");
})();
