/* AI & code: quotas (with burn-rate forecast), quota ring, local AI sessions, GitHub. */
(function () {
  'use strict';
  var fmt = KB.fmt, esc = KB.esc;
  var BRAND = { claude: '#d97757', codex: '#10a37f' };

  function windowHtml(win) {
    var c = KB.levelColor(win.pct);
    var fc = win.fullAt
      ? '<span style="color:var(--amber)">no ritmo atual: cheio ' + fmt.until(win.fullAt) + '</span>'
      : win.resetsAt ? '<span class="faint qt-ok">' + KBIcons.html('check') + ' dura até o reset</span>' : '';
    return '<div class="qt-w"><div class="qt-r"><span>' + esc(win.label) + '</span><b class="num" style="color:' + c + '">' + fmt.pct(win.pct) + '</b></div>' +
      '<div class="bar" style="--c:' + c + '"><i style="width:' + KB.clamp(win.pct, 0, 100) + '%"></i></div>' +
      '<div class="qt-r mono qt-s"><span class="dim">' + (win.resetsAt ? 'reseta ' + fmt.until(win.resetsAt) : '') + '</span>' + fc + '</div></div>';
  }

  KB.def('quotas', {
    mount: function (el, w) {
      var want = KB.list(KB.opt(w, 'providers', 'claude, codex'));
      el.innerHTML = KB.header('ai quotas', '<span class="qt-u"></span>') + '<div class="w-b qt"></div>';
      var b = el.querySelector('.w-b'), u = el.querySelector('.qt-u'), sig = '';
      el.addEventListener('click', function () { KB.act('/api/refresh').then(function () { u.textContent = 'atualizando…'; }); });
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
          KB.tween(v, x.pct, fmt.pct);
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
      '<div class="acts"><button class="btn press" data-x="close">Fechar</button>' + (x.canOpen ? '<button class="btn primary press" data-x="code">Abrir no VS Code</button>' : '') + '</div>', function (root) {
      root.addEventListener('click', function (e) {
        var b = e.target.closest('[data-x]'); if (!b) return;
        if (b.dataset.x === 'code') KB.act('/api/sessions/open', { id: x.id });
        KB.closeSheet();
      });
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
  function checksDot(c) {
    if (!c) return '';
    var color = c.failed ? 'var(--red)' : c.running ? 'var(--amber)' : c.ok ? 'var(--green)' : 'var(--text-3)';
    return '<i class="dot' + (c.running && !c.failed ? ' pulse' : '') + '" style="--c:' + color + '"></i>';
  }
  function checksText(c) {
    if (!c) return 'sem checks';
    var bits = [];
    if (c.failed) bits.push(c.failed + ' falharam');
    if (c.running) bits.push(c.running + ' rodando');
    if (c.ok) bits.push(c.ok + ' ok');
    return bits.join(' · ') || 'sem checks';
  }
  function prSheet(repo, p) {
    var armed = false;
    KB.sheet('<h3>#' + p.number + ' ' + esc(p.title) + '</h3><div class="meta">' + esc(repo) + ' · ' + esc(p.author) + ' · ' + esc(p.branch || '') + ' → ' + esc(p.base || '') + '</div>' +
      '<dl class="kv"><dt>checks</dt><dd>' + checksDot(p.checks) + ' ' + checksText(p.checks) + '</dd><dt>estado</dt><dd>' + (p.draft ? 'rascunho' : p.reviewRequested ? 'revisão pedida para você' : 'aberto') + '</dd></dl>' +
      '<div class="acts"><button class="btn press" data-x="open">Abrir no PC</button>' +
      (p.mine ? '' : '<button class="btn ok press" data-x="approve">Aprovar</button>') +
      '<button class="btn danger press" data-x="merge">Mesclar</button></div>' +
      '<div class="meta" style="margin:12px 0 0">Aprovar e mesclar pedem um token com escrita em Pull requests (Integrações › GitHub).</div>',
      function (root) {
        root.addEventListener('click', function (e) {
          var b = e.target.closest('[data-x]'); if (!b) return;
          if (b.dataset.x === 'open') { KB.act('/api/github/open', { repo: repo, pr: p.number }); KB.closeSheet(); return; }
          if (b.dataset.x === 'approve') { KB.act('/api/github/act', { action: 'approve', repo: repo, pr: p.number }); KB.closeSheet(); return; }
          if (b.dataset.x === 'merge') {
            // merging is not undoable from the panel: the first tap arms, the second one merges
            if (!armed) { armed = true; KB.haptic(); b.textContent = 'Toque de novo para mesclar'; setTimeout(function () { armed = false; b.textContent = 'Mesclar'; }, 3000); return; }
            KB.act('/api/github/act', { action: 'merge', repo: repo, pr: p.number }); KB.closeSheet();
          }
        });
      });
  }
  function runSheet(repo, run) {
    var c = runColor(run);
    KB.sheet('<h3>' + esc(run.name || 'Workflow') + '</h3><div class="meta">' + esc(repo) + ' · ' + esc(run.branch || '') + '</div>' +
      '<dl class="kv"><dt>resultado</dt><dd><i class="dot" style="--c:' + c[0] + ';display:inline-block"></i> ' + esc(run.status === 'completed' ? (run.conclusion || '—') : run.status) + '</dd></dl>' +
      '<div class="acts"><button class="btn press" data-x="open">Abrir no PC</button>' +
      (run.status === 'completed' && run.conclusion !== 'success' ? '<button class="btn primary press" data-x="rerun">Re-executar falhas</button>' : '') + '</div>',
      function (root) {
        root.addEventListener('click', function (e) {
          var b = e.target.closest('[data-x]'); if (!b) return;
          if (b.dataset.x === 'open') KB.act('/api/github/open', { repo: repo, run: run.id });
          if (b.dataset.x === 'rerun') KB.act('/api/github/act', { action: 'rerun', repo: repo, run: run.id });
          KB.closeSheet();
        });
      });
  }
  function ago(iso) { var t = Date.parse(iso || ''); return isNaN(t) ? '' : fmt.ago(t); }
  function localChip(l) {
    if (!l) return '<span class="chip faint">não clonado</span>';
    if (l.error) return '<span class="chip" style="color:var(--amber)">erro no git</span>';
    var bits = [];
    if (l.changes) bits.push(l.changes + (l.changes === 1 ? ' alteração' : ' alterações'));
    if (l.ahead) bits.push('↑' + l.ahead);
    if (l.behind) bits.push('↓' + l.behind);
    var color = l.behind ? 'var(--amber)' : l.changes || l.ahead ? 'var(--accent)' : 'var(--green)';
    return '<span class="chip" style="color:' + color + '">' + (bits.join(' · ') || 'sincronizado') + '</span>';
  }
  function repoSheet(r) {
    var l = r.local;
    var commits = (r.commits || []).map(function (c) {
      return '<div class="cm"><span class="mono faint">' + esc(c.sha) + '</span><span class="grow">' + esc(c.message) + '</span><span class="faint">' + esc(ago(c.date).replace('há ', '')) + '</span></div>';
    }).join('') || '<div class="faint">sem commits</div>';
    var acts = '<button class="btn press" data-x="web">Abrir no GitHub</button>';
    if (l) {
      acts += '<button class="btn primary press" data-x="code">VS Code</button>';
      if (l.behind) acts += '<button class="btn press" data-x="pull">Puxar ↓' + l.behind + '</button>';
      if (l.ahead) acts += '<button class="btn press" data-x="push">Enviar ↑' + l.ahead + '</button>';
    } else acts += '<button class="btn primary press" data-x="clone">Clonar neste PC</button>';
    KB.sheet('<h3>' + esc(r.repo.split('/')[1]) + '</h3><div class="meta">' + esc(r.repo) + (r.language ? ' · ' + esc(r.language) : '') + ' · ★ ' + (r.stars || 0) + ' · ' + (r.issues || 0) + ' issues</div>' +
      (r.description ? '<div class="dim" style="margin:-6px 0 12px">' + esc(r.description) + '</div>' : '') +
      (l ? '<dl class="kv"><dt>no PC</dt><dd class="mono">' + esc(l.path) + '</dd><dt>branch</dt><dd class="mono">' + esc(l.branch || '—') + '</dd><dt>estado</dt><dd>' + localChip(l) + '</dd></dl>' : '') +
      '<div class="cms">' + commits + '</div>' +
      '<div class="acts" style="flex-wrap:wrap">' + acts + '</div>',
      function (root) {
        root.addEventListener('click', function (e) {
          var b = e.target.closest('[data-x]'); if (!b) return;
          var x = b.dataset.x;
          if (x === 'web') KB.act('/api/github/open', { repo: r.repo });
          else {
            if (x === 'clone' || x === 'pull' || x === 'push') KB.toast({ title: { clone: 'Clonando…', pull: 'Puxando…', push: 'Enviando…' }[x], text: r.repo, seconds: 3 });
            KB.act('/api/github/local', { repo: r.repo, action: x });
          }
          KB.closeSheet();
        });
      });
  }
  KB.def('github', {
    mount: function (el) {
      el.innerHTML = KB.header('github') + '<div class="w-b rows gh"></div>';
      var b = el.querySelector('.w-b'), repos = [], sig = '';
      b.addEventListener('click', function (e) {
        var t = e.target.closest('[data-pr],[data-run],[data-repo]'); if (!t) return;
        KB.haptic();
        var r = repos[+t.dataset.r]; if (!r) return;
        if (t.dataset.pr != null) prSheet(r.repo, r.pulls[+t.dataset.pr]);
        else if (t.dataset.run != null) runSheet(r.repo, r.runs[+t.dataset.run]);
        else repoSheet(r);
      });
      return {
        update: function (s, full) {
          if (!full || !s.github) return;
          var g = s.github;
          // redraw only when something changed: a rebuild every poll would waste the phone's time and could eat a tap
          var next = JSON.stringify([g.repos, g.error, g.empty]);
          if (next === sig) return;
          sig = next;
          if (g.empty) { b.innerHTML = '<div class="empty">conecte o GitHub em Integrações</div>'; return; }
          repos = g.repos || [];
          var html = repos.map(function (r, ri) {
            var run = (r.runs || [])[0], rc = run ? runColor(run) : null;
            var last = (r.commits || [])[0];
            return '<div class="gh-r"><div class="row press gh-repo" data-repo data-r="' + ri + '"><span class="grow"><b>' + esc(r.repo.split('/')[1]) + '</b>' +
              (r.language ? ' <span class="faint" style="font-size:.8em">' + esc(r.language) + '</span>' : '') +
              '<span class="sub mono">' + (last ? esc(last.message) + ' · ' + esc(ago(last.date)) : 'sem commits') + '</span></span>' + localChip(r.local) +
              (run ? '<span class="chip press" data-run="0" data-r="' + ri + '"><i class="dot' + (rc[1] ? ' pulse' : '') + '" style="--c:' + rc[0] + ';display:inline-block;vertical-align:middle"></i> ' + esc(run.name || 'ci') + '</span>' : '') + '</div>' +
              (r.pulls || []).slice(0, 3).map(function (p, pi) {
                return '<div class="row press gh-pr" data-pr="' + pi + '" data-r="' + ri + '">' + checksDot(p.checks) + '<span class="mono faint">#' + p.number + '</span><span class="grow">' + esc(p.title) + '</span>' +
                  (p.reviewRequested ? '<span class="chip" style="color:var(--amber)">revisar</span>' : p.draft ? '<span class="chip">draft</span>' : '<span class="chip">' + esc(p.author) + '</span>') + '</div>';
              }).join('') + '</div>';
          }).join('');
          b.innerHTML = html + (g.error ? '<div class="mono faint" style="font-size:.8em">' + esc(g.error) + '</div>' : '') || '<div class="empty">sem dados</div>';
        },
      };
    },
  });
})();
