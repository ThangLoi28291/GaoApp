(() => {
    'use strict';
    const el=id=>document.getElementById(id);
    let data=null, stored=false, removed=false, loading=false, error=false, turn=0, endpoint='', itemId=0, version='';
    const busy=()=>!!window.ReceiptIntake?.isSaving();
    const changed=()=>window.ReceiptIntakeReview?.update();
    function render(message='') {
        const present=!!data || stored&&!removed;
        el('riReviewPhotoPreview').hidden=!present;
        el('riReviewPhotoChoose').hidden=present;
        el('riReviewPhotoReplace').hidden=!present;
        el('riReviewPhotoRemove').hidden=!present&&!loading&&!error;
        el('riReviewPhotoDrop').setAttribute('aria-busy',String(loading));
        el('riReviewPhotoStatus').textContent=message;
        el('riReviewPhotoStatus').classList.toggle('text-danger',error);
        el('riReviewUsePhoto').disabled=present;
        if(present)el('riReviewUsePhoto').checked=false;
    }
    function open(item,url) {
        turn++;data=null;removed=false;loading=false;error=false;stored=!!item.hasReviewPhoto;
        endpoint=url;itemId=item.id;version=item.rowVersion;
        el('riReviewPhotoFile').value='';
        if(stored)el('riReviewPhotoPreview').src=`${endpoint}/${itemId}/review-photo?v=${encodeURIComponent(version)}`;
        else el('riReviewPhotoPreview').removeAttribute('src');
        render(stored?'Ảnh đã lưu trong bản nháp. Khi duyệt sẽ dùng làm ảnh chính.':'Chưa chọn ảnh.');
    }
    async function choose(file) {
        if(!file || busy())return;
        const current=++turn;loading=true;error=false;render('Đang xử lý ảnh…');changed();
        let url;
        try {
            if(!['image/jpeg','image/png','image/webp'].includes(file.type) || file.size>15*1024*1024)
                throw new Error('Chọn ảnh JPG, PNG hoặc WebP không quá 15 MB.');
            url=URL.createObjectURL(file);const image=new Image();image.src=url;await image.decode();if(current!==turn)return;
            let scale=Math.min(1,1280/Math.max(image.naturalWidth,image.naturalHeight));
            const canvas=document.createElement('canvas');let jpeg='';
            for(let pass=0;pass<4;pass++) {
                canvas.width=Math.max(1,Math.round(image.naturalWidth*scale));canvas.height=Math.max(1,Math.round(image.naturalHeight*scale));
                const ctx=canvas.getContext('2d');ctx.fillStyle='#fff';ctx.fillRect(0,0,canvas.width,canvas.height);ctx.drawImage(image,0,0,canvas.width,canvas.height);
                for(const quality of [.86,.74,.62,.5]) {jpeg=canvas.toDataURL('image/jpeg',quality);if(jpeg.length<=340000)break;}
                if(jpeg.length<=340000)break;scale*=.75;
            }
            if(jpeg.length>340000)throw new Error('Ảnh quá lớn sau khi thu nhỏ. Vui lòng chọn ảnh khác.');
            data=jpeg;removed=false;el('riReviewPhotoPreview').src=data;
            render('Ảnh đã sẵn sàng. Bấm Lưu nháp hoặc Duyệt sản phẩm để lưu.');
        }catch(ex){if(current===turn){error=true;render(ex.message || 'Không đọc được ảnh. Hãy chọn ảnh khác hoặc bấm Bỏ ảnh.');}}
        finally{if(url)URL.revokeObjectURL(url);if(current===turn){loading=false;el('riReviewPhotoDrop').setAttribute('aria-busy','false');changed();}}
    }
    function remove(){if(busy())return;turn++;data=null;removed=true;loading=false;error=false;el('riReviewPhotoFile').value='';el('riReviewPhotoPreview').removeAttribute('src');render('Đã bỏ ảnh đang chọn. Bấm Lưu nháp để lưu thay đổi.');changed();}
    function init(){
        el('riReviewPhotoFile').addEventListener('change',e=>{const file=e.target.files[0];e.target.value='';choose(file);});
        el('riReviewPhotoReplace').addEventListener('click',()=>{if(!busy())el('riReviewPhotoFile').click();});
        el('riReviewPhotoChoose').addEventListener('click',e=>{if(busy())e.preventDefault();});
        el('riReviewPhotoRemove').addEventListener('click',remove);
        const drop=el('riReviewPhotoDrop');
        drop.addEventListener('dragover',e=>{e.preventDefault();if(!busy())drop.classList.add('is-dragging');});
        drop.addEventListener('dragleave',()=>drop.classList.remove('is-dragging'));
        drop.addEventListener('drop',e=>{e.preventDefault();drop.classList.remove('is-dragging');if(!busy())choose(e.dataTransfer?.files[0]);});
    }
    function request(){if(loading)throw new Error('Ảnh đang xử lý. Vui lòng đợi rồi lưu.');if(error)throw new Error('Ảnh vừa chọn chưa hợp lệ. Hãy chọn lại hoặc bấm Bỏ ảnh.');return {photoDataUrl:data,removeReviewPhoto:removed};}
    window.ReceiptIntakeReviewPhoto={init,open,request,identity:()=>JSON.stringify({data,removed}),
        blocked:()=>loading||error,present:()=>!!data||stored&&!removed,saved:item=>open(item,endpoint)};
})();
