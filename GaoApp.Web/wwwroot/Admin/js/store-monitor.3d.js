(function () {
    'use strict';
    const root=document.getElementById('gao-store-wall');
    if(!root || !root.dataset.sceneUrl) return;
    const q=id=>root.querySelector(id);
    const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const number=n=>Number(n||0).toLocaleString('vi-VN',{maximumFractionDigits:3});
    const money=n=>number(n)+' ₫';
    const stamp=value=>value?new Date(typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value) && !/(Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value + 'Z' : value).toLocaleTimeString('vi-VN',{timeZone:'Asia/Ho_Chi_Minh',hour:'2-digit',minute:'2-digit',second:'2-digit'}):'—';
    const names={pos:'Tính tiền',receipt:'Nhập hàng',warehouse:'Kiểm kho',label:'In tem',review:'Duyệt phiếu',delivery:'Giao hàng',idle:'Đi trong tiệm'};
    const htmlCache=new WeakMap();
    function setHTML(element,value){if(htmlCache.get(element)!==value){element.innerHTML=value;htmlCache.set(element,value);}}
    let scene=null,load=null,view='3d',selection=null,revoked=false;
    let data={people:[],employees:[],events:[],checkouts:[],delivery:null},state={connected:false,motion:true,theme:'light'},offset=0;
    try { if(localStorage.getItem('gao.monitor.view')==='2d')view='2d'; } catch(_) {}
    function now(){return Date.now()+offset;}
    function fit() {
        const scale=Math.min(1,window.innerWidth/1366,window.innerHeight/768);
        root.style.setProperty('--wm3-scale',String(scale));
        root.style.setProperty('--wm3-design-width',window.innerWidth/scale+'px');
        root.style.setProperty('--wm3-design-height',window.innerHeight/scale+'px');
    }
    function shownOrders(){return window.GaoDeliveryWall?.visibleOrders(data.delivery,now()) || [];}
    function button(id,title,sub,tail='') {
        return '<button type="button" data-select="'+esc(id)+'"><span><strong>'+esc(title)+'</strong><small>'+esc(sub)+'</small></span><span>'+esc(tail)+'</span></button>';
    }
    function scanFor(cart) {
        if(cart.state!=='Draft' || !cart.orderId)return null;
        return (data.events || []).filter(e=>e.module==='pos' && e.posScan && e.terminal===cart.terminal &&
            e.workKey==='order:'+cart.orderId && cart.lines?.some(l=>l.lineId===e.posScan.lineId))
            .sort((a,b)=>new Date(typeof b.occurredAtUtc === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(b.occurredAtUtc) ? b.occurredAtUtc + 'Z' : b.occurredAtUtc)-new Date(typeof a.occurredAtUtc === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(a.occurredAtUtc) ? a.occurredAtUtc + 'Z' : a.occurredAtUtc)||b.id-a.id)[0] || null;
    }
    function scanTitle(event) {
        const recent=state.connected && now()-new Date(typeof event.occurredAtUtc === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(event.occurredAtUtc) ? event.occurredAtUtc + 'Z' : event.occurredAtUtc).getTime()<12000;
        return event.action==='POS.AddItem'?(recent?'Vừa thêm sản phẩm':'Lần thêm hàng gần nhất'):(recent?'Vừa quét sản phẩm':'Lần quét gần nhất');
    }
    function scanDetail(event) {
        if(!event)return '<p class="wm3-note">Chưa ghi nhận lần quét/thêm hàng trong phiên giám sát này.</p>';
        const item=event.posScan;
        return '<section class="wm3-last-scan" aria-label="Sản phẩm vừa quét"><small>'+scanTitle(event)+' · '+stamp(event.occurredAtUtc)+
            '</small><strong>'+esc(item.itemName)+'</strong><p>Thêm <b>'+number(item.addedQuantity)+' '+esc(item.unitName)+
            '</b> · Đơn giá <b>'+money(item.unitPrice)+'</b></p><p class="wm3-note">Dòng hàng sau lần thêm: '+number(item.quantity)+
            ' '+esc(item.unitName)+' · '+money(item.lineTotal)+'</p></section>';
    }
    function deliveryButton(order) {
        const returned=order.returnedAtUtc && ['Delivering','AwaitingReconciliation','ReadyToSettle','CancellationPending'].includes(order.state);
        const status=returned?'Đã trở về · chờ đối chiếu':window.GaoDeliveryWall?.states[order.state]?.[0] || 'Trạng thái đã lưu';
        return button('delivery:'+order.orderId,'Giao cho '+(order.recipient || 'khách hàng'),
            status+' · '+order.code,'');
    }
    function deliveryList() {
        const orders=shownOrders();
        return '<h3 class="wm3-section-title">Đơn giao hàng đã lưu · '+orders.length+'</h3>'+
            '<p class="wm3-note">Phiếu chưa kết thúc vẫn được hiển thị. Chọn phiếu để xem trạng thái và lần cập nhật cuối.</p>'+
            '<div class="wm3-list">'+orders.map(deliveryButton).join('')+'</div>';
    }
    function checkout(q) {
        const lines=q.lines || [];
        const scan=scanFor(q);
        const states={Draft:'Đang tính tiền',OnHold:'Đơn đang giữ',Completed:'Đã chốt đơn',Cancelled:'Đã hủy',Voided:'Đã hủy sau chốt',Refunded:'Đã hoàn đơn'};
        return '<strong>'+esc(q.name || q.terminal)+'</strong><p class="wm3-note">'+esc(q.terminal)+' · '+esc(q.code || (q.orderId?'Đơn #'+q.orderId:'Chưa có đơn'))+'</p>'+
            '<span class="wm3-badge">'+esc(states[q.state] || 'Quầy chờ')+'</span>'+
            (q.customerName?'<p>Khách hàng: <strong>'+esc(q.customerName)+'</strong></p>':'')+
            (q.state==='Draft' && lines.length?scanDetail(scan):'')+
            (lines.length?'<table class="wm3-goods"><thead><tr><th>Sản phẩm / số lượng</th><th>Đơn giá</th><th>Thành tiền</th></tr></thead><tbody>'+
            lines.map(l=>'<tr'+(scan?.posScan.lineId===l.lineId?' class="wm3-scanned-row"':'')+'><td>'+esc(l.itemName)+'<br><span class="wm3-note">'+number(l.quantity)+' '+esc(l.unitName)+'</span></td><td>'+money(l.unitPrice)+'</td><td>'+money(l.lineTotal)+'</td></tr>').join('')+'</tbody></table>':'<p class="wm3-note">Chưa có mặt hàng trong giỏ.</p>')+
            '<div class="wm3-money"><p><span>Tổng tiền</span><strong>'+money(q.grandTotal)+'</strong></p><p><span>Đã thanh toán</span><strong>'+money(q.paidTotal)+'</strong></p><p><span>Còn phải trả</span><strong>'+money(q.balanceDue)+'</strong></p><p><span>Tiền thối</span><strong>'+money(q.changeDue)+'</strong></p></div>';
    }
    function overview() {
        return '<p class="wm3-note">Chọn nhân viên, quầy hoặc chuyến giao để xem chi tiết. Khách đi lựa hàng và đường đi là mô phỏng; số liệu nghiệp vụ lấy từ GaoApp.</p>'+
            '<h3 class="wm3-section-title">Quầy tính tiền</h3>'+
            '<div class="wm3-list">'+(data.checkouts||[]).map(q=>button('terminal:'+q.terminal,q.name || q.terminal,q.orderId?'Đơn #'+q.orderId+' · '+(q.lines||[]).length+' mặt hàng':'Quầy chờ',q.orderId?money(q.grandTotal):'')).join('')+
            '</div>'+deliveryList();
    }
    function details() {
        let html='',title='Hoạt động hiện tại';
        if(!selection)html=overview();
        else if(selection.startsWith('terminal:')) {
            const terminal=selection.slice(9),cart=(data.checkouts||[]).find(q=>q.terminal===terminal);
            title='Chi tiết quầy';html=cart?checkout(cart):'<p>Quầy không còn hoạt động.</p>';
        } else if(selection.startsWith('employee:')) {
            const id=Number(selection.slice(9)),person=(data.employees||data.people||[]).find(p=>p.userId===id);
            const actor=scene?.world.employees.get(selection),work=actor?.work;
            title=person?.person || 'Nhân viên';
            html='<p><span class="wm3-badge">'+esc(actor?.caption || (person?'Đi trong tiệm':'Đã rời cửa hàng'))+'</span></p>';
            if(work)html+='<p><strong>'+esc(work.documentTitle || work.document || names[work.module])+'</strong></p>'+
                '<p class="wm3-note">'+esc(work.text || names[work.module])+'</p>'+(work.detail?'<p>'+esc(work.detail)+'</p>':'')+
                '<p class="wm3-note">Thao tác lúc '+stamp(work.occurredAtUtc || work.editedAtUtc)+'</p>';
            if(actor?.module==='pos') {
                const cart=(data.checkouts||[]).find(q=>q.terminal===work?.terminal);if(cart)html+=checkout(cart);
            }
            if(actor?.module==='delivery') {
                const orders=shownOrders().filter(o=>o.courierUserId===id || o.pickerUserId===id);
                html+='<div class="wm3-list">'+orders.map(deliveryButton).join('')+'</div>';
            }
        } else if(selection.startsWith('delivery:') || selection.startsWith('trip:')) {
            const isTrip=selection.startsWith('trip:'),id=Number(selection.split(':')[1]);
            const orders=shownOrders().filter(o=>isTrip?o.tripId===id:o.orderId===id);
            title=isTrip?'Chuyến giao':'Đơn giao';
            html=orders.length?window.GaoDeliveryWall.render({...data.delivery,orders},state.connected,now()):'<p>Đơn/chuyến không còn trong dữ liệu giám sát hiện tại.</p>';
            html+='<p class="wm3-note">Xe chạy trên đường mô phỏng trong thời gian giao hàng. Khi ghi nhận giao xong hoặc trở về, xe về tiệm và nhân viên xuống xe đi vào. Trạng thái giao/nhận theo dữ liệu đã lưu.</p>';
        } else if(selection.startsWith('zone:')) {
            const module=selection.slice(5);title=names[module] || 'Khu lựa hàng';
            const employees=scene?[...scene.world.employees.values()].filter(a=>a.module===module):[];
            html='<div class="wm3-list">'+employees.map(a=>button(a.id,a.name,a.caption,a.work?.document || '')).join('')+'</div>';
            if(module==='pos')html+=(data.checkouts||[]).map(checkout).join('');
            else if(module==='delivery')html+=deliveryList();
            if(!html || html==='<div class="wm3-list"></div>')html='<p class="wm3-note">Chưa có nhân viên thao tác tại khu này.</p>';
        } else html='<p class="wm3-note">Khách đang lựa hàng trong mô phỏng.</p>';
        q('#wm3-detail-title').textContent=title;
        setHTML(q('#wm3-detail'),html);
        q('#wm3-overview').hidden=!selection;
    }
    function render() {
        q('#wm3-status').textContent=revoked?'Phiên đăng nhập hoặc quyền xem đã thay đổi':state.connected?
            'Đang cập nhật · '+(state.motion?'Mô phỏng đang chạy':'Hiệu ứng đã tắt'):'Mất kết nối · Dữ liệu lần cuối, mô phỏng đã dừng';
        details();
        const employees=data.employees || data.people || [];
        q('#wm3-team-count').textContent=String(new Set(employees.map(p=>p.userId)).size);
        if(view==='3d') {
            q('#wm-layout-hint').textContent='Chọn nhân viên / quầy để xem chi tiết · Đường đi là mô phỏng';
            const actors=scene?[...scene.world.employees.values()].filter(a=>!a.exiting):[];
            const active=actors.filter(a=>a.module!=='idle').length;
            q('#wm-summary').innerHTML='<div class="wm-summary-item"><span class="wm-summary-icon">◉</span><strong>'+
                (state.connected?employees.length:'—')+'</strong><div>Nhân viên online</div></div><div class="wm-summary-item"><span class="wm-summary-icon">▥</span><strong>'+
                (state.connected?active:'—')+'</strong><div>Nhân viên đang làm việc</div></div><div class="wm-summary-item"><span class="wm-summary-icon">↗</span><strong>'+
                data.events.length+'</strong><div>Cập nhật đã lưu</div></div>';
        }
        setHTML(q('#wm3-team'),employees.map(p=>{
            const id='employee:'+p.userId,actor=scene?.world.employees.get(id);
            return button(id,p.person,actor?.caption || 'Đi trong tiệm',selection===id?'●':'');
        }).join('') || '<p class="wm3-note">Chưa có nhân viên online.</p>');
        setHTML(q('#wm3-feed'),(data.events||[]).slice(0,5).map(e=>'<li><strong>'+esc(e.person)+'</strong> '+esc(e.text)+
            (e.posScan?'<br><b>'+esc(e.posScan.itemName)+'</b> · +'+number(e.posScan.addedQuantity)+' '+esc(e.posScan.unitName)+' · '+money(e.posScan.unitPrice):e.detail?'<br>'+esc(e.detail):'')+
            (e.document?'<br><span class="wm3-note">'+esc(e.document)+'</span>':'')+'<time>'+stamp(e.occurredAtUtc)+'</time></li>').join(''));
    }
    function select(id) {
        selection=id;scene?.select(id);
        if(id.startsWith('zone:'))scene?.setView(id.slice(5));
        details();render();
    }
    async function initialize() {
        if(scene || load || revoked)return;
        q('#wm3-loading').hidden=false;q('#wm3-loading').textContent='Đang tải mô hình cửa hàng…';
        load=(async()=>{
            try {
                const module=await import(root.dataset.sceneUrl);
                if(revoked)return;
                scene=module.createLiveStoreScene(q('#wm3-canvas'),q('#wm3-labels'),{
                    onSelect:select,onFps:fps=>q('#wm3-fps').textContent='3D · '+fps+' FPS',
                    onError:message=>fallback(message)
                });
                if(data.serverTimeUtc)scene.apply(data);
                scene.status(state);scene.visible(view==='3d');q('#wm3-loading').hidden=true;render();
            } catch(error) { fallback(error.message || 'Không tải được 3D. Đã chuyển về 2D.'); }
            finally { load=null; }
        })();
        await load;
    }
    function fallback(message) {
        scene?.dispose();scene=null;q('#wm3-labels').replaceChildren();
        setView('2d',false);q('#wm-sync').textContent=message;q('#wm-view').title=message;
        q('#wm3-loading').textContent=message;
    }
    function setView(next,save=true) {
        view=next;root.dataset.view=view;q('#wm-view').textContent=view==='3d'?'Chuyển sang 2D':'Mở mô hình 3D';
        q('#wm-view').setAttribute('aria-pressed',String(view==='3d'));fit();scene?.visible(view==='3d');
        if(view==='3d')initialize();
        window.dispatchEvent(new Event('gao:monitor-view'));
        if(save)try{localStorage.setItem('gao.monitor.view',view);}catch(_){}
    }
    window.addEventListener('gao:monitor-snapshot',event=>{
        if(revoked)return;
        data=event.detail;offset=new Date(typeof data.serverTimeUtc === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(data.serverTimeUtc) ? data.serverTimeUtc + 'Z' : data.serverTimeUtc).getTime()-Date.now();
        scene?.apply(data);render();
    });
    window.addEventListener('gao:monitor-state',event=>{state=event.detail;scene?.status(state);render();});
    window.addEventListener('gao:monitor-revoked',()=>{
        revoked=true;selection=null;data={people:[],employees:[],events:[],checkouts:[],delivery:null};scene?.clear();scene?.visible(false);
        q('#wm3-loading').textContent='Quyền xem hoặc phiên đăng nhập đã thay đổi. Vui lòng đăng nhập lại.';q('#wm3-loading').hidden=false;render();
    });
    q('#wm-view').addEventListener('click',()=>setView(view==='3d'?'2d':'3d'));
    q('#wm3-overview').addEventListener('click',()=>{selection=null;scene?.select(null);scene?.setView('overview');render();});
    q('#wm3-zoom-in').addEventListener('click',()=>scene?.zoom(-3));
    q('#wm3-zoom-out').addEventListener('click',()=>scene?.zoom(3));
    q('#wm-3d').addEventListener('click',event=>{
        const item=event.target.closest('[data-select]');if(item){select(item.dataset.select);return;}
        const button=event.target.closest('[data-view-area]');
        if(button){
            const area=button.dataset.viewArea;
            if(area==='overview'){selection=null;scene?.select(null);}
            else if(area==='pos') {
                const carts=[...(data.checkouts||[])].filter(q=>q.orderId && q.lines?.length && q.state==='Draft');
                carts.sort((a,b)=>new Date(scanFor(b)?.occurredAtUtc || 0)-new Date(scanFor(a)?.occurredAtUtc || 0));
                selection=carts.length?'terminal:'+carts[0].terminal:'zone:pos';scene?.select(selection);
            } else {selection='zone:'+area;scene?.select(selection);}
            scene?.setView(area);q('.wm3-views').querySelectorAll('button').forEach(b=>b.setAttribute('aria-pressed',String(b===button)));render();
        }
    });
    window.addEventListener('resize',fit);document.addEventListener('fullscreenchange',fit);
    window.addEventListener('pagehide',()=>{scene?.dispose();scene=null;});
    setInterval(()=>{if(view==='3d'&&!document.hidden)render();},1000);
    // Expose read-only scene inspection for local browser acceptance tests.
    window.GaoStoreLiveMonitor={get scene(){return scene;},get view(){return view;}};
    fit();setView(view,false);render();
})();
