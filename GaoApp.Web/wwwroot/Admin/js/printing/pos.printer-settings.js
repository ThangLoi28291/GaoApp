(function () {
    'use strict';
    const setup=JSON.parse(document.getElementById('posPrinterSettingsData').textContent), el=id=>document.getElementById(id);
    const current=setup.receiptDefault.template;
    function message(text,error=false){const box=el('printerSettingsMessage');box.hidden=false;box.className='alert '+(error?'alert-danger':'alert-success');box.textContent=text;}
    function profile(){const value=PosPrinting.preferences(setup);el('activePrintProfile').textContent='Máy in tại quầy: '+(value.mode==='helper'?'Linux Print Helper':value.mode==='qz'?value.printer:'Hộp thoại trình duyệt');}
    function modeChanged(){el('qzSettings').hidden=el('printMode').value!=='qz';el('helperSettings').hidden=el('printMode').value!=='helper';el('printCopySettings').hidden=el('printMode').value==='browser';}
    function save(){
        if(el('printMode').value!=='browser'&&!el('printCopies').reportValidity())throw new Error('Số bản in phải từ 1 đến 5.');
        PosPrinting.savePreferences(setup,{mode:el('printMode').value,printer:el('clientPrinter').value,copies:Number(el('printCopies').value),
            color:false,templateKey:current.key,template:current.design});
        profile();
    }
    async function busy(button,action){button.disabled=true;try{await action();}catch(error){message(error.message||'Chưa hoàn tất. Kiểm tra máy in và thử lại.',true);}finally{button.disabled=false;}}
    el('printMode').addEventListener('change',modeChanged);
    el('connectPrinters').addEventListener('click',event=>busy(event.currentTarget,async()=>{
        const printers=await PosPrinting.printers(),selected=el('clientPrinter').value;
        el('clientPrinter').replaceChildren(new Option('Chọn máy in',''),...printers.map(name=>new Option(name,name)));
        el('clientPrinter').value=printers.includes(selected)?selected:'';
        message(printers.length?`Đã tìm thấy ${printers.length} máy in trên thiết bị này.`:'Chưa có máy in. Cài driver trên thiết bị rồi kết nối lại.');
    }));
    el('checkHelper').addEventListener('click',event=>busy(event.currentTarget,async()=>{await PosPrinting.health();message('Print Helper sẵn sàng nhận hóa đơn 80 mm.');}));
    el('applyTemplate').addEventListener('click',event=>busy(event.currentTarget,()=>{save();message('Đã lưu máy in trên trình duyệt của quầy này.');}));
    el('testPrint').addEventListener('click',event=>busy(event.currentTarget,async()=>{
        save();
        const sample={...setup.storeInfo,orderId:0,orderNumber:'IN-THU',createdAtUtc:new Date().toISOString(),cashierName:'In thử máy in',
            subtotal:25000,discountTotal:0,grandTotal:25000,paidTotal:25000,changeDue:0,balanceDue:0,
            lines:[{itemName:'Sản phẩm in thử',quantity:1,sellingUnitName:'Cái',unitPrice:25000,lineTotal:25000}],payments:[{method:0,amount:25000}]};
        await PosPrinting.openLocal(sample,setup,[current]);message('Đã mở phiếu in thử theo mẫu mặc định của cửa hàng.');
    }));
    try{const saved=PosPrinting.preferences(setup);el('printMode').value=saved.mode;
        if(saved.printer)el('clientPrinter').append(new Option(saved.printer,saved.printer,true,true));
        el('printCopies').value=saved.copies;modeChanged();profile();
    }catch(error){modeChanged();message(error.message,true);}
})();
