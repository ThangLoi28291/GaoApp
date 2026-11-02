(() => {
    'use strict';
    const receipt = document.querySelector('.deposit-receipt');
    if (!receipt) return;
    const paperStyle = document.createElement('style');
    document.head.appendChild(paperStyle);
    const updatePaperSize = () => {
        const heightMm = Math.max(40, Math.ceil(receipt.getBoundingClientRect().height * 25.4 / 96) + 2);
        paperStyle.textContent = `@page { size: 80mm ${heightMm}mm; margin: 0; }`;
    };
    updatePaperSize();
    window.addEventListener('beforeprint', updatePaperSize);
    window.addEventListener('resize', updatePaperSize);
    if (document.fonts) document.fonts.ready.then(updatePaperSize);
    document.getElementById('depositReceiptPrint')?.addEventListener('click', () => {
        updatePaperSize();
        window.print();
    });
})();
