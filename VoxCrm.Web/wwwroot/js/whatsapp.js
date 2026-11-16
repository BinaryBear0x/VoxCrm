(() => {
    const connectForm = document.querySelector('[data-wa-connect-form]');
    const connectButton = document.querySelector('[data-wa-connect-button]');
    const connectText = document.querySelector('[data-wa-connect-text]');
    const panel = document.querySelector('[data-wa-qr-panel]');
    const image = document.querySelector('[data-wa-qr-image]');
    const loading = document.querySelector('[data-wa-qr-loading]');
    const statusText = document.querySelector('[data-wa-qr-status]');
    const errorBox = document.querySelector('[data-wa-qr-error]');
    const statusBadge = document.querySelector('[data-wa-status-badge]');
    const phoneField = document.querySelector('[data-wa-phone]');
    const lastErrorField = document.querySelector('[data-wa-last-error]');

    const toast = (message, level) => window.appToast?.(message, level);
    const showElement = (element) => element?.classList.remove('d-none');
    const hideElement = (element) => element?.classList.add('d-none');

    connectForm?.addEventListener('submit', () => {
        connectButton?.setAttribute('disabled', 'disabled');
        connectButton?.setAttribute('aria-busy', 'true');
        if (connectText) connectText.textContent = 'Bağlanıyor...';
        showElement(panel);
        showElement(loading);
        if (statusText) statusText.textContent = 'Bağlantı isteği gönderiliyor...';
        toast('WhatsApp bağlantı isteği gönderiliyor…', 'info');
    });

    if (!panel) return;

    // One poller for the whole page. It polls quickly while a connection is being set up
    // (QR / connecting), slowly otherwise, and pauses while the tab is in the background.
    const FAST_MS = 2500;
    const SLOW_MS = 15000;
    const MAX_FAST_ATTEMPTS = 60;
    const labels = {
        ready: 'Bağlı',
        connecting: 'Bağlanıyor',
        qr: 'QR okutulması bekleniyor',
        auth_failed: 'Oturum doğrulanamadı — yeniden bağlayın'
    };

    let fastAttempts = 0;
    let wantsFast = panel.dataset.waAutoPoll === 'true';
    let hasShownQr = Boolean(image?.getAttribute('src'));
    let timer = null;
    let stopped = false;
    let lastStatus = null;
    let failing = false;

    const setBadge = (status) => {
        if (!statusBadge) return;
        statusBadge.textContent = labels[status] || 'Bağlantı yok — yeniden bağlayın';
        statusBadge.className = 'badge ' + (status === 'ready'
            ? 'bg-success'
            : status === 'qr' || status === 'connecting' ? 'bg-warning text-dark' : 'bg-secondary');
    };

    const showError = (message) => {
        if (!errorBox) return;
        errorBox.textContent = message;
        showElement(errorBox);
    };

    const readStatus = async () => {
        const response = await fetch(panel.dataset.waQrUrl, {
            headers: { Accept: 'application/json' },
            cache: 'no-store'
        });
        // An expired login redirects to the sign-in page; a closed module answers 401/403.
        // Those do not heal by themselves, so polling stops. Any other non-JSON answer (for
        // example a proxy's HTML 502 during a restart) is temporary: report it and keep polling.
        if (response.redirected || response.status === 401 || response.status === 403) {
            const error = new Error('Oturumunuz sona ermiş veya bu sayfaya erişiminiz kalmamış olabilir. Sayfayı yenileyin.');
            error.fatal = true;
            throw error;
        }
        if (!(response.headers.get('content-type') || '').includes('json')) {
            throw new Error('WhatsApp servisi şu anda yanıt vermiyor. Yeniden deneniyor.');
        }
        const payload = await response.json();
        if (!response.ok || payload.ok === false) {
            throw new Error(payload.error || 'WhatsApp durumu alınamadı.');
        }
        return payload;
    };

    const announce = (payload) => {
        const status = payload.status;
        if (failing) { failing = false; toast('WhatsApp durumu yeniden alınıyor.', 'info'); }
        if (status !== lastStatus) {
            // The first reading of the page is not news; only changes are.
            if (lastStatus !== null || (status === 'qr' && wantsFast)) {
                if (status === 'ready') toast(payload.connectedPhone ? `WhatsApp bağlandı: ${payload.connectedPhone}` : 'WhatsApp bağlandı.', 'success');
                else if (lastStatus === 'ready') toast('WhatsApp bağlantısı koptu. Mesajlar bağlantı kurulana kadar kuyrukta bekler.', 'warning');
                else if (status === 'qr') toast('QR hazır. Klinik telefonundan okutun.', 'info');
                else if (status === 'auth_failed') toast(payload.lastError || 'WhatsApp oturumu doğrulanamadı. Yeniden bağlayın.', 'error');
            }
            lastStatus = status;
        }
    };

    const render = (payload) => {
        announce(payload);
        hideElement(errorBox);
        setBadge(payload.status);
        if (phoneField) phoneField.textContent = payload.connectedPhone || 'Bağlı numara yok';
        if (lastErrorField) {
            lastErrorField.textContent = payload.lastError
                || (payload.status === 'ready' ? 'Bildirilmiş hata yok' : 'Bağlantı hazır değil. QR ile yeniden bağlayın.');
        }

        // A connected device wins over any QR still held by the gateway.
        if (payload.status === 'ready') {
            hideElement(loading);
            hideElement(image);
            if (statusText) statusText.textContent = payload.connectedPhone
                ? `Bağlandı: ${payload.connectedPhone}`
                : 'WhatsApp bağlantısı hazır.';
            wantsFast = false;
            return;
        }

        if (payload.qr && image) {
            image.src = payload.qr;
            showElement(image);
            hideElement(loading);
            hasShownQr = true;
            if (statusText) statusText.textContent = 'QR hazır. Klinik telefonundan okutun.';
            return;
        }

        if (payload.status === 'auth_failed' || (payload.status === 'disconnected' && payload.lastError)) {
            hideElement(loading);
            hideElement(image);
            showError(payload.lastError || 'WhatsApp yetkilendirmesi başarısız oldu.');
            wantsFast = false;
            return;
        }

        if (!hasShownQr && statusText) {
            showElement(loading);
            statusText.textContent = payload.status === 'connecting'
                ? 'Bağlantı kuruluyor, QR bekleniyor.'
                : 'QR bekleniyor.';
        }
    };

    const schedule = () => {
        if (stopped) return;
        window.clearTimeout(timer);
        const fast = wantsFast && fastAttempts < MAX_FAST_ATTEMPTS;
        timer = window.setTimeout(poll, fast ? FAST_MS : SLOW_MS);
    };

    const poll = async () => {
        if (stopped) return;
        if (document.hidden) { schedule(); return; }
        if (wantsFast) fastAttempts += 1;
        try {
            render(await readStatus());
        } catch (error) {
            if (error.fatal) {
                stopped = true;
                showError(error.message);
                toast(error.message, 'error');
                return;
            }
            // One toast per outage, not one every few seconds.
            if (!failing) { failing = true; toast('WhatsApp durumu şu anda alınamıyor. Yeniden deneniyor.', 'warning'); }
            if (lastErrorField) lastErrorField.textContent = error.message || 'Bağlantı durumu alınamadı. Servisi kontrol edin.';
            if (!hasShownQr && fastAttempts >= 3) showError(error.message || 'WhatsApp durumu alınamadı.');
        }
        if (wantsFast && fastAttempts >= MAX_FAST_ATTEMPTS && !hasShownQr) {
            hideElement(loading);
            showError('QR zamanında üretilmedi. Tekrar Bağla ile yeni istek başlatabilirsiniz.');
            toast('QR zamanında üretilmedi. Yeniden bağlamayı deneyin.', 'warning');
            wantsFast = false;
        }
        schedule();
    };

    // Coming back to the tab refreshes immediately instead of waiting for the slow interval.
    document.addEventListener('visibilitychange', () => {
        if (!document.hidden && !stopped) { window.clearTimeout(timer); poll(); }
    });

    timer = window.setTimeout(poll, wantsFast ? 800 : SLOW_MS);
})();

(() => {
    document.querySelectorAll('[data-wa-load-default]').forEach((button) => {
        button.addEventListener('click', () => {
            const target = document.querySelector(button.dataset.waTarget || '');
            if (target) {
                target.value = button.dataset.waLoadDefault || '';
                window.appToast?.('Varsayılan metin yüklendi. Kullanmak için şablonu kaydedin.', 'info');
            }
        });
    });
})();

(() => {
    const message = document.getElementById('manualMessageText');
    const counter = document.getElementById('charCount');
    if (!message || !counter) return;

    message.addEventListener('input', () => {
        counter.textContent = `${message.value.length} karakter`;
    });
})();

(() => {
    const owner = document.getElementById('recipient');
    const pet = document.getElementById('patient');
    if (!owner || !pet) return;
    const filter = () => {
        [...pet.options].forEach((option) => {
            option.hidden = !!option.value && option.dataset.owner !== owner.value;
            option.disabled = option.hidden;
        });
        if (pet.selectedOptions[0]?.disabled) pet.value = '';
    };
    owner.addEventListener('change', filter);
    filter();
})();
