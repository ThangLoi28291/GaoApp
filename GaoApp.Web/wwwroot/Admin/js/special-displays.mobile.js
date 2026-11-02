/* UI-22: an explicit phone dismissal for native kiosk dialogs that only offer destructive/save actions. */
(() => {
    'use strict';
    const dialog=document.getElementById('g-dialog');
    if(!dialog)return;
    const phone=matchMedia('screen and (max-width: 767.98px)');
    let button=null,printing=false;
    const cleanup=()=>{button?.remove();button=null;};
    const apply=()=>{
        if(printing||!phone.matches||!dialog.open||dialog.querySelector('[data-action="close-dialog"]')){cleanup();return;}
        if(button?.isConnected)return;
        button=document.createElement('button');button.type='button';button.className='g-btn ui22-kiosk-close';
        button.dataset.ui22DialogClose='';button.textContent='Đóng';button.setAttribute('aria-label','Đóng hộp thoại');
        button.addEventListener('click',()=>dialog.close());
        (dialog.querySelector('.g-actions')||dialog).append(button);
    };
    new MutationObserver(apply).observe(dialog,{attributes:true,attributeFilter:['open'],childList:true,subtree:true});
    phone.addEventListener('change',apply);
    addEventListener('beforeprint',()=>{printing=true;cleanup();});
    addEventListener('afterprint',()=>{printing=false;apply();});
    apply();
})();
