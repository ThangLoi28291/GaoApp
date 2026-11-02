/* UI-22 preview magnification only; no fields, persistence, APIs, render payloads or print handlers are replaced. */
(() => {
    'use strict';
    let printing=false;
    addEventListener('beforeprint',()=>{printing=true;});
    addEventListener('afterprint',()=>{printing=false;});
    const image=document.getElementById('labelPreview');
    if(image){
        const stage=image.closest('.label-preview-stage'),phone=matchMedia('screen and (max-width: 767.98px)');
        let tools=null,mode='actual',priorWidth='',priorMax='';
        const applyImage=()=>{
            if(printing||!phone.matches){if(tools){tools.remove();tools=null;image.style.width=priorWidth;image.style.maxWidth=priorMax;}return;}
            if(!tools){
                priorWidth=image.style.width;priorMax=image.style.maxWidth;tools=document.createElement('div');tools.className='ui22-preview-tools';
                for(const[key,text]of[['actual','100% ảnh · Đọc rõ'],['fit','Vừa khung']]){const button=document.createElement('button');button.type='button';button.dataset.zoomMode=key;button.textContent=text;button.addEventListener('click',()=>{mode=key;applyImage();});tools.append(button);}
                const output=document.createElement('output');output.setAttribute('aria-live','polite');tools.append(output);stage.before(tools);
            }
            const css=getComputedStyle(stage),available=stage.clientWidth-parseFloat(css.paddingLeft)-parseFloat(css.paddingRight);
            const width=mode==='fit'?Math.min(image.naturalWidth,available):image.naturalWidth;
            if(image.style.maxWidth!=='none')image.style.maxWidth='none';
            if(width>0&&image.style.width!==width+'px')image.style.width=width+'px';
            tools.querySelectorAll('button').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.zoomMode===mode)));
            tools.querySelector('output').textContent=(image.naturalWidth?Math.round(width/image.naturalWidth*100)+'% ảnh':'Chờ ảnh xem trước')+' · Khổ tem khi in giữ nguyên.';
        };
        phone.addEventListener('change',applyImage);image.addEventListener('load',applyImage);new ResizeObserver(applyImage).observe(stage);
        addEventListener('beforeprint',()=>{if(tools){tools.remove();tools=null;image.style.width=priorWidth;image.style.maxWidth=priorMax;}});addEventListener('afterprint',applyImage);applyImage();
    }
    const frame=document.getElementById('receiptPreview');
    if(!frame) return;
    const stage=frame.parentElement,phone=matchMedia('screen and (max-width: 767.98px)');
    let tools=null,mode='actual',priorZoom='',scheduled=false;
    const apply=()=>{
        if(printing||!phone.matches){if(tools){tools.remove();tools=null;frame.style.zoom=priorZoom;}return;}
        if(!tools){
            priorZoom=frame.style.zoom;tools=document.createElement('div');tools.className='ui22-preview-tools';tools.setAttribute('aria-label','Mức phóng xem trước');
            for(const [key,text]of [['actual','100% · Đọc rõ'],['fit','Vừa khung']]){const button=document.createElement('button');button.type='button';button.textContent=text;button.dataset.zoomMode=key;button.addEventListener('click',()=>{mode=key;apply();});tools.append(button);}
            const output=document.createElement('output');output.setAttribute('aria-live','polite');tools.append(output);stage.before(tools);
        }
        const width=parseFloat(frame.style.width)*96/25.4,css=getComputedStyle(stage),available=stage.clientWidth-parseFloat(css.paddingLeft)-parseFloat(css.paddingRight);
        const zoom=mode==='fit'&&width>0?Math.min(1,available/width):1;
        if(frame.style.zoom!==String(zoom))frame.style.zoom=String(zoom);
        tools.querySelectorAll('button').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.zoomMode===mode)));
        tools.querySelector('output').textContent=Math.round(zoom*100)+'% · '+(mode==='actual'?'Kéo ngang để xem hết; khổ in giữ nguyên.':'Xem tổng thể; chọn 100% để đọc chi tiết.');
    };
    const schedule=()=>{if(!scheduled){scheduled=true;requestAnimationFrame(()=>{scheduled=false;apply();});}};
    new MutationObserver(schedule).observe(frame,{attributes:true,attributeFilter:['style','srcdoc']});
    new ResizeObserver(schedule).observe(stage);frame.addEventListener('load',schedule);phone.addEventListener('change',apply);
    addEventListener('beforeprint',()=>{if(tools){tools.remove();tools=null;frame.style.zoom=priorZoom;}});
    addEventListener('afterprint',schedule);apply();
})();
