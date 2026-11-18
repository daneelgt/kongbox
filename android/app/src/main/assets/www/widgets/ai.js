/* AI & code: quotas (with burn-rate forecast), quota ring, local AI sessions, GitHub. */
(function () {
  'use strict';
  var fmt = KB.fmt, esc = KB.esc;
  var BRAND = { claude: '#d97757', codex: '#10a37f' };

  function windowHtml(win) {
    var c = KB.levelColor(win.pct);
    var fc = win.fullAt
      ? '<span style="color:var(--amber)">no ritmo atual: cheio ' + fmt.until(win.fullAt) + '</span>'
      : win.resetsAt ? '<span class="faint">✓ dura até o reset</span>' : '';
    return '<div class="qt-w"><div class="qt-r"><span>' + esc(win.label) + '</span><b class="num" style="color:' + c + '">' + fmt.pct(win.pct) + '</b></div>' +
      '<div class="bar" style="--c:' + c + '"><i style="width:' + KB.clamp(win.pct, 0, 100) + '%"></i></div>' +
      '<div class="qt-r mono qt-s"><span class="dim">' + (win.resetsAt ? 'reseta ' + fmt.until(win.resetsAt) : '') + '</span>' + fc + '</div></div>';
  }

  KB.def('quotas', {
    mount: function (el, w) {
      var want = KB.list(KB.opt(w, 'providers', 'claude, codex'));
      el.innerHTML = KB.header('ai quotas', '<span class="qt-u"></span>') + '<div class="w-b qt"></div>';
      var b = el.querySelector('.w-b'), u = el.querySelector('.qt-u'), sig = '';
      return {
        update: function (s, full) {
          if (!full || !s.quotas) return;
          var list = s.quotas.filter(function (q) { return !want.length || want.indexOf(q.provider) >= 0; });
          var next = JSON.stringify(list);
          if (next === sig) return;
          sig = next;
          var newest = Math.max.apply(null, list.map(function (q) { return q.updated || 0; }).concat([0]));
          u.textContent = newest ? fmt.ago(newest) : '';
          b.innerHTML = list.map(function (q) {
            return '<div class="qt-p" style="--brand:' + (BRAND[q.provider] || 'var(--accent)') + '"><div class="qt-h"><i class="dot" style="--c:var(--brand)"></i><b>' + esc(q.name) + '</b>' +
              (q.plan ? '<span class="chip">' + esc(q.plan) + '</span>' : '') + '</div>' +
              (q.windows && q.windows.length ? q.windows.map(windowHtml).join('') : '<div class="empty">' + esc(q.error || 'sem dados') + '</div>') + '</div>';
          }).join('') || '<div class="empty">nenhum provedor ligado</div>';
        },
      };
    },
  });

  KB.def('quotaring', {
    mount: function (el, w) {
      var prov = KB.opt(w, 'provider', 'claude'), win = KB.opt(w, 'window', 'session');
      el.innerHTML = '<div class="ga">' + KB.ring(0, BRAND[prov] || 'var(--accent)', 9) + '<div class="ga-c"><div class="ga-v num">–</div><div class="ga-l mono">' + esc(prov) + ' · ' + (win === 'weekly' ? '7d' : '5h') + '</div><div class="ga-s mono faint"></div></div></div>';
      var ring = el.querySelector('svg'), v = el.querySelector('.ga-v'), sub = el.querySelector('.ga-s');
      return {
        update: function (s, full) {
          if (!full || !s.quotas) return;
          var q = s.quotas.filter(function (x) { return x.provider === prov; })[0];
          var x = q && (q.windows || []).filter(function (y) { return y.id === win; })[0];
          if (!x) { v.textContent = '–'; sub.textContent = q && q.error ? 'sem dados' : ''; return; }
          v.textContent = fmt.pct(x.pct);
          sub.textContent = x.resetsAt ? '↺ ' + fmt.dur((x.resetsAt - Date.now()) / 1000) : '';
          KB.setRing(ring, x.pct, x.pct >= 90 ? 'var(--red)' : (BRAND[prov] || 'var(--accent)'));
        },
      };
    },
  });

  // ---------- sessions ----------
  var STATE = { working: ['var(--green)', 'trabalhando'], waiting: ['var(--amber)', 'esperando você'], idle: ['var(--text-3)', 'parada'] };
  function shortModel(m) {
    return String(m || '').replace(/^claude-/, '').replace(/-\d{8}$/, '').replace(/^gpt-/, 'gpt-') || '—';
  }
  function sessionSheet(x) {
    var t = x.tokens || {};
    KB.sheet('<h3>' + esc(x.project || 'sessão') + '</h3><div class="meta">' + esc(x.tool) + ' · ' + esc(shortModel(x.model)) + (x.branch ? ' · ⎇ ' + esc(x.branch) : '') + '</div>' +
      '<dl class="kv"><dt>estado</dt><dd>' + STATE[x.state][1] + '</dd><dt>início</dt><dd>' + new Date(x.start).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }) + '</dd>' +
      '<dt>duração</dt><dd>' + fmt.dur((x.last - x.start) / 1000) + '</dd><dt>última atividade</dt><dd>' + fmt.ago(x.last) + '</dd>' +
      '<dt>prompts</dt><dd>' + x.turns + '</dd><dt>ferramentas</dt><dd>' + x.tools + '</dd>' +
      '<dt>tokens entrada</dt><dd>' + fmt.num(t['in']) + '</dd><dt>tokens saída</dt><dd>' + fmt.num(t.out) + '</dd>' +
      '<dt>cache lido</dt><dd>' + fmt.num(t.cacheRead) + '</dd>' + (t.cacheWrite ? '<dt>cache escrito</dt><dd>' + fmt.num(t.cacheWrite) + '</dd>' : '') + '</dl>' +
      '<div class="meta">Os tokens descrevem esta sessão local, não a cota da conta. Nenhum texto de prompt sai do PC.</div>' +
      '<div class="acts"><button class="btn press">Fechar</button></div>', function (root) {
      root.querySelector('.acts button').addEventListener('click', KB.closeSheet);
    });
  }
  KB.def('sessions', {
    mount: function (el) {
      el.innerHTML = KB.header('ai sessions', '<span class="ss-c"></span>') + '<div class="w-b rows"></div>';
      var b = el.querySelector('.w-b'), count = el.querySelector('.ss-c'), list = [];
      b.addEventListener('click', function (e) {
        var r = e.target.closest('[data-i]'); if (!r) return;
        KB.haptic(); sessionSheet(list[+r.dataset.i]);
      });
      return {
        update: function (s, full) {
          if (!full || !s.sessions) return;
          list = s.sessions;
          var active = list.filter(function (x) { return x.state !== 'idle'; }).length;
          count.textContent = active ? active + ' ativa' + (active > 1 ? 's' : '') : '';
          b.innerHTML = list.length ? list.map(function (x, i) {
            var st = STATE[x.state] || STATE.idle;
            return '<div class="row press" data-i="' + i + '"><i class="dot' + (x.state === 'working' ? ' pulse' : '') + '" style="--c:' + st[0] + '"></i>' +
              '<span class="grow">' + esc(x.project || 'sessão') + '<span class="sub mono">' + esc(x.tool) + ' · ' + esc(shortModel(x.model)) + ' · ' + st[1] + '</span></span>' +
              '<span class="mono dim num">' + fmt.num((x.tokens || {}).out) + ' out</span><span class="chip">' + fmt.ago(x.last).replace('há ', '') + '</span></div>';
          }).join('') : '<div class="empty">nenhuma sessão nas últimas 24 h</div>';
        },
      };
    },
  });

  // ---------- github ----------
  function runColor(r) {
    if (r.status !== 'completed') return ['var(--amber)', true];
    if (r.conclusion === 'success') return ['var(--green)', false];
    if (r.conclusion === 'failure' || r.conclusion === 'timed_out') return ['var(--red)', false];
    return ['var(--text-3)', false];
  }
  KB.def('github', {
    mount: function (el) {
      el.innerHTML = KB.header('github') + '<div class="w-b rows gh"></div>';
      var b = el.querySelector('.w-b');
      return {
        update: function (s, full) {
          if (!full || !s.github) return;
          var g = s.github;
          if (g.empty) { b.innerHTML = '<div class="empty">adicione repositórios em Geral › GitHub</div>'; return; }
          var html = (g.repos || []).map(function (r) {
            var run = (r.runs || [])[0], rc = run ? runColor(run) : null;
            return '<div class="gh-r"><div class="gh-h"><span class="mono">' + esc(r.repo) + '</span>' +
              (run ? '<span class="chip"><i class="dot' + (rc[1] ? ' pulse' : '') + '" style="--c:' + rc[0] + ';display:inline-block;vertical-align:middle"></i> ' + esc(run.name || 'ci') + '</span>' : '') + '</div>' +
              (r.pulls || []).slice(0, 4).map(function (p) {
                return '<div class="row"><span class="mono faint">#' + p.number + '</span><span class="grow">' + esc(p.title) + '</span>' +
                  (p.reviewRequested ? '<span class="chip" style="color:var(--amber)">revisar</span>' : p.draft ? '<span class="chip">draft</span>' : '<span class="chip">' + esc(p.author) + '</span>') + '</div>';
              }).join('') + '</div>';
          }).join('');
          b.innerHTML = html + (g.error ? '<div class="mono faint" style="font-size:.8em">' + esc(g.error) + '</div>' : '') || '<div class="empty">sem dados</div>';
        },
      };
    },
  });
})();
