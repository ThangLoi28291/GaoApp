(function () {
    'use strict';
    const root = document.getElementById('promotionLabels');
    if (!root) return;
    const el = id => document.getElementById(id);
    const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const money = value => Number(value).toLocaleString('vi-VN') + ' đ';
    const storageKey = `gao-label-client-v1:${root.dataset.storeId}`, pendingKey = storageKey + ':pending';
    const helperUrl = 'http://127.0.0.1:17881', protocol = 'gao-label-client-v1';
    let products = [], lines = [], page = 1, total = 0, sequence = 0, busy = false, searching = false;
    let timer, previewTimer, printerNames = [], previewProduct, previewUrl, previewSequence = 0;
    let connected = false, dirty = true, templateId = '', pending, importedTemplates = [], importedPrinters = [];
    let a4Supported = false, sheetPage = 1, sheetPages = 1;
    let config = { templates:[], printers:[], token:'' };
    function notice(message, error = false) {
        const box = el('plMessage'); box.textContent = message; box.dataset.error = String(error); box.hidden = false;
    }
    async function api(path, body) {
        const response = await fetch(root.dataset.api + path, {
            method: body ? 'POST' : 'GET', credentials: 'same-origin', cache: 'no-store',
            headers: body ? { 'Content-Type':'application/json', RequestVerificationToken: root.querySelector('input[name="__RequestVerificationToken"]').value } : {},
            body: body ? JSON.stringify(body) : undefined
        });
        const data = await response.json().catch(() => null);
        if (!response.ok) throw new Error(data?.message || data?.detail || (response.status === 403 ? 'Bạn chưa có quyền quản lý in tem khuyến mãi.' : 'Không tải được dữ liệu. Tải lại trang và thử lại.'));
        return data;
    }
    const defaults = () => ({ name:'Tem khuyến mãi 50 × 30', widthMm:50, heightMm:30, columns:1, columnGapMm:2, rowGapMm:2,
        leftMarginMm:1, rightMarginMm:1, quantityMode:'one', printerId:null, showPrintButton:true, barcodeFormat:'AUTO', layout:'promotion-shelf',
        fontSize:8, showName:true, showBarcode:true, showBarcodeText:true, showUnit:true, showPrice:true });
    function fields(form) {
        const result = {};
        form.querySelectorAll('[name]').forEach(x => result[x.name] = x.type === 'checkbox' ? x.checked : x.type === 'number' || x.name === 'dpi' ? Number(x.value) : x.value.trim());
        return result;
    }
    function fill(form, value) { form.querySelectorAll('[name]').forEach(x => { if(x.type === 'checkbox') x.checked = value[x.name] !== false; else x.value = value[x.name] ?? ''; }); }
    function paperSize(design) {
        const size=`${design.widthMm}x${design.heightMm}`;
        const a4=design.paperMode==='a4';
        el('plPaperPreset').value=a4?'a4':['50x30','80x50'].includes(size)?size:'custom';
        el('plPaperSize').textContent=a4?'A4 · 210 × 297 mm':`${design.widthMm} × ${design.heightMm} mm`;
        el('plA4Details').hidden=!a4;
        for(const name of ['widthMm','heightMm','columns','columnGapMm','rowGapMm','leftMarginMm','rightMarginMm'])el('plDesign').elements[name].readOnly=a4;
        el('plPrinterForm').elements.printableWidthMm.readOnly=a4;
        el('plPrinterForm').elements.dpi.disabled=a4 || busy;
        el('plWidthLabel').textContent=a4?'Khổ ngang A4 (mm)':'Vùng in (mm)';
        el('plTestPrint').textContent=a4?'In thử một tờ A4':'In thử một hàng';
        el('plPreview').alt=a4?'Xem toàn bộ tờ A4, xếp tem từ trái sang phải theo hàng':'Xem trước mẫu tem khuyến mãi và các cột trên cuộn';
    }
    function settings(validate = true) {
        const design = {...defaults(), ...fields(el('plDesign'))}, printer = {...fields(el('plPrinterForm')), windowsPrinterName:el('plPrinter').value};
        if(design.paperMode!=='a4')delete design.paperMode;
        const width = design.leftMarginMm + design.columns * design.widthMm + (design.columns - 1) * design.columnGapMm + design.rightMarginMm;
        if(validate) {
            if(!el('plDesign').reportValidity() || !el('plPrinterForm').reportValidity()) throw new Error('Kiểm tra các thông số mẫu và máy in.');
            if(!printer.windowsPrinterName) throw new Error('Kết nối rồi chọn máy in tem trên client.');
            if(design.paperMode==='a4') {
                if(width!==210 || design.widthMm!==90 || design.heightMm!==50 || design.columns!==2 || design.columnGapMm!==10 || design.rowGapMm!==5 || design.leftMarginMm!==10 || design.rightMarginMm!==10 || printer.dpi!==300 || printer.printableWidthMm!==210)throw new Error('Mẫu A4 cần tem 90 × 50 mm, 2 cột × 5 hàng và 300 DPI. Chọn lại khổ A4.');
            } else if(width > Math.min(108,printer.printableWidthMm)) throw new Error(`Tổng chiều rộng ${width} mm vượt vùng in máy tem. Giảm số cột hoặc lề.`);
            if(!printer.enabled) throw new Error('Máy in đang tắt trong cấu hình.');
        }
        return {design, printer, width, period:el('plPeriod').checked};
    }
    function markDirty() { dirty=true; el('plSaveState').textContent='Có thay đổi chưa lưu'; summary(); clearTimeout(previewTimer); previewTimer=setTimeout(()=>preview(),350); }
    function templateOptions() {
        el('plTemplate').replaceChildren(new Option('Mẫu mới',''), ...config.templates.map(t=>new Option(t.design.name,t.id)));
        el('plTemplate').value=templateId;
    }
    function loadPrinter(name) {
        const value=config.printers.find(x=>x.windowsPrinterName===name);
        fill(el('plPrinterForm'),value || {name:name || 'Máy in tem', dpi:203,printableWidthMm:108,offsetXmm:0,offsetYmm:0,enabled:true});
        if(el('plDesign').elements.paperMode.value==='a4') {
            el('plPrinterForm').elements.dpi.value=300;
            el('plPrinterForm').elements.printableWidthMm.value=210;
        } else if(Number(el('plPrinterForm').elements.printableWidthMm.value)>108)el('plPrinterForm').elements.printableWidthMm.value=108;
    }
    function loadTemplate(id) {
        templateId=id || ''; const saved=config.templates.find(t=>t.id===templateId);
        fill(el('plDesign'),saved?.design || defaults()); el('plPeriod').checked=saved?.period!==false;
        sheetPage=1;
        paperSize(saved?.design || defaults());
        if(saved?.printer) {
            if(![...el('plPrinter').options].some(x=>x.value===saved.printer))el('plPrinter').append(new Option(saved.printer,saved.printer));
            el('plPrinter').value=saved.printer; loadPrinter(saved.printer);
        }
        else loadPrinter(el('plPrinter').value);
        dirty=!saved; config.selected=templateId; el('plSaveState').textContent=dirty?'Chưa lưu':'Đã lưu'; templateOptions(); summary(); preview();
        el('plTemplateSettings').open=dirty; el('plPrinterSettings').open=dirty;
    }
    function saveSettings(copy = false) {
        const value=settings();
        if(!connected || !printerNames.includes(value.printer.windowsPrinterName)) throw new Error('Kết nối và kiểm tra máy in trên client trước khi lưu.');
        if(copy || !templateId) templateId=crypto.randomUUID();
        const entry={id:templateId,design:value.design,printer:value.printer.windowsPrinterName,period:value.period};
        config.templates=config.templates.filter(t=>t.id!==templateId).concat(entry);
        config.printers=config.printers.filter(p=>p.windowsPrinterName!==value.printer.windowsPrinterName).concat(value.printer);
        config.token=el('plToken').value.trim(); config.selected=templateId;
        localStorage.setItem(storageKey,JSON.stringify(config)); dirty=false; templateOptions(); summary(); el('plSaveState').textContent='Đã lưu';
        el('plTemplateSettings').open=false; el('plPrinterSettings').open=false;
        notice('Đã lưu mẫu và cấu hình máy in trên client này.');
    }
    async function helper(path, body, binary = false) {
        const token=el('plToken').value.trim();
        if(token.length<32) throw new Error('Nhập mã kết nối được tạo khi cài GaoApp Label Print Client.');
        const controller=new AbortController(), timeout=setTimeout(()=>controller.abort(),path==='/print'?60000:10000);
        try {
            const response=await fetch(helperUrl+path,{method:body?'POST':'GET',mode:'cors',targetAddressSpace:'loopback',credentials:'omit',cache:'no-store',redirect:'error',signal:controller.signal,
                headers:{Authorization:'Bearer '+token,...(body?{'Content-Type':'application/json'}:{})},body:body?JSON.stringify(body):undefined});
            if(binary && response.ok) return await response.blob();
            const data=await response.json().catch(()=>null);
            if(!response.ok || data?.ok!==true || data.protocol!==protocol) throw new Error(data?.message || 'Mã kết nối hoặc bộ in tem client không hợp lệ.');
            return data;
        } catch(e) {
            if(e instanceof TypeError || e.name==='AbortError') throw new Error('Chưa kết nối được GaoApp Label Print Client. Mở bộ kết nối trên máy này và cho phép trang truy cập mạng cục bộ khi trình duyệt yêu cầu.');
            throw e;
        } finally {clearTimeout(timeout);}
    }
    function validLines() {
        const count = lines.reduce((sum, line) => sum + line.quantity, 0);
        return lines.length > 0 && count > 0 && count <= 1000 && lines.every(x => !x.product.problem && Number.isInteger(x.quantity) && x.quantity > 0 && x.quantity <= 1000);
    }
    function summary() {
        const count = lines.reduce((sum, line) => sum + line.quantity, 0);
        const current=settings(false), a4=current.design.paperMode==='a4';
        el('plTotal').textContent = lines.length ? `${lines.length} đơn vị · ${Number.isFinite(count) ? count : '—'} tem${a4 && validLines() ? ` · ${Math.ceil(count/10)} tờ A4` : ''}` : 'Chưa chọn sản phẩm';
        el('plPrint').disabled = busy || searching || !validLines() || !connected || dirty || !!pending || a4 && !a4Supported;
        el('plPrint').textContent = busy ? 'Đang chuẩn bị / gửi in…' : 'In tem khuyến mãi';
        el('plAddPage').disabled = busy || searching || !products.some(x => !x.problem);
        el('plPrev').disabled = busy || searching || page <= 1;
        el('plNext').disabled = busy || searching || page * 24 >= total;
        for (const id of ['plCampaign','plSearch','plRefresh','plPrinter','plPeriod','plToken','plConnect','plSavePrinter','plTemplate','plImportTemplate','plNewTemplate','plCopyTemplate','plDeleteTemplate','plPreviewButton','plTestPrint','plCheckResult','plAllowNewPrint']) el(id).disabled = busy;
        el('plTestPrint').disabled=busy || !connected || dirty || !!pending || a4 && !a4Supported;
        el('plDesign').querySelectorAll('input,select').forEach(x=>x.disabled=busy);
        el('plPrinterForm').querySelectorAll('input,select').forEach(x=>x.disabled=busy);
        el('plUncertain').hidden=!pending;
        el('plPrinterForm').elements.dpi.disabled=a4 || busy;
        el('plA4ClientNotice').hidden=!(a4 && connected && !a4Supported);
        el('plSheetPrev').disabled=busy || sheetPage<=1;
        el('plSheetNext').disabled=busy || sheetPage>=sheetPages;
        el('plActivePrinter').textContent=current.printer.windowsPrinterName ? `${current.printer.windowsPrinterName} · ${current.printer.dpi} DPI · ${current.design.name}` : 'Chọn và lưu máy in trên client để in trực tiếp.';
        el('plRefresh').disabled = busy || !lines.length;
        root.querySelectorAll('[data-add],[data-remove],.pl-quantity').forEach(control => control.disabled = busy || (control.hasAttribute('data-add') && searching));
    }
    function labelProduct(product, period) {
        return {variantId:0,name:product.name,barcode:product.barcode,unit:product.unit,price:product.salePrice,receivedQuantity:0,
            promotion:{retailPrice:product.retailPrice,minimumQuantity:product.minimumQuantity,startAtUtc:product.startAtUtc,endAtUtc:product.endAtUtc,showPeriod:period}};
    }
    async function preview(product) {
        if (product) previewProduct = product;
        if (!previewProduct) previewProduct = { name:'Bộ bàn chải dây 3 chiếc', barcode:'4902430679640', unit:'Bộ', retailPrice:39000, salePrice:32100,
            minimumQuantity:1, startAtUtc:new Date().toISOString(), endAtUtc:new Date(Date.now()+20*86400000).toISOString() };
        const seq=++previewSequence, value=settings(false);
        paperSize(value.design);
        const a4=value.design.paperMode==='a4';
        el('plSheetPaging').hidden=!a4;
        el('plPreviewDimensions').textContent=a4?'A4 dọc · tem 90 × 50 mm · 2 cột × 5 hàng · ô dư để trống':`${value.design.widthMm} × ${value.design.heightMm} mm / tem · ${value.design.columns} cột · rộng cuộn ${value.width} mm`;
        try {
            const p=labelProduct(previewProduct,value.period);
            let image;
            if(a4) {
                if(lines.length && !validLines())throw new Error('Kiểm tra số tem và các sản phẩm trước khi xem mẫu A4.');
                const items=lines.length?lines.map(x=>({product:labelProduct(x.product,value.period),quantity:x.quantity})):[{product:p,quantity:5}];
                const count=items.reduce((sum,x)=>sum+x.quantity,0);
                sheetPages=Math.ceil(count/10);
                if(product && lines.length) {
                    let before=0;
                    for(const line of lines){if(line.product.conversionId===product.conversionId)break;before+=line.quantity;}
                    sheetPage=Math.floor(before/10)+1;
                }
                sheetPage=Math.min(Math.max(1,sheetPage),sheetPages);
                el('plSheetPage').textContent=`Tờ ${sheetPage}/${sheetPages}${lines.length?'':' · mẫu minh họa 5 tem'}`;
                summary();
                const body={payload:{design:value.design,printer:value.printer,items},page:sheetPage};
                if(connected && a4Supported && value.printer.windowsPrinterName)image=await helper('/preview-a4',body,true);
                else {
                    const response=await fetch(root.dataset.api+'preview-a4',{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json',RequestVerificationToken:root.querySelector('input[name="__RequestVerificationToken"]').value},body:JSON.stringify(body)});
                    if(!response.ok){const error=await response.json().catch(()=>null);throw new Error(error?.message || error?.detail || 'Không tạo được mẫu A4. Kiểm tra cấu hình và sản phẩm.');}
                    image=await response.blob();
                }
            } else if(connected) image=await helper('/preview',{design:value.design,printer:value.printer,product:p},true);
            else {
                const response=await fetch(root.dataset.labelApi+'preview',{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json',RequestVerificationToken:root.querySelector('input[name="__RequestVerificationToken"]').value},
                    body:JSON.stringify({design:value.design,product:p,dpi:value.printer.dpi,offsetXmm:value.printer.offsetXmm,offsetYmm:value.printer.offsetYmm})});
                if(!response.ok) {const error=await response.json().catch(()=>null);throw new Error(error?.message || 'Không tạo được mẫu. Kiểm tra khổ tem và mã vạch.');}
                image=await response.blob();
            }
            if(seq!==previewSequence) return;
            if(previewUrl)URL.revokeObjectURL(previewUrl); previewUrl=URL.createObjectURL(image); el('plPreview').src=previewUrl;
            el('plPreviewOpen').href=previewUrl;el('plPreviewOpen').hidden=false;
            el('plPreview').hidden=false; el('plPreviewStatus').hidden=true;
        } catch(e) {if(seq===previewSequence){el('plPreview').hidden=true;el('plPreviewOpen').hidden=true;el('plPreviewStatus').hidden=false;el('plPreviewStatus').textContent=e.message;}}
    }
    function renderLines() {
        el('plLines').innerHTML = lines.map(({product:p,quantity}) => `<tr data-line="${p.conversionId}"><td><button type="button" class="btn btn-link p-0 text-start" data-preview="${p.conversionId}">${esc(p.name)}</button><small>${esc(p.unit)} · ${esc(p.barcode)}</small>${p.minimumQuantity > 1 ? `<small>Mua từ ${esc(p.minimumQuantity)} ${esc(p.unit)}</small>` : ''}${p.problem ? `<div class="pl-error">${esc(p.problem)}</div>` : ''}</td><td><del>${money(p.retailPrice)}</del></td><td><strong>${money(p.salePrice)}</strong></td><td><input class="form-control pl-quantity" type="number" min="1" max="1000" step="1" value="${quantity}" aria-label="Số tem ${esc(p.name)} ${esc(p.unit)}" /></td><td><button class="btn btn-outline-danger btn-sm" type="button" data-remove="${p.conversionId}" aria-label="Bỏ ${esc(p.name)}">×</button></td></tr>`).join('') || '<tr><td colspan="5">Thêm sản phẩm ở phía trên.</td></tr>';
        summary();
    }
    function renderProducts() {
        el('plProducts').innerHTML = products.map(p => `<article class="pl-product"><div><strong>${esc(p.name)}</strong><small>${esc(p.unit)} · ${esc(p.sku)} · ${esc(p.barcode)}</small><span class="pl-price"><del>${money(p.retailPrice)}</del> → ${money(p.salePrice)}</span>${p.minimumQuantity>1 ? `<small>Mua từ ${esc(p.minimumQuantity)} ${esc(p.unit)}</small>` : ''}${p.problem ? `<div class="pl-error">${esc(p.problem)}</div>` : ''}</div>${p.problem ? '' : `<button type="button" class="btn btn-outline-primary btn-sm" data-add="${p.conversionId}">+ Thêm</button>`}</article>`).join('') || '<p class="text-muted">Không có sản phẩm khớp bộ lọc.</p>';
        el('plPageInfo').textContent = total ? `Trang ${page}/${Math.ceil(total/24)}` : '';
        el('plResultSummary').textContent = `${total.toLocaleString('vi-VN')} đơn vị sản phẩm`;
        summary();
    }
    function add(product, render = true) {
        if (busy || searching || product.problem) return;
        const existing = lines.find(x => x.product.conversionId === product.conversionId);
        if (existing) existing.quantity = Math.min(1000, existing.quantity + 1);
        else if (lines.length < 500) lines.push({product,quantity:1});
        else throw new Error('Tối đa 500 đơn vị sản phẩm mỗi lần.');
        if (render) { renderLines(); preview(product); }
    }
    async function search(nextPage = 1) {
        const seq = ++sequence;
        const id = Number(el('plCampaign').value);
        if (!id) { searching = false; products=[]; total=0; page=1; renderProducts(); return; }
        searching = true; summary(); el('plResultSummary').textContent = 'Đang tìm sản phẩm…';
        try {
            const result = await api('products?' + new URLSearchParams({ promotionId:id, q:el('plSearch').value.trim(), page:nextPage }));
            if (seq !== sequence) return;
            products = result.items; total = result.total; page = result.page;
            renderProducts();
        } catch (e) { if (seq === sequence) { products=[]; total=0; renderProducts(); notice(e.message,true); } }
        finally { if (seq === sequence) { searching=false; summary(); } }
    }
    async function refresh() {
        if (busy) return;
        clearTimeout(timer);
        busy = true; sequence++; searching = false; summary();
        try {
            const options = await api('refresh', { promotionId:Number(el('plCampaign').value), conversionIds:lines.map(x => x.product.conversionId) });
            lines = lines.map(line => ({...line,product:options.find(x => x.conversionId===line.product.conversionId) || {...line.product,problem:'Đơn vị không còn thuộc chương trình. Bỏ khỏi danh sách.'}}));
            preview(lines[0]?.product);
            notice('Đã cập nhật giá và khuyến mãi. Kiểm tra danh sách trước khi in.');
        } finally { busy=false; renderLines(); }
        await search(page);
    }
    async function connect() {
        if(busy)return;
        busy=true; summary(); connected=false;
        try {
            const saved=el('plPrinter').value;
            const health=await helper('/health');a4Supported=health.features?.includes('promotion-a4-v1')===true;
            printerNames=(await helper('/printers')).printers;
            if(!Array.isArray(printerNames) || !printerNames.every(x=>typeof x==='string'))throw new Error('Danh sách máy in client không hợp lệ.');
            el('plPrinter').replaceChildren(new Option('Chọn máy in trên client',''), ...printerNames.map(name=>new Option(name,name)));
            if(printerNames.includes(saved)) el('plPrinter').value=saved;
            connected=true; el('plConnectionStatus').textContent=`Đã kết nối · ${printerNames.length} máy in trên client`;
            notice(printerNames.length ? `Đã tìm thấy ${printerNames.length} máy in trên máy tính này.` : 'Chưa có máy in. Cài driver và kết nối lại.');
            if(!printerNames.includes(saved))markDirty();
            await preview();
        } catch(e) {el('plPrinterSettings').open=true;el('plConnectionStatus').textContent='Chưa kết nối · kiểm tra bộ in tem trên client';throw e;}
        finally {busy=false;summary();}
    }
    async function print(test = false) {
        if (busy || dirty || pending || !connected || !test && (searching || !validLines())) return;
        const prefs=settings();
        if(prefs.design.paperMode==='a4' && !a4Supported)throw new Error('Cập nhật GaoApp Label Print Client để in A4 qua driver Brother.');
        busy=true; summary();
        try {
            const available=(await helper('/printers')).printers;
            if(!available.includes(prefs.printer.windowsPrinterName))throw new Error('Máy in đã chọn không có trên client. Kết nối và chọn lại máy in.');
            const prepared=test ? [{product:{name:'MẪU THỬ · Bộ bàn chải dây 3 chiếc',barcode:'4902430679640',unit:'Bộ',retailPrice:39000,salePrice:32100,
                minimumQuantity:1,startAtUtc:new Date().toISOString(),endAtUtc:new Date(Date.now()+20*86400000).toISOString()},quantity:prefs.design.paperMode==='a4'?5:prefs.design.columns}]
                : await api('prepare',{promotionId:Number(el('plCampaign').value),lines:lines.map(x=>({conversionId:x.product.conversionId,quantity:x.quantity,fingerprint:x.product.fingerprint}))});
            const payload={design:prefs.design,printer:prefs.printer,items:prepared.map(item=>({product:labelProduct(item.product,prefs.period),quantity:item.quantity}))};
            pending=crypto.randomUUID(); localStorage.setItem(pendingKey,pending);
            const submitted=await helper('/print',{requestId:pending,payload});
            if(submitted.requestId!==pending || submitted.submitted!==true || submitted.quantity!==prepared.reduce((sum,x)=>sum+x.quantity,0))throw new Error('Bộ kết nối chưa xác nhận đúng lệnh in.');
            localStorage.removeItem(pendingKey); pending=null;
            notice(`Đã gửi ${submitted.quantity} tem${prefs.design.paperMode==='a4'?` trên ${Math.ceil(submitted.quantity/10)} tờ A4`:''} tới ${prefs.printer.windowsPrinterName} trên client. Kiểm tra tem thực tế tại máy in.`);
        } catch(e) {if(pending)notice(e.message+' Lệnh chưa rõ kết quả; kiểm tra máy in trước khi in tiếp.',true);else throw e;}
        finally { busy=false; summary(); }
    }
    const safe=action=>async event=>{event?.preventDefault();try{await action(event);}catch(e){notice(e.message,true);}};
    el('plSearchForm').addEventListener('submit',safe(()=>{clearTimeout(timer);return busy ? undefined : search();}));
    el('plSearch').addEventListener('input',()=>{clearTimeout(timer);sequence++;searching=true;summary();timer=setTimeout(()=>search(),350);});
    el('plCampaign').addEventListener('change',safe(()=>{clearTimeout(timer);lines=[];renderLines();return search();}));
    el('plProducts').addEventListener('click',safe(event=>{const button=event.target.closest('[data-add]');if(button)add(products.find(p=>p.conversionId===Number(button.dataset.add)));}));
    el('plAddPage').addEventListener('click',safe(()=>{for(const product of products)add(product,false);renderLines();preview(lines[0]?.product);}));
    el('plLines').addEventListener('input',event=>{if(busy || !event.target.matches('.pl-quantity'))return;const row=event.target.closest('[data-line]');lines.find(x=>x.product.conversionId===Number(row.dataset.line)).quantity=Number(event.target.value);summary();if(settings(false).design.paperMode==='a4'){clearTimeout(previewTimer);previewTimer=setTimeout(()=>preview(),350);}});
    el('plLines').addEventListener('click',event=>{const button=event.target.closest('[data-remove],[data-preview]');if(!button || busy)return;if(button.dataset.remove){lines=lines.filter(x=>x.product.conversionId!==Number(button.dataset.remove));renderLines();preview(lines[0]?.product);}else preview(lines.find(x=>x.product.conversionId===Number(button.dataset.preview))?.product);});
    el('plPrev').addEventListener('click',safe(()=>search(page-1))); el('plNext').addEventListener('click',safe(()=>search(page+1)));
    el('plRefresh').addEventListener('click',safe(refresh)); el('plConnect').addEventListener('click',safe(connect));
    el('plPrint').addEventListener('click',safe(()=>print())); el('plSavePrinter').addEventListener('click',safe(()=>saveSettings()));
    el('plTestPrint').addEventListener('click',safe(()=>print(true))); el('plPreviewButton').addEventListener('click',safe(()=>preview()));
    el('plSheetPrev').addEventListener('click',safe(()=>{if(sheetPage>1){sheetPage--;return preview();}}));
    el('plSheetNext').addEventListener('click',safe(()=>{if(sheetPage<sheetPages){sheetPage++;return preview();}}));
    el('plDesign').addEventListener('input',markDirty); el('plPrinterForm').addEventListener('input',markDirty);
    el('plPaperPreset').addEventListener('change',()=>{
        const size=el('plPaperPreset').value;
        const form=el('plDesign'), wasA4=form.elements.paperMode.value==='a4';
        const previousName=form.elements.name.value;
        if(size==='custom') {
            form.elements.paperMode.value='';
            if(wasA4){form.elements.columns.value=1;form.elements.columnGapMm.value=2;form.elements.rowGapMm.value=2;form.elements.leftMarginMm.value=1;form.elements.rightMarginMm.value=1;}
        } else if(size==='a4') {
            fill(form,{...fields(form),paperMode:'a4',widthMm:90,heightMm:50,columns:2,columnGapMm:10,rowGapMm:5,leftMarginMm:10,rightMarginMm:10});
            form.elements.name.value='A4 · 10 tem khuyến mãi';
        } else {
            const [width,height]=size.split('x').map(Number);
            form.elements.paperMode.value='';form.elements.widthMm.value=width;form.elements.heightMm.value=height;
            if(width===80 || wasA4)form.elements.columns.value=1;
            if(wasA4){form.elements.columnGapMm.value=2;form.elements.rowGapMm.value=2;form.elements.leftMarginMm.value=1;form.elements.rightMarginMm.value=1;}
            if(wasA4 || /^Tem khuyến mãi \d+ × \d+$/.test(previousName))form.elements.name.value=`Tem khuyến mãi ${width} × ${height}`;
        }
        sheetPage=1;loadPrinter(el('plPrinter').value);paperSize(settings(false).design);
        markDirty();
    });
    el('plPrinter').addEventListener('change',()=>{loadPrinter(el('plPrinter').value);markDirty();});
    el('plToken').addEventListener('input',()=>{connected=false;markDirty();el('plConnectionStatus').textContent='Mã kết nối đã đổi · kết nối lại';});
    el('plTemplate').addEventListener('change',()=>loadTemplate(el('plTemplate').value));
    el('plNewTemplate').addEventListener('click',()=>loadTemplate(''));
    el('plCopyTemplate').addEventListener('click',safe(()=>saveSettings(true)));
    el('plDeleteTemplate').addEventListener('click',safe(()=>{
        config.templates=config.templates.filter(t=>t.id!==templateId);config.selected='';localStorage.setItem(storageKey,JSON.stringify(config));loadTemplate('');notice('Đã xóa mẫu trên client này.');
    }));
    el('plImportTemplate').addEventListener('change',()=>{
        const saved=importedTemplates.find(t=>t.id===Number(el('plImportTemplate').value));if(!saved)return;
        const printer=importedPrinters.find(p=>p.id===saved.design.printerId);
        templateId='';fill(el('plDesign'),{...defaults(),...saved.design,layout:'promotion-shelf',printerId:null,fontSize:Math.min(9,saved.design.fontSize),widthMm:Math.max(50,saved.design.widthMm),heightMm:Math.max(30,saved.design.heightMm)});
        if(printer){fill(el('plPrinterForm'),printer);if(![...el('plPrinter').options].some(x=>x.value===printer.windowsPrinterName))el('plPrinter').append(new Option(printer.windowsPrinterName,printer.windowsPrinterName));el('plPrinter').value=printer.windowsPrinterName;}
        templateOptions();markDirty();notice('Đã lấy thông số từ quản lý. Kiểm tra máy in trên client rồi lưu thành mẫu riêng.');
    });
    el('plCheckResult').addEventListener('click',safe(async()=>{if(!pending)return;const result=await helper('/results/'+pending);if(result.submitted){localStorage.removeItem(pendingKey);pending=null;summary();notice('Client đã gửi lệnh vào máy in. Kiểm tra tem thực tế.');}else notice('Chưa xác định được kết quả. Kiểm tra giấy và hàng đợi Windows trước khi cho phép in mới.',true);}));
    el('plAllowNewPrint').addEventListener('click',safe(()=>{localStorage.removeItem(pendingKey);pending=null;summary();notice('Đã xác nhận kiểm tra máy in. Có thể tạo lệnh in mới.');}));
    try {
        const saved=JSON.parse(localStorage.getItem(storageKey)||'null');
        if(saved && Array.isArray(saved.templates) && Array.isArray(saved.printers))config=saved;
        el('plToken').value=config.token || ''; pending=localStorage.getItem(pendingKey);
    } catch { notice('Không đọc được lựa chọn máy in đã lưu. Chọn lại máy in.',true); }
    loadTemplate(config.selected);
    window.addEventListener('pagehide',()=>{if(previewUrl)URL.revokeObjectURL(previewUrl);});
    if(config.token)(async()=>{
        let granted=['localhost','127.0.0.1','[::1]'].includes(location.hostname);
        if(!granted)try{granted=(await navigator.permissions.query({name:'loopback-network'})).state==='granted';}catch{}
        if(granted)await connect();
        else{el('plPrinterSettings').open=true;el('plConnectionStatus').textContent='Bấm Kết nối để cho phép trang truy cập bộ in tem trên máy này.';}
    })().catch(e=>notice(e.message,true));
    (async()=>{
        try{
            const responses=await Promise.all(['templates','printers'].map(path=>fetch(root.dataset.labelApi+path,{credentials:'same-origin',cache:'no-store'})));
            if(responses.some(r=>!r.ok))throw new Error();
            [importedTemplates,importedPrinters]=await Promise.all(responses.map(r=>r.json()));
            el('plImportTemplate').replaceChildren(new Option('Chọn mẫu đã lưu trong quản lý…',''),...importedTemplates.map(t=>new Option(`${t.design.name} · ${t.design.widthMm}×${t.design.heightMm} · ${t.design.columns} cột`,t.id)));
        }catch{el('plImportTemplate').replaceChildren(new Option('Chưa tải được mẫu từ quản lý',''));}
    })();
    (async()=>{
        try {
            const campaigns=await api('campaigns');
            el('plCampaign').replaceChildren(new Option(campaigns.length?'Chọn chương trình giảm giá…':'Chưa có chương trình giảm giá bán lẻ đang mở',''),...campaigns.map(p=>new Option(p.name,p.id)));
            const chosen=Number(new URLSearchParams(location.search).get('promotionId'));
            if(campaigns.some(p=>p.id===chosen))el('plCampaign').value=String(chosen);
            else if(campaigns.length===1)el('plCampaign').value=String(campaigns[0].id);
            if(el('plCampaign').value)await search();
        } catch(e){el('plCampaign').replaceChildren(new Option('Không tải được chương trình',''));notice(e.message,true);}
    })();
})();
