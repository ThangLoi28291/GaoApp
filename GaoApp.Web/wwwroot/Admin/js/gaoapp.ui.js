(function (window, document) {
    'use strict';

    if (window.GaoAppUI && window.GaoAppUI.initialized) {
        return;
    }

    var unsafeMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
    var submitSnapshots = new WeakMap();
    var submitTimers = new WeakMap();

    function getAntiForgeryToken() {
        var meta = document.querySelector('meta[name="request-verification-token"]');
        return meta ? (meta.getAttribute('content') || '') : '';
    }

    function isUnsafeMethod(method) {
        return unsafeMethods.has(String(method || 'GET').toUpperCase());
    }

    function isSameOrigin(url) {
        try {
            return new URL(url || window.location.href, window.location.href).origin === window.location.origin;
        } catch (_) {
            return true;
        }
    }

    window.GaoAppAntiForgery = {
        headerName: 'RequestVerificationToken',
        getToken: getAntiForgeryToken
    };

    function installJQueryAntiForgery() {
        var jq = window.jQuery;
        if (!jq || jq.__gaoAppAntiForgeryInstalled) {
            return;
        }

        jq.ajaxPrefilter(function (options, _, xhr) {
            var method = options.type || options.method || 'GET';
            if (!isUnsafeMethod(method) || !isSameOrigin(options.url)) {
                return;
            }

            var token = getAntiForgeryToken();
            if (token) {
                xhr.setRequestHeader(window.GaoAppAntiForgery.headerName, token);
            }
        });

        jq.__gaoAppAntiForgeryInstalled = true;
    }

    function installFetchAntiForgery() {
        if (!window.fetch || window.fetch.__gaoAppAntiForgeryPatched) {
            return;
        }

        var originalFetch = window.fetch;
        var patchedFetch = function (input, init) {
            var requestInit = init ? Object.assign({}, init) : {};
            var method = requestInit.method || (input && input.method) || 'GET';
            var url = typeof input === 'string'
                ? input
                : (window.URL && input instanceof window.URL
                    ? input.href
                    : (input && input.url ? input.url : window.location.href));

            if (isUnsafeMethod(method) && isSameOrigin(url)) {
                var token = getAntiForgeryToken();
                if (token) {
                    var sourceHeaders = requestInit.headers;
                    if (!sourceHeaders && window.Request && input instanceof window.Request) {
                        sourceHeaders = input.headers;
                    }

                    var headers = new Headers(sourceHeaders || undefined);
                    headers.set(window.GaoAppAntiForgery.headerName, token);
                    requestInit.headers = headers;
                }
            }

            return originalFetch.call(this, input, requestInit);
        };

        patchedFetch.__gaoAppAntiForgeryPatched = true;
        patchedFetch.__gaoAppOriginalFetch = originalFetch;
        window.fetch = patchedFetch;
    }

    function configureNotifications() {
        if (window.toastr) {
            Object.assign(window.toastr.options, {
                escapeHtml: true,
                closeButton: true,
                progressBar: true,
                newestOnTop: true,
                positionClass: 'toast-top-right',
                preventDuplicates: true,
                showDuration: 300,
                hideDuration: 1000,
                timeOut: 2500,
                extendedTimeOut: 1000,
                showEasing: 'swing',
                hideEasing: 'linear',
                showMethod: 'fadeIn',
                hideMethod: 'fadeOut'
            });
        }

        function show(type, message, title, options) {
            if (!message || !window.toastr || typeof window.toastr[type] !== 'function') {
                return null;
            }

            return window.toastr[type](message, title, Object.assign({}, options, { escapeHtml: true }));
        }

        window.GaoAppNotify = {
            show: show,
            success: function (message, title, options) {
                return show('success', message, title, options);
            },
            error: function (message, title, options) {
                return show('error', message, title, options);
            },
            warning: function (message, title, options) {
                return show('warning', message, title, options);
            },
            info: function (message, title, options) {
                return show('info', message, title, options);
            }
        };
    }

    function snapshotSubmitControl(control) {
        if (submitSnapshots.has(control)) {
            return;
        }

        submitSnapshots.set(control, {
            html: control instanceof HTMLButtonElement ? control.innerHTML : null,
            value: control instanceof HTMLInputElement ? control.value : null,
            ariaDisabled: control.getAttribute('aria-disabled')
        });
    }

    function lockSubmitForm(form, submitter) {
        form.dataset.gaoappSubmitting = 'true';
        form.setAttribute('aria-busy', 'true');

        form.querySelectorAll('[type="submit"]').forEach(function (control) {
            snapshotSubmitControl(control);
            control.setAttribute('aria-disabled', 'true');
        });

        var activeSubmitter = submitter && form.contains(submitter)
            ? submitter
            : form.querySelector('[type="submit"]');

        if (activeSubmitter) {
            activeSubmitter.classList.add('gaoapp-submit-loading');

            var loadingText = activeSubmitter.getAttribute('data-submit-loading-text')
                || form.getAttribute('data-submit-loading-text')
                || 'Đang xử lý...';

            if (activeSubmitter instanceof HTMLButtonElement) {
                activeSubmitter.textContent = loadingText;
            } else if (activeSubmitter instanceof HTMLInputElement) {
                activeSubmitter.value = loadingText;
            }
        }

        var unlockMs = Number.parseInt(form.getAttribute('data-submit-unlock-ms') || '', 10);
        if (Number.isFinite(unlockMs) && unlockMs > 0) {
            var existingTimer = submitTimers.get(form);
            if (existingTimer) {
                window.clearTimeout(existingTimer);
            }

            submitTimers.set(form, window.setTimeout(function () {
                unlockSubmitForm(form);
            }, unlockMs));
        }
    }

    function unlockSubmitForm(form) {
        var timer = submitTimers.get(form);
        if (timer) {
            window.clearTimeout(timer);
            submitTimers.delete(form);
        }

        delete form.dataset.gaoappSubmitting;
        form.removeAttribute('aria-busy');

        form.querySelectorAll('[type="submit"]').forEach(function (control) {
            var snapshot = submitSnapshots.get(control);
            if (snapshot) {
                if (control instanceof HTMLButtonElement && snapshot.html !== null) {
                    control.innerHTML = snapshot.html;
                } else if (control instanceof HTMLInputElement && snapshot.value !== null) {
                    control.value = snapshot.value;
                }

                if (snapshot.ariaDisabled === null) {
                    control.removeAttribute('aria-disabled');
                } else {
                    control.setAttribute('aria-disabled', snapshot.ariaDisabled);
                }

                submitSnapshots.delete(control);
            } else {
                control.removeAttribute('aria-disabled');
            }

            control.classList.remove('gaoapp-submit-loading');
        });
    }

    function handleGuardedSubmit(event) {
        var form = event.target instanceof HTMLFormElement
            ? event.target
            : null;

        if (!form || !form.matches('form[data-submit-guard]')) {
            return;
        }

        if (form.dataset.gaoappSubmitting === 'true') {
            event.preventDefault();
            return;
        }

        if (event.defaultPrevented) {
            return;
        }

        lockSubmitForm(form, event.submitter || null);

        // Nếu một listener chạy sau hủy submit, trả form về trạng thái ban đầu.
        window.queueMicrotask(function () {
            if (event.defaultPrevented) {
                unlockSubmitForm(form);
            }
        });
    }

    function isUploadImage(image) {
        if (image.hasAttribute('data-image-fallback')) {
            return true;
        }

        var source = image.currentSrc || image.getAttribute('src') || '';
        if (!source || source.startsWith('data:') || source.startsWith('blob:')) {
            return false;
        }

        try {
            return new URL(source, window.location.href).pathname.toLowerCase().startsWith('/uploads/');
        } catch (_) {
            return source.toLowerCase().startsWith('/uploads/');
        }
    }

    var fallbackSvg = [
        '<svg xmlns="http://www.w3.org/2000/svg" width="320" height="240" viewBox="0 0 320 240">',
        '<rect width="320" height="240" fill="#f5f5f9"/>',
        '<g fill="none" stroke="#a8a9b4" stroke-width="8" stroke-linecap="round" stroke-linejoin="round">',
        '<rect x="92" y="54" width="136" height="112" rx="12"/>',
        '<circle cx="132" cy="92" r="12"/>',
        '<path d="m104 150 40-38 27 25 20-18 25 31"/>',
        '</g>',
        '<text x="160" y="202" text-anchor="middle" font-family="Arial,sans-serif" font-size="18" fill="#696a75">Không có ảnh</text>',
        '</svg>'
    ].join('');
    var fallbackImageUri = 'data:image/svg+xml;charset=UTF-8,' + encodeURIComponent(fallbackSvg);

    function applyImageFallback(image) {
        if (!isUploadImage(image) || image.dataset.imageFallbackApplied === 'true') {
            return;
        }

        image.dataset.imageFallbackApplied = 'true';
        image.classList.add('gaoapp-image-fallback');
        image.removeAttribute('srcset');

        if (!image.hasAttribute('alt')) {
            image.setAttribute('alt', 'Không có ảnh');
        }

        image.src = fallbackImageUri;
        image.dispatchEvent(new CustomEvent('gaoapp:image-fallback', {
            bubbles: true,
            detail: { image: image }
        }));
    }

    function installImageFallback() {
        document.addEventListener('error', function (event) {
            if (event.target instanceof HTMLImageElement) {
                applyImageFallback(event.target);
            }
        }, true);

        document.querySelectorAll('img[data-image-fallback], img[src^="/uploads/"]').forEach(function (image) {
            if (image.complete && image.naturalWidth === 0) {
                applyImageFallback(image);
            }
        });
    }

    installJQueryAntiForgery();
    installFetchAntiForgery();
    configureNotifications();
    installImageFallback();
    window.addEventListener('submit', handleGuardedSubmit);
    window.addEventListener('pageshow', function () {
        document.querySelectorAll('form[data-submit-guard][data-gaoapp-submitting="true"]')
            .forEach(unlockSubmitForm);
    });

    window.GaoAppUI = {
        initialized: true,
        unlockSubmitForm: unlockSubmitForm,
        applyImageFallback: applyImageFallback
    };
})(window, document);
