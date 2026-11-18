/* Personal: note (editable from the phone), checklist, counter, breathe, quote, title, phone battery. */
(function () {
  'use strict';
  var esc = KB.esc;

  // ---------- note ----------
  KB.def('note', {
    mount: function (el, w, ctx) {
      var text = KB.opt(w, 'text', '');
      el.innerHTML = KB.header('note', '<span class="faint">toque para editar</span>') + '<div class="w-b nt"></div>';
      var b = el.querySelector('.w-b');
      function draw() {
        b.innerHTML = esc(text).split('\n').map(function (l) {
          var m = /^(\s*)(\/\/.*|#.*)$/.exec(l);
          if (m) return '<div class="nt-c">' + l + '</div>';
          if (/^\s*(›|>|-|•|\*)\s?/.test(l)) return '<div class="nt-i">' + l.replace(/^\s*(›|&gt;|-|•|\*)\s?/, '<span class="ar">›</span> ') + '</div>';
          return '<div>' + (l || '&nbsp;') + '</div>';
        }).join('');
      }
      draw();
      el.addEventListener('click', function () {
        if (KB.preview) return;
        KB.haptic();
        KB.sheet('<h3>Nota</h3><div class="meta">salva no PC, aparece em todos os celulares</div><textarea spellcheck="false"></textarea><div class="acts"><button class="btn press" data-x="0">Cancelar</button><button class="btn primary press" data-x="1">Salvar</button></div>', function (root) {
          var ta = root.querySelector('textarea');
          ta.value = text;
          setTimeout(function () { ta.focus(); }, 350);
          root.addEventListener('click', function (e) {
            var x = e.target.closest('[data-x]'); if (!x) return;
            if (x.dataset.x === '1') {
              text = ta.value; draw();
              KB.api('/api/widget/' + encodeURIComponent(ctx.page.id) + '/' + encodeURIComponent(w.id) + '/note', { method: 'POST', body: { text: text } }).then(function (r) {
                if (!r.ok) KB.toast({ title: 'Nota não salva', text: (r.data && r.data.error) || 'agente offline', level: 'error' });
              });
            }
            KB.closeSheet();
          });
        });
      });
      return {};
    },
  });

  // ---------- checklist ----------
  function today() { var d = new Date(); return d.getFullYear() + '-' + (d.getMonth() + 1) + '-' + d.getDate(); }
  KB.def('checklist', {
    mount: function (el, w) {
      var items = KB.list(KB.opt(w, 'items', [])), daily = KB.opt(w, 'daily', true), key = 'check:' + w.id;
      var st = KB.store.get(key, { day: today(), done: {} });
      el.innerHTML = KB.header('checklist', '<span class="ck-n num"></span>') + '<div class="w-b rows cl"></div>';
      var b = el.querySelector('.w-b'), n = el.querySelector('.ck-n');
      function draw() {
        if (daily && st.day !== today()) st = { day: today(), done: {} };
        var count = 0;
        b.innerHTML = items.map(function (it, i) {
          var on = !!st.done[it]; if (on) count++;
          return '<button class="row cl-i press' + (on ? ' on' : '') + '" data-i="' + i + '"><span class="cl-b"><svg viewBox="0 0 24 24"><path d="M6 12.5l4 4 8-9" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"/></svg></span><span class="grow">' + esc(it) + '</span></button>';
        }).join('') || '<div class="empty">sem itens</div>';
        n.textContent = count + '/' + items.length;
      }
      b.addEventListener('click', function (e) {
        var r = e.target.closest('[data-i]'); if (!r) return;
        KB.haptic();
        var it = items[+r.dataset.i];
        st.done[it] = !st.done[it];
        KB.store.set(key, st); draw();
        if (items.every(function (x) { return st.done[x]; })) KB.toast({ title: 'Checklist zerado', text: 'Tudo feito hoje.', level: 'ok' });
      });
      draw();
      return { secondly: function () { if (daily && st.day !== today()) draw(); } };
    },
  });

  // ---------- counter ----------
  KB.def('counter', {
    mount: function (el, w) {
      var key = 'count:' + w.id, n = KB.store.get(key, 0);
      el.innerHTML = KB.header(KB.opt(w, 'title', 'counter')) + '<div class="w-b co"><button class="co-b press" data-d="-1">−</button><div class="co-v num"></div><button class="co-b press" data-d="1">+</button></div>';
      var v = el.querySelector('.co-v');
      function draw(bump) {
        v.textContent = n;
        if (bump) { v.classList.remove('bump'); void v.offsetWidth; v.classList.add('bump'); }
      }
      el.querySelector('.co').addEventListener('click', function (e) {
        var b = e.target.closest('[data-d]'); if (!b) return;
        KB.haptic(); n += +b.dataset.d; KB.store.set(key, n); draw(true);
      });
      v.addEventListener('click', function () { if (n !== 0 && confirmReset()) { n = 0; KB.store.set(key, n); draw(true); } });
      var armed = 0;
      function confirmReset() { if (Date.now() - armed < 1500) return true; armed = Date.now(); KB.toast({ title: 'Toque de novo para zerar', seconds: 1.5 }); return false; }
      draw();
      return {};
    },
  });

  // ---------- breathe (box breathing 4-4-4-4) ----------
  KB.def('breathe', {
    mount: function (el) {
      el.innerHTML = '<div class="br"><div class="br-o"><div class="br-c"></div></div><div class="br-t"></div><div class="br-s mono faint">box breathing · 4-4-4-4</div></div>';
      var c = el.querySelector('.br-c'), t = el.querySelector('.br-t');
      var phases = [['Inspire', 1], ['Segure', 1], ['Expire', .45], ['Segure', .45]];
      function secondly() {
        var s = Math.floor(Date.now() / 1000) % 16, p = Math.floor(s / 4), left = 4 - s % 4;
        t.innerHTML = phases[p][0] + ' <span class="num faint">' + left + '</span>';
        c.style.transform = 'scale(' + phases[p][1] + ')';
      }
      secondly();
      return { secondly: secondly };
    },
  });

  // ---------- quote ----------
  var QUOTES = [
    ['Talk is cheap. Show me the code.', 'Linus Torvalds'],
    ['Premature optimization is the root of all evil.', 'Donald Knuth'],
    ['Simplicity is prerequisite for reliability.', 'Edsger Dijkstra'],
    ['First, solve the problem. Then, write the code.', 'John Johnson'],
    ['Make it work, make it right, make it fast.', 'Kent Beck'],
    ['Programs must be written for people to read.', 'Harold Abelson'],
    ['Deleted code is debugged code.', 'Jeff Sickel'],
    ['The best error message is the one that never shows up.', 'Thomas Fuchs'],
    ['Code never lies, comments sometimes do.', 'Ron Jeffries'],
    ['Simple things should be simple, complex things should be possible.', 'Alan Kay'],
    ['Any fool can write code that a computer can understand.', 'Martin Fowler'],
    ['Stay hungry. Stay foolish.', 'Stewart Brand'],
    ['There are only two hard things: cache invalidation and naming things.', 'Phil Karlton'],
    ['It works on my machine.', 'todo dev, um dia'],
  ];
  KB.def('quote', {
    mount: function (el, w) {
      var custom = KB.list(KB.opt(w, 'custom', []));
      var list = custom.length ? custom.map(function (q) { var p = q.split(' — '); return [p[0], p[1] || '']; }) : QUOTES;
      var day = Math.floor((Date.now() - new Date().getTimezoneOffset() * 60000) / 864e5);
      var q = list[day % list.length];
      el.innerHTML = '<div class="qu"><div class="qu-t">' + esc(q[0]) + '</div>' + (q[1] ? '<div class="qu-a mono">— ' + esc(q[1]) + '</div>' : '') + '</div>';
      return {};
    },
  });

  // ---------- title ----------
  KB.def('label', {
    mount: function (el, w) {
      el.classList.add('bare');
      el.innerHTML = '<div class="lb"><span class="ar">' + KBIcons.html('chevron-right') + '</span>' + esc(KB.opt(w, 'text', '')) + '<i class="cur"></i></div>';
      return {};
    },
  });

  // ---------- phone battery ----------
  KB.def('battery', {
    mount: function (el) {
      el.innerHTML = KB.header('phone battery') + '<div class="w-b bt"><div class="bt-cell"><i></i></div><div><div class="bt-v num">–</div><div class="bt-s mono dim"></div></div></div>';
      var fill = el.querySelector('.bt-cell i'), v = el.querySelector('.bt-v'), s = el.querySelector('.bt-s'), browser = null;
      if (!KB.android && navigator.getBattery) navigator.getBattery().then(function (b) { browser = b; read(); }).catch(function () { });
      function read() {
        var b = null;
        if (KB.android && KB.android.battery) { try { b = JSON.parse(KB.android.battery(KB.key)); } catch (e) { b = null; } }
        else if (browser) b = { level: Math.round(browser.level * 100), charging: browser.charging };
        if (!b || b.level == null) { v.textContent = '–'; s.textContent = 'só no app'; return; }
        var c = b.level <= 15 ? 'var(--red)' : b.level <= 30 ? 'var(--amber)' : 'var(--green)';
        fill.style.width = b.level + '%'; fill.style.background = c;
        v.textContent = b.level + '%';
        s.innerHTML = (b.charging ? KBIcons.html('bolt') + ' carregando' : 'na bateria') + (b.temp ? ' · ' + b.temp.toFixed(1) + '°' : '');
      }
      read();
      var t = setInterval(read, 30000);
      return { destroy: function () { clearInterval(t); } };
    },
  });
})();
