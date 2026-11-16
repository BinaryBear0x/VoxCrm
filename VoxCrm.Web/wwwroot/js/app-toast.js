// Shared toast helper: window.appToast(message, level, delayMs).
// Server messages (TempData) are rendered as toasts by _ToastNotifications; this is for things that
// happen in the browser (status changes, background polling, local actions) so the user is told about those too.
(() => {
    if (window.appToast) return;

    const styles = { success: 'text-bg-success', error: 'text-bg-danger', warning: 'text-bg-warning', info: 'text-bg-info' };
    const icons = { success: 'bi-check-circle-fill', error: 'bi-exclamation-triangle-fill', warning: 'bi-exclamation-triangle-fill', info: 'bi-info-circle-fill' };
    const delays = { success: 4000, error: 6500, warning: 5500, info: 5000 };

    window.appToast = (message, level = 'info', delay) => {
        if (!message || !window.bootstrap) return;
        const kind = styles[level] ? level : 'info';

        let container = document.querySelector('.toast-container.toast-layer');
        if (!container) {
            container = document.createElement('div');
            container.className = 'toast-container position-fixed bottom-0 end-0 p-3 toast-layer';
            document.body.appendChild(container);
        }

        const toast = document.createElement('div');
        toast.className = `toast align-items-center ${styles[kind]} border-0 shadow-sm app-toast`;
        toast.setAttribute('role', 'alert');
        toast.setAttribute('aria-live', 'assertive');
        toast.setAttribute('aria-atomic', 'true');

        const row = document.createElement('div');
        row.className = 'd-flex';
        const body = document.createElement('div');
        body.className = 'toast-body';
        const icon = document.createElement('i');
        icon.className = `bi ${icons[kind]} me-2`;
        body.append(icon, document.createTextNode(String(message)));
        const close = document.createElement('button');
        close.type = 'button';
        close.className = 'btn-close btn-close-white me-2 m-auto';
        close.setAttribute('data-bs-dismiss', 'toast');
        close.setAttribute('aria-label', 'Kapat');
        row.append(body, close);
        toast.append(row);
        container.append(toast);

        toast.addEventListener('hidden.bs.toast', () => toast.remove());
        bootstrap.Toast.getOrCreateInstance(toast, { delay: delay ?? delays[kind] }).show();
    };
})();

// After a rejected save the server hands back what was submitted (see ValidationFeedbackFilter).
// Hand-written forms do not bind values themselves, so put the typed values back into empty fields.
(() => {
    const block = document.getElementById('attempted-values');
    if (!block) return;
    let attempted;
    try { attempted = JSON.parse(block.textContent); } catch { return; }

    const skipped = new Set(['password', 'hidden', 'file', 'submit', 'button']);
    document.querySelectorAll('form').forEach((form) => {
        [...form.elements].forEach((field) => {
            if (!field.name || skipped.has(field.type) || !(field.name in attempted)) return;
            const value = attempted[field.name];
            if (field.type === 'checkbox') {
                if (!field.checked && /(^|,)true($|,)/i.test(value)) field.checked = true;
            } else if (field.type === 'radio') {
                if (field.value === value) field.checked = true;
            } else if (field.tagName === 'SELECT') {
                if ([...field.options].some((o) => o.value === value)) field.value = value;
            } else if (!field.value) {
                field.value = value;
            }
        });
    });
})();
