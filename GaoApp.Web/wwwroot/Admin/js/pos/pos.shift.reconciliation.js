(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else { root.PosShiftReconciliation = api; document.addEventListener('DOMContentLoaded', api.init); }
})(typeof window === 'undefined' ? globalThis : window, function () {
    'use strict';
    const money = value => Number(value || 0).toLocaleString('vi-VN') + ' đ';
    const esc = value => String(value ?? '').replace(/[&<>"']/g, ch => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[ch]));
    const utcDate = value => new Date(typeof value==='string' && !/Z$|[+-]\d\d:\d\d$/.test(value) ? value+'Z' : value);
    const date = value => value ? utcDate(value).toLocaleString('vi-VN') : '—';
    const day = value => {
        const d = utcDate(value);
        return Number.isNaN(d.getTime()) ? '' : `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
    };
    const method = value => ({ Cash:'Tiền mặt', BankTransfer:'Chuyển khoản', Card:'Thẻ', EWallet:'Ví điện tử', Other:'Khác' }[value] || value || 'Không phát sinh tiền');
    const status = value => ({ Draft:'Chưa chốt', OnHold:'Đang giữ', Completed:'Hoàn tất', Refunded:'Đã hoàn tiền', Voided:'Hủy sau chốt',
        Cancelled:'Đã hủy', Open:'Đang mở', Closed:'Đã đóng', Pending:'Chờ xử lý', Approved:'Đã duyệt', Rejected:'Từ chối',
        Withdrawn:'Đã rút', Creating:'Đang tạo', Received:'ACB xác nhận nhận tiền', ReviewRequired:'Cần kiểm tra', Paid:'Đã nhận tiền',
        ManualConfirmed:'Đã xác nhận thủ công', Failed:'Cần kiểm tra' }[value] || value);
    const badge = (text, variant = '') => `<span class="recon-badge ${variant ? 'is-'+variant : ''}">${esc(text)}</span>`;
    const amount = (label, value) => `<div><small>${esc(label)}</small><strong>${money(value)}</strong></div>`;
    const orderLink = id => `<a href="/admin/pos/order-detail/${Number(id)}" target="_blank" rel="noopener">Mở đơn #${Number(id)}</a>`;
    const requestLink = (id, shiftId) => `/admin/pos-shift/requests?tab=cash&shiftId=${Number(shiftId)}&transactionId=${Number(id)}`;
    const paymentTable = payments => `<div class="recon-table-wrap"><table class="recon-table"><thead><tr><th>Phương thức</th><th>Số tiền</th><th>Đối chiếu</th><th></th></tr></thead><tbody>${payments.map(p =>
        `<tr><td>${esc(method(p.method))}${p.isDebtCollection ? '<small>Thu công nợ</small>' : ''}${p.cancelled ? badge('Đã hủy','danger') : ''}</td><td class="money">${money(p.amount)}</td><td>${p.reference?esc(p.reference):''}${p.method==='BankTransfer'?'<small>'+esc(p.confirmation)+'</small>':''}<details class="recon-evidence"><summary>Thông tin gốc</summary>${esc(date(p.paidAtUtc))}<br>${esc(p.actor)}<br>${esc(p.provider||'')}</details></td><td>
        ${p.pendingRequestId ? `<a href="/admin/pos-shift/requests?tab=payment&requestId=${Number(p.pendingRequestId)}" target="_blank" rel="noopener">Xem yêu cầu</a>` : p.canRequest ? `<a href="/admin/pos-shift/requests?tab=payment&paymentId=${Number(p.id)}" target="_blank" rel="noopener">Đổi phương thức</a>` : ''}</td></tr>`).join('') || '<tr><td colspan="4">Chưa có khoản thanh toán.</td></tr>'}</tbody></table></div>`;
    function orderFlags(o) {
        const live = o.payments.filter(p => !p.cancelled && !p.isDebtCollection);
        const flags = [];
        if (o.surplus > 0) flags.push('Nhận dư ' + money(o.surplus));
        if (o.remaining > 0) flags.push(o.isCreditSale ? 'Bán nợ khi chốt ' + money(o.remaining) : 'Còn thiếu ' + money(o.remaining));
        if (o.cashReceived > 0 && (o.bankReceived > 0 || o.otherReceived > 0)) flags.push('Nhiều phương thức');
        if (live.length > 1) flags.push('Nhận tiền nhiều lần');
        if (live.some(p => p.method === 'BankTransfer' && !p.bankVerified)) flags.push('Chuyển khoản cần đối chiếu');
        if (['Draft','OnHold'].includes(o.status) && live.length) flags.push('Đã nhận tiền, chưa chốt');
        if (o.payments.some(p => p.cancelled)) flags.push('Có khoản đã hủy');
        if ((o.cancelled || ['Voided','Cancelled'].includes(o.status)) && live.length) flags.push('Kiểm tra tiền của đơn hủy');
        return flags;
    }
    function rowsFor(data, tab) {
        const source = { orders:data.orders, cash:data.cashTransactions, refunds:data.refunds, other:data.otherMovements,
            qrs:data.qrs, adjustments:[...(data.adjustments || []),...(data.paymentAdjustments || []).map(x=>({...x,kind:'Payment'}))].sort((a,b)=>new Date(b.createdAtUtc)-new Date(a.createdAtUtc)) }[tab] || [];
        return source.map(x => {
            const payments = x.payments || [];
            const flags = tab === 'orders' ? orderFlags(x) : tab === 'cash' ? (x.pendingRequestId ? ['Chờ duyệt điều chỉnh'] : x.cancelled && !x.depositEntryId ? ['Phiếu đã hủy'] : []) :
                tab === 'qrs' ? (!x.paymentId && !['Cancelled','Completed'].includes(x.status) ? ['QR chưa ghi nhận tiền / cần kiểm tra'] : x.reviewReason ? [x.reviewReason] : []) :
                tab === 'adjustments' && x.status === 'Pending' ? ['Chờ duyệt'] : tab === 'other' && x.pendingRequestId ? ['Chờ duyệt đổi phương thức'] : tab === 'refunds' && x.status !== 'Completed' ? ['Phiếu hoàn chưa hoàn tất'] : [];
            const when = tab === 'orders' ? x.completedAtUtc || x.createdAtUtc : x.createdAtUtc;
            return { value:x, flags, when, attention:flags.length > 0,
                cash:tab === 'cash' || x.method === 'Cash' || payments.some(p => !p.cancelled && p.method === 'Cash'),
                bank:x.method === 'BankTransfer' || payments.some(p => !p.cancelled && p.method === 'BankTransfer') || tab === 'qrs',
                search:JSON.stringify(x).toLocaleLowerCase('vi') };
        });
    }
    function filterRows(rows, { query = '', day: selectedDay = '', scope = 'all' }) {
        const q = query.trim().toLocaleLowerCase('vi').replace(/\./g,'');
        return rows.filter(r => (!selectedDay || day(r.when) === selectedDay) &&
            (scope === 'all' || scope === 'attention' && r.attention || scope === 'cash' && r.cash || scope === 'bank' && r.bank) &&
            (!q || r.search.replace(/\./g,'').includes(q)));
    }
    function renderOrder(o, flags) {
        const lines = `<div class="recon-table-wrap"><table class="recon-table"><thead><tr><th>Sản phẩm</th><th>Số lượng</th><th>Đơn giá</th><th>Thành tiền</th></tr></thead><tbody>${o.lines.map(l =>
            `<tr><td>${esc(l.name)}</td><td>${esc(l.quantity)}</td><td class="money">${money(l.unitPrice)}</td><td class="money">${money(l.total)}</td></tr>`).join('')}</tbody></table></div>`;
        return item(o.number, `${date(o.completedAtUtc || o.createdAtUtc)} · ${o.customer}`, [amount('Tổng đơn',o.grandTotal), ...(o.cashReceived?[amount('Tiền mặt',o.cashReceived)]:[]),
            ...(o.bankReceived?[amount('Chuyển khoản',o.bankReceived)]:[])], [badge(status(o.status)),...(flags.length?[badge(flags[0],'warning')]:[])],
            `${flags.length?'<p>'+flags.map(f=>badge(f,'warning')).join(' ')+'</p>':''}${paymentTable(o.payments)}
            <details class="recon-evidence"><summary>Chi tiết đơn và cách tính vào quỹ</summary><p>Người tạo: <strong>${esc(o.actor)}</strong> · ${esc(date(o.createdAtUtc))}${o.note ? '<br>Ghi chú: '+esc(o.note) : ''}</p>
            <div class="recon-amounts">${amount('Cọc sử dụng',o.deposit)}${amount('Thẻ / ví / khác',o.otherReceived)}${amount(o.isCreditSale ? 'Công nợ lúc chốt đơn' : 'Còn thiếu',o.remaining)}${amount('Tiền nhận dư',o.surplus)}${amount('Tiền mặt tính vào tiền bán của ca',o.cashApplied)}${amount('Không tiền mặt tính vào tiền bán của ca',o.nonCashApplied)}</div>
            <p class="recon-help">Tiền nhận dư cần đối chiếu với tiền thực tế đã trả lại khách. Công nợ lúc chốt chưa trừ các lần thu nợ sau đó; các lần thu này được theo dõi riêng trong ca thực hiện thu.</p>
            <h3>Chi tiết hàng trong đơn</h3>${lines}
            <div class="recon-item-actions">${orderLink(o.id)}</div></details>`, flags.length > 0, o.cancelled);
    }
    function item(title, subtitle, amounts, badges, body, attention = false, cancelled = false) {
        return `<details class="recon-item ${attention ? 'is-attention' : ''} ${cancelled ? 'is-cancelled' : ''}"><summary>
            <div><div class="recon-item-title">${esc(title)}</div><small>${esc(subtitle)}</small></div><div class="recon-amounts">${amounts.join('')}</div><div class="recon-badges">${badges.join('')}</div>
            </summary><div class="recon-item-body">${body}</div></details>`;
    }
    function renderRow(data, tab, row) {
        const x = row.value, flags = row.flags;
        if (tab === 'orders') return renderOrder(x,flags);
        if (tab === 'cash') return item(`Phiếu #${x.id} · ${x.reason}`,`${date(x.createdAtUtc)} · ${x.actor}`,
            [amount(x.type === 'CashIn' ? 'Thu tiền mặt' : 'Chi tiền mặt',x.amount)], [badge(x.cancelled&&x.depositEntryId?'Đã đổi phương thức cọc':x.type === 'CashIn' ? 'Thu tiền' : 'Chi tiền',x.cancelled?'':x.type === 'CashIn' ? 'good' : 'danger'),...flags.map(f => badge(f,'warning'))],
            `<p><strong>Nội dung:</strong> ${esc(x.reason)}<br><strong>Ghi chú:</strong> ${esc(x.note || '—')}<br><strong>Người lập:</strong> ${esc(x.actor)}</p>
            ${x.canRequest ? `<a class="btn btn-outline-primary" href="${requestLink(x.id,data.shiftId)}" target="_blank" rel="noopener">${x.pendingRequestId ? 'Xem yêu cầu chờ duyệt' : 'Yêu cầu sửa / hủy phiếu'}</a>` : '<p class="recon-help">Phiếu giữ lại để đối chiếu lịch sử.</p>'}`,row.attention,x.cancelled);
        if (tab === 'refunds') return item(x.number,`${date(x.createdAtUtc)} · Đơn #${x.orderId}`, [amount('Hoàn tiền',x.refundTotal),amount('Hoàn vào cọc',x.depositRestored)],
            [badge(status(x.status)),...flags.map(f => badge(f,'warning'))], `<p><strong>Lý do:</strong> ${esc(x.reason)}<br>${esc(x.note || '')}</p>${paymentTable(x.payments)}<div class="recon-item-actions">${orderLink(x.orderId)}</div>`,row.attention);
        if (tab === 'other') {
            const name = { 'Deposit:Receive':'Nhận cọc', 'Deposit:Refund':'Hoàn cọc', 'Deposit:Apply':'Sử dụng cọc', 'Deposit:Return':'Khôi phục cọc do trả hàng', 'Deposit:Void':'Khôi phục cọc do hủy đơn', DebtCollection:'Thu công nợ' }[x.kind] || x.kind;
            return item(`${name} #${x.id}`,`${date(x.createdAtUtc)} · ${x.customer || 'Chưa lưu thông tin khách'}`,[amount('Số tiền',x.amount)], [badge(method(x.method))],
                `<p><strong>Khách hàng:</strong> ${esc(x.customer || '—')}<br><strong>Người ghi nhận:</strong> ${esc(x.actor)}<br><strong>Nội dung:</strong> ${esc(x.note || '—')}<br><strong>Mã giao dịch:</strong> ${esc(x.reference || '—')}</p>
                ${x.kind==='Deposit:Receive'&&(x.canRequest||x.pendingRequestId)?`<a class="btn btn-outline-primary" href="/admin/pos-shift/requests?tab=payment&shiftId=${Number(data.shiftId)}&${x.pendingRequestId?'requestId='+Number(x.pendingRequestId):'entryId='+Number(x.id)}" target="_blank" rel="noopener">${x.pendingRequestId?'Xem yêu cầu chờ duyệt':'Đổi phương thức nhận cọc'}</a>`:''}
                ${(x.allocations || []).length ? '<h3>Tiền thu phân bổ cho từng đơn</h3><div class="recon-table-wrap"><table class="recon-table"><thead><tr><th>Đơn</th><th>Số tiền thu</th><th>Chi tiết</th></tr></thead><tbody>'+x.allocations.map(a=>`<tr><td>${esc(a.number)}</td><td class="money">${money(a.amount)}</td><td>${orderLink(a.orderId)}</td></tr>`).join('')+'</tbody></table></div>' : ''}
                <p class="recon-help">Nhận/hoàn cọc và thu công nợ bằng tiền mặt đã được phản ánh trong phiếu thu/chi. Sử dụng hoặc khôi phục cọc là thay đổi số dư cọc.</p>${x.orderId ? orderLink(x.orderId) : ''}`);
        }
        if (tab === 'qrs') return item(`QR #${x.id} · ${x.code}`,`${date(x.createdAtUtc)} · Đơn #${x.orderId}`, [amount('Số tiền QR',x.amount)],
            [badge(status(x.status),row.attention ? 'warning' : ''),badge(x.confirmation)], `<p>${esc(x.reviewReason || 'Chưa có ghi chú cần kiểm tra.')}</p>
            <p>${x.paymentId ? 'Đã liên kết khoản thanh toán #'+Number(x.paymentId) : 'QR chưa liên kết khoản thanh toán đã lưu.'}</p>${orderLink(x.orderId)}`,row.attention);
        if(x.kind === 'Payment') return item(x.depositEntryId?`Đổi phương thức cọc #${x.depositEntryId}`:`Sửa thanh toán · Đơn #${x.orderId}`,`${date(x.createdAtUtc)} · ${x.actor}`,
            [amount('Giữ nguyên tiền nhận',x.amount),amount('Thay đổi quỹ tiền mặt khi duyệt',x.expectedDelta)],[badge(status(x.status),row.attention ? 'warning' : '')],
            `<p><strong>Trước:</strong> ${esc(method(x.oldMethod))} · ${esc(x.oldReference || '—')}<br><strong>Đề nghị:</strong> ${esc(method(x.newMethod))} · ${esc(x.newReference || '—')}<br><strong>Lý do:</strong> ${esc(x.reason)}</p>
            <p>${x.reviewer ? 'Người xử lý: '+esc(x.reviewer)+' · '+esc(date(x.reviewedAtUtc)) : 'Chờ quản lý xử lý'}<br>${esc(x.reviewNote || '')}</p>
            <p class="recon-help">Yêu cầu chờ duyệt chưa thay đổi phương thức và số tiền của ca.</p><a href="/admin/pos-shift/requests?tab=payment&requestId=${Number(x.id)}&shiftId=${Number(data.shiftId)}" target="_blank" rel="noopener">Xem yêu cầu sửa thanh toán</a>`,row.attention);
        return item(`Yêu cầu #${x.id} · ${x.isCancellation ? 'Hủy' : 'Sửa'} phiếu #${x.transactionId}`,`${date(x.createdAtUtc)} · ${x.actor}`,
            [amount('Thay đổi tiền dự kiến khi duyệt',x.expectedDelta)], [badge(status(x.status),row.attention ? 'warning' : '')],
            `<p><strong>Trước:</strong> ${esc(x.beforeType === 'CashIn' ? 'Thu' : 'Chi')} ${money(x.beforeAmount)}<br><strong>Đề nghị:</strong> ${x.isCancellation ? 'Hủy phiếu' : esc(x.afterType === 'CashIn' ? 'Thu' : 'Chi')+' '+money(x.afterAmount)}<br><strong>Lý do:</strong> ${esc(x.reason)}</p>
            <p>${x.reviewer ? 'Người duyệt: '+esc(x.reviewer)+' · '+esc(date(x.reviewedAtUtc)) : 'Chưa có người duyệt'}<br>${esc(x.reviewNote || '')}</p>
            <p class="recon-help">Yêu cầu chờ duyệt chưa thay đổi số tiền dự kiến của ca.</p>
            <a href="${requestLink(x.transactionId,data.shiftId)}" target="_blank" rel="noopener">Xem lịch sử yêu cầu của phiếu</a>`,row.attention);
    }
    function init() {
        const root = document.getElementById('shiftReconciliation');
        if (!root) return;
        const $ = id => document.getElementById(id), query = new URLSearchParams(location.search);
        let data, draft, tab = 'orders', page = 1, sequence = 0, controller;
        const pageSize = 25;
        function actualCash() { return draft ? draft.actual : data.actualCash; }
        function selectTab(next, scope = null) {
            tab = next;page = 1;
            if (scope) $('reconScope').value = scope;
            $('reconTabs').querySelectorAll('button').forEach(b => { b.classList.toggle('active',b.dataset.tab === tab);b.setAttribute('aria-pressed',b.dataset.tab === tab ? 'true' : 'false'); });
            if(tab==='qrs'||tab==='adjustments') $('reconTabs').querySelector('.recon-more-tabs').open=true;
            renderList();
        }
        function renderList() {
            if (!data) return;
            const rows = filterRows(rowsFor(data,tab),{query:$('reconSearch').value,day:$('reconDay').value,scope:$('reconScope').value});
            const pages = Math.max(1,Math.ceil(rows.length/pageSize));page = Math.min(page,pages);
            $('reconList').innerHTML = rows.slice((page-1)*pageSize,page*pageSize).map(r => renderRow(data,tab,r)).join('') ||
                '<div class="recon-empty">Không có giao dịch phù hợp bộ lọc.</div>';
            $('reconPageInfo').textContent = rows.length ? `${(page-1)*pageSize+1}–${Math.min(page*pageSize,rows.length)} / ${rows.length} giao dịch` : '0 giao dịch';
            $('reconPrev').disabled = page <= 1;$('reconNext').disabled = page >= pages;
        }
        function render() {
            const actual = actualCash(), diff = actual == null ? null : actual-data.expectedCash;
            $('reconShift').textContent = data.shiftCode + ' · ' + status(data.status);
            $('reconIdentity').textContent = `${data.ownerName} · ${data.terminalName}`;
            $('reconPeriod').textContent = `${date(data.openedAtUtc)} → ${data.closedAtUtc ? date(data.closedAtUtc) : 'Đang hoạt động'}`;
            $('reconUpdated').textContent = 'Cập nhật '+date(data.asOfUtc);
            $('reconExpected').textContent = money(data.expectedCash);
            $('reconActual').textContent = actual == null ? 'Chưa kiểm đếm' : money(actual);
            $('reconCountSource').textContent = draft ? 'Bảng kiểm đếm đang lưu, ca chưa đóng' : actual != null ? 'Tiền thực đếm đã lưu khi đóng ca' : 'Kiểm đếm từ màn hình đóng ca';
            $('reconDifference').textContent = diff == null ? '—' : (diff < 0 ? 'Thiếu ' : diff > 0 ? 'Thừa ' : '')+money(Math.abs(diff));
            $('reconDifferenceNote').textContent = diff === 0 ? 'Khớp quỹ theo số tiền thực đếm' : diff == null ? 'Chưa có tiền thực đếm để đối chiếu' : 'Đối chiếu các giao dịch bên dưới';
            $('reconDifferenceCard').className = 'recon-card'+(diff == null ? '' : diff < 0 ? ' is-short' : diff > 0 ? ' is-over' : ' is-match');
            $('reconReturn').textContent = data.canReturnToClose ? 'Quay lại đóng ca' : 'Về ca POS';
            $('reconReturn').href = data.canReturnToClose ? `/admin/pos-shift?resumeClose=1&shiftId=${data.shiftId}` : '/admin/pos-shift';
            $('reconRequests').href = `/admin/pos-shift/requests?tab=cash&shiftId=${data.shiftId}`;
            if($('reconPaymentRequests')) $('reconPaymentRequests').href = `/admin/pos-shift/requests?tab=payment&shiftId=${data.shiftId}`;
            const parts = [['Tiền đầu ca',data.openingCash,'opening'],['+ Tiền mặt bán hàng',data.cashSales,'orders'],['+ Thu tiền mặt ngoài đơn',data.cashIn,'cash'],['− Chi tiền mặt ngoài đơn',data.cashOut,'cash'],['− Hoàn tiền mặt',data.cashRefunds,'refunds']];
            $('reconFormula').innerHTML = parts.map(([label,value,target]) => `<button data-formula-tab="${target}"><span>${label} ↗</span><strong>${money(value)}</strong></button>`).join('')+
                `<button class="recon-formula-total" data-formula-tab="orders"><span>= Tiền mặt dự kiến</span><strong>${money(data.expectedCash)}</strong></button>`;
            const delta = data.detailExpectedCash-data.expectedCash;
            $('reconDataCheck').className = 'recon-data-check'+(delta !== 0 || data.needsReconciliation ? ' is-warning' : '');
            $('reconDataCheck').hidden = delta === 0 && !data.needsReconciliation;
            $('reconDataCheck').textContent = delta !== 0 ? `Tính từ chi tiết giao dịch: ${money(data.detailExpectedCash)}. Chênh với số tổng ca ${money(delta)}; làm mới và kiểm tra lại các khoản đã chốt, thu/chi, hoàn tiền.` :
                data.needsReconciliation ? 'Ca có điều chỉnh sau khi đóng, đang cần quản lý đối soát lại.' : 'Tổng chi tiết giao dịch khớp với tiền dự kiến của ca.';
            const denoms = type => data.denominations.filter(x => x.type === type).map(x => `<span>${money(x.value)} × ${x.quantity} = ${money(x.amount)}</span>`).join('');
            $('reconDenominations').innerHTML = `<p><strong>Đầu ca:</strong> ${money(data.openingCash)}<br>${esc(data.openNote || '')}</p><div class="recon-denoms">${denoms('Opening') || '<span>Chưa lưu bảng mệnh giá đầu ca.</span>'}</div>`+
                (draft ? `<p><strong>Đang kiểm đếm:</strong> ${money(draft.actual)}<br>${esc(draft.note || '')}</p><div class="recon-denoms">${draft.denominations.filter(x=>x.quantity>0).map(x=>`<span>${money(x.value)} × ${x.quantity}</span>`).join('') || '<span>Nhập tổng tiền trực tiếp.</span>'}</div>` : `<p><strong>Cuối ca:</strong> ${data.actualCash == null ? 'Chưa đóng ca' : money(data.actualCash)}</p><div class="recon-denoms">${denoms('Closing')}</div>`);
            const counts = ['orders','cash','refunds','other','qrs','adjustments'].map(t => [t,rowsFor(data,t)]);
            $('reconTabs').querySelectorAll('button').forEach(b => b.querySelector('span').textContent = counts.find(([t])=>t===b.dataset.tab)[1].length);
            const attention = [];
            counts.forEach(([t,rows]) => {const n = rows.filter(r=>r.attention).length;if(n) attention.push({tab:t,scope:'attention',title:`${n} ${ {orders:'đơn có điểm cần đối chiếu',cash:'phiếu thu/chi có thay đổi',refunds:'phiếu hoàn chưa hoàn tất',other:'khoản cọc/công nợ',qrs:'QR cần kiểm tra',adjustments:'yêu cầu chờ duyệt'}[t] }`,note:'Mở danh sách và xem chi tiết từng giao dịch.'});});
            if (diff) {
                const matching = data.orders.filter(o => o.cashReceived === Math.abs(diff) || o.bankReceived === Math.abs(diff) || o.grandTotal === Math.abs(diff));
                if(matching.length) attention.unshift({tab:'orders',scope:'all',query:String(Math.abs(diff)),title:`${matching.length} đơn có số tiền bằng mức lệch quỹ`,note:'Có thể kiểm tra trước; trùng số tiền chưa xác định được nguyên nhân.'});
            }
            $('reconAttentionCount').textContent = attention.length+' nhóm';
            $('reconAttention').innerHTML = attention.map(a=>`<button data-attention-tab="${a.tab}" data-scope="${a.scope}" data-query="${esc(a.query || '')}"><strong>${esc(a.title)} ↗</strong></button>`).join('') ||
                '<div class="is-clear">Đối chiếu từng khoản với tiền mặt và giao dịch ngân hàng.</div>';
            const selectedDay = $('reconDay').value;
            const days = [...new Set(counts.flatMap(([,rows])=>rows.map(r=>day(r.when))).filter(Boolean))].sort().reverse();
            $('reconDay').innerHTML = '<option value="">Toàn bộ ca</option>'+days.map(d=>`<option value="${d}">${d === day(new Date()) ? 'Hôm nay · ' : ''}${d.split('-').reverse().join('/')}</option>`).join('');
            if(days.includes(selectedDay)) $('reconDay').value = selectedDay;
            selectTab(tab);$('reconContent').hidden = false;
        }
        async function load() {
            const request = ++sequence;controller?.abort();controller = new AbortController();
            const abort = controller, timeout = setTimeout(()=>abort.abort(),20000);
            root.setAttribute('aria-busy','true');$('reconRefresh').disabled = true;$('reconError').hidden = true;
            try {
                const requested = query.get('shiftId');
                const response = await fetch('/admin/pos-shift/reconciliation/data'+(requested ? '?shiftId='+encodeURIComponent(requested) : ''),{signal:abort.signal,cache:'no-store'});
                const result = await response.json();
                if(!response.ok) throw Error(result.message || 'Không tải được ca cần đối soát.');
                if(request !== sequence) return;
                data = result;draft = null;
                if(Number(root.dataset.userId) === data.ownerId && data.status === 'Open') {
                    try {draft = window.PosShiftCloseDraft.read({storeId:data.storeId,userId:data.ownerId,terminalId:data.terminalId,shiftId:data.shiftId});}
                    catch(error) {$('reconError').textContent = error.message;$('reconError').hidden = false;}
                }
                query.set('shiftId',data.shiftId);history.replaceState(null,'',location.pathname+'?'+query.toString());
                render();
            } catch(error) {
                if(request === sequence) {$('reconError').textContent = error.name === 'AbortError' ? 'Chưa tải được dữ liệu trong 20 giây. Bấm Làm mới để thử lại.' : error.message;$('reconError').hidden = false;}
            } finally {clearTimeout(timeout);if(request === sequence){root.setAttribute('aria-busy','false');$('reconRefresh').disabled = false;$('reconLoading').hidden = true;}}
        }
        $('reconRefresh').addEventListener('click',load);
        $('reconTabs').addEventListener('click',e=>{const b=e.target.closest('[data-tab]');if(b) selectTab(b.dataset.tab);});
        $('reconFormula').addEventListener('click',e=>{const b=e.target.closest('[data-formula-tab]');if(!b)return;if(b.dataset.formulaTab === 'opening'){$('reconDenominations').parentElement.open=true;return;}$('reconDay').value='';$('reconSearch').value='';selectTab(b.dataset.formulaTab,'all');$('reconTabs').scrollIntoView({block:'start',behavior:'smooth'});});
        $('reconAttention').addEventListener('click',e=>{const b=e.target.closest('[data-attention-tab]');if(b){$('reconDay').value='';$('reconSearch').value=b.dataset.query || '';selectTab(b.dataset.attentionTab,b.dataset.scope);$('reconTabs').scrollIntoView({block:'start',behavior:'smooth'});}});
        ['reconSearch','reconDay','reconScope'].forEach(id=>$(id).addEventListener(id === 'reconSearch' ? 'input' : 'change',()=>{page=1;renderList();}));
        $('reconPrev').addEventListener('click',()=>{page--;renderList();});$('reconNext').addEventListener('click',()=>{page++;renderList();});
        load();
    }
    return { init, orderFlags, rowsFor, filterRows, renderRow, esc };
});
