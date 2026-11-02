(() => {
  'use strict';
  const body = document.body;
  // Decorative effects have a store-scoped preference; operating-system reduced
  // motion takes precedence. No business/cart preference is changed here.
  const effectKey = `gao.storefront.effects.v1:${body.dataset.cartScope}`;
  const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
  const effectsToggle = document.getElementById('effects-toggle');
  const effectsStatus = document.getElementById('effects-status');
  let effectsOff = false;
  try { effectsOff = localStorage.getItem(effectKey) === 'off'; } catch { /* Optional preference only. */ }
  function applyEffects() {
    const off = reducedMotion.matches || effectsOff;
    body.dataset.effects = off ? 'off' : 'on';
    if (effectsToggle) { effectsToggle.setAttribute('aria-pressed', String(off)); effectsToggle.disabled = reducedMotion.matches; }
    if (effectsStatus) effectsStatus.textContent = reducedMotion.matches ? 'Đã giảm hiệu ứng theo cài đặt thiết bị.' : off ? 'Đã tắt hiệu ứng trang trí.' : 'Hiệu ứng nhẹ đang bật.';
  }
  effectsToggle?.addEventListener('click', () => { effectsOff = !effectsOff; try { localStorage.setItem(effectKey, effectsOff ? 'off' : 'on'); } catch { /* Current page still works. */ } applyEffects(); });
  reducedMotion.addEventListener('change', applyEffects); applyEffects();
  const storageKey = `gao.storefront.cart.v1:${body.dataset.cartScope}`;
  const quantityLimit = 1000000;
  const money = value => new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 }).format(value) + ' đ';
  const unitMoney = value => new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(value) + ' đ';
  const quantityText = value => new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 4 }).format(value);
  const fallback = '/assets/product-fallback.svg';
  let toastTimer;
  function toast(message) {
    const box = document.getElementById('toast');
    box.textContent = message; box.hidden = false;
    clearTimeout(toastTimer); toastTimer = setTimeout(() => { box.hidden = true; }, 5000);
  }
  const validId = value => Number.isSafeInteger(value) && value > 0 && value <= 2147483647;
  const validQuantity = value => Number.isFinite(value) && value > 0 && value <= quantityLimit && Math.abs(value * 10000 - Math.round(value * 10000)) < 0.00001;
  function parseQuantity(value) {
    const text = String(value).trim().replace(',', '.');
    if (!/^\d{1,7}(?:\.\d{1,4})?$/.test(text)) return null;
    const number = Number(text); return validQuantity(number) ? number : null;
  }
  let storageProblem = false;
  let storageReadAvailable = true;
  let storageRevision = '';
  function readCart() {
    let raw;
    try { raw = localStorage.getItem(storageKey); storageReadAvailable = true; }
    catch { storageReadAvailable = false; storageProblem = true; return []; }
    try {
      if (!raw) { storageRevision = ''; return []; }
      if (raw.length > 32768) throw new Error('Invalid cart');
      const saved = JSON.parse(raw);
      if (saved.version !== 1 || !Array.isArray(saved.items)) throw new Error('Invalid cart');
      storageRevision = typeof saved.revision === 'string' && saved.revision.length <= 80 ? saved.revision : '';
      const clean = [];
      for (const item of saved.items) {
        if (!item || !validId(item.productId) || !validId(item.variantId) || !validId(item.unitId) || !validQuantity(item.quantity) || clean.length >= 100 || clean.some(x => x.variantId === item.variantId && x.unitId === item.unitId)) { storageProblem = true; continue; }
        clean.push({ productId: item.productId, variantId: item.variantId, unitId: item.unitId, quantity: item.quantity });
      }
      return clean;
    } catch { storageRevision = ''; storageProblem = true; return []; }
  }
  let selections = readCart();
  const cartSignature = items => JSON.stringify(items);
  function persist() {
    const revision = globalThis.crypto?.randomUUID?.() || `${Date.now().toString(36)}_${Math.random().toString(36).slice(2)}`;
    try { localStorage.setItem(storageKey, JSON.stringify({ version: 1, revision, items: selections })); storageRevision = revision; }
    catch { storageReadAvailable = false; storageProblem = true; toast('Trình duyệt chưa cho lưu giỏ. Giỏ hiện chỉ được giữ khi bạn còn ở trang này.'); }
    updateCount();
    window.dispatchEvent(new CustomEvent('storefront-cart-changed'));
  }
  function snapshotCart() {
    const latest = readCart(); if (storageReadAvailable) selections = latest;
    updateCount();
    return { items: selections.map(item => ({ ...item })), revision: storageRevision, signature: cartSignature(selections), recovered: storageProblem };
  }
  window.GaoStorefrontCart = Object.freeze({
    snapshot: snapshotCart,
    clearIfUnchanged(snapshot) {
      if (!snapshot || typeof snapshot.signature !== 'string' || typeof snapshot.revision !== 'string') return false;
      const latest = snapshotCart();
      if (latest.signature !== snapshot.signature || latest.revision !== snapshot.revision) return false;
      selections = []; persist(); return true;
    }
  });
  window.GaoStorefrontApi = Object.freeze({
    async post(path, payload, signal) {
      if (!['/shop/checkout/quote', '/shop/checkout/place', '/shop/orders/access','/shop/orders/qr'].includes(path)) throw new Error('Đường dẫn không hợp lệ.');
      const token = document.querySelector('#cart-csrf input[name="__RequestVerificationToken"]')?.value;
      if (!token) throw new Error('Phiên xem đã thay đổi. Hãy tải lại trang.');
      const response = await fetch(path, { method: 'POST', credentials: 'same-origin', cache: 'no-store', signal,
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: token }, body: JSON.stringify(payload) });
      const data = await response.json().catch(() => null);
      if (!response.ok) {
        const error = new Error(data?.message || data?.title || (response.status === 429 ? 'Bạn thao tác hơi nhanh. Hãy thử lại sau một phút.' : 'Chưa xử lý được yêu cầu. Vui lòng thử lại.'));
        error.status = response.status; error.fields = data?.errors; error.safeToRequote = data?.safeToRequote === true; throw error;
      }
      return data;
    }
  });
  function updateCount() {
    const count = selections.reduce((sum, x) => sum + x.quantity, 0);
    document.querySelectorAll('[data-cart-count]').forEach(node => { node.textContent = count > 99 ? '99+' : quantityText(count); node.hidden = count === 0; });
  }
  updateCount();
  document.querySelectorAll('[data-image-fallback]').forEach(image => {
    const recover = () => { image.removeAttribute('srcset'); if (!image.src.endsWith(fallback)) image.src = fallback; };
    image.addEventListener('error', recover);
    // A fast failed request can finish before this deferred script attaches.
    if (image.complete && image.naturalWidth === 0) recover();
  });
  async function resolve(items, signal) {
    const token = document.querySelector('#cart-csrf input[name="__RequestVerificationToken"]')?.value;
    if (!token) throw new Error('Phiên xem đã thay đổi. Hãy tải lại trang rồi thử lại.');
    const response = await fetch('/shop/cart/resolve', {
      method: 'POST', credentials: 'same-origin', cache: 'no-store', signal,
      headers: { 'Content-Type': 'application/json', RequestVerificationToken: token }, body: JSON.stringify({ items })
    });
    const result = await response.json().catch(() => null);
    if (!response.ok) throw new Error(response.status === 429 ? 'Bạn kiểm tra giỏ hơi nhanh. Vui lòng thử lại sau một phút.' : result?.message || result?.title || 'Chưa kiểm tra được giỏ. Hãy tải lại trang hoặc thử lại sau.');
    return result;
  }
  function node(tag, className, text) {
    const item = document.createElement(tag); if (className) item.className = className; if (text !== undefined) item.textContent = text; return item;
  }
  function button(text, label, action) {
    const item = node('button', '', text); item.type = 'button'; item.setAttribute('aria-label', label); item.addEventListener('click', action); return item;
  }
  function fieldQuantity(input, value) {
    const parsed = parseQuantity(value);
    input.setCustomValidity(parsed === null ? 'Nhập số lượng dương, tối đa 1.000.000 và bốn số lẻ.' : '');
    if (parsed === null) { input.reportValidity(); return null; }
    return parsed;
  }

  const detail = document.querySelector('[data-detail]');
  if (detail) {
    const form = document.getElementById('product-choice');
    const variant = document.getElementById('variant-choice');
    const unit = document.getElementById('unit-choice');
    const quantity = document.getElementById('product-quantity');
    const message = document.getElementById('choice-message');
    const parameters = new URLSearchParams(location.search);
    const replaceVariant = Number(parameters.get('replaceVariant'));
    const replaceUnit = Number(parameters.get('replaceUnit'));
    const oldChoice = selections.find(x => x.productId === Number(detail.dataset.productId) && x.variantId === replaceVariant && x.unitId === replaceUnit);
    const editing = Boolean(oldChoice);
    function updateChoice(preferredUnit) {
      const choices = [...unit.options];
      choices.forEach(option => { const visible = option.dataset.variant === variant.value; option.hidden = !visible; option.disabled = !visible; option.selected = false; });
      const selected = choices.find(x => !x.disabled && x.value === String(preferredUnit)) || choices.find(x => !x.disabled);
      if (selected) selected.selected = true;
      updatePrice();
    }
    function updatePrice() {
      const selected = unit.selectedOptions[0]; if (!selected) return;
      document.getElementById('detail-price').textContent = `${selected.dataset.priceText || unitMoney(Number(selected.dataset.price))} / ${selected.textContent}`;
      document.getElementById('unit-note').textContent = `Giá áp dụng cho đúng một ${selected.textContent}.` + (selected.dataset.weight ? ` Trọng lượng giao: ${quantityText(Number(selected.dataset.weight))} kg.` : '');
    }
    if (editing) {
      if ([...variant.options].some(x => Number(x.value) === oldChoice.variantId)) variant.value = String(oldChoice.variantId);
      quantity.value = String(oldChoice.quantity); form.querySelector('[type=submit]').textContent = 'Cập nhật lựa chọn trong giỏ';
    }
    updateChoice(editing ? oldChoice.unitId : undefined);
    variant.addEventListener('change', () => updateChoice()); unit.addEventListener('change', updatePrice);
    quantity.addEventListener('input', () => quantity.setCustomValidity(''));
    form.querySelectorAll('[data-quantity-step]').forEach(control => control.addEventListener('click', () => {
      const current = fieldQuantity(quantity, quantity.value); if (current === null) return;
      quantity.value = String(Math.min(quantityLimit, Math.max(.0001, Math.round((current + Number(control.dataset.quantityStep)) * 10000) / 10000)));
    }));
    let adding = false;
    async function add(goToCheckout) {
      if (adding) return;
      const amount = fieldQuantity(quantity, quantity.value); if (amount === null) return;
      const choice = { productId: Number(detail.dataset.productId), variantId: Number(variant.value), unitId: Number(unit.value), quantity: amount };
      // A delayed response must never commit over a normal storage event or a newer stored envelope.
      const snapshot = { revision: storageRevision, items: cartSignature(selections) };
      const proposed = selections.filter(x => !editing || x.variantId !== replaceVariant || x.unitId !== replaceUnit).map(x => ({ ...x }));
      const existing = proposed.find(x => x.variantId === choice.variantId && x.unitId === choice.unitId);
      if (existing) existing.quantity = Math.round((existing.quantity + amount) * 10000) / 10000; else proposed.push(choice);
      if (proposed.length > 100 || proposed.some(x => !validQuantity(x.quantity))) { message.textContent = 'Giỏ tối đa 100 lựa chọn; tổng số lượng mỗi lựa chọn không quá 1.000.000.'; message.hidden = false; return; }
      adding = true; message.hidden = true; form.setAttribute('aria-busy', 'true');
      const actions = [...form.querySelectorAll('.purchase-actions button')]; actions.forEach(x => { x.disabled = true; });
      try {
        const fresh = await resolve(proposed);
        const selected = fresh.items.find(x => x.variantId === choice.variantId && x.unitId === choice.unitId);
        if (!selected?.available) throw new Error(selected?.message || 'Lựa chọn này hiện không còn được bán.');
        const memoryChanged = storageRevision !== snapshot.revision || cartSignature(selections) !== snapshot.items;
        const latest = readCart();
        const storageChanged = storageReadAvailable && (storageRevision !== snapshot.revision || cartSignature(latest) !== snapshot.items);
        if (memoryChanged || storageChanged) {
          if (storageReadAvailable) selections = latest;
          updateCount();
          throw new Error('Giỏ vừa được thay đổi ở trang khác. Đã giữ giỏ mới nhất; hãy kiểm tra rồi thử lại.');
        }
        // Deliberately no await between this fresh comparison and the synchronous localStorage write.
        // This guards the delayed-request case; localStorage compare/write is not a cross-tab transaction.
        selections = proposed; persist();
        const option = unit.selectedOptions[0]; if (option) { option.dataset.price = String(selected.unitPrice); option.dataset.priceText = selected.unitPriceText; } updatePrice();
        toast(editing ? 'Đã cập nhật lựa chọn và kiểm tra lại giá.' : 'Đã thêm vào giỏ theo giá vừa kiểm tra.');
        if (editing) location.assign('/shop/cart'); else if (goToCheckout) location.assign('/shop/checkout');
      } catch (error) { message.textContent = error.message || 'Chưa thêm được vào giỏ. Vui lòng thử lại.'; message.hidden = false; }
      finally { adding = false; form.removeAttribute('aria-busy'); actions.forEach(x => { x.disabled = false; }); }
    }
    form.addEventListener('submit', event => { event.preventDefault(); add(false); });
    document.getElementById('buy-now').addEventListener('click', () => add(true));
    document.querySelectorAll('[data-gallery-src]').forEach(control => control.addEventListener('click', () => {
      const image = document.getElementById('detail-image'), source = control.dataset.gallerySrc;
      image.srcset = `${source}?width=320 320w, ${source}?width=640 640w, ${source}?width=960 960w`;
      image.src = `${source}?width=640`; image.alt = control.dataset.galleryAlt;
      control.closest('[data-product-viewer]')?.dispatchEvent(new CustomEvent('storefront-gallery-photo'));
    }));
  }

  const cartPage = document.querySelector('[data-cart-page]');
  if (cartPage) {
    const loading = document.getElementById('cart-loading'); const error = document.getElementById('cart-error');
    const empty = document.getElementById('cart-empty'); const content = document.getElementById('cart-content');
    let sequence = 0; let activeRequest; let restoreFocus;
    async function refresh() {
      const current = ++sequence; activeRequest?.abort(); activeRequest = new AbortController();
      restoreFocus = document.activeElement?.dataset.focusKey || restoreFocus;
      error.hidden = true; empty.hidden = true; content.hidden = true;
      if (selections.length === 0) { loading.hidden = true; empty.hidden = false; return; }
      loading.hidden = false;
      try {
        const fresh = await resolve(selections, activeRequest.signal); if (current !== sequence) return;
        render(fresh); loading.hidden = true; content.hidden = false;
        if (restoreFocus) { document.querySelector(`[data-focus-key="${restoreFocus}"]`)?.focus({ preventScroll: true }); restoreFocus = null; }
      } catch (problem) {
        if (current !== sequence || problem.name === 'AbortError') return;
        loading.hidden = true; error.querySelector('[data-error-message]').textContent = problem.message || 'Vui lòng kiểm tra kết nối rồi thử lại.'; error.hidden = false;
      }
    }
    function setQuantity(selection, input, value) {
      const amount = fieldQuantity(input, value); if (amount === null) return;
      const selected = selections.find(x => x.variantId === selection.variantId && x.unitId === selection.unitId);
      if (!selected) return; selected.quantity = amount; persist(); refresh();
    }
    function render(result) {
      const lines = document.getElementById('cart-lines'); lines.replaceChildren();
      for (const line of result.items) {
        const article = node('article', 'cart-line'); article.dataset.variantId = String(line.variantId); article.dataset.unitId = String(line.unitId);
        const image = node('img', 'cart-line-image'); image.src = line.imagePath || fallback; image.alt = ''; image.width = 100; image.height = 100; image.loading = 'lazy'; image.addEventListener('error', () => { if (!image.src.endsWith(fallback)) image.src = fallback; }); article.append(image);
        const info = node('div', 'cart-line-info');
        const heading = node('h2'); const link = node('a', '', line.productName || 'Lựa chọn không còn được bán'); link.href = `/shop/products/${line.productId}`; heading.append(link); info.append(heading);
        if (line.available) {
          info.append(node('p', '', `${line.variantName} · ${line.unitName}`));
          info.append(node('p', '', `${line.unitPriceText} / ${line.unitName}`));
          info.append(node('p', 'line-price', line.lineAmountText));
          const change = node('a', 'text-link', 'Đổi biến thể / đơn vị'); change.href = `/shop/products/${line.productId}?replaceVariant=${line.variantId}&replaceUnit=${line.unitId}`; info.append(change);
        } else info.append(node('p', 'line-message', line.message));
        article.append(info);
        const controls = node('div', 'cart-line-controls'); const quantity = node('div', 'quantity-control');
        const input = node('input'); input.type = 'number'; input.value = String(line.quantity); input.min = '.0001'; input.max = String(quantityLimit); input.step = '.0001'; input.inputMode = 'decimal'; input.setAttribute('aria-label', `Số lượng ${line.productName || 'lựa chọn không còn bán'}`);
        input.dataset.focusKey = `${line.variantId}_${line.unitId}_quantity`;
        input.addEventListener('input', () => input.setCustomValidity('')); input.addEventListener('change', () => setQuantity(line, input, input.value));
        const down = button('−', 'Giảm số lượng', () => { const value = parseQuantity(input.value); if (value !== null) setQuantity(line, input, String(Math.max(.0001, Math.round((value - 1) * 10000) / 10000))); });
        const up = button('+', 'Tăng số lượng', () => { const value = parseQuantity(input.value); if (value !== null) setQuantity(line, input, String(Math.min(quantityLimit, Math.round((value + 1) * 10000) / 10000))); });
        down.dataset.focusKey = `${line.variantId}_${line.unitId}_down`; up.dataset.focusKey = `${line.variantId}_${line.unitId}_up`;
        quantity.append(down, input, up); controls.append(quantity);
        const remove = button('Xóa', `Xóa ${line.productName || 'lựa chọn không còn bán'} khỏi giỏ`, () => { selections = selections.filter(x => x.variantId !== line.variantId || x.unitId !== line.unitId); persist(); refresh(); toast('Đã xóa lựa chọn khỏi giỏ.'); }); remove.className = 'remove-line'; controls.append(remove); article.append(controls); lines.append(article);
      }
      document.getElementById('cart-estimate').textContent = result.goodsEstimateText;
      document.getElementById('cart-notice').textContent = result.canContinue ? result.notice : 'Giỏ còn lựa chọn không được bán. Hãy xóa hoặc chọn lại trước khi tiếp tục.';
      const next = document.getElementById('continue-cart');
      if (next) { next.setAttribute('aria-disabled', String(!result.canContinue)); if (result.canContinue) next.href = '/shop/checkout'; else next.removeAttribute('href'); }
    }
    document.getElementById('cart-retry').addEventListener('click', refresh);
    document.getElementById('refresh-cart').addEventListener('click', refresh);
    if (storageProblem) toast(storageReadAvailable ? 'Đã bỏ qua phần giỏ lưu không hợp lệ; các lựa chọn hợp lệ vẫn được giữ. Bạn có thể tiếp tục chỉnh giỏ.' : 'Trình duyệt chưa cho đọc giỏ đã lưu. Bạn có thể chọn lại sản phẩm trong trang này.');
    refresh();
    window.addEventListener('storage', event => { if (event.key === storageKey) { selections = readCart(); updateCount(); refresh(); window.dispatchEvent(new CustomEvent('storefront-cart-changed')); } });
  } else window.addEventListener('storage', event => { if (event.key === storageKey) { selections = readCart(); updateCount(); window.dispatchEvent(new CustomEvent('storefront-cart-changed')); } });

  // Give browser navigation immediate feedback without delaying or intercepting the destination.
  document.querySelectorAll('a[href]').forEach(link => link.addEventListener('click', event => {
    if (!event.defaultPrevented && !event.ctrlKey && !event.metaKey && !event.shiftKey && link.target !== '_blank' && link.origin === location.origin && link.hash === '' && link.pathname !== location.pathname)
      document.getElementById('page-loading').hidden = false;
  }));
  window.addEventListener('pageshow', () => { document.getElementById('page-loading').hidden = true; });

  // Native Tab scrolling can leave the focus ring behind the fixed navigation.
  // Measure the real obstruction, including safe-area height, after native focus scrolling.
  // Use immediate scrolling for consistent keyboard feedback and reduced-motion preferences.
  let focusFrame;
  function revealFocusedControl() {
    const target = document.activeElement;
    const nav = document.querySelector('.bottom-nav');
    if (!(target instanceof HTMLElement) || !nav || nav.contains(target) ||
        !target.matches('a[href],button,input,select,textarea,summary,canvas[tabindex],[role="button"],[role="link"]')) return;
    const rect = target.getBoundingClientRect();
    const viewport = window.visualViewport;
    const top = (viewport?.offsetTop || 0) + 12;
    const bottom = Math.min(nav.getBoundingClientRect().top, viewport ? viewport.offsetTop + viewport.height : window.innerHeight) - 12;
    let delta = 0;
    if (rect.height > bottom - top) delta = rect.top - top;
    else if (rect.bottom > bottom) delta = Math.ceil(rect.bottom - bottom);
    else if (rect.top < top) delta = Math.floor(rect.top - top);
    if (delta) window.scrollBy({ top: delta, behavior: 'instant' });
  }
  function queueFocusReveal() {
    revealFocusedControl();
    cancelAnimationFrame(focusFrame);
    focusFrame = requestAnimationFrame(revealFocusedControl);
  }
  document.addEventListener('focusin', queueFocusReveal);
  window.visualViewport?.addEventListener('resize', queueFocusReveal);
})();
