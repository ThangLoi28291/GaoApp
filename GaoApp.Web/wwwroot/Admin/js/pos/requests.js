(() => {
    'use strict';
    const root = document.getElementById('posOperationsRequests');
    if (!root) return;
    const buttons = [...root.querySelectorAll('[data-request-tab]')];
    const pages = {cash:window.POSCashAdjustmentPage,payment:window.POSPaymentAdjustmentPage};
    const saved = new Map();
    let active;
    function setUrl(query) {
        history.replaceState(null,'','/admin/pos-shift/requests?'+query.toString());
        const shiftId = Number(query.get('shiftId')) || null;
        document.getElementById('operationsReconciliation').href = '/admin/pos-shift/reconciliation'+(shiftId?'?shiftId='+shiftId:'');
    }
    function select(next, initial = false) {
        if (!pages[next] || active === next || root.querySelector('dialog[open]')) return;
        const current = new URLSearchParams(location.search);
        if (active) saved.set(active,pages[active].getQuery());
        const query = initial ? current : saved.get(next) || new URLSearchParams(
            ['shiftId','status'].filter(key=>current.has(key)).map(key=>[key,current.get(key)]));
        query.set('tab',next);setUrl(query);active=next;
        buttons.forEach(button=>{
            const selected=button.dataset.requestTab===next;
            button.classList.toggle('is-active',selected);button.setAttribute('aria-selected',String(selected));button.tabIndex=selected?0:-1;
        });
        root.querySelectorAll('[data-request-panel]').forEach(panel=>panel.hidden=panel.dataset.requestPanel!==next);
        pages[next].init();
    }
    window.POSRequestTabs = {clearFilterUrl(tab){saved.delete(tab);setUrl(new URLSearchParams({tab}));}};
    buttons.forEach((button,index)=>{
        button.addEventListener('click',()=>select(button.dataset.requestTab));
        button.addEventListener('keydown',event=>{
            let target;
            if(event.key==='ArrowRight')target=buttons[(index+1)%buttons.length];
            if(event.key==='ArrowLeft')target=buttons[(index+buttons.length-1)%buttons.length];
            if(event.key==='Home')target=buttons[0];if(event.key==='End')target=buttons[buttons.length-1];
            if(target){event.preventDefault();select(target.dataset.requestTab);target.focus();}
        });
    });
    select(root.dataset.activeTab === 'payment' ? 'payment' : 'cash',true);
})();
