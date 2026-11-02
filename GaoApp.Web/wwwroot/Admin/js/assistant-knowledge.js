(() => {
    'use strict';
    const root = document.getElementById('assistantKnowledgeAdmin');
    if (!root) return;
    root.querySelector('#policyPreview').addEventListener('click', () => {
        root.querySelector('#policyPreviewTitle').textContent = root.querySelector('#policyTitle').value;
        root.querySelector('#policyPreviewBody').textContent = root.querySelector('#policyBody').value;
        root.querySelector('#policyPreviewBox').hidden = false;
    });
    for (const form of root.querySelectorAll('form')) form.addEventListener('submit', e => {
        if (form.dataset.submitting) { e.preventDefault(); return; }
        if (!form.checkValidity()) return;
        form.dataset.submitting = 'true';
        for (const button of form.querySelectorAll('button')) button.disabled = true;
    });
    window.addEventListener('pageshow', () => {
        for (const form of root.querySelectorAll('form')) {
            delete form.dataset.submitting;
            for (const button of form.querySelectorAll('button')) button.disabled = false;
        }
    });
})();