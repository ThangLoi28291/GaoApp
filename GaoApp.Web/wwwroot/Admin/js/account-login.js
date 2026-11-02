(function (host, factory) {
    'use strict';
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (host && host.document) api.init(host.document, host);
})(typeof window !== 'undefined' ? window : null, function () {
    'use strict';

    function makeBeam(origin, target, minimumReach = 0) {
        const distance = Math.hypot(target.x - origin.x, target.y - origin.y);
        const angle = distance < 12 ? 0 : Math.atan2(target.y - origin.y, target.x - origin.x);
        const length = Math.max(minimumReach, distance + 24);
        const dx = Math.cos(angle), dy = Math.sin(angle), spread = length * .18;
        return { origin, angle, length, points: [
            { x: origin.x, y: origin.y },
            { x: origin.x + dx * length - dy * spread, y: origin.y + dy * length + dx * spread },
            { x: origin.x + dx * length + dy * spread, y: origin.y + dy * length - dx * spread }
        ] };
    }

    // Clip the actual light cone, rather than revealing a whole password on a hover or toggle.
    function clipBeamToRect(points, rect) {
        let polygon = points.map(p => ({ ...p }));
        const edges = [
            ['x', rect.x, true], ['x', rect.x + rect.w, false],
            ['y', rect.y, true], ['y', rect.y + rect.h, false]
        ];
        for (const [axis, limit, minimum] of edges) {
            const input = polygon; polygon = [];
            if (!input.length) break;
            const inside = p => minimum ? p[axis] >= limit : p[axis] <= limit;
            let a = input[input.length - 1];
            for (const b of input) {
                if (inside(a) !== inside(b)) {
                    const ratio = (limit - a[axis]) / (b[axis] - a[axis]);
                    polygon.push({ x: a.x + (b.x - a.x) * ratio, y: a.y + (b.y - a.y) * ratio });
                }
                if (inside(b)) polygon.push(b);
                a = b;
            }
        }
        return polygon;
    }

    function polygonArea(points) {
        return Math.abs(points.reduce((area, a, i) => {
            const b = points[(i + 1) % points.length];
            return area + a.x * b.y - b.x * a.y;
        }, 0)) / 2;
    }

    function init(doc, win) {
        const form = doc.getElementById('loginForm');
        const submit = doc.getElementById('btnLogin');
        const password = doc.getElementById('PasswordInput');
        const toggle = doc.getElementById('btnTogglePassword');
        if (form && submit) form.addEventListener('submit', event => {
            if (event.defaultPrevented || !form.checkValidity()) return;
            if (win.jQuery && win.jQuery.validator && !win.jQuery(form).valid()) return;
            submit.disabled = true;
            submit.textContent = 'Đang đăng nhập...';
            clearLight();
        });
        const root = doc.getElementById('loginHalloween');
        if (!root || !password || !toggle) return;
        const phone = win.matchMedia('(max-width: 767.98px)');
        const skyCanvas = root.querySelector('.halloween-atmosphere');
        const effectCanvas = root.querySelector('.halloween-effects');
        const sky = skyCanvas && skyCanvas.getContext('2d');
        const ctx = effectCanvas && effectCanvas.getContext('2d');
        const reveal = root.querySelector('.password-light-reveal');
        const revealText = reveal.querySelector('span');
        const owl = root.querySelector('.halloween-owl');
        const pupils = [...owl.querySelectorAll('.owl-pupil')];
        const pumpkins = [...root.querySelectorAll('.halloween-pumpkin')];
        const shell = root.querySelector('.login-shell');
        const brand = root.querySelector('.login-brand');
        const wolf = root.querySelector('.halloween-wolf');
        const bats = [...root.querySelectorAll('.halloween-bat')];
        const flashlight = toggle.querySelector('.login-flashlight');
        const atlas = new win.Image();
        if (!phone.matches) atlas.src = root.dataset.halloweenAtlas;
        const reduced = win.matchMedia('(prefers-reduced-motion: reduce)');
        let motionReduced = reduced.matches;
        root.classList.toggle('login-reduced-motion', motionReduced);
        reduced.addEventListener('change', event => {
            motionReduced = event.matches;
            root.classList.toggle('login-reduced-motion', motionReduced);
        });
        let enabled = false, pointerInside = false, pointerType = 'mouse', visible = true;
        let width = 0, height = 0, card = {}, scene = {}, origin = {}, passwordArea = {}, overlay = {}, owlArea = {};
        let pointer = { x: 0, y: 0 }, crawler = { x: 0, y: 0 }, facing = 0, gait = 0;
        let elapsed = 0, last = win.performance.now(), painted = 0, raf = 0, night = 1;
        const random = n => { const v = Math.sin(n * 127.1 + 311.7) * 43758.5453; return v - Math.floor(v); };
        const stars = Array.from({ length: 46 }, (_, i) => ({ x: random(i + 1), y: random(i + 91), r: .5 + random(i + 177) * .9, phase: random(i + 255) * 6.28 }));
        const leaves = Array.from({ length: 15 }, (_, i) => ({ x: random(i + 310), y: random(i + 390), speed: 12 + random(i + 490) * 19, size: 13 + random(i + 510) * 12, phase: random(i + 590) * 6.28 }));
        const border = [];

        function clearLight() {
            if (reveal) {
                reveal.classList.remove('is-illuminated');
                revealText.textContent = '';
                reveal.style.clipPath = 'polygon(0 0,0 0,0 0)';
            }
            if (owl) { owl.style.opacity = '0'; owl.classList.remove('is-illuminated'); }
            if (pumpkins) pumpkins.forEach(p => p.classList.remove('is-illuminated'));
        }
        function relativeRect(element, rootRect) {
            const r = element.getBoundingClientRect();
            return { x: r.left - rootRect.left, y: r.top - rootRect.top, w: r.width, h: r.height };
        }
        function layout() {
            if (phone.matches) {
                clearLight();
                for (const canvas of [skyCanvas, effectCanvas]) if (canvas) { canvas.width = 0; canvas.height = 0; }
                return;
            }
            const r = root.getBoundingClientRect();
            width = r.width; height = r.height;
            card = relativeRect(shell, r); scene = relativeRect(brand, r);
            const dpr = Math.min(win.devicePixelRatio || 1, 2);
            for (const canvas of [skyCanvas, effectCanvas]) {
                canvas.width = Math.round(width * dpr); canvas.height = Math.round(height * dpr);
                canvas.getContext('2d').setTransform(dpr, 0, 0, dpr, 0, 0);
            }
            const size = parseFloat(win.getComputedStyle(pumpkins[0]).width);
            for (let i = 0; i < pumpkins.length; i++) {
                const right = i % 2, bottom = i > 1;
                pumpkins[i].style.left = (right ? card.x + card.w - size + 17 : card.x - 12) + 'px';
                pumpkins[i].style.top = (bottom ? card.y + card.h - size / 2 : card.y - size * .63) + 'px';
            }
            const ll = root.querySelector('.halloween-lantern-left'), lr = root.querySelector('.halloween-lantern-right');
            const lw = parseFloat(win.getComputedStyle(ll).width);
            ll.style.left = (card.x - lw * .64) + 'px'; ll.style.top = (card.y + card.h * .3) + 'px';
            lr.style.left = (card.x + card.w - lw * .36) + 'px'; lr.style.top = (card.y + card.h * .4) + 'px';
            const buttonRect = relativeRect(toggle, r);
            origin = { x: buttonRect.x + buttonRect.w / 2, y: buttonRect.y + buttonRect.h / 2 };
            overlay = relativeRect(reveal, r);
            const inputRect = relativeRect(password, r);
            passwordArea = { x: inputRect.x + 15, y: inputRect.y + 1, w: Math.max(0, inputRect.w - 91), h: inputRect.h - 2 };
            owlArea = relativeRect(owl, r);
            if (!pointerInside) { pointer = { x: card.x - 8, y: card.y + card.h * .3 }; crawler = { ...pointer }; }
            border.length = 0;
            const x = card.x - 3, y = card.y - 3, w = card.w + 6, h = card.h + 6, radius = 21;
            const line = (ax, ay, bx, by) => { const steps = Math.ceil(Math.hypot(bx - ax, by - ay) / 5); for (let i = 0; i < steps; i++) border.push({ x: ax + (bx - ax) * i / steps, y: ay + (by - ay) * i / steps }); };
            const arc = (cx, cy, a, b) => { for (let i = 0; i < 13; i++) { const v = a + (b - a) * i / 12; border.push({ x: cx + Math.cos(v) * radius, y: cy + Math.sin(v) * radius }); } };
            line(x + radius, y, x + w - radius, y); arc(x + w - radius, y + radius, -Math.PI / 2, 0);
            line(x + w, y + radius, x + w, y + h - radius); arc(x + w - radius, y + h - radius, 0, Math.PI / 2);
            line(x + w - radius, y + h, x + radius, y + h); arc(x + radius, y + h - radius, Math.PI / 2, Math.PI);
            line(x, y + h - radius, x, y + radius); arc(x + radius, y + radius, Math.PI, Math.PI * 1.5);
        }
        function setPointer(event) {
            if (phone.matches) return;
            const r = root.getBoundingClientRect();
            pointer = { x: Math.max(0, Math.min(width, event.clientX - r.left)), y: Math.max(0, Math.min(height, event.clientY - r.top)) };
            pointerInside = true; pointerType = event.pointerType || 'mouse';
        }
        root.addEventListener('pointermove', setPointer, { passive: true });
        root.addEventListener('pointerdown', setPointer, { passive: true });
        root.addEventListener('pointerleave', () => { pointerInside = false; clearLight(); });
        toggle.addEventListener('click', event => {
            if (phone.matches) return; // Native mobile reveal belongs to account-login.mobile.js.
            enabled = !enabled;
            root.classList.toggle('flashlight-active', enabled);
            toggle.setAttribute('aria-pressed', String(enabled));
            toggle.setAttribute('aria-label', enabled ? 'Tắt đèn pin' : 'Bật đèn pin để xem mật khẩu');
            if (!enabled) clearLight();
            if (enabled && (event.detail === 0 || pointerType !== 'mouse')) {
                pointerInside = true;
                pointer = { x: passwordArea.x + passwordArea.w / 2, y: passwordArea.y + passwordArea.h / 2 };
            }
        });
        // The real input remains a native password input for autofill, validation and form posting.
        password.addEventListener('input', () => { if (reveal.classList.contains('is-illuminated')) revealText.textContent = password.value; });
        password.addEventListener('scroll', () => { revealText.style.transform = `translateX(${-password.scrollLeft}px)`; }, { passive: true });
        root.querySelectorAll('[data-login-theme]').forEach(button => button.addEventListener('click', () => {
            root.dataset.mode = button.dataset.loginTheme;
            root.querySelectorAll('[data-login-theme]').forEach(b => b.setAttribute('aria-pressed', String(b === button)));
            clearLight();
        }));
        if (!ctx || !sky) {
            toggle.addEventListener('click', () => { if (!phone.matches) password.type = enabled ? 'text' : 'password'; });
            return;
        }
        const resizeObserver = new win.ResizeObserver(layout);
        resizeObserver.observe(root);
        resizeObserver.observe(root.querySelector('.login-card'));
        layout();
        const intersectionObserver = new win.IntersectionObserver(entries => { visible = entries[0].isIntersecting; root.classList.toggle('login-not-visible', !visible); last = win.performance.now(); }, { threshold: .01 });
        intersectionObserver.observe(root);
        doc.addEventListener('visibilitychange', () => { last = win.performance.now(); root.classList.toggle('login-not-visible', doc.hidden); if (doc.hidden) { pointerInside = false; clearLight(); } });
        win.addEventListener('blur', () => { pointerInside = false; clearLight(); });

        function sprite(element, column, row) { element.style.backgroundPosition = (column * 100 / 3) + '% ' + (row * 50) + '%'; }
        function drawSprite(column, row, x, y, size, alpha, angle) {
            if (!atlas.complete || !atlas.naturalWidth) return;
            const w = atlas.naturalWidth / 4, h = atlas.naturalHeight / 3;
            ctx.save(); ctx.globalAlpha = alpha; ctx.translate(x, y); ctx.rotate(angle || 0);
            ctx.drawImage(atlas, column * w, row * h, w, h, -size / 2, -size / 2, size, size); ctx.restore();
        }
        function vine(x, y, angle, alpha, seed) {
            ctx.save(); ctx.translate(x, y); ctx.rotate(angle); ctx.globalAlpha = alpha * .65; ctx.strokeStyle = '#ad7c44'; ctx.lineWidth = 1;
            ctx.beginPath(); ctx.moveTo(0, 0); ctx.bezierCurveTo(20, -5, 24, -24, 12, -29); ctx.bezierCurveTo(-1, -34, -3, -17, 5, -17); ctx.bezierCurveTo(16, -17, 12, -24, 8, -24); ctx.stroke();
            ctx.beginPath(); ctx.moveTo(5, -3); ctx.bezierCurveTo(-10, -11, -14, 7, -26, 7); ctx.stroke(); ctx.restore();
            drawSprite(2, 2, x + Math.sin(angle) * 14, y - Math.cos(angle) * 18, 29, alpha * .78, angle + seed);
        }
        function drawSpider(walking) {
            ctx.save(); ctx.translate(crawler.x, crawler.y); ctx.rotate(facing);
            ctx.globalAlpha = night; ctx.strokeStyle = '#bf985f'; ctx.lineWidth = 1.6; ctx.lineCap = 'round';
            // Opposite leg pairs alternate; joint and foot travel is driven by distance walked.
            for (const side of [-1, 1]) for (let i = 0; i < 4; i++) {
                const stride = Math.sin(gait + i * Math.PI / 2 + (side > 0 ? Math.PI : 0)) * walking;
                const hipY = -12 + i * 7, footY = [-36, -19, 19, 36][i] + stride * 8;
                const kneeX = side * (23 + Math.abs(i - 1.5) * 2 + stride * 3);
                ctx.beginPath(); ctx.moveTo(side * 6, hipY); ctx.lineTo(kneeX, footY * .7 - stride * 3); ctx.lineTo(side * (36 + (i % 2) * 3), footY); ctx.stroke();
            }
            const body = ctx.createRadialGradient(-2, 3, 1, 0, 5, 13); body.addColorStop(0, '#ffe9a3'); body.addColorStop(.38, '#d6a04d'); body.addColorStop(1, '#6e4223');
            ctx.fillStyle = body; ctx.shadowColor = '#f7bd64'; ctx.shadowBlur = 10;
            ctx.beginPath(); ctx.ellipse(0, 5, 9, 12, 0, 0, Math.PI * 2); ctx.fill();
            ctx.shadowBlur = 0; ctx.fillStyle = '#be8847'; ctx.beginPath(); ctx.ellipse(0, -9, 6, 7, 0, 0, Math.PI * 2); ctx.fill();
            ctx.fillStyle = '#f8e9b2'; for (const x of [-2.5, 2.5]) { ctx.beginPath(); ctx.arc(x, -12, 1.2, 0, 6.28); ctx.fill(); }
            ctx.restore();
        }
        function updateLight(beam) {
            const polygon = clipBeamToRect(beam.points, passwordArea);
            if (polygon.length >= 3 && polygonArea(polygon) > 2 && password.value) {
                reveal.classList.add('is-illuminated'); revealText.textContent = password.value;
                revealText.style.transform = `translateX(${-password.scrollLeft}px)`;
                reveal.style.clipPath = 'polygon(' + polygon.map(p => `${(p.x - overlay.x).toFixed(2)}px ${(p.y - overlay.y).toFixed(2)}px`).join(',') + ')';
            } else { reveal.classList.remove('is-illuminated'); revealText.textContent = ''; }
            const owlHit = polygonArea(clipBeamToRect(beam.points, owlArea)) / Math.max(1, owlArea.w * owlArea.h);
            owl.style.opacity = owlHit > .03 ? String(Math.min(1, owlHit * 3 + .15)) : '0';
            owl.style.filter = 'brightness(' + (1 + owlHit * .7).toFixed(2) + ')';
            owl.classList.toggle('is-illuminated', owlHit > .03);
            const r = root.getBoundingClientRect();
            pumpkins.forEach(p => {
                const a = relativeRect(p, r), inset = a.w * .16;
                const surface = { x: a.x + inset, y: a.y + inset, w: a.w - 2 * inset, h: a.h - 2 * inset };
                p.classList.toggle('is-illuminated', polygonArea(clipBeamToRect(beam.points, surface)) > 25 && night > .4);
            });
        }
        function draw(now) {
            if (!root.isConnected) { resizeObserver.disconnect(); intersectionObserver.disconnect(); return; }
            if (phone.matches) { raf = 0; return; }
            raf = win.requestAnimationFrame(draw);
            if (!visible || doc.hidden || now - painted < 33) return;
            const dt = Math.min((now - last) / 1000, .065); last = now; painted = now;
            if (!motionReduced) elapsed += dt;
            const t = elapsed; night += (Number(root.dataset.mode !== 'day') - night) * Math.min(dt * 2, 1);
            sky.clearRect(0, 0, width, height); ctx.clearRect(0, 0, width, height);
            for (const star of stars) {
                const x = star.x * width, y = star.y * height * .46;
                if (x > scene.x + scene.w && y > card.y) continue;
                sky.globalAlpha = (.35 + Math.sin(t * .7 + star.phase) * .22) * night; sky.fillStyle = '#eddcbd'; sky.beginPath(); sky.arc(x, y, star.r, 0, 6.28); sky.fill();
            }
            sky.globalAlpha = 1;
            if (!pointerInside && !motionReduced) pointer = { x: card.x - 8 + Math.sin(t * .48) * 12, y: card.y + card.h * .3 + Math.sin(t * .7) * 35 };
            const dx = pointer.x - crawler.x, dy = pointer.y - crawler.y;
            const factor = motionReduced ? 1 : 1 - Math.exp(-dt * 5.5);
            const distance = Math.hypot(dx * factor, dy * factor);
            crawler.x += dx * factor; crawler.y += dy * factor;
            if (Math.hypot(dx, dy) > 2) { const angle = Math.atan2(dy, dx) + Math.PI / 2; let delta = (angle - facing + Math.PI * 3) % (Math.PI * 2) - Math.PI; facing += delta * Math.min(1, dt * 9); }
            if (!motionReduced) gait += distance * .13;
            const wolfSize = parseFloat(win.getComputedStyle(wolf).width), imageWidth = scene.h * 1.06 * 1586 / 992;
            const baseX = imageWidth * .267 - (imageWidth - scene.w) * .26 - wolfSize * .62, baseY = scene.h * .45 - wolfSize * .96;
            const cycle = t % 18; let wx = baseX, wy = baseY, flip = 1, opacity = 1;
            if (cycle < 7.5) { sprite(wolf, 0, 1); wy += Math.sin(t * 2.1) * 1.4; }
            else if (cycle < 11.5) { const p = (cycle - 7.5) / 4; sprite(wolf, 1 + Math.floor(t * 10) % 3, 1); wx -= (baseX + 125) * p; wy += scene.h * .19 * p - Math.sin(p * Math.PI) * 20; flip = -1; }
            else if (cycle < 14) opacity = 0;
            else { const p = (cycle - 14) / 4; sprite(wolf, 1 + Math.floor(t * 10) % 3, 1); wx = -120 + (baseX + 120) * p; wy += scene.h * .2 * (1 - p) - Math.sin(p * Math.PI) * 28; }
            wolf.style.left = wx + 'px'; wolf.style.top = wy + 'px'; wolf.style.transform = `scaleX(${flip})`; wolf.style.opacity = String(opacity);
            bats.forEach((bat, i) => { const x = ((t * (i ? 27 : 34) + i * 161) % (scene.w + 100)) - 50; bat.style.transform = `translate(${x}px,${Math.sin(t * 1.3 + i * 2) * 15 + i * 72}px)`; sprite(bat, Math.floor(t * 7 + i) % 2, 2); });
            pupils.forEach(p => { const x = Math.max(-2, Math.min(2, (pointer.x - owlArea.x - owlArea.w / 2) / 80)), y = Math.max(-1.6, Math.min(1.6, (pointer.y - owlArea.y - owlArea.h * .55) / 100)); p.style.transform = `translate(${x}px,${y}px)`; });
            if (night > .01) {
                ctx.save(); ctx.globalAlpha = night; ctx.lineWidth = 1.35; ctx.strokeStyle = '#c58c4d'; ctx.shadowColor = '#eea650'; ctx.shadowBlur = 4;
                const count = Math.max(1, Math.floor(border.length * night)); ctx.beginPath(); for (let i = 0; i < count; i++) { const p = border[i]; if (i) ctx.lineTo(p.x, p.y); else ctx.moveTo(p.x, p.y); } if (count === border.length) ctx.closePath(); ctx.stroke(); ctx.restore();
                for (let i = 1; i < 8; i++) { vine(card.x + card.w * i / 8, card.y - 3, 0, night, i * .27); vine(card.x + card.w * i / 8, card.y + card.h + 3, Math.PI, night, i * .47); }
                for (let i = 1; i < 5; i++) { vine(card.x - 3, card.y + card.h * i / 5, -Math.PI / 2, night, i * .72); vine(card.x + card.w + 3, card.y + card.h * i / 5, Math.PI / 2, night, i * .83); }
                for (const leaf of leaves) drawSprite(2, 2, leaf.x * width + Math.sin(t * .7 + leaf.phase) * 30, (leaf.y * height + t * leaf.speed) % (height + 90) - 45, leaf.size, night * .42, t * .7 + leaf.phase);
            }
            drawSpider(motionReduced ? 0 : Math.min(1, distance / 3));
            if (enabled && pointerInside && !submit.disabled) {
                const beam = makeBeam(origin, pointer, passwordArea.w + 95); updateLight(beam);
                ctx.save(); ctx.translate(origin.x, origin.y); ctx.rotate(beam.angle);
                const g = ctx.createLinearGradient(0, 0, beam.length, 0); g.addColorStop(0, 'rgba(255,227,153,.43)'); g.addColorStop(.6, 'rgba(255,219,135,.17)'); g.addColorStop(1, 'rgba(255,219,135,0)');
                ctx.fillStyle = g; ctx.shadowColor = '#f7d68c'; ctx.shadowBlur = 16;
                ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(beam.length, -beam.length * .18); ctx.lineTo(beam.length, beam.length * .18); ctx.closePath(); ctx.fill(); ctx.restore();
                flashlight.style.transform = `rotate(${beam.angle * 180 / Math.PI + 90}deg)`;
            } else clearLight();
        }
        phone.addEventListener('change', () => {
            enabled = false; pointerInside = false; root.classList.remove('flashlight-active');
            toggle.setAttribute('aria-pressed', 'false'); clearLight();
            if (phone.matches) { win.cancelAnimationFrame(raf); raf = 0; layout(); }
            else {
                if (!atlas.src) atlas.src = root.dataset.halloweenAtlas;
                last = win.performance.now(); layout();
                if (!raf) raf = win.requestAnimationFrame(draw);
            }
        });
        if (!phone.matches) raf = win.requestAnimationFrame(draw);
    }
    return { makeBeam, clipBeamToRect, polygonArea, init };
});
