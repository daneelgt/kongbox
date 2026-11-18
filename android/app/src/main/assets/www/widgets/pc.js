/* Computer widgets: stat, gauge, chart, meters, gpu, top processes, processes, media, volume, shortcuts, button, rules. */
(function () {
  'use strict';
  var fmt = KB.fmt, esc = KB.esc;

  function metric(name) { return KB.metrics[name] || KB.metrics.cpu; }
  function histMax(m, vals) { return m.max || Math.max.apply(null, (vals || []).concat([1])) * 1.15; }

  // ---------- stat tile ----------
  KB.def('stat', {
    mount: function (el, w) {
      var name = KB.opt(w, 'metric', 'cpu'), m = metric(name);
      el.innerHTML = KB.header(m.label) + '<div class="w-b st"><div class="st-v num">–</div><div class="st-s mono dim"></div></div>' + KB.spark(m.color);
      var v = el.querySelector('.st-v'), s = el.querySelector('.st-s'), sp = el.querySelector('.spark');
      return {
        update: function (st) {
          if (!st.sys) return;
          var val = m.get(st.sys), shown = m.show(val), sp = shown.indexOf(' ');
          // "12.4 KB/s" → big number, small unit (the way iOS widgets set units)
          v.innerHTML = sp > 0 ? esc(shown.slice(0, sp)) + '<small>' + esc(shown.slice(sp + 1)) + '</small>' : esc(shown);
          s.textContent = m.sub ? m.sub(st.sys) : '';
          var h = st.hist && st.hist[name];
          if (h) KB.setSpark(sp, h.slice(-60), histMax(m, h.slice(-60)));
        },
      };
    },
  });

  // ---------- gauge ----------
  KB.def('gauge', {
    mount: function (el, w) {
      var m = metric(KB.opt(w, 'metric', 'cpu'));
      el.innerHTML = '<div class="ga">' + KB.ring(0, m.color, 9) + '<div class="ga-c"><div class="ga-v num">–</div><div class="ga-l mono">' + esc(m.label) + '</div></div></div>';
      var ring = el.querySelector('svg'), v = el.querySelector('.ga-v');
      return {
        update: function (st) {
          if (!st.sys) return;
          var val = m.get(st.sys);
          v.textContent = m.show(val);
          var pct = m.max ? val / m.max * 100 : 0;
          KB.setRing(ring, pct, m.max === 100 && val >= 90 ? 'var(--red)' : m.color);
        },
      };
    },
  });

  // ---------- chart ----------
  KB.def('chart', {
    mount: function (el, w) {
      var names = KB.list(KB.opt(w, 'metrics', 'cpu')).filter(function (n) { return KB.metrics[n]; });
      if (!names.length) names = ['cpu'];
      var legend = names.map(function (n) { return '<span style="color:' + metric(n).color + '">' + esc(metric(n).short || metric(n).label) + ' <b class="num" data-v="' + n + '">–</b></span>'; }).join('');
      el.innerHTML = '<div class="w-h ch-h"><span class="ch-l mono">' + legend + '</span></div>' +
        '<div class="w-b ch"><svg viewBox="0 0 120 40" preserveAspectRatio="none">' +
        '<g stroke="rgba(255,255,255,.06)"><line x1="0" y1="10" x2="120" y2="10"/><line x1="0" y1="20" x2="120" y2="20"/><line x1="0" y1="30" x2="120" y2="30"/></g>' +
        names.map(function (n) { return '<path class="area" data-a="' + n + '" fill="' + metric(n).color + '" opacity=".14"/><path data-l="' + n + '" fill="none" stroke="' + metric(n).color + '" stroke-width="1.6" vector-effect="non-scaling-stroke" stroke-linejoin="round"/>'; }).join('') +
        '</svg></div>';
      var svg = el.querySelector('svg');
      return {
        update: function (st) {
          if (!st.sys) return;
          names.forEach(function (n) {
            var m = metric(n), b = el.querySelector('[data-v="' + n + '"]');
            if (b) b.textContent = m.show(m.get(st.sys));
          });
          if (!st.hist) return;
          // one shared scale for rates so download and upload are comparable
          var shared = 0;
          names.forEach(function (n) { if (!metric(n).max) shared = Math.max(shared, histMax(metric(n), st.hist[n])); });
          names.forEach(function (n) {
            var vals = st.hist[n] || [], m = metric(n);
            var p = KB.sparkPath(vals, m.max || shared, 120, 40);
            svg.querySelector('[data-l="' + n + '"]').setAttribute('d', p.line);
            svg.querySelector('[data-a="' + n + '"]').setAttribute('d', p.area);
          });
        },
      };
    },
  });

  // ---------- memory & disks ----------
  KB.def('meters', {
    mount: function (el) {
      el.innerHTML = KB.header('memory & disks') + '<div class="w-b rows mt"></div>';
      var b = el.querySelector('.w-b');
      return {
        update: function (st) {
          var s = st.sys; if (!s) return;
          var rows = [];
          if (s.ram) rows.push({ k: 'ram', v: s.ram.pct, sub: fmt.bytes(s.ram.used) + ' / ' + fmt.bytes(s.ram.total) });
          if (s.gpu && s.gpu.vramTotal) rows.push({ k: 'vram', v: s.gpu.vramPct, sub: fmt.bytes(s.gpu.vramUsed) + ' / ' + fmt.bytes(s.gpu.vramTotal) });
          (s.disks || []).forEach(function (d) { rows.push({ k: d.name, v: d.pct, sub: fmt.bytes(d.total - d.used) + ' livres' }); });
          if (b.children.length !== rows.length) {
            b.innerHTML = rows.map(function () { return '<div class="mt-r"><span class="mono k"></span><div class="bar"><i></i></div><b class="num"></b><span class="mono dim s"></span></div>'; }).join('');
          }
          rows.forEach(function (r, i) {
            var e = b.children[i];
            e.querySelector('.k').textContent = r.k.toLowerCase();
            var bar = e.querySelector('i');
            bar.style.width = r.v + '%';
            bar.parentNode.style.setProperty('--c', KB.levelColor(r.v));
            e.querySelector('b').textContent = fmt.pct(r.v);
            e.querySelector('.s').textContent = r.sub;
          });
        },
      };
    },
  });

  // ---------- gpu ----------
  KB.def('gpu', {
    mount: function (el) {
      el.innerHTML = KB.header('gpu') + '<div class="w-b gp"></div>';
      var b = el.querySelector('.w-b');
      return {
        update: function (st) {
          var g = st.sys && st.sys.gpu;
          if (!g) { b.innerHTML = '<div class="empty">sem leitura de GPU</div>'; return; }
          b.innerHTML = '<div class="gp-n">' + esc(g.name || 'GPU') + '</div><div class="gp-g">' +
            [['uso', fmt.pct(g.util)], ['temp', g.temp != null ? Math.round(g.temp) + '°' : '–'], ['energia', g.power != null ? Math.round(g.power) + ' W' : '–'], ['vram', g.vramTotal ? fmt.bytes(g.vramUsed) : '–']]
              .map(function (x) { return '<div><span class="mono faint">' + x[0] + '</span><b class="num">' + x[1] + '</b></div>'; }).join('') +
            '</div><div class="bar" style="--c:var(--purple)"><i style="width:' + (g.util || 0) + '%"></i></div>';
        },
      };
    },
  });

  // ---------- processes ----------
  function procSheet(p) {
    KB.sheet('<h3>' + esc(p.name) + '</h3><div class="meta">' + p.count + ' instância(s) · cpu ' + fmt.pct(p.cpu) + ' · ' + fmt.bytes(p.mem) + '</div>' +
      (p.protected ? '<pre>Processo do sistema: o KONGBOX não oferece encerrar.</pre><div class="acts"><button class="btn press" data-x="close">Fechar</button></div>'
        : '<div class="acts"><button class="btn press" data-x="close">Cancelar</button>' + (p.count > 1 ? '<button class="btn danger press" data-x="one">Encerrar 1</button>' : '') +
          '<button class="btn danger press" data-x="all">Encerrar ' + (p.count > 1 ? 'todos' : '') + '</button></div>'),
    function (root) {
      root.addEventListener('click', function (e) {
        var b = e.target.closest('[data-x]'); if (!b) return;
        KB.haptic();
        if (b.dataset.x === 'close') { KB.closeSheet(); return; }
        KB.api('/api/procs/kill', { method: 'POST', body: { name: p.name, all: b.dataset.x === 'all' || p.count === 1 } }).then(function (r) {
          KB.toast({ title: p.name, text: r.data && (r.data.msg || r.data.error) || 'sem resposta', level: r.ok ? 'ok' : 'error' });
        });
        KB.closeSheet();
      });
    });
  }

  function procList(el, limit, sort) {
    el.addEventListener('click', function (e) {
      var r = e.target.closest('[data-p]'); if (!r) return;
      var list = KB.full.procs || [];
      for (var i = 0; i < list.length; i++) if (list[i].name === r.dataset.p) { KB.haptic(); procSheet(list[i]); return; }
    });
    return function (st) {
      if (!st.procs) return;
      var list = st.procs.filter(function (p) { return p.name !== 'Idle' && p.name !== 'System'; }).slice();
      if (sort === 'mem') list.sort(function (a, b) { return b.mem - a.mem; });
      var max = Math.max.apply(null, list.slice(0, limit).map(function (p) { return sort === 'mem' ? p.mem : p.cpu; }).concat([1]));
      el.innerHTML = list.slice(0, limit).map(function (p) {
        var v = sort === 'mem' ? p.mem : p.cpu;
        return '<div class="row pr press" data-p="' + esc(p.name) + '"><span class="grow">' + esc(p.name) + (p.count > 1 ? ' <span class="faint">×' + p.count + '</span>' : '') +
          '<span class="pr-b"><i style="width:' + (v / max * 100).toFixed(1) + '%"></i></span></span><span class="num mono">' + fmt.pct(p.cpu) + '</span><span class="num mono dim pr-m">' + fmt.bytes(p.mem) + '</span></div>';
      }).join('') || '<div class="empty">carregando processos</div>';
    };
  }

  KB.def('topprocs', {
    mount: function (el, w) {
      var sort = KB.opt(w, 'sort', 'cpu');
      el.innerHTML = KB.header('top ' + (sort === 'mem' ? 'memória' : 'cpu')) + '<div class="w-b rows"></div>';
      var upd = procList(el.querySelector('.w-b'), 6, sort);
      return { update: function (s, full) { if (full) upd(s); } };
    },
  });

  KB.def('processes', {
    mount: function (el) {
      var sort = 'cpu';
      el.innerHTML = '<div class="w-h"><span>processes</span><span class="tag"><button class="chip press" data-s="cpu">cpu</button> <button class="chip press" data-s="mem">memória</button></span></div><div class="w-b rows ps"></div>';
      var body = el.querySelector('.w-b'), upd = procList(body, 30, sort);
      el.querySelector('.w-h').addEventListener('click', function (e) {
        var b = e.target.closest('[data-s]'); if (!b) return;
        sort = b.dataset.s; upd = procList(body, 30, sort); upd(KB.full);
        mark();
      });
      function mark() { Array.prototype.forEach.call(el.querySelectorAll('[data-s]'), function (b) { b.style.color = b.dataset.s === sort ? 'var(--text)' : ''; }); }
      mark();
      return { update: function (s, full) { if (full) upd(s); } };
    },
  });

  // ---------- media ----------
  var ICON = {
    prev: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M6 6h2v12H6zM9.5 12 18 6v12z"/></svg>',
    next: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M16 6h2v12h-2zM6 18V6l8.5 6z"/></svg>',
    play: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M8 5.5v13l11-6.5z"/></svg>',
    pause: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M7 5h3.5v14H7zM13.5 5H17v14h-3.5z"/></svg>',
  };
  KB.icons = ICON;
  KB.def('media', {
    mount: function (el) {
      el.innerHTML = '<div class="md-bg"></div><div class="md">' +
        '<div class="md-art"></div>' +
        '<div class="md-info"><div class="md-app mono"></div><div class="md-t"></div><div class="md-a dim"></div>' +
        '<div class="md-p" data-noswipe><div class="bar"><i></i></div><div class="md-time mono faint"><span class="cur">0:00</span><span class="dur">0:00</span></div></div>' +
        '<div class="md-c"><button class="press" data-m="prev">' + ICON.prev + '</button><button class="press md-pp" data-m="play-pause">' + ICON.play + '</button><button class="press" data-m="next">' + ICON.next + '</button></div></div></div>';
      var bg = el.querySelector('.md-bg'), art = el.querySelector('.md-art'), t = el.querySelector('.md-t'), a = el.querySelector('.md-a'), app = el.querySelector('.md-app');
      var bar = el.querySelector('.bar i'), cur = el.querySelector('.cur'), dur = el.querySelector('.dur'), pp = el.querySelector('.md-pp');
      // the text column gets its own scale: it is much narrower than the widget once the artwork is in
      KB.observe(el.querySelector('.md-info'));
      var lastArt = null, m = null, at = 0;
      el.querySelector('.md-c').addEventListener('click', function (e) {
        var b = e.target.closest('[data-m]'); if (!b) return;
        KB.haptic();
        if (m && b.dataset.m === 'play-pause') { m.playing = !m.playing; pp.innerHTML = m.playing ? ICON.pause : ICON.play; }
        KB.api('/api/media/' + b.dataset.m, { method: 'POST' });
      });
      el.querySelector('.md-p').addEventListener('click', function (e) {
        if (!m || !m.canSeek || !m.dur) return;
        var r = this.getBoundingClientRect(), p = KB.clamp((e.clientX - r.left) / r.width, 0, 1);
        KB.haptic();
        m.pos = p * m.dur; at = Date.now(); tickPos();
        KB.api('/api/media/seek?sec=' + (p * m.dur).toFixed(1), { method: 'POST' });
      });
      function tickPos() {
        if (!m) return;
        var pos = m.pos + (m.playing ? (Date.now() - at) / 1000 : 0);
        if (m.dur) pos = Math.min(pos, m.dur);
        cur.textContent = fmt.clock(pos);
        dur.textContent = m.dur ? fmt.clock(m.dur) : '';
        bar.style.width = m.dur ? (pos / m.dur * 100) + '%' : '0%';
      }
      return {
        secondly: tickPos,
        update: function (s) {
          m = s.media;
          el.classList.toggle('idle', !m || !m.title);
          if (!m || !m.title) {
            t.textContent = 'Nada tocando'; a.textContent = 'dê play em algo no PC'; app.textContent = '// media';
            art.innerHTML = ICON.play; bg.style.backgroundImage = ''; bar.style.width = '0%'; cur.textContent = ''; dur.textContent = '';
            lastArt = null; return;
          }
          at = Date.now();
          t.textContent = m.title;
          a.textContent = [m.artist, m.album].filter(Boolean).join(' — ');
          app.textContent = '// ' + (m.app || 'media').toLowerCase();
          pp.innerHTML = m.playing ? ICON.pause : ICON.play;
          if (m.art !== lastArt) {
            lastArt = m.art;
            if (m.art) {
              var url = '/api/art?k=' + m.art;
              art.innerHTML = '<img alt="" src="' + url + '">';
              bg.style.backgroundImage = 'url("' + url + '")';
            } else { art.innerHTML = ICON.play; bg.style.backgroundImage = ''; }
          }
          tickPos();
        },
      };
    },
  });

  // ---------- volume ----------
  KB.def('volume', {
    mount: function (el) {
      el.innerHTML = '<div class="vo"><div class="vo-s" data-noswipe><i></i><span class="vo-v num"></span></div><button class="vo-m press"></button></div>';
      var slider = el.querySelector('.vo-s'), fill = slider.querySelector('i'), val = el.querySelector('.vo-v'), mute = el.querySelector('.vo-m');
      var level = 0, muted = false, dragging = false, sendT = 0;
      function draw() {
        fill.style.height = (muted ? 0 : level) + '%';
        val.textContent = muted ? 'mudo' : level + '%';
        var waves = muted ? '<path d="M16 9l5 6M21 9l-5 6"/>' : (level > 0 ? '<path d="M15.5 9.5a3.5 3.5 0 0 1 0 5"/>' : '') + (level >= 40 ? '<path d="M18 7a7 7 0 0 1 0 10"/>' : '');
        mute.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z" fill="currentColor"/>' + waves + '</svg>';
        mute.classList.toggle('on', muted);
      }
      function setFromY(y) {
        var r = slider.getBoundingClientRect();
        level = Math.round(KB.clamp((r.bottom - y) / r.height, 0, 1) * 100);
        muted = false; draw();
        clearTimeout(sendT);
        sendT = setTimeout(function () { KB.api('/api/volume', { method: 'POST', body: { level: level, muted: false } }); }, 60);
      }
      slider.addEventListener('pointerdown', function (e) { dragging = true; try { slider.setPointerCapture(e.pointerId); } catch (err) { } setFromY(e.clientY); });
      slider.addEventListener('pointermove', function (e) { if (dragging) setFromY(e.clientY); });
      slider.addEventListener('pointerup', function () { dragging = false; KB.haptic(); });
      slider.addEventListener('pointercancel', function () { dragging = false; });
      mute.addEventListener('click', function () {
        KB.haptic(); muted = !muted; draw();
        KB.api('/api/volume', { method: 'POST', body: { toggleMute: true } });
      });
      return {
        update: function (s) {
          if (!s.volume || dragging || Date.now() - sendT < 1500) return;
          level = s.volume.level; muted = s.volume.muted; draw();
        },
      };
    },
  });

  // ---------- shortcuts ----------
  function runShortcut(id, el) {
    KB.haptic();
    if (el) { el.classList.add('fired'); setTimeout(function () { el.classList.remove('fired'); }, 450); }
    KB.api('/api/run/' + encodeURIComponent(id), { method: 'POST' }).then(function (r) {
      if (!r.ok) KB.toast({ title: 'Atalho falhou', text: (r.data && r.data.error) || 'agente não respondeu', level: 'error' });
    });
  }
  function keyHtml(s) {
    return '<button class="key press" data-id="' + esc(s.id) + '" style="--kc:' + esc(s.color || '#3a3a3c') + '"><span class="key-i">' + esc(s.icon || '▢') + '</span><span class="key-l">' + esc(s.label || s.id) + '</span></button>';
  }
  function toggles(el, st) {
    Array.prototype.forEach.call(el.querySelectorAll('.key'), function (k) {
      var sc = (KB.cfg.shortcuts || []).filter(function (x) { return x.id === k.dataset.id; })[0];
      if (!sc) return;
      var on = null;
      if (sc.id === 'mute' || sc.toggle === 'mute') on = !!st.muted;
      else if (sc.toggle && st.push && st.push[sc.toggle]) on = /^(1|on|true|yes|sim)$/i.test(String(st.push[sc.toggle].value));
      k.classList.toggle('on', on === true);
    });
  }
  KB.def('shortcuts', {
    mount: function (el, w) {
      var cols = KB.clamp(+KB.opt(w, 'columns', 4) || 4, 1, 8), ids = KB.list(KB.opt(w, 'ids', ''));
      var list = (KB.cfg.shortcuts || []).filter(function (s) { return !ids.length || ids.indexOf(s.id) >= 0; });
      if (ids.length) list.sort(function (a, b) { return ids.indexOf(a.id) - ids.indexOf(b.id); });
      el.classList.add('bare');
      el.innerHTML = list.length ? '<div class="keys" style="--cols:' + cols + '">' + list.map(keyHtml).join('') + '</div>' : '<div class="empty">nenhum atalho — crie em Atalhos</div>';
      Array.prototype.forEach.call(el.querySelectorAll('.key'), function (k) { KB.observe(k); });
      el.addEventListener('click', function (e) { var k = e.target.closest('.key'); if (k) runShortcut(k.dataset.id, k); });
      return { update: function (s) { toggles(el, s); } };
    },
  });
  KB.def('button', {
    mount: function (el, w) {
      var id = KB.opt(w, 'id', '');
      var s = (KB.cfg.shortcuts || []).filter(function (x) { return x.id === id; })[0];
      el.classList.add('bare');
      el.innerHTML = s ? '<div class="keys one">' + keyHtml(s) + '</div>' : '<div class="empty">atalho "' + esc(id) + '" não existe</div>';
      Array.prototype.forEach.call(el.querySelectorAll('.key'), function (k) { KB.observe(k); });
      el.addEventListener('click', function (e) { var k = e.target.closest('.key'); if (k) runShortcut(k.dataset.id, k); });
      return { update: function (st) { toggles(el, st); } };
    },
  });

  // ---------- rules ----------
  KB.def('rules', {
    mount: function (el) {
      el.innerHTML = '<div class="w-h"><span>rules</span><span class="tag"><button class="chip press" data-p>pausar 1 h</button></span></div><div class="w-b rows"></div>';
      var b = el.querySelector('.w-b'), btn = el.querySelector('[data-p]'), paused = false;
      btn.addEventListener('click', function () {
        KB.haptic();
        KB.api('/api/rules/pause', { method: 'POST', body: { minutes: paused ? 0 : 60 } });
        paused = !paused; btn.textContent = paused ? 'retomar' : 'pausar 1 h';
      });
      return {
        update: function (s, full) {
          if (!full) return;
          paused = !!s.rulesPausedUntil;
          btn.textContent = paused ? 'retomar' : 'pausar 1 h';
          var r = s.rules || [];
          b.innerHTML = r.length ? r.map(function (x) {
            var c = !x.enabled ? 'var(--text-3)' : paused ? 'var(--amber)' : x.holding ? 'var(--green)' : 'var(--text-3)';
            return '<div class="row"><i class="dot' + (x.holding && !paused ? ' pulse' : '') + '" style="--c:' + c + '"></i><span class="grow">' + esc(x.name) +
              '<span class="sub mono">' + (x.enabled ? (x.lastFired ? 'disparou ' + fmt.ago(x.lastFired) : 'aguardando') : 'desligada') + '</span></span></div>';
          }).join('') : '<div class="empty">nenhuma regra</div>';
        },
      };
    },
  });
})();
