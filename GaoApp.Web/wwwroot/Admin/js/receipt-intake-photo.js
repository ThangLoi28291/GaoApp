(() => {
    'use strict';
    const byId=id=>document.getElementById(id);
    let data=null,loading=false,error=false,request=0;
    function reset(){request++;data=null;loading=false;error=false;byId('riPhoto').value='';byId('riPhotoPreview').hidden=true;byId('riPhotoPreview').removeAttribute('src');byId('riPhotoRemove').hidden=true;byId('riPhotoStatus').textContent='';}
    async function choose(){
        const file=byId('riPhoto').files[0];reset();if(!file)return;
        const turn=request;loading=true;byId('riPhotoStatus').textContent='Đang thu nhỏ ảnh…';
        let url;
        try{
            if(!['image/jpeg','image/png','image/webp'].includes(file.type)||file.size>15*1024*1024)throw new Error('Chọn ảnh JPEG, PNG hoặc WebP không quá 15 MB.');
            url=URL.createObjectURL(file);const img=new Image();img.src=url;await img.decode();if(turn!==request)return;
            const scale=Math.min(1,1280/Math.max(img.naturalWidth,img.naturalHeight));
            const canvas=document.createElement('canvas');canvas.width=Math.max(1,Math.round(img.naturalWidth*scale));canvas.height=Math.max(1,Math.round(img.naturalHeight*scale));
            const context=canvas.getContext('2d');context.fillStyle='#fff';context.fillRect(0,0,canvas.width,canvas.height);context.drawImage(img,0,0,canvas.width,canvas.height);
            let quality=.78;let jpeg=canvas.toDataURL('image/jpeg',quality);
            while(jpeg.length>340000&&quality>.3){quality-=.12;jpeg=canvas.toDataURL('image/jpeg',quality);}
            if(jpeg.length>340000)throw new Error('Ảnh quá nhiều chi tiết. Hãy chụp gần riêng phần nhãn sản phẩm.');
            data=jpeg;byId('riPhotoPreview').src=data;byId('riPhotoPreview').hidden=false;
            byId('riPhotoStatus').textContent='Ảnh sẽ được lưu cùng lần nhận hàng này.';
        }catch(ex){if(turn===request){error=true;byId('riPhotoStatus').textContent=ex.message||'Không đọc được ảnh. Chọn ảnh khác.';}}
        finally{if(url)URL.revokeObjectURL(url);if(turn===request){loading=false;byId('riPhotoRemove').hidden=false;}}
    }
    window.ReceiptIntakePhoto={reset,current:()=>data,blocked:()=>loading||error};
    function start(){byId('riPhoto')?.addEventListener('change',choose);byId('riPhotoRemove')?.addEventListener('click',reset);}
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
})();
