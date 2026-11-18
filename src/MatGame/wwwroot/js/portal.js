// The portal of MatGame: the panels (sign-in, profile) and their forms against the host API (/api/matgame/...).
// The cookie signs in; every change carries the antiforgery token of the page (meta mg-antiforgery, renewed by every answer of the session API).

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];

let antiforgery = document.querySelector('meta[name="mg-antiforgery"]')?.content ?? '';

async function call(url, { method = 'POST', body } = {}) {
    const response = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: antiforgery },
        body: body === undefined ? undefined : JSON.stringify(body),
        credentials: 'same-origin',
    });
    const data = await response.json().catch(() => null);
    if (data?.antiforgery) antiforgery = data.antiforgery;
    if (!response.ok) {
        const error = new Error(data?.error ?? `Fehler ${response.status}`);
        error.field = data?.field ?? null;
        throw error;
    }
    return data;
}

function toast(text) {
    const box = $('#mg-toast');
    if (!box) return;
    box.textContent = text;
    box.hidden = false;
    clearTimeout(toast.timer);
    toast.timer = setTimeout(() => (box.hidden = true), 3200);
}

// a cover that does not load leaves the card's colour and initial
for (const img of $$('.mg-card__cover img')) {
    const drop = () => img.remove();
    if (img.complete && img.naturalWidth === 0) drop();
    else img.addEventListener('error', drop);
}

// ---------------------------------------------------------------- panels

function open(name) {
    for (const overlay of $$('[data-overlay]')) overlay.hidden = overlay.dataset.overlay !== name;
    const first = $(`[data-overlay="${name}"] form:not([hidden]) input:not([type=hidden])`);
    first?.focus();
}

function closeAll() {
    for (const overlay of $$('[data-overlay]')) {
        if (overlay.querySelector('[data-close]')) overlay.hidden = true;
    }
}

for (const button of $$('[data-open]')) button.addEventListener('click', () => open(button.dataset.open));
for (const button of $$('[data-close]')) button.addEventListener('click', closeAll);
for (const overlay of $$('[data-overlay]')) {
    overlay.addEventListener('click', e => {
        if (e.target === overlay && overlay.querySelector('[data-close]')) closeAll();
    });
}
document.addEventListener('keydown', e => {
    if (e.key === 'Escape') closeAll();
});

// the tabs of the sign-in panel
for (const tab of $$('.mg-tab')) {
    tab.addEventListener('click', () => {
        const panel = tab.closest('.mg-panel');
        for (const t of $$('.mg-tab', panel)) t.setAttribute('aria-selected', String(t === tab));
        for (const form of $$('form[data-form]', panel)) form.hidden = form.dataset.form !== tab.dataset.tab;
        $(`form[data-form="${tab.dataset.tab}"] input`, panel)?.focus();
    });
}

// ---------------------------------------------------------------- forms

function values(form) {
    const result = {};
    for (const input of $$('input', form)) if (input.name) result[input.name] = input.value;
    return result;
}

function showError(form, error) {
    const box = $('.mg-error', form);
    for (const input of $$('input', form)) input.removeAttribute('aria-invalid');
    if (error.field) $(`input[name="${error.field}"]`, form)?.setAttribute('aria-invalid', 'true');
    if (box) {
        box.textContent = error.message;
        box.hidden = false;
    } else {
        toast(error.message);
    }
}

for (const form of $$('form[data-api]')) {
    form.addEventListener('submit', async e => {
        e.preventDefault();
        const button = $('button[type=submit]', form);
        button?.setAttribute('disabled', '');
        $('.mg-error', form)?.setAttribute('hidden', '');
        try {
            await call(form.dataset.api, { method: form.dataset.method ?? 'POST', body: values(form) });
            if (form.dataset.done) {
                toast(form.dataset.done);
                form.reset();
                return;
            }
            const back = form.closest('[data-return]')?.dataset.return;
            if (back) location.href = back;
            else location.reload();
        } catch (error) {
            showError(form, error);
        } finally {
            button?.removeAttribute('disabled');
        }
    });
}

for (const button of $$('[data-remove-device]')) {
    button.addEventListener('click', async () => {
        try {
            await call(`/api/matgame/account/devices/${button.dataset.removeDevice}`, { method: 'DELETE' });
            button.closest('li')?.remove();
            toast('Gerät abgemeldet.');
        } catch (error) {
            toast(error.message);
        }
    });
}

$('[data-logout]')?.addEventListener('click', async () => {
    try {
        await call('/api/matgame/session/logout', { body: {} });
    } finally {
        location.href = '/';
    }
});
