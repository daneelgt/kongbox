/* KONGBOX panel core. Plain ES2015: runs in old Android WebViews, no build step. */
(function () {
  'use strict';

  var KB = window.KB = {
    cfg: null, state: {}, full: {}, defs: {}, pages: [], index: 0,
    preview: /[?&]preview=1/.test(location.search),
    android: window.Android || null,
  };

  // ---------- tiny helpers ----------
  KB.$ = function (sel, root) { return (root || document).querySelector(sel); };
  KB.esc = function (s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  };
  KB.h = function (tag, cls, html) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html != null) e.innerHTML = html;
    return e;
  };
  KB.opt = function (w, key, def) {
    var o = w.options || {};
    if (o[key] !== undefined && o[key] !== null && o[key] !== '') return o[key];
    var d = KB.defs[w.type] && KB.defs[w.type].meta && KB.defs[w.type].meta[key];
    return d !== undefined ? d : def;
  };
  KB.list = function (v) {
    if (Array.isArray(v)) return v.map(function (x) { return String(x).trim(); }).filter(Boolean);
    return String(v || '').split(/[,\n]/).map(function (x) { return x.trim(); }).filter(Boolean);
  };
  KB.clamp = function (v, a, b) { return Math.max(a, Math.min(b, v)); };
  KB.levelColor = function (pct) { return pct >= 90 ? 'var(--red)' : pct >= 70 ? 'var(--amber)' : 'var(--green)'; };

  // ---------- formatting ----------
  var fmt = KB.fmt = {
    pct: function (v) { return v == null ? '–' : Math.round(v) + '%'; },
    bytes: function (b) {
      if (b == null) return '–';
      var u = ['B', 'KB', 'MB', 'GB', 'TB'], i = 0;
      while (b >= 1024 && i < u.length - 1) { b /= 1024; i++; }
      return (b >= 100 || i === 0 ? Math.round(b) : b.toFixed(1)) + ' ' + u[i];
    },
    rate: function (b) { return b == null ? '–' : fmt.bytes(b) + '/s'; },
    num: function (n) {
      if (n == null) return '–';
      if (n >= 1e9) return (n / 1e9).toFixed(1) + 'B';
      if (n >= 1e6) return (n / 1e6).toFixed(1) + 'M';
      if (n >= 1e3) return (n / 1e3).toFixed(1) + 'k';
      return String(Math.round(n));
    },
    dur: function (sec) {
      sec = Math.max(0, Math.round(sec));
      var d = Math.floor(sec / 86400), h = Math.floor(sec % 86400 / 3600), m = Math.floor(sec % 3600 / 60);
      if (d) return d + ' d ' + h + ' h';
      if (h) return h + ' h ' + (m < 10 ? '0' : '') + m + ' min';
      if (m) return m + ' min';
      return sec + ' s';
    },
    clock: function (sec) {
      sec = Math.max(0, Math.floor(sec));
      var h = Math.floor(sec / 3600), m = Math.floor(sec % 3600 / 60), s = sec % 60;
      return (h ? h + ':' + (m < 10 ? '0' : '') : '') + m + ':' + (s < 10 ? '0' : '') + s;
    },
    ago: function (ms) {
      if (!ms) return '–';
      var s = (Date.now() - ms) / 1000;
      if (s < 60) return 'agora';
      return 'há ' + fmt.dur(s);
    },
    until: function (ms) { return ms ? 'em ' + fmt.dur((ms - Date.now()) / 1000) : '–'; },
    time: function (d, sec) {
      var h24 = !KB.cfg || !KB.cfg.display || KB.cfg.display.hour24 !== false;
      var h = d.getHours(), m = d.getMinutes(), s = d.getSeconds();
      var hh = h24 ? h : (h % 12 || 12);
      return (h24 && hh < 10 ? '0' : '') + hh + ':' + (m < 10 ? '0' : '') + m + (sec ? ':' + (s < 10 ? '0' : '') + s : '');
    },
  };

  // ---------- svg primitives ----------
  KB.ring = function (pct, color, stroke) {
    stroke = stroke || 8;
    var r = 50 - stroke / 2, c = 2 * Math.PI * r;
    var off = c * (1 - KB.clamp(pct || 0, 0, 100) / 100);
    return '<svg class="ring" viewBox="0 0 100 100"><circle class="track" cx="50" cy="50" r="' + r + '" stroke-width="' + stroke + '"/>' +
      '<circle class="fill" cx="50" cy="50" r="' + r + '" stroke-width="' + stroke + '" stroke="' + color + '" stroke-dasharray="' + c.toFixed(1) +
      '" stroke-dashoffset="' + off.toFixed(1) + '" transform="rotate(-90 50 50)"/></svg>';
  };
  KB.setRing = function (svg, pct, color) {
    if (!svg) return;
    var f = svg.querySelector('.fill');
    var c = parseFloat(f.getAttribute('stroke-dasharray'));
    f.setAttribute('stroke-dashoffset', (c * (1 - KB.clamp(pct || 0, 0, 100) / 100)).toFixed(1));
    if (color) f.setAttribute('stroke', color);
  };
  KB.sparkPath = function (vals, max, w, h) {
    if (!vals || vals.length < 2) return { line: '', area: '' };
    var m = max || Math.max.apply(null, vals.concat([1]));
    var step = w / (vals.length - 1), d = '';
    for (var i = 0; i < vals.length; i++) {
      var x = (i * step).toFixed(1), y = (h - KB.clamp(vals[i] / m, 0, 1) * (h - 2) - 1).toFixed(1);
      d += (i ? 'L' : 'M') + x + ' ' + y;
    }
    return { line: d, area: d + 'L' + w + ' ' + h + 'L0 ' + h + 'Z' };
  };
  KB.spark = function (color) {
    return '<svg class="spark" viewBox="0 0 100 40" preserveAspectRatio="none"><path class="area" fill="' + color + '"/><path class="line" stroke="' + color + '"/></svg>';
  };
  KB.setSpark = function (svg, vals, max) {
    if (!svg) return;
    var p = KB.sparkPath(vals, max, 100, 40);
    svg.querySelector('.line').setAttribute('d', p.line);
    svg.querySelector('.area').setAttribute('d', p.area);
  };

  // ---------- metrics shared by stat / gauge / chart ----------
  KB.metrics = {
    cpu: { label: 'cpu', color: 'var(--accent)', max: 100, get: function (s) { return s.cpu; }, show: fmt.pct },
    gpu: { label: 'gpu', color: 'var(--purple)', max: 100, get: function (s) { return s.gpu ? s.gpu.util : null; }, show: fmt.pct },
    ram: { label: 'ram', color: 'var(--green)', max: 100, get: function (s) { return s.ram ? s.ram.pct : null; }, show: fmt.pct,
      sub: function (s) { return s.ram ? fmt.bytes(s.ram.used) + ' / ' + fmt.bytes(s.ram.total) : ''; } },
    vram: { label: 'vram', color: 'var(--purple)', max: 100, get: function (s) { return s.gpu ? s.gpu.vramPct : null; }, show: fmt.pct },
    disk: { label: 'disco', color: 'var(--cyan)', max: 100, get: function (s) { return s.disks && s.disks.length ? Math.max.apply(null, s.disks.map(function (d) { return d.pct; })) : null; }, show: fmt.pct },
    down: { label: 'download', short: '↓', color: 'var(--cyan)', max: 0, get: function (s) { return s.net ? s.net.down : null; }, show: fmt.rate },
    up: { label: 'upload', short: '↑', color: 'var(--amber)', max: 0, get: function (s) { return s.net ? s.net.up : null; }, show: fmt.rate },
    ping: { label: 'ping', color: 'var(--green)', max: 0, get: function (s) { return s.ping; }, show: function (v) { return v == null ? '–' : Math.round(v) + ' ms'; } },
    gputemp: { label: 'gpu temp', color: 'var(--red)', max: 100, get: function (s) { return s.gpu ? s.gpu.temp : null; }, show: function (v) { return v == null ? '–' : Math.round(v) + '°'; } },
  };

  // ---------- network ----------
  KB.api = function (path, opts) {
    opts = opts || {};
    var init = { method: opts.method || 'GET', headers: {} };
    if (init.method !== 'GET') init.headers['X-Kong'] = '1';
    if (opts.body !== undefined) {
      init.headers['Content-Type'] = 'application/json';
      init.body = typeof opts.body === 'string' ? opts.body : JSON.stringify(opts.body);
    }
    return fetch(path, init).then(function (r) {
      var ct = r.headers.get('content-type') || '';
      var p = ct.indexOf('json') >= 0 ? r.json() : r.text();
      return p.then(function (d) { return { ok: r.ok, status: r.status, data: d }; });
    }).catch(function () { return { ok: false, status: 0, data: null }; });
  };

  var feedCache = {};
  /** Public data feeds through the agent's proxy, shared and cached per URL. */
  KB.feed = function (url, ttl) {
    var c = feedCache[url];
    if (c && Date.now() - c.at < (ttl || 300000)) return c.p;
    var p = KB.api('/api/proxy?u=' + encodeURIComponent(url)).then(function (r) {
      if (!r.ok) { if (c && c.data) return c.data; throw new Error('feed'); }
      feedCache[url].data = r.data;
      return r.data;
    });
    feedCache[url] = { at: Date.now(), p: p, data: c ? c.data : null };
    p.catch(function () { feedCache[url].at = 0; });
    return p;
  };

  KB.weather = function () {
    var w = (KB.cfg && KB.cfg.weather) || {};
    var url = 'https://api.open-meteo.com/v1/forecast?latitude=' + (w.lat || 0) + '&longitude=' + (w.lon || 0) +
      '&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day' +
      '&hourly=temperature_2m,weather_code,precipitation_probability,is_day' +
      '&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,uv_index_max,precipitation_probability_max,daylight_duration' +
      '&timezone=auto&forecast_days=7';
    return KB.feed(url, 600000);
  };

  // ---------- local storage (never required) ----------
  KB.store = {
    get: function (k, def) { try { var v = localStorage.getItem('kb:' + k); return v == null ? def : JSON.parse(v); } catch (e) { return def; } },
    set: function (k, v) { try { localStorage.setItem('kb:' + k, JSON.stringify(v)); } catch (e) { /* private mode */ } },
  };

  // ---------- haptics & sound ----------
  KB.haptic = function () {
    if (KB.cfg && KB.cfg.display && KB.cfg.display.vibrate === false) return;
    if (KB.android && KB.android.haptic) { try { KB.android.haptic(KB.key); } catch (e) { } return; }
  };
  KB.buzz = function (pattern) {
    if (KB.cfg && KB.cfg.display && KB.cfg.display.vibrate === false) return;
    if (KB.android && KB.android.buzz) { try { KB.android.buzz(KB.key, pattern || '0,60,80,60'); } catch (e) { } }
  };

  // ---------- island: notifications & countdowns ----------
  var island = KB.$('#island'), islandTimer = 0, islandQueue = [], islandBusy = false;
  var levelColors = { ok: 'var(--green)', warn: 'var(--amber)', error: 'var(--red)', info: 'var(--accent)' };
  KB.toast = function (t) { islandQueue.push(t); if (!islandBusy) nextIsland(); };
  function nextIsland() {
    var t = islandQueue.shift();
    if (!t) { islandBusy = false; return; }
    islandBusy = true;
    island.style.setProperty('--c', levelColors[t.level] || levelColors.info);
    island.innerHTML = '<div class="ic">' + KB.esc(t.icon || (t.level === 'error' ? '!' : t.level === 'warn' ? '!' : t.level === 'ok' ? '✓' : '›')) + '</div>' +
      '<div class="tx"><b>' + KB.esc(t.title || '') + '</b>' + (t.text ? '<span>' + KB.esc(t.text) + '</span>' : '') + '</div>' +
      (t.actions ? '<div class="acts"></div>' : '');
    if (t.actions) {
      var acts = island.querySelector('.acts');
      t.actions.forEach(function (a) {
        var b = KB.h('button', 'btn press' + (a.primary ? ' primary' : ''), KB.esc(a.label));
        b.addEventListener('click', function (e) { e.stopPropagation(); closeIsland(); a.run(); });
        acts.appendChild(b);
      });
    }
    island.classList.add('show');
    if (t.onShow) t.onShow(island);
    clearTimeout(islandTimer);
    islandTimer = setTimeout(closeIsland, (t.seconds || 5) * 1000);
  }
  function closeIsland() {
    clearTimeout(islandTimer);
    island.classList.remove('show');
    setTimeout(nextIsland, 450);
  }
  island.addEventListener('click', closeIsland);

  // ---------- sheets ----------
  var sheet = KB.$('#sheet'), sheetBody = KB.$('#sheetBody'), scrim = KB.$('#scrim'), sheetClose = null;
  KB.sheet = function (html, onMount, onClose) {
    sheetBody.innerHTML = html;
    sheet.classList.add('show');
    scrim.classList.add('show');
    sheetClose = onClose || null;
    if (onMount) onMount(sheetBody);
  };
  KB.closeSheet = function () {
    sheet.classList.remove('show');
    scrim.classList.remove('show');
    var c = sheetClose; sheetClose = null;
    if (c) c();
  };
  KB.sheetOpen = function () { return sheet.classList.contains('show'); };
  scrim.addEventListener('click', KB.closeSheet);

  // ---------- widget registry ----------
  /** KB.def('clock', { mount: function (el, w, ctx) { return { update: fn(state, full), destroy: fn } } }) */
  KB.def = function (type, impl) { KB.defs[type] = impl; };

  KB.mount = function (host, w, ctx) {
    var def = KB.defs[w.type];
    host.classList.add('w', 'w-' + w.type);
    if (CAPPED[w.type]) host.classList.add('cap');
    if (!def) { host.innerHTML = '<div class="empty">widget ' + KB.esc(w.type) + '</div>'; return null; }
    var inst = null;
    try { inst = def.mount(host, w, ctx || {}) || {}; }
    catch (e) { host.innerHTML = '<div class="empty">erro em ' + KB.esc(w.type) + '</div>'; return null; }
    sizeObserver(host);
    var safe = {
      update: function (s, full) { if (inst.update) { try { inst.update(s, full); } catch (e) { /* one widget never breaks the page */ } } },
      destroy: function () { if (ro) ro.unobserve(host); if (inst.destroy) { try { inst.destroy(); } catch (e) { } } },
      secondly: inst.secondly ? function () { try { inst.secondly(); } catch (e) { } } : null,
      el: host, w: w,
    };
    return safe;
  };

  // Display widgets (clock, gauges) scale freely with their slot; list widgets stop growing at 1/24 of the screen's
  // short side so a full-page list stays a list instead of becoming a poster.
  var CAPPED = { quotas: 1, sessions: 1, github: 1, processes: 1, topprocs: 1, meters: 1, inbox: 1, hackernews: 1, rules: 1,
    checklist: 1, exchange: 1, crypto: 1, note: 1, gpu: 1, progress: 1, forecast: 1, calendar: 1, timer: 1, stopwatch: 1, chart: 1 };
  function unitFor(el, w, h) {
    var u = Math.max(6, Math.min(w / 12, h / 7.5));
    if (el.classList.contains('cap')) u = Math.min(u, Math.max(11, Math.min(innerWidth, innerHeight) / 24));
    return u;
  }
  var ro = window.ResizeObserver ? new ResizeObserver(function (entries) {
    entries.forEach(function (e) {
      e.target.style.setProperty('--u', unitFor(e.target, e.contentRect.width, e.contentRect.height).toFixed(2) + 'px');
    });
  }) : null;
  function sizeObserver(el) {
    if (ro) ro.observe(el);
    else el.style.setProperty('--u', unitFor(el, el.clientWidth, el.clientHeight) + 'px');
  }
  KB.observe = sizeObserver;

  KB.header = function (label, tag) {
    return '<div class="w-h"><span>' + KB.esc(label) + '</span>' + (tag != null ? '<span class="tag">' + tag + '</span>' : '') + '</div>';
  };

  // ---------- pages ----------
  var track = KB.$('#track'), dots = KB.$('#dots');
  var instances = [];

  function place(el, w) {
    var x = w.x || 0, y = w.y || 0, ww = w.w || 1, hh = w.h || 1;
    if (document.body.classList.contains('portrait')) {
      // two halves stacked: the left half on top, the right half below
      if (x >= 6) { x -= 6; y += 6; } else if (x + ww > 6) { ww = 6 - x; }
    }
    el.style.gridColumn = (x + 1) + ' / span ' + ww;
    el.style.gridRow = (y + 1) + ' / span ' + hh;
  }

  function render() {
    instances.forEach(function (list) { list.forEach(function (i) { if (i) i.destroy(); }); });
    instances = [];
    track.innerHTML = '';
    dots.innerHTML = '';
    KB.pages = (KB.cfg.pages || []).filter(function (p) { return p.enabled !== false; });
    if (!KB.pages.length) KB.pages = [{ id: 'empty', title: 'Vazio', widgets: [] }];
    KB.pages.forEach(function (p, pi) {
      var page = KB.h('section', 'page');
      page.dataset.id = p.id;
      var list = [];
      (p.widgets || []).forEach(function (w, wi) {
        var el = KB.h('div');
        el.style.setProperty('--i', wi);
        place(el, w);
        page.appendChild(el);
        list.push(KB.mount(el, w, { page: p, pageIndex: pi }));
      });
      if (!(p.widgets || []).length) page.innerHTML = '<div class="w" style="grid-column:1/-1;grid-row:1/-1"><div class="empty">página vazia — adicione widgets no editor</div></div>';
      track.appendChild(page);
      instances.push(list);
      dots.appendChild(KB.h('i'));
    });
    dots.style.display = KB.pages.length > 1 ? '' : 'none';
    KB.index = KB.clamp(KB.index, 0, KB.pages.length - 1);
    go(KB.index, false);
    tick(true);
  }
  KB.render = render;

  function go(i, animate) {
    i = KB.clamp(i, 0, KB.pages.length - 1);
    var changed = i !== KB.index;
    KB.index = i;
    track.classList.toggle('dragging', animate === false);
    track.style.transform = 'translate3d(' + (-i * 100) + '%,0,0)';
    Array.prototype.forEach.call(dots.children, function (d, k) { d.classList.toggle('on', k === i); });
    var page = track.children[i];
    if (page && (changed || animate === false)) {
      page.classList.remove('enter');
      void page.offsetWidth;
      page.classList.add('enter');
    }
    if (animate === false) requestAnimationFrame(function () { track.classList.remove('dragging'); });
    KB.store.set('page', KB.pages[i] ? KB.pages[i].id : '');
  }
  KB.go = go;
  KB.flip = function (dir) { userActive(); go(KB.index + dir); KB.haptic(); };
  KB.gotoId = function (id) {
    for (var i = 0; i < KB.pages.length; i++) if (KB.pages[i].id === id || String(i + 1) === String(id)) { go(i); return true; }
    return false;
  };

  // ---------- swipe ----------
  var drag = null;
  document.addEventListener('pointerdown', function (e) {
    userActive();
    if (KB.sheetOpen() || e.target.closest('[data-noswipe]')) return;
    drag = { x: e.clientX, y: e.clientY, t: Date.now(), dx: 0, axis: null, id: e.pointerId, target: e.target };
  });
  document.addEventListener('pointermove', function (e) {
    if (!drag || e.pointerId !== drag.id) return;
    var dx = e.clientX - drag.x, dy = e.clientY - drag.y;
    if (!drag.axis && (Math.abs(dx) > 10 || Math.abs(dy) > 10)) drag.axis = Math.abs(dx) > Math.abs(dy) ? 'x' : 'y';
    if (drag.axis !== 'x') return;
    drag.dx = dx;
    var edge = (KB.index === 0 && dx > 0) || (KB.index === KB.pages.length - 1 && dx < 0);
    var eff = edge ? dx * .3 : dx; // rubber band at the ends
    track.classList.add('dragging');
    track.style.transform = 'translate3d(calc(' + (-KB.index * 100) + '% + ' + eff + 'px),0,0)';
  });
  function endDrag(e) {
    if (!drag || (e && e.pointerId !== drag.id)) return;
    var d = drag; drag = null;
    track.classList.remove('dragging');
    if (d.axis !== 'x') { go(KB.index); return; }
    var v = d.dx / Math.max(1, Date.now() - d.t);
    var dir = (d.dx < -innerWidth * .18 || v < -.45) ? 1 : (d.dx > innerWidth * .18 || v > .45) ? -1 : 0;
    go(KB.index + dir);
    if (dir) KB.haptic();
    KB.swallowClick = true;
    setTimeout(function () { KB.swallowClick = false; }, 60);
  }
  document.addEventListener('pointerup', endDrag);
  document.addEventListener('pointercancel', endDrag);
  document.addEventListener('click', function (e) { if (KB.swallowClick) { e.stopPropagation(); e.preventDefault(); } }, true);

  // long-press on empty space or tap on dots → overview
  var lp = 0;
  document.addEventListener('pointerdown', function (e) {
    if (!e.target.classList.contains('page')) return;
    lp = setTimeout(showOverview, 550);
  });
  ['pointerup', 'pointermove', 'pointercancel'].forEach(function (n) { document.addEventListener(n, function () { clearTimeout(lp); }); });
  dots.addEventListener('click', showOverview);

  var overview = KB.$('#overview');
  function showOverview() {
    var g = KB.$('#overviewGrid');
    g.innerHTML = '';
    KB.pages.forEach(function (p, i) {
      var t = KB.h('button', 'tile press' + (i === KB.index ? ' on' : ''),
        '<span>' + String(i + 1).padStart(2, '0') + '</span><div class="e">' + KB.esc(p.icon || '▢') + '</div><b>' + KB.esc(p.title || p.id) + '</b>');
      t.addEventListener('click', function (e) { e.stopPropagation(); overview.classList.remove('show'); go(i); });
      g.appendChild(t);
    });
    overview.classList.add('show');
  }
  overview.addEventListener('click', function () { overview.classList.remove('show'); });

  // ---------- idle return, night, pixel shift ----------
  var lastTouch = Date.now(), nightWokeUntil = 0;
  function userActive() { lastTouch = Date.now(); }
  KB.userActive = userActive;

  function minutes(hhmm) { var p = String(hhmm || '0:0').split(':'); return (+p[0] || 0) * 60 + (+p[1] || 0); }
  function isNight() {
    var n = KB.cfg && KB.cfg.night;
    if (!n || !n.enabled || KB.preview) return false;
    var d = new Date(), now = d.getHours() * 60 + d.getMinutes(), a = minutes(n.start), b = minutes(n.end);
    return a <= b ? (now >= a && now < b) : (now >= a || now < b);
  }
  var nightEl = KB.$('#night'), nightOn = false;
  nightEl.addEventListener('click', function () { nightWokeUntil = Date.now() + 30000; applyNight(); });
  function applyNight() {
    var on = isNight() && Date.now() > nightWokeUntil;
    if (on !== nightOn) {
      nightOn = on;
      nightEl.classList.toggle('show', on);
      if (KB.android && KB.android.setBrightness) {
        try { KB.android.setBrightness(KB.key, on ? (KB.cfg.night.brightness || 0.02) : -1); } catch (e) { }
      }
    }
    if (on) {
      var d = new Date();
      KB.$('#nightT').textContent = fmt.time(d, false);
      KB.$('#nightD').textContent = d.toLocaleDateString('pt-BR', { weekday: 'long', day: 'numeric', month: 'long' });
      // drift the night clock around so no pixel burns in
      var m = d.getMinutes();
      KB.$('#nightT').style.transform = 'translate(' + ((m * 7) % 40 - 20) + 'px,' + ((m * 11) % 40 - 20) + 'px)';
    }
  }

  function housekeeping() {
    var disp = (KB.cfg && KB.cfg.display) || {};
    var idle = (Date.now() - lastTouch) / 1000;
    if (disp.idleReturnSec > 0 && idle > disp.idleReturnSec && KB.index !== 0 && !KB.sheetOpen()) go(0);
    applyNight();
    if (disp.pixelShift) {
      document.body.classList.add('shift');
      var k = Math.floor(Date.now() / 120000);
      document.body.style.setProperty('--sx', ((k * 3) % 7 - 3) + 'px');
      document.body.style.setProperty('--sy', ((k * 5) % 7 - 3) + 'px');
    } else document.body.classList.remove('shift');
  }

  // ---------- approvals (Claude Code permission prompts) ----------
  var shownApproval = null;
  function handleApprovals(list) {
    if (!list || !list.length) {
      if (shownApproval) { shownApproval = null; if (KB.sheetOpen() && KB.$('#approval')) KB.closeSheet(); }
      return;
    }
    var a = list[0];
    if (shownApproval === a.id) return;
    shownApproval = a.id;
    nightWokeUntil = Date.now() + 60000; applyNight();
    KB.buzz('0,90,60,90');
    KB.sheet(
      '<div id="approval"><h3>Claude Code quer usar <span class="mono">' + KB.esc(a.tool) + '</span></h3>' +
      '<div class="meta">~/' + KB.esc(a.project || '') + ' · expira <span id="apT"></span></div>' +
      '<pre>' + KB.esc(a.detail) + '</pre>' +
      '<div class="acts"><button class="btn danger press" data-a="0">Negar</button><button class="btn ok press" data-a="1">Permitir</button></div></div>',
      function (root) {
        var t = KB.$('#apT', root);
        var timer = setInterval(function () { if (!document.body.contains(t)) return clearInterval(timer); t.textContent = fmt.until(a.expires); }, 500);
        t.textContent = fmt.until(a.expires);
        Array.prototype.forEach.call(root.querySelectorAll('[data-a]'), function (b) {
          b.addEventListener('click', function () {
            KB.haptic();
            KB.api('/api/approvals/' + a.id, { method: 'POST', body: { allow: b.dataset.a === '1' } });
            KB.closeSheet();
          });
        });
      });
  }

  // ---------- polling ----------
  var lastNotice = -1, lastGoto = 0, cfgVersion = -1, reloadSeq = -1, failures = 0, lastFull = 0, booted = false;

  function applyState(s, full) {
    KB.state = s;
    if (full) KB.full = s;
    if (cfgVersion >= 0 && s.config !== cfgVersion && !KB.preview) loadConfig();
    if (reloadSeq >= 0 && s.reload !== reloadSeq) location.reload();
    reloadSeq = s.reload;
    // notifications since the last poll
    if (lastNotice < 0) lastNotice = s.lastNotice || 0;
    (s.notices || []).forEach(function (n) {
      if (n.id <= lastNotice) return;
      lastNotice = n.id;
      if (n.wake) { nightWokeUntil = Date.now() + 30000; applyNight(); }
      if (n.vibrate !== false) KB.buzz();
      KB.toast({ title: n.title, text: n.text, level: n.level, seconds: n.seconds });
    });
    // page requests from webhooks and rules, with a countdown
    var g = s['goto'];
    if (g && g.seq !== lastGoto) {
      var first = lastGoto === 0;
      lastGoto = g.seq;
      if (!first) requestGoto(g);
    }
    handleApprovals(s.approvals);
    var merged = full ? s : mergeFull(s);
    instances[KB.index] && instances[KB.index].forEach(function (i) { if (i) i.update(merged, full); });
  }

  function mergeFull(s) {
    var m = {};
    for (var k in KB.full) m[k] = KB.full[k];
    for (var j in s) m[j] = s[j];
    return m;
  }

  function requestGoto(g) {
    var target = -1;
    for (var i = 0; i < KB.pages.length; i++) if (KB.pages[i].id === g.page || String(i + 1) === String(g.page)) target = i;
    if (target < 0 || target === KB.index) return;
    if ((Date.now() - lastTouch) < 15000 && !g.countdown) return; // never pull a page away mid-use
    var n = g.countdown || 0;
    if (!n) { go(target); return; }
    var name = KB.pages[target].title || KB.pages[target].id, cancelled = false;
    KB.toast({
      title: 'Indo para ' + name, text: 'em ' + n + '…', seconds: n + .4, icon: '→',
      actions: [{ label: 'Ficar', run: function () { cancelled = true; } }, { label: 'Ir', primary: true, run: function () { cancelled = true; go(target); } }],
      onShow: function (el) {
        var left = n, span = el.querySelector('.tx span');
        var t = setInterval(function () {
          left--;
          if (cancelled) return clearInterval(t);
          if (left <= 0) { clearInterval(t); if (!cancelled && (Date.now() - lastTouch) > 3000) go(target); return; }
          if (span) span.textContent = 'em ' + left + '…';
        }, 1000);
      },
    });
  }

  function tick(forceFull) {
    if (document.hidden && !forceFull) return;
    var full = forceFull || Date.now() - lastFull > 4500;
    if (full) lastFull = Date.now();
    KB.api('/api/state?since=' + Math.max(0, lastNotice) + (full ? '&full=1' : '')).then(function (r) {
      if (!r.ok || !r.data) { failures++; if (failures > 2) KB.$('#conn').classList.add('show'); return; }
      failures = 0;
      KB.$('#conn').classList.remove('show');
      if (!booted) { booted = true; KB.$('#boot').classList.add('hide'); }
      applyState(r.data, full);
    });
  }

  function loadConfig() {
    return KB.api('/api/config').then(function (r) {
      if (!r.ok || !r.data) { KB.$('#bootMsg').textContent = 'agente offline — tentando de novo'; setTimeout(loadConfig, 2500); return; }
      applyConfig(r.data);
    });
  }

  function applyConfig(cfg) {
    KB.cfg = cfg;
    cfgVersion = cfg.version;
    var a = cfg.appearance || {}, root = document.documentElement.style;
    if (a.accent) root.setProperty('--accent', a.accent);
    if (a.radius != null) root.setProperty('--r', a.radius + 'px');
    if (a.gap != null) { root.setProperty('--gap', a.gap + 'px'); root.setProperty('--pad', a.gap + 'px'); }
    document.body.classList.toggle('flat', a.preset === 'flat');
    document.body.classList.toggle('seamless', a.preset === 'seamless');
    KB.onKey();
    var saved = KB.store.get('page', '');
    if (cfgVersion >= 0 && !KB.pages.length && saved) {
      (cfg.pages || []).filter(function (p) { return p.enabled !== false; }).forEach(function (p, i) { if (p.id === saved) KB.index = i; });
    }
    orientation();
    render();
    if (KB.preview) { booted = true; KB.$('#boot').classList.add('hide'); }
  }

  function orientation() {
    var p = innerHeight > innerWidth * 1.15;
    var changed = p !== document.body.classList.contains('portrait');
    document.body.classList.toggle('portrait', p);
    return changed;
  }
  window.addEventListener('resize', function () { if (orientation() && KB.cfg) render(); });

  // the editor's live preview posts the unsaved config into this frame
  window.addEventListener('message', function (e) {
    if (e.origin !== location.origin || !e.data || e.data.kongbox !== 'config') return;
    var c = e.data.cfg;
    c.version = -1;
    var keep = KB.index;
    applyConfig(c);
    if (typeof e.data.page === 'number') go(e.data.page, false); else go(keep, false);
  });

  document.addEventListener('visibilitychange', function () { if (!document.hidden) tick(true); });

  /** The app injects KB.key into this frame after load, then calls this to push phone-side settings. */
  KB.key = '';
  KB.onKey = function () {
    if (!KB.android || !KB.key || !KB.cfg) return;
    var disp = KB.cfg.display || {};
    try { if (KB.android.setVolumeKeys) KB.android.setVolumeKeys(KB.key, disp.volumeKeys !== false); } catch (e) { }
    try { if (KB.android.setOrientation) KB.android.setOrientation(KB.key, disp.orientation || 'auto'); } catch (e) { }
  };

  KB.start = function () {
    if (KB.preview) {
      document.body.classList.add('preview');
      if (window.parent !== window) window.parent.postMessage({ kongbox: 'ready' }, location.origin);
    } else loadConfig();
    // 1 s while the agent answers; back off to 4 s while it is away (fewer failed requests, less battery)
    (function loop() { tick(false); setTimeout(loop, failures > 2 ? 4000 : 1000); })();
    setInterval(housekeeping, 5000);
    // the clock widgets tick by themselves every second
    setInterval(function () {
      var list = instances[KB.index] || [];
      list.forEach(function (i) { if (i && i.secondly) { try { i.secondly(); } catch (e) { } } });
    }, 1000);
  };
})();
