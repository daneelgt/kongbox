/* Web & data: pushed value, notifications, Hacker News, exchange rates, crypto, custom HTML, image. */
(function () {
  'use strict';
  var esc = KB.esc, fmt = KB.fmt;
  var LEVEL = { ok: 'var(--green)', warn: 'var(--amber)', error: 'var(--red)' };
  function autoLevel(v) {
    var s = String(v).toLowerCase();
    if (/^(passed|success|ok|green|up|online|done|pass|sucesso)$/.test(s)) return 'ok';
    if (/^(failed|failure|error|down|offline|red|fail|erro|falhou)$/.test(s)) return 'error';
    if (/^(running|pending|building|queued|warn|warning|rodando)$/.test(s)) return 'warn';
    return '';
  }

  // ---------- pushed value ----------
  KB.def('push', {
    mount: function (el, w) {
      var key = KB.opt(w, 'key', ''), style = KB.opt(w, 'style', 'value');
      el.innerHTML = KB.header(key || 'push', '<span class="pu-u"></span>') + '<div class="w-b pu"></div>';
      var b = el.querySelector('.w-b'), u = el.querySelector('.pu-u'), sig = '';
      return {
        update: function (s, full) {
          if (!full) return;
          var p = (s.push || {})[key];
          var next = JSON.stringify(p || null);
          if (next === sig) return;
          sig = next;
          if (!p) { b.innerHTML = '<div class="empty">POST /api/push/' + esc(key) + '</div>'; u.textContent = ''; return; }
          u.textContent = fmt.ago(p.updated);
          var lv = p.level || autoLevel(p.value), color = p.color || LEVEL[lv] || 'var(--text)';
          var prog = p.progress != null ? (p.progress <= 1 ? p.progress * 100 : p.progress) : null;
          if (style === 'gauge' && prog != null) {
            b.innerHTML = '<div class="ga">' + KB.ring(prog, color === 'var(--text)' ? 'var(--accent)' : color, 9) + '<div class="ga-c"><div class="ga-v num">' + esc(p.value) + esc(p.unit || '') + '</div><div class="ga-l mono">' + esc(p.title || p.sub || '') + '</div></div></div>';
          } else if (style === 'list' && Array.isArray(p.items)) {
            b.innerHTML = '<div class="rows">' + p.items.map(function (it) {
              var o = typeof it === 'object' ? it : { label: it };
              return '<div class="row"><i class="dot" style="--c:' + (LEVEL[o.level || autoLevel(o.value || '')] || 'var(--text-3)') + '"></i><span class="grow">' + esc(o.label) + '</span><span class="mono num">' + esc(o.value == null ? '' : o.value) + '</span></div>';
            }).join('') + '</div>';
          } else {
            b.innerHTML = '<div class="pu-v num" style="color:' + color + '">' + (p.icon ? esc(p.icon) + ' ' : '') + esc(p.value) + '<small>' + esc(p.unit || '') + '</small></div>' +
              (p.sub || p.title ? '<div class="pu-s mono dim">' + esc(p.sub || p.title) + '</div>' : '') +
              (prog != null ? '<div class="bar" style="--c:' + (color === 'var(--text)' ? 'var(--accent)' : color) + '"><i style="width:' + KB.clamp(prog, 0, 100) + '%"></i></div>' : '');
          }
        },
      };
    },
  });

  // ---------- notifications ----------
  function noticeSheet(n, after) {
    var fromWindows = n.source === 'windows' && n.aumid;
    KB.sheet('<h3>' + esc(n.title) + '</h3><div class="meta">' + esc(n.app || 'webhook') + ' · ' + fmt.ago(n.time) + '</div>' +
      (n.text ? '<pre>' + esc(n.text) + '</pre>' : '') +
      '<div class="acts"><button class="btn danger press" data-x="del">Apagar</button>' +
      (fromWindows ? '<button class="btn primary press" data-x="open">Abrir no PC</button>' : '<button class="btn press" data-x="close">Fechar</button>') + '</div>',
      function (root) {
        root.addEventListener('click', function (e) {
          var x = e.target.closest('[data-x]'); if (!x) return;
          if (x.dataset.x === 'open') KB.act('/api/notify/' + n.id + '/open');
          if (x.dataset.x === 'del') { KB.haptic(); if (after) after(); }
          KB.closeSheet();
        });
      });
  }
  KB.def('inbox', {
    mount: function (el) {
      el.innerHTML = '<div class="w-h"><span>notifications</span><span class="tag"><button class="chip press" data-clear>limpar</button></span></div><div class="w-b rows ps ib" data-noswipe></div>';
      var b = el.querySelector('.w-b'), sig = '', list = [];
      function keep(items) { list = items; sig = items.slice().reverse().map(function (n) { return n.id; }).join(','); }
      function draw(items) {
        list = items.slice().reverse();
        b.innerHTML = list.map(function (n) {
          var c = n.source === 'windows' ? 'var(--accent)' : (LEVEL[n.level] || 'var(--accent)');
          return '<div class="row ib-r" data-id="' + n.id + '"><i class="dot" style="--c:' + c + '"></i><span class="grow">' + (n.app ? '<span class="ib-app">' + esc(n.app) + '</span>' : '') + esc(n.title) +
            '<span class="sub">' + esc(n.text || '') + '</span></span><span class="chip">' + fmt.ago(n.time).replace('há ', '') + '</span></div>';
        }).join('') || '<div class="empty">nenhuma notificação</div>';
      }
      function byId(id) { return list.filter(function (n) { return String(n.id) === String(id); })[0]; }
      function remove(id) {
        keep(list.filter(function (n) { return String(n.id) !== String(id); }));
        if (!list.length) draw([]);
        KB.api('/api/notify/' + id, { method: 'DELETE' }).then(function (r) {
          if (!r.ok && r.status !== 404) { KB.toast('Não deu para apagar a notificação'); sig = ''; }
        });
      }
      // a row slides out to the side (swipe to dismiss); the list itself scrolls vertically with the finger
      var sw = null, swallow = false;
      b.addEventListener('pointerdown', function (e) {
        var r = e.target.closest('.ib-r'); if (!r || r.dataset.gone) return;
        sw = { r: r, id: e.pointerId, x: e.clientX, y: e.clientY, dx: 0, axis: null, t: performance.now() };
      });
      b.addEventListener('pointermove', function (e) {
        if (!sw || e.pointerId !== sw.id) return;
        var dx = e.clientX - sw.x, dy = e.clientY - sw.y;
        if (!sw.axis && (Math.abs(dx) > 8 || Math.abs(dy) > 8)) {
          sw.axis = Math.abs(dx) > Math.abs(dy) ? 'x' : 'y';
          if (sw.axis === 'x') { try { b.setPointerCapture(e.pointerId); } catch (_) { } sw.r.style.transition = 'none'; }
        }
        if (sw.axis !== 'x') return;
        sw.dx = dx;
        var w = sw.r.offsetWidth || 1;
        sw.r.style.transform = 'translate3d(' + dx + 'px,0,0)';
        sw.r.style.opacity = String(1 - Math.min(Math.abs(dx) / w, 1) * .7);
        sw.r.classList.toggle('kill', Math.abs(dx) > w * .35);
      });
      function release(e) {
        if (!sw || (e && e.pointerId !== sw.id)) return;
        var s = sw; sw = null;
        if (s.axis !== 'x') return;
        swallow = true; setTimeout(function () { swallow = false; }, 80);
        var w = s.r.offsetWidth || 1, speed = Math.abs(s.dx) / Math.max(performance.now() - s.t, 1);
        s.r.style.transition = '';
        if (Math.abs(s.dx) > w * .35 || (speed > .6 && Math.abs(s.dx) > 30)) {
          var dir = s.dx > 0 ? 1 : -1, h = s.r.offsetHeight;
          s.r.dataset.gone = '1';
          KB.haptic();
          s.r.style.transform = 'translate3d(' + dir * w * 1.1 + 'px,0,0)';
          s.r.style.opacity = '0';
          // after it leaves, close the gap
          setTimeout(function () {
            s.r.style.transition = 'height .26s var(--ease), margin .26s var(--ease)';
            s.r.style.height = h + 'px';
            s.r.offsetHeight; // commit the start height
            s.r.style.height = '0px';
            s.r.style.marginBottom = 'calc(var(--u) * -.35)';
            setTimeout(function () { if (s.r.parentNode) s.r.parentNode.removeChild(s.r); remove(s.r.dataset.id); }, 270);
          }, 200);
        } else {
          s.r.classList.remove('kill');
          s.r.style.transform = '';
          s.r.style.opacity = '';
        }
      }
      b.addEventListener('pointerup', release);
      b.addEventListener('pointercancel', release);
      b.addEventListener('click', function (e) {
        if (swallow) return;
        var r = e.target.closest('.ib-r'); if (!r || r.dataset.gone) return;
        var n = byId(r.dataset.id); if (!n) return;
        KB.haptic();
        noticeSheet(n, function () { remove(n.id); draw(list.slice().reverse()); });
      });
      el.querySelector('[data-clear]').addEventListener('click', function () {
        if (!list.length) return;
        KB.api('/api/notify', { method: 'DELETE' }).then(function (r) {
          if (!r.ok) { KB.toast('Não deu para limpar as notificações'); return; }
          KB.haptic(); keep([]); draw([]);
        });
      });
      return {
        update: function (s, full) {
          if (!full || !s.inbox || sw) return;
          var next = s.inbox.map(function (n) { return n.id; }).join(',');
          if (next === sig) return;
          sig = next;
          draw(s.inbox);
        },
      };
    },
  });

  // ---------- Hacker News ----------
  KB.def('hackernews', {
    mount: function (el, w) {
      var count = KB.clamp(+KB.opt(w, 'count', 8) || 8, 3, 20);
      el.innerHTML = KB.header('hacker news', '<span style="color:#ff6600">Y</span>') + '<div class="w-b rows hn"></div>';
      var b = el.querySelector('.w-b');
      b.addEventListener('click', function (e) { var r = e.target.closest('[data-url]'); if (r) KB.openOnPc(r.dataset.url); });
      function load() {
        KB.feed('https://hacker-news.firebaseio.com/v0/topstories.json', 300000).then(function (ids) {
          return Promise.all(ids.slice(0, count).map(function (id) {
            return KB.feed('https://hacker-news.firebaseio.com/v0/item/' + id + '.json', 600000).catch(function () { return null; });
          }));
        }).then(function (items) {
          b.innerHTML = items.filter(Boolean).map(function (it, i) {
            var host = '';
            try { host = it.url ? new URL(it.url).hostname.replace(/^www\./, '') : 'news.ycombinator.com'; } catch (e) { }
            return '<div class="row press" data-url="' + esc(it.url || ('https://news.ycombinator.com/item?id=' + it.id)) + '"><span class="mono faint num">' + (i + 1) + '</span><span class="grow">' + esc(it.title) + '<span class="sub mono">' + esc(host) + '</span></span><span class="mono num" style="color:#ff6600">▲' + it.score + '</span><span class="mono faint num">' + (it.descendants || 0) + ' ' + KBIcons.html('chat') + '</span></div>';
          }).join('');
        }).catch(function () { b.innerHTML = '<div class="empty">sem conexão com o HN</div>'; });
      }
      load();
      var t = setInterval(load, 300000);
      return { destroy: function () { clearInterval(t); } };
    },
  });

  // ---------- exchange ----------
  KB.def('exchange', {
    mount: function (el, w) {
      var base = String(KB.opt(w, 'base', 'USD')).toUpperCase().trim();
      var syms = KB.list(KB.opt(w, 'symbols', 'BRL, EUR')).map(function (s) { return s.toUpperCase(); }).filter(function (s) { return s !== base; });
      el.innerHTML = KB.header('1 ' + base.toLowerCase(), '<span class="ex-d"></span>') + '<div class="w-b rows"></div>';
      el.addEventListener('click', function () { KB.openOnPc('https://www.google.com/finance/quote/' + encodeURIComponent(base) + '-' + encodeURIComponent(syms[0] || 'BRL')); });
      var b = el.querySelector('.w-b'), d = el.querySelector('.ex-d');
      function load() {
        KB.feed('https://api.frankfurter.dev/v1/latest?base=' + encodeURIComponent(base) + '&symbols=' + syms.map(encodeURIComponent).join(','), 1800000).then(function (r) {
          d.textContent = r.date || '';
          b.innerHTML = syms.map(function (s) {
            var v = r.rates && r.rates[s];
            return '<div class="row"><span class="mono dim">' + s + '</span><span class="grow"></span><b class="num">' + (v == null ? '–' : v.toLocaleString('pt-BR', { maximumFractionDigits: v < 10 ? 4 : 2 })) + '</b></div>';
          }).join('');
        }).catch(function () { b.innerHTML = '<div class="empty">câmbio indisponível</div>'; });
      }
      load();
      var t = setInterval(load, 1800000);
      return { destroy: function () { clearInterval(t); } };
    },
  });

  // ---------- crypto ----------
  KB.def('crypto', {
    mount: function (el, w) {
      var coins = KB.list(KB.opt(w, 'coins', 'bitcoin, ethereum')).map(function (c) { return c.toLowerCase(); });
      var cur = String(KB.opt(w, 'currency', 'brl')).toLowerCase().trim();
      el.innerHTML = KB.header('crypto', '<span>' + esc(cur) + '</span>') + '<div class="w-b rows cr"></div>';
      el.addEventListener('click', function (e) { var r = e.target.closest('[data-coin]'); KB.openOnPc('https://www.coingecko.com/pt/moedas/' + encodeURIComponent(r ? r.dataset.coin : coins[0])); });
      var b = el.querySelector('.w-b');
      function load() {
        KB.feed('https://api.coingecko.com/api/v3/simple/price?ids=' + coins.map(encodeURIComponent).join(',') + '&vs_currencies=' + encodeURIComponent(cur) + '&include_24hr_change=true', 120000).then(function (r) {
          b.innerHTML = coins.map(function (c) {
            var x = r[c] || {}, v = x[cur], ch = x[cur + '_24h_change'];
            var color = ch == null ? 'var(--text-3)' : ch >= 0 ? 'var(--green)' : 'var(--red)';
            return '<div class="row press" data-coin="' + esc(c) + '"><span class="grow">' + esc(c) + '</span><b class="num">' + (v == null ? '–' : v.toLocaleString('pt-BR', { maximumFractionDigits: v < 1 ? 6 : 0 })) + '</b><span class="mono num" style="color:' + color + ';min-width:4.2em;text-align:right">' + (ch == null ? '' : (ch >= 0 ? '+' : '') + ch.toFixed(1) + '%') + '</span></div>';
          }).join('');
        }).catch(function () { b.innerHTML = '<div class="empty">CoinGecko indisponível</div>'; });
      }
      load();
      var t = setInterval(load, 120000);
      return { destroy: function () { clearInterval(t); } };
    },
  });

  // ---------- custom HTML (sandboxed: scripts yes, same-origin no) ----------
  KB.def('html', {
    mount: function (el, w) {
      el.classList.add('bare');
      var f = document.createElement('iframe');
      f.setAttribute('sandbox', 'allow-scripts');
      f.setAttribute('referrerpolicy', 'no-referrer');
      f.className = 'hx';
      f.srcdoc = String(KB.opt(w, 'html', ''));
      el.appendChild(f);
      return {};
    },
  });

  // ---------- image ----------
  KB.def('image', {
    mount: function (el, w) {
      var url = String(KB.opt(w, 'url', '')).trim(), fit = KB.opt(w, 'fit', 'cover');
      el.classList.add('bare', 'im');
      if (!/^https:\/\//i.test(url)) { el.innerHTML = '<div class="empty">informe uma URL https</div>'; return {}; }
      var img = new Image();
      img.alt = '';
      img.style.objectFit = fit;
      img.onerror = function () { el.innerHTML = '<div class="empty">imagem não carregou</div>'; };
      img.src = url;
      el.appendChild(img);
      return {};
    },
  });
})();
