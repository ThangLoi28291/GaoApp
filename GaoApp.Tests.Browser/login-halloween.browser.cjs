'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const { clipBeamToRect, polygonArea } = require('../GaoApp.Web/wwwroot/Admin/js/account-login.js');

(async () => {
    const baseUrl = process.env.LOGIN_PREVIEW_URL || 'http://127.0.0.1:5198';
    const output = path.resolve(__dirname, '../Logs/login-halloween');
    fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const checks = [], errors = [];
    try {
        const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
        // Preserve offline testing of the original CDN dependencies.
        await context.route('https://code.jquery.com/jquery-3.7.1.min.js', r => r.fulfill({ path: path.resolve(__dirname, '../GaoApp.Web/wwwroot/lib/jquery/dist/jquery.min.js'), contentType: 'application/javascript' }));
        await context.route('https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css', r => r.fulfill({ path: path.resolve(__dirname, '../GaoApp.Web/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css'), contentType: 'text/css' }));
        await context.addInitScript(() => {
            const get = HTMLCanvasElement.prototype.getContext;
            window.__loginLegFrames = [];
            HTMLCanvasElement.prototype.getContext = function (...args) {
                const ctx = get.apply(this, args);
                if (!ctx || args[0] !== '2d' || !this.classList.contains('halloween-effects') || ctx.__observed) return ctx;
                ctx.__observed = true;
                let vertices = [], legs = [];
                for (const method of ['beginPath', 'moveTo', 'lineTo', 'stroke']) {
                    const native = ctx[method].bind(ctx);
                    ctx[method] = (...a) => {
                        if (method === 'beginPath') vertices = [];
                        if (method === 'moveTo' || method === 'lineTo') vertices.push(a);
                        if (method === 'stroke' && vertices.length === 3) {
                            legs.push(vertices);
                            if (legs.length === 8) {
                                window.__loginLegFrames.push(legs); legs = [];
                                if (window.__loginLegFrames.length > 120) window.__loginLegFrames.shift();
                            }
                        }
                        return native(...a);
                    };
                }
                return ctx;
            };
        });
        const page = await context.newPage();
        page.on('pageerror', e => errors.push(e.message));
        const load = async (scenario = 'paired') => {
            const response = await page.goto(`${baseUrl}/admin/account/login?case=${scenario}`);
            assert.equal(response.status(), 200, `Login fixture response: ${response.status()}`);
            await page.waitForFunction(() => !!window.jQuery?.validator && (matchMedia('(max-width: 767.98px)').matches
                ? document.getElementById('loginHalloween')?.dataset.mobileLoginReady === 'true' && document.querySelector('.halloween-effects')?.width === 0
                : document.querySelector('.halloween-effects')?.width > 0));
            await page.waitForTimeout(150);
        };
        const center = async selector => {
            const r = await page.locator(selector).boundingBox();
            assert.ok(r, selector);
            return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
        };
        const aim = async selector => { const p = await center(selector); await page.mouse.move(p.x, p.y); await page.waitForTimeout(180); };
        const away = async () => { await page.mouse.move(1430, 12); await page.waitForTimeout(220); };
        await load();
        assert.equal(await page.locator('#PasswordInput').getAttribute('type'), 'password');
        assert.equal(await page.locator('[data-login-theme]').count(), 2);
        assert.equal(await page.locator('button').filter({ hasText: /hiệu ứng/i }).count(), 0);
        assert.notEqual(await page.locator('.halloween-pumpkin').first().evaluate(e => getComputedStyle(e).animationName), 'none');
        assert.equal(await page.locator('.halloween-owl').evaluate(e => getComputedStyle(e).opacity), '0');
        checks.push('Effects start automatically; no effects switch; owl initially hidden.');

        const password = 'Demo-only-123456789';
        await page.locator('[name=UserName]').fill('preview');
        await page.locator('#PasswordInput').fill(password);
        await page.locator('#btnTogglePassword').click();
        await page.waitForTimeout(180);
        assert.equal(await page.locator('#btnTogglePassword').getAttribute('aria-pressed'), 'true');
        assert.equal(await page.locator('#PasswordInput').getAttribute('type'), 'password');
        assert.equal(await page.locator('.password-light-reveal span').textContent(), '');
        assert.equal(await page.locator('.halloween-owl').evaluate(e => getComputedStyle(e).opacity), '0');
        await aim('#PasswordInput');
        assert.equal(await page.locator('.password-light-reveal span').textContent(), password);
        assert.equal(await page.locator('.password-light-reveal').evaluate(e => getComputedStyle(e).visibility), 'visible');
        const clip = await page.locator('.password-light-reveal').evaluate(e => e.style.clipPath);
        assert.match(clip, /^polygon\(/);
        const visualText = await page.locator('.password-light-reveal').evaluate(e => {
            const r = e.getBoundingClientRect(), t = e.querySelector('span').getBoundingClientRect();
            const points = [...e.style.clipPath.matchAll(/(-?[\d.]+)px\s+(-?[\d.]+)px/g)].map(m => ({ x: Number(m[1]), y: Number(m[2]) }));
            return { points, rect: { x: t.left - r.left, y: t.top - r.top, w: t.width, h: t.height } };
        });
        assert.ok(polygonArea(clipBeamToRect(visualText.points, visualText.rect)) > 50, 'The light intersects actual text glyph area, not just empty input space.');
        assert.equal(await page.locator('.password-light-reveal').getAttribute('aria-hidden'), 'true');
        await away();
        assert.equal(await page.locator('.password-light-reveal span').textContent(), '');
        await aim('.halloween-owl');
        assert.ok(Number(await page.locator('.halloween-owl').evaluate(e => getComputedStyle(e).opacity)) > .2);
        await away();
        assert.equal(await page.locator('.halloween-owl').evaluate(e => getComputedStyle(e).opacity), '0');
        await aim('.halloween-pumpkin-tl');
        assert.ok(await page.locator('.halloween-pumpkin-tl').evaluate(e => e.classList.contains('is-illuminated')));
        assert.match(await page.locator('.halloween-pumpkin-tl').evaluate(e => getComputedStyle(e).filter), /brightness\(1\.85\)/);
        await away();
        assert.equal(await page.locator('.halloween-pumpkin-tl').evaluate(e => e.classList.contains('is-illuminated')), false);
        checks.push('Flashlight reveals clipped password and owl only on a hit; pumpkins brighten and dim with light.');

        await aim('#PasswordInput');
        await page.evaluate(() => window.dispatchEvent(new Event('blur')));
        await page.waitForTimeout(160);
        assert.equal(await page.locator('.password-light-reveal span').textContent(), '');
        await aim('#PasswordInput');
        await page.screenshot({ path: path.join(output, 'login-desktop-light.png') });
        await page.mouse.move(1100, 250);
        await page.waitForTimeout(180);
        for (let i = 0; i < 12; i++) { await page.mouse.move(450 + i * 35, 170 + i * 20); await page.waitForTimeout(45); }
        const articulation = await page.evaluate(() => new Set(window.__loginLegFrames.map(v => JSON.stringify(v))).size);
        assert.ok(articulation > 8, `Spider leg poses: ${articulation}`);
        checks.push('Spider renders eight jointed moving legs while following the pointer; blur clears revealed text.');

        await page.locator('[data-login-theme=day]').click();
        await page.waitForTimeout(1600);
        assert.equal(await page.locator('#loginHalloween').getAttribute('data-mode'), 'day');
        assert.equal(await page.locator('.halloween-props').evaluate(e => getComputedStyle(e).opacity), '0');
        const dayContrast = await page.evaluate(() => {
            const luminance = value => {
                const channels = value.match(/[\d.]+/g).slice(0, 3).map(Number).map(v => v / 255).map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4);
                return channels[0] * .2126 + channels[1] * .7152 + channels[2] * .0722;
            };
            const fg = luminance(getComputedStyle(document.querySelector('.login-title')).color);
            const bg = luminance(getComputedStyle(document.querySelector('.login-panel')).backgroundColor);
            return (Math.max(fg, bg) + .05) / (Math.min(fg, bg) + .05);
        });
        assert.ok(dayContrast >= 4.5, `Day login heading contrast: ${dayContrast}`);
        await page.screenshot({ path: path.join(output, 'login-day.png') });
        await page.locator('[data-login-theme=night]').click();
        await page.waitForTimeout(1600);
        await page.locator('#btnTogglePassword').click();
        await away();
        await page.screenshot({ path: path.join(output, 'login-desktop.png') });
        checks.push('Day/night transitions and decorative scene work.');

        for (const scenario of ['paired', 'pairing', 'admin', 'error']) {
            for (const width of [1440, 800, 390, 320]) {
                await page.setViewportSize({ width, height: 900 }); await load(scenario);
                const dimensions = await page.evaluate(() => ({ viewport: innerWidth, document: document.documentElement.scrollWidth, body: document.body.scrollWidth }));
                assert.ok(dimensions.document <= width && dimensions.body <= width, `${scenario} ${width}: ${JSON.stringify(dimensions)}`);
                assert.equal(await page.locator('#PasswordInput').isVisible(), true);
                assert.equal(await page.locator('#btnLogin').isEnabled(), true);
                if (width < 768) {
                    assert.equal(await page.locator('.login-brand').isVisible(), false);
                    assert.equal(await page.locator('.gao-login-mobile-brand').isVisible(), true);
                    await page.locator('#PasswordInput').fill(password);
                    await page.locator('#btnTogglePassword').click();
                    assert.equal(await page.locator('#PasswordInput').getAttribute('type'), 'text');
                    assert.equal(await page.locator('.password-light-reveal span').textContent(), '');
                    await page.locator('#btnTogglePassword').click();
                    assert.equal(await page.locator('#PasswordInput').getAttribute('type'), 'password');
                }
                assert.equal(await page.locator('[name=SelectedTerminalId]').count(), scenario === 'pairing' ? 1 : 0);
                if (scenario === 'pairing') {
                    for (const name of ['PairingKey', 'SelectedTerminalId', 'DeviceName']) assert.equal(await page.locator(`[name=${name}]`).isVisible(), true);
                }
                if (scenario === 'error') assert.match(await page.locator('.validation-summary-errors').innerText(), /không đúng/);
                if (width === 390 && ['paired', 'pairing'].includes(scenario)) await page.screenshot({ path: path.join(output, `login-${scenario}-mobile.png`), fullPage: true });
            }
        }
        checks.push('Paired login, POS pairing, POS administration and server error render at 1440/800/390/320px without horizontal overflow.');

        await page.setViewportSize({ width: 1440, height: 1000 }); await load();
        await page.locator('#btnLogin').click();
        await page.locator('[name=UserName]').fill('preview');
        await page.locator('#PasswordInput').fill(password);
        await page.locator('#btnTogglePassword').click();
        await aim('#PasswordInput');
        assert.equal(await page.locator('.password-light-reveal span').textContent(), password);
        await away();
        assert.equal(await page.locator('.password-light-reveal span').textContent(), '');
        await load('pairing');
        await page.locator('#btnLogin').click();
        assert.equal(await page.locator('#btnLogin').isEnabled(), true);
        assert.ok(await page.locator('[data-valmsg-for=UserName]').innerText());
        await page.locator('[name=UserName]').fill('preview');
        await page.locator('#PasswordInput').fill(password);
        await page.locator('[name=PairingKey]').fill('demo-key');
        await page.locator('[name=SelectedTerminalId]').selectOption('7');
        await page.locator('[name=DeviceName]').fill('Máy kiểm tra');
        const payload = await page.locator('#loginForm').evaluate(f => Object.fromEntries(new FormData(f)));
        assert.equal(payload.UserName, 'preview'); assert.equal(payload.Password, password);
        assert.equal(payload.SelectedTerminalId, '7'); assert.equal(payload.PairingKey, 'demo-key');
        assert.equal(payload.DeviceName, 'Máy kiểm tra'); assert.equal(payload.ReturnUrl, '/admin/pos');
        assert.ok(payload.__RequestVerificationToken);
        assert.equal(new URL(await page.locator('#loginForm').getAttribute('action'), baseUrl).pathname.toLowerCase(), '/admin/account/login');
        // Capture the request without attempting authentication or writing any data.
        let posted;
        await page.route('**/admin/account/login', async route => {
            if (route.request().method() === 'POST') {
                posted = new URLSearchParams(route.request().postData());
                await new Promise(resolve => setTimeout(resolve, 350));
                await route.abort();
            } else await route.continue();
        });
        const postRequest = page.waitForRequest(r => r.method() === 'POST' && new URL(r.url()).pathname.toLowerCase() === '/admin/account/login');
        await page.locator('#btnLogin').click({ noWaitAfter: true });
        await page.waitForFunction(() => document.querySelector('#btnLogin')?.disabled);
        assert.equal(await page.locator('.password-light-reveal span').textContent(), '');
        posted = new URLSearchParams((await postRequest).postData());
        assert.equal(posted?.get('Password'), password);
        checks.push('Invalid login stays editable; valid POST keeps all model fields, password, route and antiforgery token.');

        const reducedPage = await context.newPage();
        await reducedPage.emulateMedia({ reducedMotion: 'reduce' });
        await reducedPage.goto(`${baseUrl}/admin/account/login?case=paired`);
        await reducedPage.waitForFunction(() => document.querySelector('#loginHalloween')?.classList.contains('login-reduced-motion'));
        assert.equal(await reducedPage.locator('.halloween-pumpkin').first().evaluate(e => getComputedStyle(e).animationPlayState), 'paused');
        await reducedPage.locator('#PasswordInput').fill(password);
        await reducedPage.locator('#btnTogglePassword').focus();
        await reducedPage.keyboard.press('Enter');
        await reducedPage.waitForTimeout(180);
        assert.equal(await reducedPage.locator('.password-light-reveal span').textContent(), password);
        assert.equal(await reducedPage.locator('#PasswordInput').getAttribute('type'), 'password');
        checks.push('Reduced motion respects system preference; keyboard flashlight works.');
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(output, 'browser-report.json'), JSON.stringify({ checks, errors }, null, 2));
        process.stdout.write(JSON.stringify({ passed: checks.length, checks, errors }, null, 2) + '\n');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
