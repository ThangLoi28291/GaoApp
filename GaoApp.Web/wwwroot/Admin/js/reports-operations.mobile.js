(() => {
  'use strict';
  // Presentation only. operations-report.page.js remains the data and command owner.
  const root = document.querySelector('[data-operations-report]');
  if (!root) return;
  const phone = window.matchMedia('(max-width: 767.98px)');
  const saved = new Map();
  const dialogBodies = new Map();
  let printing = false;
  let frame = 0;

  function attribute(node, name, value) {
    if (node.getAttribute(name) === value) return;
    if (!saved.has(node)) saved.set(node, new Map());
    const attributes = saved.get(node);
    if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
    node.setAttribute(name, value);
  }

  function decorateTable(table) {
    const headings = [];
    const occupied = [];
    Array.from(table.tHead?.rows || []).forEach((row, rowIndex) => {
      let column = 0;
      Array.from(row.cells).forEach(cell => {
        while (occupied[column] > rowIndex) column++;
        const label = cell.textContent.replace(/\s+/g, ' ').trim();
        for (let offset = 0; offset < cell.colSpan; offset++) {
          if (label) headings[column + offset] = label;
          occupied[column + offset] = rowIndex + cell.rowSpan;
        }
        column += cell.colSpan;
      });
    });
    if (!headings.length) return;
    attribute(table, 'data-rmo-table', '');
    attribute(table, 'role', 'table');
    Array.from(table.tHead?.rows || []).forEach(row => {
      attribute(row, 'role', 'row');
      Array.from(row.cells).forEach(cell => attribute(cell, 'role', 'columnheader'));
    });
    Array.from(table.tBodies).forEach(body => {
      attribute(body, 'role', 'rowgroup');
      Array.from(body.rows).forEach(row => {
        attribute(row, 'data-rmo-row', '');
        attribute(row, 'role', 'row');
        let column = 0;
        Array.from(row.cells).forEach((cell, index) => {
          attribute(cell, 'role', cell.tagName === 'TH' ? 'rowheader' : 'cell');
          attribute(cell, 'data-rmo-cell', '');
          const emptyRow = cell.colSpan >= headings.length;
          if (!emptyRow) attribute(cell, 'data-rmo-label', headings[column] || '');
          if (index === 0 || emptyRow || cell.querySelector('button, .or-detail-actions')) {
            attribute(cell, 'data-rmo-wide', '');
          }
          if (index === 0 && !emptyRow) attribute(cell, 'data-rmo-identity', '');
          column += cell.colSpan;
        });
      });
    });
  }

  function decorateDialog(dialog) {
    if (dialogBodies.has(dialog)) return;
    const form = dialog.querySelector('form');
    const heading = form?.querySelector(':scope > .mr-dialog-heading');
    const actions = form?.querySelector(':scope > .mr-dialog-actions');
    if (!form || !heading || !actions) return;
    const original = Array.from(form.childNodes);
    const scroll = document.createElement('div');
    scroll.className = 'rmo-dialog-scroll';
    scroll.dataset.rmoDialogScroll = '';
    form.insertBefore(scroll, actions);
    original.forEach(node => {
      if (node !== heading && node !== actions) scroll.appendChild(node);
    });
    dialogBodies.set(dialog, { form, original, scroll });
    attribute(dialog, 'data-rmo-dialog', '');
  }

  function restore() {
    dialogBodies.forEach(({ form, original, scroll }) => {
      original.forEach(node => form.appendChild(node));
      scroll.remove();
    });
    dialogBodies.clear();
    saved.forEach((attributes, node) => {
      attributes.forEach((value, name) => {
        if (value === null) node.removeAttribute(name);
        else node.setAttribute(name, value);
      });
    });
    saved.clear();
  }

  function apply() {
    frame = 0;
    if (printing || !phone.matches) {
      restore();
      return;
    }
    attribute(document.body, 'data-reports-operations-phone', '');
    attribute(root, 'data-reports-operations-phone-root', '');
    root.querySelectorAll('.mr-table').forEach(decorateTable);
    root.querySelectorAll('[data-money-dialog], [data-opening-dialog], [data-action-dialog]').forEach(decorateDialog);
    // Tables are replaced by the owner on each result/filter update.
    saved.forEach((_, node) => { if (!node.isConnected) saved.delete(node); });
  }

  function schedule() {
    if (!frame) frame = window.requestAnimationFrame(apply);
  }
  new MutationObserver(schedule).observe(root, { childList: true, subtree: true });
  phone.addEventListener('change', schedule);
  window.addEventListener('beforeprint', () => {
    printing = true;
    if (frame) window.cancelAnimationFrame(frame);
    frame = 0;
    restore();
  });
  window.addEventListener('afterprint', () => { printing = false; schedule(); });
  apply();
})();
