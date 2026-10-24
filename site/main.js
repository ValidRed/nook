// Nook site: the opening splash, a live demo for each feature, and the newest release.
(() => {
  'use strict';

  const REPO = 'ValidRed/nook';
  const root = document.documentElement;
  const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
  const sleep = ms => new Promise(r => setTimeout(r, ms));

  const EASE = {
    inQuad: 'cubic-bezier(0.55, 0.085, 0.68, 0.53)',
    outQuad: 'cubic-bezier(0.25, 0.46, 0.45, 0.94)',
    backOut: 'cubic-bezier(0.34, 1.56, 0.64, 1)',
    outExpo: 'cubic-bezier(0.16, 1, 0.3, 1)',
    inOutCubic: 'cubic-bezier(0.65, 0, 0.35, 1)',
    win: 'cubic-bezier(0.1, 0.9, 0.2, 1)',
    move: 'cubic-bezier(0.45, 0.05, 0.25, 1)'
  };

  // Runs an animation, then keeps its end state as inline style so animations don't pile up.
  function run(el, frames, opts) {
    const a = el.animate(frames, Object.assign({ fill: 'forwards' }, opts));
    return a.finished.then(() => {
      try { a.commitStyles(); } catch (_) { /* element hidden; nothing to keep */ }
      a.cancel();
    }, () => {});
  }

  let readyCallbacks = [];
  function ready() {
    root.classList.remove('intro');
    root.classList.add('ready');
    readyCallbacks.splice(0).forEach(f => f());
  }
  function whenReady(f) {
    if (root.classList.contains('ready')) f(); else readyCallbacks.push(f);
  }

  // ---------- intro: the same fluid drain as the app's first launch ----------

  function playIntro() {
    if (!root.classList.contains('intro')) { ready(); return; }
    try { sessionStorage.setItem('nook-intro', '1'); } catch (_) { /* private mode */ }

    const splash = $('.splash'), bg = $('.splash-bg'), svg = $('.splash-fluid');
    const logo = $('.splash-logo'), tile = $('.splash-tile'), squares = $$('.splash-sq');
    const navLogo = $('[data-nav-logo]');
    const W = innerWidth, H = innerHeight;
    const period = 240, amp = 9, drainMs = 1050;
    const startY = -3 * amp, endY = H + 3 * amp;
    const timers = [];
    let done = false;

    splash.classList.add('on');
    navLogo.style.opacity = '0';
    svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
    svg.setAttribute('preserveAspectRatio', 'none');
    svg.innerHTML =
      '<defs>' +
      '<linearGradient id="fluidFront" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3d8bf2" stop-opacity=".97"/><stop offset="1" stop-color="#1a55c4"/></linearGradient>' +
      '<linearGradient id="fluidBack" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#7ab4ff" stop-opacity=".6"/><stop offset="1" stop-color="#3d8bf2" stop-opacity=".6"/></linearGradient>' +
      '</defs>';

    // A wide band of fluid with a sine-wave surface; it slides sideways (waves) and down (draining).
    function wave(a, fill) {
      const width = W + period * 2;
      let d = `M0 ${a}`;
      for (let x = 0; x <= width; x += 6) d += ` L${x} ${(a + Math.sin(x / period * 2 * Math.PI) * a).toFixed(2)}`;
      d += ` L${width} ${H + a * 6} L0 ${H + a * 6} Z`;
      const g = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      const p = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      p.setAttribute('d', d);
      p.setAttribute('fill', fill);
      g.appendChild(p);
      svg.appendChild(g);
      return { g, p };
    }
    function flow(w, fromY, toY, drain, waveMs) {
      w.p.animate([{ transform: 'translateX(0)' }, { transform: `translateX(${-period}px)` }], { duration: waveMs, iterations: Infinity });
      w.g.animate([{ transform: `translateY(${fromY}px)` }, { transform: `translateY(${toY}px)` }], { duration: drain, easing: EASE.inQuad, fill: 'forwards' });
    }
    const back = wave(amp * 0.75, 'url(#fluidBack)');
    const front = wave(amp, 'url(#fluidFront)');
    flow(back, startY - 8, endY, drainMs + 90, 1500);
    flow(front, startY, endY, drainMs, 1100);
    // When the surface passes a given height (it speeds up like water going down a drain).
    const passes = y => Math.sqrt(Math.max(0, Math.min(1, (y - startY) / (endY - startY)))) * drainMs;

    // The squares pop up out of the fluid as the surface passes each one.
    const box = logo.getBoundingClientRect();
    let lastPop = 0;
    squares.forEach((sq, i) => {
      const r = sq.getBoundingClientRect();
      const b = Math.max(0, passes(r.top + r.height / 2) - 30 + (i % 2) * 45);
      lastPop = Math.max(lastPop, b);
      sq.animate([{ scale: '0.2' }, { scale: '1' }], { delay: b, duration: 480, easing: EASE.backOut, fill: 'both' });
      sq.animate([{ translate: '0 16px' }, { translate: '0 0' }], { delay: b, duration: 520, easing: EASE.outExpo, fill: 'both' });
      sq.animate([{ opacity: 0 }, { opacity: 1 }], { delay: b, duration: 140, fill: 'both' });
    });

    // A few droplets leap from the surface as it uncovers the bottom of the logo.
    const splashAt = passes(box.bottom) - 20, cx = box.left + box.width / 2;
    let seed = 7;
    const rnd = n => (seed = (seed * 16807) % 2147483647) % n;
    for (let i = 0; i < 7; i++) {
      const size = 4 + rnd(4), t0 = splashAt + i * 18, rise = 26 + rnd(34), spread = (i - 3) * (6 + rnd(8));
      const drop = document.createElement('i');
      drop.className = 'splash-drop';
      drop.style.cssText = `width:${size}px;height:${size}px;left:${cx - size / 2 + (i - 3) * 14}px;top:${box.bottom - 4}px`;
      splash.appendChild(drop);
      drop.animate([
        { translate: '0 0', easing: EASE.outQuad },
        { translate: `${spread * 0.48}px ${-rise}px`, offset: 300 / 620, easing: EASE.inQuad },
        { translate: `${spread}px 18px` }
      ], { delay: t0, duration: 620, fill: 'both' });
      drop.animate([{ opacity: 0 }, { opacity: 0.9, offset: 60 / 620 }, { opacity: 0.9, offset: 420 / 620 }, { opacity: 0 }], { delay: t0, duration: 620, fill: 'both' });
    }

    // A soft pulse once everything has landed, then the logo glides into the nav bar while the page rises in.
    const settled = lastPop + 520, glideAt = Math.max(drainMs, settled) + 120, landAt = glideAt + 480;
    tile.animate([{ scale: '1' }, { scale: '1.04', offset: 0.45 }, { scale: '1' }], { delay: settled - 60, duration: 220, easing: EASE.inOutCubic });
    const target = navLogo.getBoundingClientRect();
    const dx = target.left + target.width / 2 - cx, dy = target.top + target.height / 2 - (box.top + box.height / 2);
    logo.animate([{ transform: 'none' }, { transform: `translate(${dx}px, ${dy}px) scale(${target.width / box.width})` }],
      { delay: glideAt, duration: 480, easing: EASE.inOutCubic, fill: 'forwards' });
    logo.animate([{ opacity: 1 }, { opacity: 0 }], { delay: landAt - 120, duration: 120, fill: 'forwards' });
    bg.animate([{ opacity: 1 }, { opacity: 0 }], { delay: glideAt, duration: 360, easing: 'ease-out', fill: 'forwards' });

    function finish() {
      if (done) return;
      done = true;
      timers.forEach(clearTimeout);
      splash.classList.remove('on');
      splash.querySelectorAll('*').forEach(e => e.getAnimations().forEach(a => a.cancel()));
      navLogo.style.opacity = '';
      ready();
    }
    timers.push(setTimeout(ready, glideAt));
    timers.push(setTimeout(() => run(navLogo, [{ opacity: 0 }, { opacity: 1 }], { duration: 120 }).then(() => { navLogo.style.opacity = ''; }), landAt - 80));
    timers.push(setTimeout(finish, landAt + 40));
    splash.addEventListener('pointerdown', finish);
    addEventListener('keydown', finish, { once: true });
    addEventListener('wheel', finish, { once: true, passive: true });
  }

  // ---------- pieces the demos are built from ----------

  const U = 84, EDGE = 20, AREA = 577; // icon grid unit, desktop margin, and the height above the taskbar
  const NAMES = {
    'microsoft-edge': 'Microsoft Edge', 'file-explorer': 'File Explorer', outlook: 'Outlook', notepad: 'Notepad',
    paint: 'Paint', photos: 'Photos', 'snipping-tool': 'Snipping Tool', camera: 'Camera', 'sound-recorder': 'Sound Recorder',
    'media-player': 'Media Player', terminal: 'Terminal', settings: 'Settings', 'microsoft-store': 'Microsoft Store',
    'windows-security': 'Windows Security', clock: 'Clock', weather: 'Weather', calculator: 'Calculator',
    'sticky-notes': 'Sticky Notes', 'phone-link': 'Phone Link', 'microsoft-to-do': 'Microsoft To Do', 'control-panel': 'Control Panel'
  };
  const img = id => `<img src="assets/icons/${id}.png" alt="" draggable="false">`;
  const MAG = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"><circle cx="10.5" cy="10.5" r="6.5"/><path d="m15.5 15.5 5 5"/></svg>';
  const CAPS = '<div class="aw-caps"><span><svg viewBox="0 0 10 10"><path d="M0 5h10"/></svg></span><span><svg viewBox="0 0 10 10"><rect x=".5" y=".5" width="9" height="9" rx="1.5"/></svg></span><span><svg viewBox="0 0 10 10"><path d="M0 0l10 10M10 0 0 10"/></svg></span></div>';
  const ARROW = '<svg viewBox="0 0 22 30"><path d="M2 2v21l5.2-4.8 3.4 8 3.3-1.4-3.3-7.8h6.8z" fill="#fff" stroke="#000" stroke-width="1.3" stroke-linejoin="round"/></svg>';
  const WS_ICONS = {
    code: '<svg viewBox="0 0 24 24"><path d="M9 7l-5 5 5 5M15 7l5 5-5 5"/></svg>',
    design: '<svg viewBox="0 0 24 24"><path d="M12 3a9 9 0 1 0 0 18c1.1 0 1.6-.8 1.6-1.6 0-.5-.2-.8-.4-1.1-.3-.3-.4-.6-.4-1.1 0-.9.7-1.6 1.6-1.6H16a5 5 0 0 0 5-5c0-4.2-4-7.6-9-7.6z"/><circle cx="7.5" cy="11" r="1"/><circle cx="10" cy="7.3" r="1"/><circle cx="14.5" cy="7.3" r="1"/></svg>'
  };
  const BLOBS = '<i class="blob b1"></i><i class="blob b2"></i><i class="blob b3"></i><i class="blob b4"></i><i class="blob b5"></i>';
  const blob = (x, y, w, h, color, opacity, cls = '') =>
    `<i class="blob ${cls}" style="left:${x}px;top:${y}px;width:${w}px;height:${h}px;background:${color};opacity:${opacity}"></i>`;

  function make(tag, cls, html) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html != null) e.innerHTML = html;
    return e;
  }
  const place = (e, r) => {
    e.style.left = r.x + 'px';
    e.style.top = r.y + 'px';
    if (r.w != null) { e.style.width = r.w + 'px'; e.style.height = r.h + 'px'; }
  };

  // The time the way Nook's clocks show it: just the digits, no AM or PM.
  const timeFormat = new Intl.DateTimeFormat([], { hour: 'numeric', minute: '2-digit' });
  const clockText = now => timeFormat.formatToParts(now).filter(p => p.type !== 'dayPeriod').map(p => p.value).join('').trim();
  const dateText = now => now.toLocaleDateString([], { weekday: 'long', month: 'long', day: 'numeric' });
  const islands = [];
  function tick() {
    const now = new Date();
    $$('[data-now]').forEach(e => { e.textContent = clockText(now); });
    $$('[data-today]').forEach(e => { e.textContent = dateText(now); });
    $$('[data-tb-time]').forEach(e => { e.textContent = now.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }); });
    $$('[data-tb-date]').forEach(e => { e.textContent = now.toLocaleDateString(); });
    islands.forEach(i => i.refit());
  }

  // A demo screen: a desk drawn at a fixed size and scaled to fit, which only plays while it's on screen.
  function stageKit(stage) {
    const desk = $('.desk', stage);
    const W = parseFloat(stage.style.getPropertyValue('--w')) || 1000;
    const H = parseFloat(stage.style.getPropertyValue('--h')) || 625;
    const fit = () => stage.style.setProperty('--s', stage.clientWidth / W);
    fit();
    new ResizeObserver(fit).observe(stage);

    let onScreen = false;
    const waiting = [];
    const wake = () => { if (onScreen && !document.hidden) waiting.splice(0).forEach(r => r()); };
    new IntersectionObserver(es => { onScreen = es[0].isIntersecting; wake(); }, { threshold: 0.2 }).observe(stage);
    document.addEventListener('visibilitychange', wake);

    return {
      stage, desk, W, H,
      add: (cls, html) => desk.appendChild(make('div', cls, html)),
      visible: () => new Promise(r => { waiting.push(r); wake(); }),
      // Position of an element's center in desk coordinates.
      center(e) {
        let x = e.offsetWidth / 2, y = e.offsetHeight / 2;
        for (let n = e; n && n !== desk; n = n.offsetParent) { x += n.offsetLeft; y += n.offsetTop; }
        return { x, y };
      }
    };
  }

  // Plays a demo's scenes over and over, waiting whenever it's scrolled away or the tab is hidden.
  function loop(kit, scenes, pause = 800) {
    whenReady(async () => {
      await sleep(600);
      for (let i = 0; ; i = (i + 1) % scenes.length) {
        await kit.visible();
        try { await scenes[i](); } catch (_) { /* keep looping */ }
        await sleep(pause);
      }
    });
  }

  // The list beside a demo: the point it's showing right now stands out.
  function points(stage) {
    const list = $('.points', stage.closest('.feature'));
    const items = list ? $$('li', list) : [];
    return {
      set(i) {
        if (!list) return;
        list.classList.add('live');
        items.forEach((li, k) => li.classList.toggle('on', k === i));
      }
    };
  }

  function pointer(kit, start) {
    const el = kit.add('cursor', ARROW);
    let cur = Object.assign({}, start), hovered = null;
    const at = p => `translate(${p.x - 2}px, ${p.y - 2}px)`; // the arrow's tip is 2px in
    el.style.transform = at(cur);
    return {
      get pos() { return cur; },
      moveTo(x, y, ms) {
        const from = at(cur);
        cur = { x, y };
        el.style.transform = at(cur);
        return el.animate([{ transform: from }, { transform: at(cur) }], { duration: ms, easing: EASE.move }).finished.catch(() => {});
      },
      press(down) { el.classList.toggle('down', down); },
      async click() {
        el.classList.add('down');
        const r = kit.add('ripple');
        r.style.left = cur.x + 'px';
        r.style.top = cur.y + 'px';
        r.animate([{ transform: 'scale(0.3)', opacity: 0.9 }, { transform: 'scale(1.3)', opacity: 0 }], { duration: 450, easing: 'ease-out' })
          .finished.then(() => r.remove(), () => r.remove());
        await sleep(110);
        el.classList.remove('down');
      },
      hover(e) {
        if (hovered) hovered.classList.remove('hover');
        hovered = e;
        if (e) e.classList.add('hover');
      },
      fade(on) { return run(el, [{ opacity: on ? 0 : 1 }, { opacity: on ? 1 : 0 }], { duration: 220 }); }
    };
  }

  function taskbar(kit, pinned) {
    const tb = kit.add('taskbar',
      `<div class="tb-search">${MAG}Search</div><div class="tb-apps">${pinned.map(id => `<div class="tb-app" data-tb="${id}">${img(id)}</div>`).join('')}</div>` +
      '<div class="tb-tray"><span data-tb-time></span><span data-tb-date></span></div>');
    const apps = $('.tb-apps', tb);
    return {
      // Lights up an app's button, adding it if it isn't pinned; returns a function that turns it off again.
      open(id) {
        let b = $(`[data-tb="${id}"]`, apps);
        const isPinned = !!b;
        if (!b) {
          b = apps.appendChild(make('div', 'tb-app', img(id)));
          b.dataset.tb = id;
          run(b, [{ opacity: 0, transform: 'scale(0.4)' }, { opacity: 1, transform: 'none' }], { duration: 260, easing: EASE.backOut });
        }
        $$('.tb-app', apps).forEach(x => x.classList.remove('active'));
        b.classList.add('running', 'active');
        return async () => {
          b.classList.remove('active', 'running');
          if (isPinned) return;
          await run(b, [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'scale(0.4)' }], { duration: 180 });
          b.remove();
        };
      }
    };
  }

  // Widgets line up from the desk's top-right corner, one desktop icon column per unit.
  const cellX = (kit, g) => kit.W - EDGE - (g.col + g.cols) * U;
  const cellY = (g, row = g.row) => EDGE + row * U;
  function widget(kit, g) {
    let w;
    if (g.cols === 1 && g.rows === 1) {
      w = make('div', 'tile1', `<div class="tile1-icon">${g.apps.slice(0, 4).map(img).join('')}</div><span>${g.name}</span>`);
    } else {
      const apps = g.apps.map(id => `<div class="w-app" data-app="${id}">${img(id)}${g.labels ? `<span>${NAMES[id]}</span>` : ''}</div>`).join('');
      w = make('div', 'widget' + (g.color ? ' ' + g.color : ''),
        `<div class="w-panel${g.labels ? ' with-labels' : ''}"><div class="w-title">${g.name}</div>` +
        `<div class="w-apps" style="grid-template-columns:repeat(${g.cols},1fr)">${apps}</div></div>`);
      w.style.width = g.cols * U + 'px';
      w.style.height = g.rows * U + 'px';
    }
    place(w, { x: cellX(kit, g), y: cellY(g) });
    return kit.desk.appendChild(w);
  }

  // A workspace button: an icon tile, with a name only if it has one.
  function wsButton(kit, x, y, icon, name) {
    const b = kit.add('ws-btn', `<div class="ws-tile">${WS_ICONS[icon]}</div>${name ? `<span>${name}</span>` : ''}`);
    place(b, { x, y });
    return b;
  }

  // Simple sketches of app windows.
  const textLines = (widths, top) => widths.map((w, i) => w ? `<div class="sk" style="left:16px;top:${top + i * 18}px;width:calc(${w}% - 32px);height:8px"></div>` : '').join('');
  const WINDOWS = {
    'microsoft-edge': {
      cls: 'light',
      body: '<div class="sk" style="left:12px;right:12px;top:10px;height:22px;border-radius:11px"></div>' +
        '<div class="sk" style="left:12px;right:12px;top:44px;height:42%;background:linear-gradient(135deg,#9cc8ff,#3d8bf2 55%,#1a55c4)"></div>' +
        '<div class="sk" style="left:12px;width:50%;top:calc(42% + 58px);height:14px"></div>' +
        '<div class="sk" style="left:12px;width:78%;top:calc(42% + 84px);height:9px"></div>' +
        '<div class="sk" style="left:12px;width:66%;top:calc(42% + 100px);height:9px"></div>'
    },
    terminal: {
      cls: 'dark-term',
      body: '<div class="term-lines">PS C:\\code\\site&gt; <i>npm</i> run dev\n\n  ready in 412 ms\n  Local:  http://localhost:5173/\n\nPS C:\\code\\site&gt; <span class="s-caret"></span></div>'
    },
    notepad: {
      cls: 'light',
      body: '<div class="sk" style="left:0;right:0;top:0;height:28px;border-radius:0"></div>' + textLines([86, 64, 92, 40, 0, 78, 88, 55, 0, 70, 46, 82, 60], 44)
    },
    paint: {
      cls: 'light',
      body: '<div class="sk" style="left:0;right:0;top:0;height:40px;border-radius:0"></div>' +
        ['#1c1c1c', '#e81123', '#ff8c00', '#fff100', '#16c60c', '#0078d4', '#886ce4'].map((c, i) =>
          `<i class="sk" style="left:${14 + i * 20}px;top:13px;width:14px;height:14px;border-radius:50%;background:${c}"></i>`).join('') +
        '<div class="sk" style="left:14px;right:14px;top:54px;bottom:14px;border-radius:2px;background:#fff;box-shadow:0 1px 4px rgba(0,0,0,.12)">' +
        '<svg viewBox="0 0 200 120" preserveAspectRatio="xMidYMid slice" style="width:100%;height:100%;display:block"><circle cx="126" cy="36" r="16" fill="#ffb900"/>' +
        '<path d="M0 92 Q 40 60 80 84 T 160 78 T 200 70 V120 H0Z" fill="#0078d4" opacity=".85"/><path d="M0 104 Q 50 84 100 100 T 200 92 V120 H0Z" fill="#16c60c" opacity=".8"/></svg></div>'
    },
    photos: {
      cls: '',
      body: '<div class="ph-grid">' + ['linear-gradient(135deg,#f26b3a,#8e2de2)', 'linear-gradient(160deg,#7ab4ff,#1a55c4)', 'linear-gradient(135deg,#16c60c,#0e5e6f)',
        'linear-gradient(200deg,#ffb900,#e81123)', 'linear-gradient(135deg,#5a9ef8,#6b2e73)', 'linear-gradient(160deg,#9cc8ff,#2e6b3f)'].map(g => `<i style="background:${g}"></i>`).join('') + '</div>'
    },
    'file-explorer': {
      cls: '',
      body: '<div class="sk" style="left:8px;top:8px;bottom:8px;width:24%"></div>' +
        [0, 1, 2, 3, 4, 5].map(i => `<div class="sk" style="left:calc(24% + 20px);right:10px;top:${10 + i * 26}px;height:18px"></div>`).join('')
    }
  };
  function appWindow(parent, id, r) {
    const spec = WINDOWS[id];
    const w = parent.appendChild(make('div', 'win' + (spec.cls ? ' ' + spec.cls : ''),
      `<div class="aw-bar">${img(id)}<span>${NAMES[id]}</span>${CAPS}</div><div class="win-body">${spec.body}</div>`));
    place(w, r);
    return w;
  }

  // The desktop clock, like a phone's lock screen: the date above big digits (and, if asked, what's playing
  // below), centered on a point.
  function deskClock(parent, x, y, glance) {
    const c = parent.appendChild(make('div', 'dclock', '<small data-today></small><b data-now></b>' + (glance ? `<em>${glance}</em>` : '')));
    place(c, { x, y });
    return c;
  }
  // Any size, like the app's slider; the date and the glance row are about a fifth of the time.
  function sizeClock(c, time) {
    const small = Math.max(7, Math.round(time * 0.196)) + 'px';
    $('b', c).style.fontSize = time + 'px';
    $$('small, em', c).forEach(e => { e.style.fontSize = small; });
  }

  // ---------- the island ----------

  const PREV = '<svg viewBox="0 0 24 24"><path d="M6 5h2.2v14H6zM19.5 5.5v13L9.5 12z"/></svg>';
  const PAUSE = '<svg class="big" viewBox="0 0 24 24"><rect x="6" y="4.5" width="4" height="15" rx="1.2"/><rect x="14" y="4.5" width="4" height="15" rx="1.2"/></svg>';
  const NEXT = '<svg viewBox="0 0 24 24"><path d="M15.8 5H18v14h-2.2zM4.5 5.5v13l10-6.5z"/></svg>';
  const HEADPHONES = '<svg viewBox="0 0 24 24"><path d="M4 15v-3a8 8 0 0 1 16 0v3"/><rect x="3" y="14" width="4" height="6" rx="1.5"/><rect x="17" y="14" width="4" height="6" rx="1.5"/></svg>';
  const PAGES = ['musicPage', 'timePage', 'sysPage'];
  const STATS = [
    { name: 'CPU', v: 14, min: 3, max: 64, top: 100, fmt: v => Math.round(v) + '%' },
    { name: 'Memory', v: 41, min: 38, max: 47, top: 100, fmt: v => Math.round(v) + '%' },
    { name: 'GPU', v: 9, min: 2, max: 38, top: 100, fmt: v => Math.round(v) + '%' },
    { name: 'Network', v: 1.4, min: 0.1, max: 6.5, top: 8, fmt: v => v.toFixed(1) + ' MB/s' }
  ];
  const SONG = 221; // 3:41
  const mmss = s => Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');

  function bars(n) {
    let h = '';
    for (let i = 0; i < n; i++) h += `<i style="--d:${(0.36 + ((i * 37) % 23) / 70).toFixed(2)}s;--o:-${(i * 0.21).toFixed(2)}s"></i>`;
    return `<span class="bars">${h}</span>`;
  }

  function island(kit) {
    const stats = STATS.map(s => Object.assign({}, s, { hist: Array.from({ length: 24 }, () => s.v) }));
    const isle = kit.add('isle',
      '<div class="isle-p compact" data-p="time"><span class="t" data-now></span></div>' +
      `<div class="isle-p compact music" data-p="music"><span class="art"></span><span class="t" data-now></span>${bars(5)}</div>` +
      `<div class="isle-p music-pg" data-p="musicPage"><div class="mp-top"><span class="art"></span><div><b>Midnight Drive</b><small>Neon Coast</small></div>${bars(6)}</div>` +
      `<div class="mp-bar"><span data-elapsed>1:24</span><span class="track"><i></i></span><span>${mmss(SONG)}</span></div>` +
      `<div class="mp-ctl">${PREV}${PAUSE}${NEXT}</div></div>` +
      '<div class="isle-p time-pg" data-p="timePage"><b data-now></b><small data-today></small></div>' +
      '<div class="isle-p sys-pg" data-p="sysPage">' + stats.map(s =>
        `<div class="stat"><div><small>${s.name}</small><b></b></div><svg viewBox="0 0 60 30" preserveAspectRatio="none"><path class="sl-fill"/><path class="sl-line"/></svg></div>`).join('') + '</div>' +
      `<div class="isle-p phones" data-p="phones">${HEADPHONES}<b>Headphones</b><em>76%</em></div>` +
      '<div class="dots"><i></i><i></i><i></i></div>');
    const panels = {};
    $$('.isle-p', isle).forEach(p => { panels[p.dataset.p] = p; });
    const now0 = new Date();
    $$('[data-now]', isle).forEach(e => { e.textContent = clockText(now0); });
    $$('[data-today]', isle).forEach(e => { e.textContent = dateText(now0); });

    let now = null;
    function fit() {
      const p = panels[now];
      isle.style.width = p.offsetWidth + 'px';
      isle.style.height = p.offsetHeight + 'px';
      isle.style.borderRadius = now === 'time' || now === 'music' ? '17px' : now === 'phones' ? '20px' : '26px';
    }
    function show(name) {
      const from = PAGES.indexOf(now), to = PAGES.indexOf(name);
      if (now) panels[now].classList.remove('on');
      // Scrolling between pages slides them up or down.
      if (from >= 0 && to >= 0 && !reduceMotion) {
        const dir = to > from ? 1 : -1;
        panels[now].animate([{ translate: '0 0' }, { translate: `0 ${-16 * dir}px` }], { duration: 320, easing: EASE.outExpo });
        panels[name].animate([{ translate: `0 ${16 * dir}px` }, { translate: '0 0' }], { duration: 560, easing: EASE.outExpo });
      }
      now = name;
      panels[name].classList.add('on');
      fit();
      isle.classList.toggle('open', to >= 0);
      $$('.dots i', isle).forEach((d, i) => d.classList.toggle('on', i === to));
      $$('.bars', isle).forEach(b => b.classList.toggle('play', name === 'music' || name === 'musicPage'));
      if (name === 'sysPage') drawStats(false);
    }

    function drawStats(step) {
      stats.forEach((s, i) => {
        if (step) {
          s.v = Math.max(s.min, Math.min(s.max, s.v + (Math.random() - 0.5) * (s.max - s.min) * 0.3));
          s.hist.push(s.v);
          s.hist.shift();
        }
        const el = $$('.stat', panels.sysPage)[i];
        $('b', el).textContent = s.fmt(s.v);
        const pts = s.hist.map((v, k) => `${(k / (s.hist.length - 1) * 60).toFixed(1)} ${(28 - v / s.top * 24).toFixed(1)}`);
        const line = 'M' + pts.join(' L');
        $('.sl-line', el).setAttribute('d', line);
        $('.sl-fill', el).setAttribute('d', line + ' L60 30 L0 30 Z');
      });
    }
    drawStats(false);

    // The song keeps playing and the stats keep moving, a second at a time.
    let elapsed = 84;
    const track = $('.track i', isle), elapsedText = $('[data-elapsed]', isle);
    const second = () => {
      elapsed = (elapsed + 1) % SONG;
      elapsedText.textContent = mmss(elapsed);
      track.style.width = (elapsed / SONG * 100).toFixed(1) + '%';
      if (now === 'sysPage') drawStats(true);
    };
    second();
    if (!reduceMotion) setInterval(() => { if (!document.hidden) second(); }, 1000);

    const api = {
      el: isle,
      show,
      refit() { if (now) fit(); },
      glass(on) { isle.classList.toggle('glass', on); }
    };
    islands.push(api);
    return api;
  }

  // ---------- hero: everything at once, calmly ----------

  function startHero() {
    const stage = $('[data-demo="hero"]');
    if (!stage) return;
    const kit = stageKit(stage);
    kit.add('wall', BLOBS);
    wsButton(kit, EDGE, EDGE, 'code', 'Coding');
    wsButton(kit, EDGE, EDGE + U, 'design');
    widget(kit, { name: 'Work', cols: 2, rows: 2, col: 0, row: 0, apps: ['microsoft-edge', 'file-explorer', 'outlook', 'notepad'] });
    widget(kit, { name: 'Create', cols: 3, rows: 2, col: 0, row: 2, apps: ['paint', 'photos', 'snipping-tool', 'camera', 'sound-recorder', 'media-player'] });
    widget(kit, { name: 'Tools', cols: 1, rows: 1, col: 0, row: 4, apps: ['terminal', 'settings', 'microsoft-store', 'windows-security'] });
    // The desk is drawn like a 1600-wide screen, so the clock is the app's 112 px at 0.625, in the upper third.
    const clock = deskClock(kit.desk, kit.W / 2, AREA * 0.32);
    sizeClock(clock, 112 * 0.625);
    const isle = island(kit);
    taskbar(kit, ['file-explorer', 'microsoft-edge', 'microsoft-store']);
    isle.show('music');
    if (reduceMotion) return;

    loop(kit, [async () => {
      await sleep(5200);
      isle.show('musicPage');
      await sleep(3400);
      isle.show('music');
      await sleep(5200);
      isle.show('phones');
      await sleep(2400);
      isle.show('music');
    }], 0);
  }

  // ---------- groups ----------

  function startGroups() {
    const stage = $('[data-demo="groups"]');
    if (!stage) return;
    const kit = stageKit(stage), pts = points(stage);
    const GROUPS = {
      tools: { name: 'Tools', cols: 1, rows: 1, col: 0, row: 0, apps: ['terminal', 'settings', 'microsoft-store', 'windows-security'] },
      work: { name: 'Work', cols: 2, rows: 2, col: 1, row: 0, apps: ['microsoft-edge', 'file-explorer', 'outlook', 'notepad'] },
      everyday: { name: 'Everyday', cols: 2, rows: 3, col: 1, row: 2, labels: true, color: 'teal', apps: ['clock', 'weather', 'calculator', 'sticky-notes', 'phone-link', 'microsoft-to-do'] },
      create: { name: 'Create', cols: 3, rows: 2, col: 3, row: 0, apps: ['paint', 'photos', 'snipping-tool', 'camera', 'sound-recorder', 'media-player'] }
    };
    const RESULTS = {
      p: [['paint', 'In Create'], ['photos', 'In Create'], ['phone-link', 'In Everyday']],
      pa: [['paint', 'In Create'], ['control-panel', 'Windows app'], ['notepad', 'In Work']]
    };

    kit.add('wall', BLOBS);
    const ghost = kit.add('ghost');
    Object.values(GROUPS).forEach(g => { g.el = widget(kit, g); });
    const tools = GROUPS.tools;
    const popup = kit.add('popup acrylic',
      `<div class="popup-head">${tools.name}<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"><path d="M4 20h4L19 9l-4-4L4 16z"/><path d="m13.5 6.5 4 4"/></svg></div>` +
      `<div class="popup-apps">${tools.apps.map(id => `<div class="p-app" data-app="${id}">${img(id)}<span>${NAMES[id]}</span></div>`).join('')}</div>`);
    place(popup, { x: cellX(kit, tools) - 8 - popup.offsetWidth, y: EDGE + 2 });
    const appwin = kit.add('appwin', `<div class="aw-bar"><img alt=""><span></span>${CAPS}</div><div class="aw-body"></div>`);
    const search = kit.add('search acrylic',
      `<div class="s-box"><span class="s-text"></span><span class="s-caret"></span><span class="s-placeholder">Search your groups and apps</span>${MAG}</div>` +
      '<div class="s-results"></div><div class="s-hint">↑ ↓ to choose · Enter to open · Esc to close</div>');
    const keys = kit.add('keys acrylic');
    const tb = taskbar(kit, ['file-explorer', 'microsoft-edge', 'microsoft-store']);

    if (reduceMotion) {
      popup.classList.add('open');
      popup.style.opacity = '1';
      return;
    }
    const ptr = pointer(kit, { x: 560, y: 470 });

    async function openPopup() {
      popup.classList.add('open');
      await run(popup, [{ opacity: 0, transform: 'translateY(8px) scale(0.98)' }, { opacity: 1, transform: 'none' }], { duration: 260, easing: EASE.win });
    }
    async function closePopup() {
      await run(popup, [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateY(4px)' }], { duration: 140, easing: 'ease-in' });
      popup.classList.remove('open');
    }

    // Opens an app window the way Windows 11 does; returns a function that closes it.
    async function openApp(id) {
      const terminal = id === 'terminal';
      appwin.className = 'appwin open' + (terminal ? ' terminal' : '');
      $('.aw-bar img', appwin).src = `assets/icons/${id}.png`;
      $('.aw-bar span', appwin).textContent = NAMES[id];
      $('.aw-body', appwin).innerHTML = terminal ? 'PS C:\\Users\\you&gt;&nbsp;<span class="s-caret"></span>' : img(id);
      const off = tb.open(id);
      await run(appwin, [{ opacity: 0, transform: 'scale(0.92)' }, { opacity: 1, transform: 'none' }], { duration: 280, easing: EASE.win });
      return async () => {
        await run(appwin, [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'scale(0.96)' }], { duration: 160, easing: 'ease-in' });
        appwin.classList.remove('open');
        await off();
      };
    }

    function setQuery(q) {
      $('.s-text', search).textContent = q;
      $('.s-placeholder', search).style.display = q ? 'none' : '';
      $('.s-results', search).innerHTML = (RESULTS[q] || []).map((r, i) =>
        `<div class="s-row${i === 0 ? ' sel' : ''}">${img(r[0])}<div><b>${NAMES[r[0]]}</b><small>${r[1]}</small></div></div>`).join('');
    }
    async function showKeys(html) {
      keys.innerHTML = html;
      await run(keys, [{ opacity: 0, transform: 'translate(-50%, 8px)' }, { opacity: 1, transform: 'translate(-50%, 0)' }], { duration: 240, easing: EASE.win });
      return $$('kbd', keys);
    }
    const hideKeys = () => run(keys, [{ opacity: 1 }, { opacity: 0 }], { duration: 200 });

    async function popupScene() {
      pts.set(0);
      const t = kit.center($('.tile1-icon', tools.el));
      await ptr.moveTo(t.x + 4, t.y + 6, 900);
      ptr.hover(tools.el);
      await sleep(260);
      await ptr.click();
      ptr.hover(null);
      await openPopup();
      await sleep(550);
      const term = $('[data-app="terminal"]', popup), c = kit.center(term);
      await ptr.moveTo(c.x + 6, c.y + 4, 650);
      ptr.hover(term);
      await sleep(320);
      await ptr.click();
      ptr.hover(null);
      closePopup();
      const close = await openApp('terminal');
      await ptr.moveTo(ptr.pos.x - 140, ptr.pos.y + 250, 1000);
      await sleep(900);
      await close();
    }

    async function dragScene() {
      pts.set(1);
      const g = GROUPS.create, w = g.el, title = $('.w-title', w);
      const toRow = g.row === 0 ? 2 : 0, dy = (toRow - g.row) * U;
      const grab = kit.center(title);
      grab.y += 2;
      await ptr.moveTo(grab.x, grab.y, 900);
      ptr.hover(title);
      await sleep(260);
      ptr.press(true);
      ptr.hover(null);
      w.classList.add('lifted');
      place(ghost, { x: cellX(kit, g) + 3, y: cellY(g) + 3, w: g.cols * U - 6, h: g.rows * U - 6 });
      ghost.classList.add('show');
      await sleep(200);
      // The pointer lets go a little off the grid; the widget then glides into the outlined spot.
      const off = { x: -9, y: dy + (dy > 0 ? 11 : -11) }, ms = 1100;
      const step = toRow > g.row ? 1 : -1;
      setTimeout(() => { ghost.style.top = cellY(g, g.row + step) + 3 + 'px'; }, ms * 0.3);
      setTimeout(() => { ghost.style.top = cellY(g, toRow) + 3 + 'px'; }, ms * 0.65);
      const drag = run(w, [{ transform: 'translate(0, 0)' }, { transform: `translate(${off.x}px, ${off.y}px)` }], { duration: ms, easing: EASE.move });
      await ptr.moveTo(grab.x + off.x, grab.y + off.y, ms);
      await drag;
      await sleep(160);
      ptr.press(false);
      w.classList.remove('lifted');
      await run(w, [{ transform: `translate(${off.x}px, ${off.y}px)` }, { transform: `translate(0px, ${dy}px)` }], { duration: 280, easing: EASE.outExpo });
      g.row = toRow;
      w.style.top = cellY(g) + 'px';
      w.style.transform = '';
      ghost.classList.remove('show');
      await sleep(250);
      await ptr.moveTo(ptr.pos.x - 180, ptr.pos.y + (toRow ? 120 : 260), 900);
    }

    async function searchScene() {
      pts.set(2);
      await ptr.fade(false);
      const k = await showKeys('<kbd>Ctrl</kbd><i>+</i><kbd>Alt</kbd><i>+</i><kbd class="space">Space</kbd>');
      for (const key of k) { key.classList.add('down'); await sleep(170); }
      setQuery('');
      search.classList.add('open');
      run(search, [{ opacity: 0, transform: 'translateY(-8px)' }, { opacity: 1, transform: 'none' }], { duration: 260, easing: EASE.win });
      await sleep(140);
      k.forEach(key => key.classList.remove('down'));
      hideKeys();
      await sleep(600);
      setQuery('p');
      await sleep(280);
      setQuery('pa');
      await sleep(1000);
      const [enter] = await showKeys('<kbd>Enter</kbd>');
      enter.classList.add('down');
      await sleep(170);
      enter.classList.remove('down');
      hideKeys();
      await run(search, [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateY(-4px)' }], { duration: 150, easing: 'ease-in' });
      search.classList.remove('open');
      const close = await openApp('paint');
      await sleep(1500);
      await close();
      await ptr.fade(true);
    }

    loop(kit, [popupScene, dragScene, searchScene]);
  }

  // ---------- workspaces ----------

  function startWorkspaces() {
    const stage = $('[data-demo="workspaces"]');
    if (!stage) return;
    const kit = stageKit(stage), pts = points(stage);
    // Zones as fractions of the screen above the taskbar, like the app's layouts.
    const MAIN_AND_TWO = [[0, 0, 0.5, 1], [0.5, 0, 0.5, 0.5], [0.5, 0.5, 0.5, 0.5]];
    const SIDE_BY_SIDE = [[0, 0, 0.5, 1], [0.5, 0, 0.5, 1]];
    const zoneRect = z => ({ x: z[0] * kit.W + 4, y: z[1] * AREA + 4, w: z[2] * kit.W - 8, h: z[3] * AREA - 8 });
    const FLOATING = { x: 330, y: 130, w: 360, h: 260 };

    kit.add('wall', BLOBS);
    const coding = wsButton(kit, EDGE, EDGE, 'code', 'Coding');
    const design = wsButton(kit, EDGE, EDGE + U, 'design');
    widget(kit, { name: 'Work', cols: 2, rows: 2, col: 0, row: 0, apps: ['microsoft-edge', 'file-explorer', 'outlook', 'notepad'] });
    const zones = kit.add('zones');
    const tb = taskbar(kit, ['file-explorer', 'microsoft-edge']);

    if (reduceMotion) {
      ['microsoft-edge', 'terminal', 'notepad'].forEach((id, i) => { appWindow(kit.desk, id, zoneRect(MAIN_AND_TWO[i])); tb.open(id); });
      return;
    }
    const ptr = pointer(kit, { x: 600, y: 470 });

    async function openWin(id, r) {
      const w = appWindow(kit.desk, id, r);
      const off = tb.open(id);
      await run(w, [{ opacity: 0, transform: 'scale(0.94)' }, { opacity: 1, transform: 'none' }], { duration: 300, easing: EASE.win });
      return { w, off };
    }
    async function closeAll(list) {
      await Promise.all(list.map(o => run(o.w, [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'scale(0.96)' }], { duration: 200, easing: 'ease-in' })));
      list.forEach(o => { o.w.remove(); o.off(); });
    }
    async function showZones(layout) {
      zones.innerHTML = '';
      layout.forEach((z, i) => place(zones.appendChild(make('div', 'zone', `<b>${i + 1}</b>`)), zoneRect(z)));
      await run(zones, [{ opacity: 0 }, { opacity: 1 }], { duration: 260 });
    }
    const hideZones = () => run(zones, [{ opacity: 1 }, { opacity: 0 }], { duration: 320 });
    async function press(btn) {
      const c = kit.center($('.ws-tile', btn));
      await ptr.moveTo(c.x + 4, c.y + 6, 900);
      ptr.hover(btn);
      await sleep(260);
      await ptr.click();
      ptr.hover(null);
      btn.classList.add('busy');
    }

    async function codingScene() {
      // Notepad is already open: the workspace moves it instead of opening another.
      const note = await openWin('notepad', FLOATING);
      await sleep(800);
      pts.set(0);
      await press(coding);
      await showZones(MAIN_AND_TWO);
      await sleep(500);
      const edge = await openWin('microsoft-edge', zoneRect(MAIN_AND_TWO[0]));
      await sleep(320);
      const term = await openWin('terminal', zoneRect(MAIN_AND_TWO[1]));
      await sleep(420);
      pts.set(2);
      const r = zoneRect(MAIN_AND_TWO[2]), s = note.w.style;
      await run(note.w, [{ left: s.left, top: s.top, width: s.width, height: s.height },
        { left: r.x + 'px', top: r.y + 'px', width: r.w + 'px', height: r.h + 'px' }], { duration: 700, easing: EASE.outExpo });
      coding.classList.remove('busy');
      hideZones();
      await ptr.moveTo(640, 470, 900);
      await sleep(1800);
      await closeAll([edge, term, note]);
    }

    async function designScene() {
      pts.set(1);
      await press(design);
      await showZones(SIDE_BY_SIDE);
      await sleep(450);
      const paint = await openWin('paint', zoneRect(SIDE_BY_SIDE[0]));
      await sleep(320);
      const photos = await openWin('photos', zoneRect(SIDE_BY_SIDE[1]));
      design.classList.remove('busy');
      hideZones();
      await ptr.moveTo(600, 470, 900);
      await sleep(1800);
      await closeAll([paint, photos]);
    }

    loop(kit, [codingScene, designScene]);
  }

  // ---------- the island ----------

  function startIsland() {
    const stage = $('[data-demo="island"]');
    if (!stage) return;
    const kit = stageKit(stage), pts = points(stage);
    kit.add('wall',
      blob(-120, 40, 420, 320, '#2f6ce0', 0.85) + blob(380, 80, 380, 300, '#1a4fb8', 0.8) +
      blob(120, -130, 260, 200, '#3d8bf2', 0.5) + blob(230, -70, 190, 160, '#7ab4ff', 0.8, 'sweep'));
    // A window under the island, so frosted glass has something to blur.
    appWindow(kit.desk, 'microsoft-edge', { x: 90, y: 66, w: 460, h: 280 });
    const isle = island(kit);
    const tag = kit.add('style-tag', 'Black');
    const wheel = kit.add('wheel-hint');
    place(wheel, { x: 528, y: 24 }); // just right of the open island, so it doesn't cover the page
    isle.show('time');

    if (reduceMotion) { isle.show('musicPage'); return; }
    const ptr = pointer(kit, { x: 520, y: 240 });
    const mid = kit.W / 2;
    let glass = false;

    async function setStyle(on) {
      glass = on;
      isle.glass(on);
      await run(tag, [{ opacity: 1 }, { opacity: 0 }], { duration: 160 });
      tag.textContent = on ? 'Frosted glass' : 'Black';
      await run(tag, [{ opacity: 0 }, { opacity: 1 }], { duration: 220 });
    }

    async function scene() {
      pts.set(0);
      isle.show('time');
      await sleep(1300);
      isle.show('music');
      await sleep(1500);
      await ptr.moveTo(mid + 4, 30, 900);
      await sleep(150);
      await ptr.click();
      isle.show('musicPage');
      await sleep(1400);
      // A turn of the mouse wheel goes down through the pages.
      await ptr.fade(false);
      await run(wheel, [{ opacity: 0 }, { opacity: 1 }], { duration: 220 });
      await sleep(450);
      isle.show('timePage');
      await sleep(1400);
      isle.show('sysPage');
      await sleep(2600);
      await run(wheel, [{ opacity: 1 }, { opacity: 0 }], { duration: 220 });
      await ptr.fade(true);
      await ptr.moveTo(520, 240, 900);
      isle.show('music');
      await sleep(1200);
      pts.set(1);
      isle.show('phones');
      await sleep(2400);
      isle.show('music');
      await sleep(1000);
      pts.set(2);
      await setStyle(!glass);
      await sleep(1800);
    }

    loop(kit, [scene]);
  }

  // ---------- the desktop clock ----------

  function startClock() {
    const stage = $('[data-demo="clock"]');
    if (!stage) return;
    const kit = stageKit(stage), pts = points(stage);
    // A wide screen and a tall one, drawn as if they were 1200 and 680 wide (f scales the app's sizes). The wide
    // one's wallpaper has a mountain whose peak rises to the time, for the depth effect.
    const RANGE = '<path class="far" d="M0 232 L70 196 L130 214 L205 168 L262 196 L300 182 L330 200 L600 188 V338 H0Z"/>';
    const PEAK = '<path d="M150 338 L238 232 L274 248 L330 98 L350 128 L370 122 L440 214 L520 196 L600 236 V338Z"/>';
    const mountains = paths => `<svg class="mtn" viewBox="0 0 600 338" preserveAspectRatio="none" aria-hidden="true">${paths}</svg>`;
    const screens = [
      { x: 60, y: 92, w: 600, h: 338, f: 0.5, wall: blob(260, -120, 380, 340, '#2f6ce0', 0.85) + blob(-120, 110, 360, 240, '#163486', 0.85) + blob(380, 60, 220, 200, '#7ab4ff', 0.5) + mountains(RANGE + PEAK) },
      { x: 710, y: 21, w: 230, h: 409, f: 0.34, wall: blob(-60, -80, 260, 260, '#2f6ce0', 0.85) + blob(40, 220, 240, 260, '#1a4fb8', 0.8) + blob(110, 100, 150, 150, '#7ab4ff', 0.45) }
    ];
    screens.forEach((s, i) => {
      s.el = kit.add('screen', `<div class="wall">${s.wall}</div><div class="mini-tb"><i></i><i></i><i></i></div>`);
      place(s.el, s);
      s.clock = deskClock(s.el, s.w / 2, (s.h - 16) * 0.3 + 6, i === 0 ? '♪ Midnight Drive — Neon Coast' : '');
    });
    // the subject, cut out and laid over the time
    const depth = screens[0].el.appendChild(make('div', 'depth', mountains(PEAK)));
    const size = px => screens.forEach(s => sizeClock(s.clock, px * s.f));
    const glass = on => screens.forEach(s => s.clock.classList.toggle('glass', on));
    size(112);
    if (reduceMotion) return;

    const fade = (e, on) => run(e, [{ opacity: on ? 0 : 1 }, { opacity: on ? 1 : 0 }], { duration: 450 });

    async function screensScene() {
      pts.set(0);
      await sleep(1500);
      await fade(screens[1].clock, false);
      await sleep(1100);
      await fade(screens[1].clock, true);
      await sleep(900);
    }
    async function styleScene() {
      pts.set(1);
      for (const px of [96, 80, 128, 168]) { size(px); await sleep(650); }
      await sleep(500);
      size(112);
      await sleep(900);
      glass(true);
      await sleep(2200);
      glass(false);
      await sleep(700);
    }
    // Like the app, only the digits go behind the subject: the date and glance rows are left out of the cutout.
    function keepRowsInFront() {
      const c = screens[0].clock, top = c.offsetTop - c.offsetHeight / 2, stops = [];
      $$('small, em', c).forEach(e => stops.push([top + e.offsetTop - 2, top + e.offsetTop + e.offsetHeight + 2]));
      const grad = 'linear-gradient(to bottom, ' + stops.map(([a, b]) => `#000 ${a}px, transparent ${a}px, transparent ${b}px, #000 ${b}px`).join(', ') + ')';
      depth.style.webkitMaskImage = depth.style.maskImage = grad;
    }

    async function depthScene() {
      pts.set(2);
      keepRowsInFront();
      await sleep(500);
      await run(depth, [{ opacity: 0 }, { opacity: 1 }], { duration: 700, fill: 'forwards' });
      await sleep(2600);
      await run(depth, [{ opacity: 1 }, { opacity: 0 }], { duration: 600, fill: 'forwards' });
      await sleep(400);
    }

    loop(kit, [screensScene, styleScene, depthScene]);
  }

  // ---------- cards and demos that drift toward the pointer and turn to face it ----------

  function startFloat() {
    if (reduceMotion || !matchMedia('(hover: hover) and (pointer: fine)').matches) return;
    const items = $$('[data-float]').map(el => ({ el, inner: $('.float-inner', el), tilt: $('.tilt', el), x: 0, y: 0, rx: 0, ry: 0, glare: 0 }));
    const clamp = (v, m) => Math.max(-m, Math.min(m, v));
    let px = null, py = null, raf = 0;
    const kick = () => { if (!raf) raf = requestAnimationFrame(step); };
    addEventListener('pointermove', e => { px = e.clientX; py = e.clientY; kick(); }, { passive: true });
    document.documentElement.addEventListener('mouseleave', () => { px = py = null; kick(); });
    addEventListener('scroll', kick, { passive: true });

    function step() {
      raf = 0;
      let moving = false;
      const hw = innerWidth / 2, hh = innerHeight / 2;
      for (const it of items) {
        const r = it.el.getBoundingClientRect();
        if (r.bottom < -50 || r.top > innerHeight + 50) continue;
        const t = it.tilt.getBoundingClientRect(), cx = t.left + t.width / 2, cy = t.top + t.height / 2;
        // Big demos lean less than the small cards.
        const k2 = t.width > 480 ? 0.45 : 1;
        let x = 0, y = 0, rx = 0, ry = 0, glare = 0;
        if (px !== null) {
          // Gentle lean toward the pointer anywhere on screen, stronger when it's over or near the card.
          const nx = (px - cx) / hw, ny = (py - cy) / hh;
          const lx = clamp((px - cx) / (t.width / 2), 1), ly = clamp((py - cy) / (t.height / 2), 1);
          const gap = Math.hypot(Math.max(0, Math.abs(px - cx) - t.width / 2), Math.max(0, Math.abs(py - cy) - t.height / 2));
          glare = Math.max(0, 1 - gap / 220);
          x = clamp(nx * 16, 16) * k2;
          y = clamp(ny * 12, 12) * k2;
          ry = clamp(nx * 8 + lx * 8 * glare, 14) * k2;
          rx = clamp(-ny * 7 - ly * 7 * glare, 12) * k2;
          it.tilt.style.setProperty('--gx', ((px - t.left) / t.width * 100).toFixed(1) + '%');
          it.tilt.style.setProperty('--gy', ((py - t.top) / t.height * 100).toFixed(1) + '%');
        }
        const k = 0.1;
        it.x += (x - it.x) * k;
        it.y += (y - it.y) * k;
        it.rx += (rx - it.rx) * k;
        it.ry += (ry - it.ry) * k;
        it.glare += (glare - it.glare) * k;
        if (Math.abs(x - it.x) + Math.abs(y - it.y) + Math.abs(rx - it.rx) + Math.abs(ry - it.ry) > 0.02 || Math.abs(glare - it.glare) > 0.005) moving = true;
        it.inner.style.transform = `translate3d(${it.x.toFixed(2)}px, ${it.y.toFixed(2)}px, 0)`;
        it.tilt.style.transform = `perspective(1400px) rotateX(${it.rx.toFixed(2)}deg) rotateY(${it.ry.toFixed(2)}deg)`;
        it.tilt.style.setProperty('--glare', it.glare.toFixed(3));
      }
      if (moving) kick();
    }
  }

  // ---------- page chrome ----------

  function startPage() {
    const nav = $('.nav');
    const onScroll = () => nav.classList.toggle('scrolled', scrollY > 8);
    addEventListener('scroll', onScroll, { passive: true });
    onScroll();

    const io = new IntersectionObserver(es => es.forEach(e => {
      if (!e.isIntersecting) return;
      e.target.classList.add('in');
      io.unobserve(e.target);
    }), { threshold: 0.12, rootMargin: '0px 0px -40px 0px' });
    $$('[data-reveal]').forEach(e => io.observe(e));

    $$('[data-year]').forEach(e => { e.textContent = new Date().getFullYear(); });
  }

  // The newest release (pre-releases included), so the download button never goes stale.
  // Releases before 0.7 carry WinGroups.exe, Nook's old name.
  async function loadRelease() {
    try {
      const res = await fetch(`https://api.github.com/repos/${REPO}/releases?per_page=10`, { headers: { Accept: 'application/vnd.github+json' } });
      if (!res.ok) return;
      for (const r of await res.json()) {
        if (r.draft) continue;
        const assets = r.assets || [];
        const exe = assets.find(a => a.name === 'Nook.exe') || assets.find(a => a.name === 'WinGroups.exe') || assets.find(a => /\.exe$/i.test(a.name));
        if (!exe) continue;
        $$('[data-download]').forEach(a => { a.href = exe.browser_download_url; });
        $$('[data-version]').forEach(e => { e.textContent = r.tag_name.replace(/^v/i, ''); });
        const kb = exe.size / 1024;
        $$('[data-size]').forEach(e => { e.textContent = kb < 1024 ? Math.round(kb) + ' KB' : (kb / 1024).toFixed(1) + ' MB'; });
        return;
      }
    } catch (_) { /* offline or rate-limited: keep the links in the page */ }
  }

  startPage();
  startHero();
  startGroups();
  startWorkspaces();
  startIsland();
  startClock();
  tick();
  setInterval(tick, 5000);
  startFloat();
  playIntro();
  loadRelease();
})();
