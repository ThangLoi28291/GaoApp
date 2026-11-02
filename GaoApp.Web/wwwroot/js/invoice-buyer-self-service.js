(function () {
    'use strict';
    const expires = Number(document.body.dataset.invoiceExpires);
    const serverNow = Number(document.body.dataset.invoiceServerNow);
    const started = performance.now();
    const remaining = Math.min(7200000, expires - serverNow);
    let cleared = false;
    function clearPage() {
        if (cleared) return;
        cleared = true;
        const main = document.createElement('main');
        const heading = document.createElement('h1');
        heading.textContent = 'Thông tin hóa đơn';
        const notice = document.createElement('p');
        notice.textContent = 'Liên kết nhập thông tin hóa đơn đã đóng. Vui lòng tải lại trang hoặc liên hệ cửa hàng.';
        main.append(heading, notice);
        document.body.replaceChildren(main);
    }
    function checkExpiry() {
        if (!Number.isFinite(remaining) || remaining <= performance.now() - started || Date.now() >= expires)
            clearPage();
    }
    checkExpiry();
    if (!cleared) setTimeout(clearPage, Math.max(0, remaining));
    document.addEventListener('visibilitychange', checkExpiry);
    window.addEventListener('pagehide', clearPage);
    window.addEventListener('pageshow', event => {
        if (event.persisted) { clearPage(); window.location.reload(); }
        else checkExpiry();
    });
    document.addEventListener('submit', event => {
        checkExpiry();
        if (cleared) event.preventDefault();
    }, true);
})();
