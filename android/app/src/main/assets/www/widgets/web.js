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
  KB.def('inbox', {
    mount: function (el) {
      el.innerHTML = KB.header('notifications') + '<div class="w-b rows"></div>';
      var b = el.querySelector('.w-b'), last = -1;
      return {
        update: function (s, full) {
          if (!full || !s.inbox) return;
          var top = s.inbox.length ? s.inbox[s.inbox.length - 1].id : 0;
          if (top === last) return;
          last = top;
          b.innerHTML = s.inbox.slice().reverse().map(function (n) {
            return '<div class="row"><i class="dot" style="--c:' + (LEVEL[n.level] || 'var(--accent)') + '"></i><span class="grow">' + esc(n.title) + '<span class="sub">' + esc(n.text || '') + '</span></span><span class="chip">' + fmt.ago(n.time).replace('há ', '') + '</span></div>';
          }).join('') || '<div class="empty">POST /api/notify</div>';
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
      function load() {
        KB.feed('https://hacker-news.firebaseio.com/v0/topstories.json', 300000).then(function (ids) {
          return Promise.all(ids.slice(0, count).map(function (id) {
            return KB.feed('https://hacker-news.firebaseio.com/v0/item/' + id + '.json', 600000).catch(function () { return null; });
          }));
        }).then(function (items) {
          b.innerHTML = items.filter(Boolean).map(function (it, i) {
            var host = '';
            try { host = it.url ? new URL(it.url).hostname.replace(/^www\./, '') : 'news.ycombinator.com'; } catch (e) { }
            return '<div class="row"><span class="mono faint num">' + (i + 1) + '</span><span class="grow">' + esc(it.title) + '<span class="sub mono">' + esc(host) + '</span></span><span class="mono num" style="color:#ff6600">▲' + it.score + '</span><span class="mono faint num">' + (it.descendants || 0) + '💬</span></div>';
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
      el.innerHTML = KB.header('crypto', '<span>' + esc(cur) + '</span>') + '<div class="w-b rows"></div>';
      var b = el.querySelector('.w-b');
      function load() {
        KB.feed('https://api.coingecko.com/api/v3/simple/price?ids=' + coins.map(encodeURIComponent).join(',') + '&vs_currencies=' + encodeURIComponent(cur) + '&include_24hr_change=true', 120000).then(function (r) {
          b.innerHTML = coins.map(function (c) {
            var x = r[c] || {}, v = x[cur], ch = x[cur + '_24h_change'];
            var color = ch == null ? 'var(--text-3)' : ch >= 0 ? 'var(--green)' : 'var(--red)';
            return '<div class="row"><span class="grow">' + esc(c) + '</span><b class="num">' + (v == null ? '–' : v.toLocaleString('pt-BR', { maximumFractionDigits: v < 1 ? 6 : 0 })) + '</b><span class="mono num" style="color:' + color + ';min-width:4.2em;text-align:right">' + (ch == null ? '' : (ch >= 0 ? '+' : '') + ch.toFixed(1) + '%') + '</span></div>';
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
