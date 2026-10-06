(() => {
  'use strict';
  const root = document.querySelector('[data-operations-report]');
  if (!root) return;
  const section = root.dataset.section;
  const canManage = root.dataset.canManage === 'true';
  const $ = selector => root.querySelector(selector);
  const form = $('[data-filters]');
  const fields = form.elements;
  const esc = value => String(value ?? '').replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
  const number = value => value == null ? 'Chưa xác định' : new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 }).format(value);
  const compact = value => value == null ? '—' : new Intl.NumberFormat('vi-VN', { notation: 'compact', maximumFractionDigits: 1 }).format(value);
  const money = value => `<span title="${esc(number(value))} VND">${esc(number(value))}</span>`;
  const localDay = value => new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Ho_Chi_Minh' }).format(value || new Date());
  const date = value => value ? new Intl.DateTimeFormat('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', day: '2-digit', month: '2-digit', year: 'numeric' }).format(new Date(value.endsWith('Z') ? value : value + 'Z')) : 'Chưa có';
  const time = value => new Intl.DateTimeFormat('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }).format(new Date(value.endsWith('Z') ? value : value + 'Z'));
  const guid = () => crypto.randomUUID();
  const fundName = key => ({ cash: 'Tiền mặt', bank: 'Chuyển khoản', other: 'Thẻ, ví & khác' }[key] || key);
  const agingName = key => ({ current: 'Chưa quá hạn', '1-30': 'Quá hạn 1–30 ngày', '31-60': 'Quá hạn 31–60 ngày', '61-90': 'Quá hạn 61–90 ngày', '90+': 'Quá hạn trên 90 ngày', unknown: 'Chưa có hạn', settled: 'Đã hết nợ' }[key] || key);
  const stateName = key => ({ normal: 'Bình thường', low: 'Sắp hết', negative: 'Tồn âm', empty: 'Hết hàng', slow: 'Chậm luân chuyển', incomplete: 'Thiếu lịch sử' }[key] || key);
  const badge = (label, style = '') => `<span class="or-status ${style}">${esc(label)}</span>`;
  let data, page = 1, charts = [], abort, generation = 0, sources, requestId, action;
  let query = '';
  let autoOpened = false;

  function preset() {
    const today = new Date(localDay() + 'T12:00:00Z');
    let from = new Date(today), to = new Date(today);
    if (fields.preset.value === 'month') from.setUTCDate(1);
    if (fields.preset.value === 'last-month') { to.setUTCDate(0); from = new Date(to); from.setUTCDate(1); }
    if (fields.preset.value === 'week') from.setUTCDate(from.getUTCDate() - 6);
    if (fields.preset.value !== 'custom') {
      fields.fromDate.value = from.toISOString().slice(0, 10);
      fields.toDate.value = to.toISOString().slice(0, 10);
    }
  }
  preset();
  for (const input of [fields.fromDate, fields.toDate]) { input.min = '2000-01-01'; input.max = localDay(); }
  const initial = new URLSearchParams(location.search);
  for (const name of ['fromDate', 'toDate', 'fund', 'lowStockThreshold']) if (initial.has(name) && fields[name]) fields[name].value = initial.get(name);
  if (initial.has('fromDate') || initial.has('toDate')) fields.preset.value = 'custom';
  root.querySelector(`[data-nav="${section}"]`)?.setAttribute('aria-current', 'page');
  fields.preset.addEventListener('change', preset);
  fields.fromDate.addEventListener('change', () => fields.preset.value = 'custom');
  fields.toDate.addEventListener('change', () => fields.preset.value = 'custom');

  async function api(url, body, signal) {
    const response = await fetch(url, {
      method: body == null ? 'GET' : 'POST', credentials: 'same-origin', signal,
      headers: body == null ? { Accept: 'application/json' } : { 'Content-Type': 'application/json', RequestVerificationToken: $('input[name="__RequestVerificationToken"]').value },
      body: body == null ? undefined : JSON.stringify(body)
    });
    const json = await response.json().catch(() => null);
    if (!response.ok) throw new Error(json?.message || json?.detail || (response.status === 403 ? 'Anh chưa có quyền thực hiện thao tác này.' : 'Không tải được dữ liệu. Vui lòng thử lại.'));
    if (!json) throw new Error('Phiên đăng nhập hoặc dữ liệu trả về không hợp lệ. Tải lại trang để tiếp tục.');
    return json;
  }
  function destroyCharts() { charts.forEach(chart => chart.destroy()); charts = []; }
  async function load() {
    if (!form.reportValidity()) return;
    abort?.abort(); abort = new AbortController();
    const version = ++generation;
    root.setAttribute('aria-busy', 'true'); $('[data-results]').hidden = true; $('[data-error]').hidden = true; $('[data-live]').hidden = false;
    destroyCharts();
    const params = new URLSearchParams({ fromDate: fields.fromDate.value, toDate: fields.toDate.value });
    for (const key of ['fund', 'warehouseId', 'lowStockThreshold']) if (fields[key]?.value) params.set(key, fields[key].value);
    if (section === 'inventory' && initial.has('warehouseId') && !data) params.set('warehouseId', initial.get('warehouseId'));
    query = params.toString();
    try {
      const result = await api(`/admin/reports/operations/${section}/data?${query}`, null, abort.signal);
      if (version !== generation) return;
      data = result; page = 1;
      if (section === 'inventory') {
        const wanted = params.get('warehouseId') || '';
        fields.warehouseId.innerHTML = '<option value="">Tất cả kho</option>' + result.warehouses.map(x => `<option value="${esc(x.id)}">${esc(x.name)}</option>`).join('');
        fields.warehouseId.value = wanted;
      }
      history.replaceState(null, '', `${location.pathname}?${query}`);
      const dates = new URLSearchParams({ fromDate: fields.fromDate.value, toDate: fields.toDate.value });
      root.querySelectorAll('.mr-nav a').forEach(link => link.href = link.getAttribute('href').split('?')[0] + '?' + dates.toString());
      $('[data-context]').textContent = `${date(result.period.fromDate)} – ${date(result.period.toDate)} · UTC+7 · Số tiền VND`;
      $('[data-quality]').innerHTML = result.warnings.map(x => `<p>${esc(x)}</p>`).join('');
      $('[data-live]').hidden = true; $('[data-results]').hidden = false;
      if ($('[data-export]')) $('[data-export]').href = `/admin/reports/operations/${section}/export?${query}`;
      render();
      if (!autoOpened && section === 'cashflow' && canManage && Number(initial.get('payExpenseId')) > 0) { autoOpened = true; await openMoney(null, Number(initial.get('payExpenseId'))); }
    } catch (error) {
      if (error.name === 'AbortError' || version !== generation) return;
      data = null; $('[data-live]').hidden = true; $('[data-error]').hidden = false; $('[data-error] span').textContent = error.message;
    } finally { if (version === generation) root.setAttribute('aria-busy', 'false'); }
  }
  form.addEventListener('submit', event => { event.preventDefault(); load(); });
  root.querySelectorAll('[data-refresh]').forEach(button => button.addEventListener('click', load));

  function kpi(title, value, caption, icon = 'bx-wallet', isMoney = true) {
    return `<article class="mr-kpi"><div class="mr-kpi-title"><i class="bx ${icon}" aria-hidden="true"></i>${esc(title)}</div><strong title="${esc(number(value))}${isMoney ? ' VND' : ''}">${isMoney ? compact(value) : number(value)}</strong><small>${esc(caption)}</small></article>`;
  }
  function table(headers, rows, caption = '') {
    if (!rows.length) return '<div class="mr-empty">Không có dữ liệu phù hợp trong kỳ này.</div>';
    return `<table class="mr-table">${caption ? `<caption>${esc(caption)}</caption>` : ''}<thead><tr>${headers.map(x => `<th scope="col">${esc(x)}</th>`).join('')}</tr></thead><tbody>${rows.map(row => `<tr>${row.map((cell, i) => `<td class="${i > 0 ? 'mr-num' : ''}">${cell}</td>`).join('')}</tr>`).join('')}</tbody></table>`;
  }
  function panel(id, title, caption, detail) {
    return `<section class="mr-panel"><div class="mr-panel-heading"><div><h2>${esc(title)}</h2><p class="mr-caption">${esc(caption)}</p></div></div><div class="mr-chart" id="${id}" role="img" aria-label="${esc(title)}"></div><details class="or-summary-table"><summary>Xem số liệu biểu đồ</summary><div class="mr-table-wrap">${detail}</div></details></section>`;
  }
  function chart(id, options, hasData = true) {
    const host = document.getElementById(id);
    if (!host) return;
    if (!hasData || !window.ApexCharts) { host.innerHTML = '<div class="or-chart-empty">Chưa có số liệu để vẽ biểu đồ.</div>'; return; }
    const css = getComputedStyle(root);
    const instance = new ApexCharts(host, {
      ...options,
      chart: { height: 285, background: 'transparent', toolbar: { show: false }, fontFamily: 'inherit', foreColor: css.getPropertyValue('--mr-muted').trim(),
        animations: { enabled: !matchMedia('(prefers-reduced-motion: reduce)').matches }, ...options.chart },
      colors: options.colors || ['#13ad72', '#e9ad61', '#6a99eb', '#a6cbb5', '#9d80d8', '#d87f79'],
      theme: { mode: document.documentElement.classList.contains('dark-style') ? 'dark' : 'light' },
      dataLabels: { enabled: false, ...options.dataLabels }, grid: { borderColor: css.getPropertyValue('--mr-border').trim() },
      stroke: { width: 2.5, curve: 'smooth', ...options.stroke },
      tooltip: { y: { formatter: value => number(value) }, ...options.tooltip },
      yaxis: options.yaxis || { labels: { formatter: value => compact(value) } },
      legend: { position: 'bottom', fontSize: '11px' },
      noData: { text: 'Chưa có dữ liệu' }
    });
    charts.push(instance); instance.render().then(() => root.dataset.reportTheme = document.documentElement.classList.contains('dark-style') ? 'dark' : 'light');
  }
  function render() {
    destroyCharts();
    if (section === 'cashflow') renderCash();
    if (section === 'inventory') renderStock();
    if (section === 'debts') renderDebt();
    $('[data-search]').value = '';
    renderTable();
  }
  function renderCash() {
    const external = data.items.filter(x => !x.isTransfer && x.source !== 'Phiếu tiền mặt ca');
    const incoming = external.reduce((sum, x) => sum + Math.max(0, x.amount), 0);
    const outgoing = -external.reduce((sum, x) => sum + Math.min(0, x.amount), 0);
    const balances = data.summary.filter(x => x.fund !== 'other');
    const closing = balances.length > 0 && balances.every(x => x.closing != null) ? balances.reduce((sum, x) => sum + x.closing, 0) : null;
    $('[data-kpis]').innerHTML = kpi('Thực thu', incoming, 'Không gồm chuyển giữa hai quỹ', 'bx-trending-up') + kpi('Thực chi', outgoing, 'Hoàn tiền, chi phí và các phiếu chi', 'bx-trending-down') + kpi('Dòng tiền thuần', incoming - outgoing, 'Thực thu − thực chi', 'bx-transfer') + kpi('Số dư cuối kỳ', closing, closing == null ? 'Cần số dư đầu kỳ và dữ liệu đủ căn cứ' : 'Các quỹ đang chọn', 'bx-wallet');
    const fundRows = data.summary.filter(x => x.fund !== 'other' || x.inflow || x.outflow).map(x => [esc(x.name), money(x.opening), money(x.inflow), money(x.outflow), money(x.closing)]);
    $('[data-panels]').innerHTML = '<div class="mr-grid">' +
      panel('or-cash-trend', 'Thu và chi theo thời gian', 'Tách thực thu, thực chi; loại chuyển quỹ', table(['Kỳ', 'Thu', 'Chi', 'Thuần'], data.trend.map(x => [esc(x.label), money(x.inflow), money(x.outflow), money(x.net)]))) +
      `<section class="mr-panel"><div class="or-fund-actions"><div><h2>Tình hình từng quỹ</h2><p class="mr-caption">Đầu kỳ + thu − chi = cuối kỳ</p></div>${canManage ? '<button class="mr-button" type="button" data-opening>Số dư ban đầu</button>' : ''}</div><div class="mr-table-wrap or-fund-table">${table(['Quỹ', 'Đầu kỳ', 'Thu', 'Chi', 'Cuối kỳ'], fundRows)}</div><p class="mr-caption" style="margin-top:18px">Chênh lệch kiểm đếm các ca đóng trong kỳ: <strong>${number(data.closingShiftDifference)} VND</strong></p><p class="mr-caption">Thu/chi từng quỹ có cả chuyển quỹ. Chênh lệch kiểm đếm dùng để đối chiếu, chưa tự hạch toán vào số dư.</p></section></div>`;
    chart('or-cash-trend', { chart: { type: 'line' }, series: [{ name: 'Thực thu', type: 'column', data: data.trend.map(x => x.inflow) }, { name: 'Thực chi', type: 'column', data: data.trend.map(x => x.outflow) }, { name: 'Thuần', type: 'line', data: data.trend.map(x => x.net) }], xaxis: { categories: data.trend.map(x => x.label) }, stroke: { width: [0, 0, 3] }, plotOptions: { bar: { columnWidth: '45%', borderRadius: 3 } } }, data.items.length > 0);
    $('[data-state]').innerHTML = '<option value="all">Tất cả giao dịch</option><option value="in">Tiền vào</option><option value="out">Tiền ra</option><option value="transfer">Chuyển quỹ</option><option value="manual">Phiếu nhập tay / liên kết</option>';
  }
  function renderStock() {
    $('[data-kpis]').innerHTML = kpi('Giá trị tồn cuối kỳ', data.inventoryValue, data.provisionalCount ? 'Có phần giá vốn tạm tính' : 'Theo sổ định giá tại cuối kỳ', 'bx-package') + kpi('Sắp hết / hết hàng', data.lowCount, 'Theo ngưỡng đơn vị gốc đã chọn', 'bx-error-circle', false) + kpi('Chậm luân chuyển', data.slowCount, 'Còn hàng, không xuất bán trong kỳ', 'bx-time-five', false) + kpi('Mặt hàng tồn âm', data.negativeCount, 'Cần kiểm tra số lượng và giá vốn', 'bx-error', false);
    const categories = new Map();
    data.items.filter(x => x.closingValue != null && x.closingValue > 0).forEach(x => categories.set(x.category, (categories.get(x.category) || 0) + x.closingValue));
    const values = [...categories].sort((a, b) => b[1] - a[1]);
    const statuses = ['normal', 'low', 'empty', 'negative', 'slow', 'incomplete'].map(key => ({ key, count: data.items.filter(x => x.state === key).length }));
    const cover = data.items.filter(x => x.daysCover != null && x.daysCover >= 0).sort((a, b) => a.daysCover - b.daysCover).slice(0, 8);
    const title = data.canViewCost ? 'Giá trị tồn theo nhóm hàng' : 'Tình trạng mặt hàng trong kho';
    const detail = data.canViewCost ? table(['Nhóm', 'Giá trị đã xác định'], values.map(x => [esc(x[0]), money(x[1])])) : table(['Trạng thái', 'Số dòng mặt hàng/kho'], statuses.map(x => [esc(stateName(x.key)), number(x.count)]));
    $('[data-panels]').innerHTML = '<div class="mr-grid">' + panel('or-stock-value', title, data.canViewCost ? 'Chỉ phần có định giá và giá trị dương; không cộng đơn vị hàng' : 'Đếm từng mặt hàng tại từng kho', detail) + panel('or-stock-cover', 'Khả năng đáp ứng bán hàng', '8 mặt hàng có số ngày đủ bán thấp nhất theo kỳ đã chọn', table(['Mặt hàng / kho', 'Số ngày'], cover.map(x => [esc(x.name + ' · ' + x.warehouse), number(x.daysCover)]))) + '</div>';
    if (data.canViewCost) chart('or-stock-value', { chart: { type: 'treemap' }, series: [{ data: values.map(x => ({ x: x[0], y: x[1] })) }], plotOptions: { treemap: { distributed: true, enableShades: false } }, colors: ['#a0dec0', '#b3d1f1', '#dac9ed', '#f0d09c', '#a6dcd7'], dataLabels: { enabled: true, style: { colors: ['#1d3d2c'] }, formatter: (text, opts) => [text, compact(opts.value)] } }, values.length > 0);
    else chart('or-stock-value', { chart: { type: 'donut' }, series: statuses.map(x => x.count), labels: statuses.map(x => stateName(x.key)), stroke: { width: 0 } }, data.items.length > 0);
    chart('or-stock-cover', { chart: { type: 'bar' }, series: [{ name: 'Số ngày đủ bán', data: cover.map(x => x.daysCover) }], xaxis: { categories: cover.map(x => x.sku + ' · ' + x.warehouse), labels: { formatter: value => number(value) } }, yaxis: { labels: { maxWidth: 150 } }, plotOptions: { bar: { horizontal: true, borderRadius: 4, barHeight: '48%' } } }, cover.length > 0);
    $('[data-state]').innerHTML = '<option value="all">Tất cả mặt hàng</option>' + ['low', 'empty', 'negative', 'slow', 'incomplete'].map(x => `<option value="${x}">${stateName(x)}</option>`).join('') + '<option value="provisional">Có giá vốn tạm</option>';
  }
  function renderDebt() {
    $('[data-kpis]').innerHTML = kpi('Khách hàng còn nợ', data.receivable, 'Dư phải thu đến cuối ngày đã chọn', 'bx-user') + kpi('Còn phải trả NCC', data.payable, 'Tiền hàng và chi phí mua hàng', 'bx-store') + kpi('Phải thu quá hạn', data.overdueReceivable, 'Chỉ khoản có hạn thanh toán', 'bx-time-five') + kpi('Phải trả quá hạn', data.overduePayable, 'Chỉ khoản có hạn thanh toán', 'bx-error-circle');
    const bands = ['current', '1-30', '31-60', '61-90', '90+', 'unknown'];
    const series = ['receivable', 'payable'].map(kind => ({ name: kind === 'receivable' ? 'Phải thu' : 'Phải trả', data: bands.map(band => data.items.filter(x => x.kind === kind && x.aging === band).reduce((sum, x) => sum + Math.max(0, x.closing), 0)) }));
    const agingRows = bands.map((band, i) => [esc(agingName(band)), money(series[0].data[i]), money(series[1].data[i])]);
    $('[data-panels]').innerHTML = '<div class="mr-grid">' + panel('or-debt-aging', 'Phân bố tuổi nợ', 'Phân nhóm theo hạn thanh toán đến cuối kỳ', table(['Tuổi nợ', 'Phải thu', 'Phải trả'], agingRows)) + panel('or-debt-trend', 'Biến động công nợ khách hàng', 'Giảm nợ gồm thu tiền, trả hàng và hủy đơn', table(['Kỳ', 'Tăng nợ', 'Giảm nợ'], data.trend.map(x => [esc(x.label), money(x.newDebt), money(x.settled)]))) + '</div>';
    chart('or-debt-aging', { chart: { type: 'bar', stacked: true }, series, xaxis: { categories: ['Chưa quá hạn', '1–30', '31–60', '61–90', '>90', 'Chưa có hạn'] }, plotOptions: { bar: { borderRadius: 3, columnWidth: '48%' } }, stroke: { width: 0 } }, data.items.some(x => x.closing > 0));
    chart('or-debt-trend', { chart: { type: 'area' }, series: [{ name: 'Tăng nợ', data: data.trend.map(x => x.newDebt) }, { name: 'Giảm nợ', data: data.trend.map(x => x.settled) }], xaxis: { categories: data.trend.map(x => x.label) }, fill: { type: 'gradient', gradient: { opacityFrom: .3, opacityTo: .03 } } }, data.trend.some(x => x.newDebt || x.settled));
    $('[data-state]').innerHTML = '<option value="all">Tất cả công nợ</option><option value="receivable">Phải thu khách hàng</option><option value="payable">Phải trả nhà cung cấp</option><option value="overdue">Có nợ quá hạn</option><option value="unknown">Chưa có hạn thanh toán</option><option value="open">Còn dư nợ</option>';
  }
  function filteredItems() {
    const search = $('[data-search]').value.trim().toLocaleLowerCase('vi');
    const state = $('[data-state]').value;
    return data.items.filter(x => {
      const text = section === 'cashflow' ? `${x.id} ${x.name} ${x.source} ${x.reference || ''}` : section === 'inventory' ? `${x.name} ${x.sku} ${x.warehouse} ${x.category}` : `${x.party} ${x.document}`;
      if (!text.toLocaleLowerCase('vi').includes(search)) return false;
      if (state === 'all') return true;
      if (section === 'cashflow') return state === 'in' ? x.amount > 0 : state === 'out' ? x.amount < 0 : state === 'transfer' ? x.isTransfer : x.manualId != null;
      if (section === 'inventory') return state === 'provisional' ? x.isProvisional : x.state === state;
      return state === 'open' ? x.closing > 0 : state === 'unknown' ? x.aging === 'unknown' : state === 'overdue' ? ['1-30', '31-60', '61-90', '90+'].includes(x.aging) : x.kind === state;
    });
  }
  function renderTable() {
    if (!data) return;
    const all = filteredItems(), pages = Math.max(1, Math.ceil(all.length / 25)); page = Math.min(page, pages);
    const items = all.slice((page - 1) * 25, page * 25);
    let headers, rows;
    if (section === 'cashflow') {
      headers = ['Nội dung', 'Ngày UTC+7', 'Quỹ', 'Thu', 'Chi', 'Nguồn', 'Thao tác'];
      rows = items.map(x => [esc(x.name) + `<small>${esc(x.id)}${x.reference ? ' · ' + esc(x.reference) : ''}</small>`, esc(time(x.atUtc)), esc(fundName(x.fund)), money(x.amount > 0 ? x.amount : 0), money(x.amount < 0 ? -x.amount : 0), esc(x.source), canManage && x.canReverse ? `<button class="mr-button" type="button" data-reverse="${x.manualId}">${x.source === 'Liên kết phiếu ca' ? 'Hủy liên kết' : 'Đảo phiếu'}</button>` : '—']);
    } else if (section === 'inventory') {
      headers = ['Sản phẩm', 'Kho', 'ĐVT gốc', 'Đầu kỳ', 'Nhập', 'Xuất', 'Cuối kỳ', 'Giá trị tồn', 'Đủ bán (ngày)', 'Tình trạng'];
      rows = items.map(x => [esc(x.name) + `<small>${esc(x.sku)}</small>`, esc(x.warehouse), esc(x.unit), number(x.opening), number(x.inbound), number(x.outbound), number(x.closing), money(x.closingValue), number(x.daysCover), badge(stateName(x.state), x.state === 'negative' ? 'danger' : x.state !== 'normal' ? 'warning' : '') + (x.isProvisional ? ' ' + badge('Giá vốn tạm', 'warning') : '') + (x.isIncomplete && x.state !== 'incomplete' ? ' ' + badge('Thiếu dữ liệu', 'warning') : '')]);
    } else {
      headers = ['Đối tượng / chứng từ', 'Loại', 'Hạn trả', 'Đầu kỳ', 'Tăng nợ', 'Giảm nợ', 'Cuối kỳ', 'Tuổi nợ', 'Thao tác'];
      rows = items.map(x => [esc(x.party) + `<small>${esc(x.document)}</small>`, x.kind === 'receivable' ? 'Phải thu' : 'Phải trả', date(x.dueDate), money(x.opening), money(x.increase), money(x.decrease), money(x.closing), badge(agingName(x.aging), ['1-30', '31-60', '61-90', '90+'].includes(x.aging) ? 'warning' : ''), canManage && x.kind === 'payable' ? `<div class="or-detail-actions"><button class="mr-button" type="button" data-due="${x.id}">Hạn trả</button>${x.closing > 0 ? `<button class="mr-button" type="button" data-pay="${x.id}">Trả nợ</button>` : ''}</div>` : '—']);
    }
    $('[data-table]').innerHTML = table(headers, rows);
    $('[data-count]').textContent = `${all.length} / ${data.items.length} dòng · Số liệu đối chiếu theo kỳ đã chọn`;
    $('[data-page]').textContent = `Trang ${page} / ${pages}`;
    $('[data-prev]').disabled = page === 1; $('[data-next]').disabled = page === pages;
  }
  $('[data-search]').addEventListener('input', () => { page = 1; renderTable(); });
  $('[data-state]').addEventListener('change', () => { page = 1; renderTable(); });
  $('[data-prev]').addEventListener('click', () => { page--; renderTable(); });
  $('[data-next]').addEventListener('click', () => { page++; renderTable(); });

  function showError(dialog, error) { const node = dialog.querySelector('[data-form-error]'); node.hidden = false; node.textContent = error.message; }
  function clearError(dialog) { dialog.querySelector('[data-form-error]').hidden = true; }
  root.querySelectorAll('[data-close]').forEach(button => button.addEventListener('click', () => button.closest('dialog').close()));
  const moneyDialog = $('[data-money-dialog]'), moneyForm = $('[data-money-form]');
  function sourceList() { return moneyForm.elements.mode.value === 'expense' ? sources.expenses : sources.payables; }
  function configureMoney() {
    const f = moneyForm.elements, mode = f.mode.value;
    $('[data-source-label]').hidden = !['expense', 'payable'].includes(mode);
    f.source.required = ['expense', 'payable'].includes(mode);
    f.source.innerHTML = '<option value="">Chọn chứng từ…</option>' + sourceList().map(x => `<option value="${x.id}">${esc(x.name)} · ${number(x.amount)} VND</option>`).join('');
    $('[data-target-label]').hidden = mode !== 'transfer';
    f.fund.disabled = mode === 'internal'; if (mode === 'internal') f.fund.value = 'cash';
    $('[data-cash-label]').hidden = f.fund.value !== 'cash'; f.cashVoucher.required = mode === 'internal';
    f.cashVoucher.innerHTML = `<option value="">${mode === 'internal' ? 'Chọn phiếu tiền mặt…' : 'Lập phiếu mới ngoài ca'}</option>` + sources.cashVouchers.filter(x => mode === 'internal' || (mode === 'in' ? x.amount > 0 : x.amount < 0)).map(x => `<option value="${x.id}">${esc(x.name)} · ${number(Math.abs(x.amount))} VND</option>`).join('');
    f.amount.readOnly = false; f.amount.max = '1000000000000';
    $('[data-legacy-evidence]').hidden = true;
  }
  async function openMoney(payableId, expenseId) {
    try {
      const requested = payableId ? '?payableId=' + payableId : expenseId ? '?expenseId=' + expenseId : '';
      sources = await api('/admin/reports/treasury/sources' + requested); requestId = guid(); moneyForm.reset();
      clearError(moneyDialog); moneyForm.elements.date.value = localDay(); moneyForm.elements.date.max = localDay(); moneyForm.elements.date.min = '2000-01-01';
      if (payableId) {
        if (!sources.payables.some(x => x.id === payableId)) throw new Error('Khoản này đã được thanh toán hoặc không còn hợp lệ. Làm mới báo cáo để đối chiếu.');
        moneyForm.elements.mode.value = 'payable';
      }
      if (expenseId) {
        if (!sources.expenses.some(x => x.id === expenseId)) throw new Error('Chi phí đã thanh toán hoặc chưa được ghi nhận. Làm mới báo cáo để đối chiếu.');
        moneyForm.elements.mode.value = 'expense';
      }
      configureMoney();
      if (payableId) { moneyForm.elements.source.value = String(payableId); selectSource(); }
      if (expenseId) { moneyForm.elements.source.value = String(expenseId); selectSource(); }
      moneyDialog.showModal();
    } catch (error) { $('[data-error]').hidden = false; $('[data-error] span').textContent = error.message; }
  }
  function selectSource() {
    const f = moneyForm.elements, source = sourceList().find(x => x.id === Number(f.source.value));
    if (source) { f.amount.value = source.amount; f.amount.max = source.amount; f.amount.readOnly = f.mode.value === 'expense' || source.requiresEvidence; f.name.value = source.name; if (source.requiresEvidence && source.date) f.date.value = source.date.slice(0, 10); }
    $('[data-legacy-evidence]').hidden = !source?.requiresEvidence;
  }
  if (canManage) {
    moneyForm.elements.mode.addEventListener('change', configureMoney);
    moneyForm.elements.fund.addEventListener('change', () => {
      const f = moneyForm.elements; $('[data-cash-label]').hidden = f.fund.value !== 'cash'; f.cashVoucher.value = ''; f.targetFund.value = f.fund.value === 'cash' ? 'bank' : 'cash';
    });
    moneyForm.elements.source.addEventListener('change', selectSource);
    moneyForm.elements.cashVoucher.addEventListener('change', () => {
      const f = moneyForm.elements, source = sources.cashVouchers.find(x => x.id === Number(f.cashVoucher.value));
      if (source) { f.amount.value = Math.abs(source.amount); f.date.value = source.date.slice(0, 10); if (!f.name.value) f.name.value = source.name; }
    });
    $('[data-money-new]')?.addEventListener('click', () => openMoney());
    moneyForm.addEventListener('submit', async event => {
      event.preventDefault(); const f = moneyForm.elements, mode = f.mode.value;
      const source = sourceList().find(x => x.id === Number(f.source.value));
      const voucher = sources.cashVouchers.find(x => x.id === Number(f.cashVoucher.value));
      const input = { clientRequestId: requestId, fund: f.fund.value, targetFund: mode === 'internal' ? 'cash' : mode === 'transfer' ? f.targetFund.value : null,
        date: f.date.value, amount: mode === 'internal' ? voucher?.amount : Number(f.amount.value) * (mode === 'in' ? 1 : -1), name: f.name.value,
        reference: f.reference.value || null, note: f.note.value || null,
        operatingExpenseId: mode === 'expense' ? Number(f.source.value) : null,
        purchasePayableId: mode === 'payable' ? Number(f.source.value) : null,
        sourceRowVersion: source?.rowVersion || null, reconcileLegacyPayment: ['expense', 'payable'].includes(mode) && !!source?.requiresEvidence, posShiftCashTransactionId: f.fund.value === 'cash' && f.cashVoucher.value ? Number(f.cashVoucher.value) : null };
      await saveDialog(moneyDialog, event.submitter, () => api('/admin/reports/treasury/entries', input));
    });
    $('[data-opening-form]').addEventListener('submit', async event => {
      event.preventDefault(); const f = event.target.elements;
      await saveDialog($('[data-opening-dialog]'), event.submitter, () => api('/admin/reports/treasury/opening', { fund: f.fund.value, date: f.date.value, amount: Number(f.amount.value), note: f.note.value }));
    });
    $('[data-action-form]').addEventListener('submit', async event => {
      event.preventDefault(); const f = event.target.elements;
      const input = action.kind === 'due' ? { rowVersion: action.rowVersion, dueDate: f.date.value || null } : { clientRequestId: action.requestId, rowVersion: action.rowVersion, date: f.date.value, reason: f.reason.value };
      const url = action.kind === 'due' ? `/admin/reports/treasury/payables/${action.id}/due-date` : `/admin/reports/treasury/entries/${action.id}/reverse`;
      await saveDialog($('[data-action-dialog]'), event.submitter, () => api(url, input));
    });
  }
  async function saveDialog(dialog, button, save) {
    button.disabled = true; clearError(dialog);
    try { await save(); dialog.close(); await load(); } catch (error) { showError(dialog, error); } finally { button.disabled = false; }
  }
  root.addEventListener('click', event => {
    const button = event.target.closest('button'); if (!button || !canManage) return;
    if (button.hasAttribute('data-opening')) {
      const dialog = $('[data-opening-dialog]'), f = $('[data-opening-form]'); f.reset(); clearError(dialog);
      f.elements.date.value = data.period.fromDate.slice(0, 10); f.elements.date.max = localDay(); f.elements.date.min = '2000-01-01'; dialog.showModal();
    }
    if (button.dataset.pay) openMoney(Number(button.dataset.pay));
    if (button.dataset.reverse || button.dataset.due) {
      const isDue = !!button.dataset.due;
      const row = isDue ? data.items.find(x => x.kind === 'payable' && x.id === Number(button.dataset.due)) : data.items.find(x => x.manualId === Number(button.dataset.reverse));
      if (!row) return;
      const dialog = $('[data-action-dialog]'), f = $('[data-action-form]'); f.reset(); clearError(dialog);
      action = { kind: isDue ? 'due' : 'reverse', id: isDue ? row.id : row.manualId, rowVersion: row.rowVersion, requestId: guid() };
      const linked = row.source === 'Liên kết phiếu ca';
      $('#or-action-title').textContent = isDue ? 'Cập nhật hạn thanh toán' : linked ? 'Hủy liên kết phiếu ca' : 'Đảo phiếu thu chi';
      $('[data-action-description]').textContent = isDue ? row.party + ' · ' + row.document : linked ? `${row.name}. Hủy liên kết thanh toán với chứng từ, phiếu tiền mặt của ca vẫn giữ nguyên. Công nợ liên quan được ghi ngược chiều vào ngày chọn.` : `${row.name} · ${number(row.amount)} VND. Xác nhận tiền được ghi ngược chiều vào ngày chọn; phiếu gốc vẫn được giữ lại.`;
      $('[data-action-date-label]').firstChild.textContent = isDue ? 'Hạn thanh toán (để trống nếu chưa có)' : 'Ngày đảo phiếu';
      $('[data-action-reason-label]').hidden = isDue; f.elements.reason.required = !isDue; f.elements.date.required = !isDue;
      f.elements.date.value = isDue ? row.dueDate?.slice(0, 10) || '' : localDay(); f.elements.date.max = isDue ? '2100-12-31' : localDay(); f.elements.date.min = isDue ? '2000-01-01' : localDay(new Date(row.atUtc.endsWith('Z') ? row.atUtc : row.atUtc + 'Z'));
      dialog.showModal();
    }
  });
  let themeTimer;
  let lastTheme = document.documentElement.classList.contains('dark-style');
  new MutationObserver(() => {
    const nextTheme = document.documentElement.classList.contains('dark-style');
    if (nextTheme === lastTheme) return; lastTheme = nextTheme;
    clearTimeout(themeTimer); themeTimer = setTimeout(() => {
      if (data && !$('[data-results]').hidden) { const state = $('[data-state]').value, search = $('[data-search]').value; render(); $('[data-state]').value = state; $('[data-search]').value = search; renderTable(); }
    }, 100);
  }).observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
  load();
})();
