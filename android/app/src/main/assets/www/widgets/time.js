/* Time widgets: clock, analog, world clocks, word clock, calendar, progress, countdown, pomodoro, timer, stopwatch. */
(function () {
  'use strict';
  var fmt = KB.fmt, esc = KB.esc;
  var DAYS = ['dom', 'seg', 'ter', 'qua', 'qui', 'sex', 'sáb'];

  function showSeconds() { return !KB.cfg || !KB.cfg.display || KB.cfg.display.showSeconds !== false; }

  function zoned(tz) {
    if (!tz) return new Date();
    try {
      var s = new Date().toLocaleString('en-US', { timeZone: tz });
      return new Date(s);
    } catch (e) { return new Date(); }
  }

  // ---------- clock (iPad lock screen: date on top, big bold time) ----------
  KB.def('clock', {
    mount: function (el, w) {
      var status = KB.opt(w, 'status', true), weather = KB.opt(w, 'weather', true), date = KB.opt(w, 'date', true);
      el.innerHTML =
        '<div class="lk">' +
        (date || weather ? '<div class="lk-d">' + (date ? '<span class="lk-date"></span>' : '') + (weather ? '<span class="lk-w"></span>' : '') + '</div>' : '') +
        '<div class="lk-t num"><span class="hm"></span><span class="ss"></span></div>' +
        (status ? '<div class="lk-s mono"></div>' : '') +
        '</div>';
      var hm = el.querySelector('.hm'), ss = el.querySelector('.ss'), d = el.querySelector('.lk-date'), wx = el.querySelector('.lk-w'), st = el.querySelector('.lk-s');
      function secondly() {
        var now = new Date();
        hm.textContent = fmt.time(now, false);
        ss.textContent = showSeconds() ? (now.getSeconds() < 10 ? '0' : '') + now.getSeconds() : '';
        // narrow cards get the short date ("qua, 18 de nov"), wide ones the full one
        if (d) d.textContent = now.toLocaleDateString('pt-BR', el.clientWidth < 360 ? { weekday: 'short', day: 'numeric', month: 'short' } : { weekday: 'long', day: 'numeric', month: 'long' }).replace(/\./g, '');
      }
      function loadWx() {
        if (!wx) return;
        KB.weather().then(function (data) {
          var c = data.current;
          wx.innerHTML = KB.wxIcon(c.weather_code, c.is_day) + '<span class="num">' + Math.round(c.temperature_2m) + '°</span>';
        }).catch(function () { wx.innerHTML = ''; });
      }
      secondly(); loadWx();
      var wxTimer = setInterval(loadWx, 600000);
      return {
        secondly: secondly,
        update: function (s) {
          if (!st || !s.sys) return;
          var sys = s.sys, host = (sys.host || 'pc').toLowerCase();
          var bits = ['cpu ' + fmt.pct(sys.cpu), 'ram ' + fmt.pct(sys.ram && sys.ram.pct)];
          if (sys.gpu && sys.gpu.util != null) bits.push('gpu ' + fmt.pct(sys.gpu.util));
          var q = (s.quotas || [])[0];
          if (q && q.windows && q.windows[0]) bits.push(q.provider + ' ' + fmt.pct(q.windows[0].pct));
          st.innerHTML = '<span class="pr">kong@' + esc(host) + '</span> <span class="ar">›</span> ' + esc(bits.join(' · '));
        },
        destroy: function () { clearInterval(wxTimer); },
      };
    },
  });

  // ---------- analog face (iPad Clock widget): numbers, ticks, orange second hand ----------
  /** One clock face as SVG. Hands carry classes h/m/s so setFace() can turn them. */
  KB.clockFace = function (label, light, seconds) {
    var nums = '', ticks = '';
    for (var i = 1; i <= 12; i++) {
      var a = i * Math.PI / 6;
      nums += '<text x="' + (50 + 35.5 * Math.sin(a)).toFixed(2) + '" y="' + (50 - 35.5 * Math.cos(a) + 4.6).toFixed(2) + '">' + i + '</text>';
    }
    for (var t = 0; t < 60; t++) {
      if (t % 5 === 0) continue;
      ticks += '<line x1="50" y1="5" x2="50" y2="7.5" transform="rotate(' + t * 6 + ' 50 50)"/>';
    }
    return '<svg class="face' + (light ? ' light' : '') + '" viewBox="0 0 100 100">' +
      '<circle class="bg" cx="50" cy="50" r="49"/>' +
      '<g class="tk">' + ticks + '</g><g class="nm">' + nums + '</g>' +
      (label ? '<text class="lb" x="50" y="68">' + esc(label) + '</text>' : '') +
      '<line class="h" x1="50" y1="50" x2="50" y2="29" stroke-width="4.2" stroke-linecap="round"/>' +
      '<line class="m" x1="50" y1="50" x2="50" y2="15" stroke-width="3" stroke-linecap="round"/>' +
      '<circle class="hub" cx="50" cy="50" r="3"/>' +
      (seconds ? '<line class="s" x1="50" y1="60" x2="50" y2="10" stroke-width="1.1" stroke-linecap="round"/><circle class="shub" cx="50" cy="50" r="1.6"/>' : '') +
      '</svg>';
  };
  KB.setFace = function (svg, d) {
    var sec = d.getSeconds(), min = d.getMinutes() + sec / 60, hr = d.getHours() % 12 + min / 60;
    var h = svg.querySelector('.h'), m = svg.querySelector('.m'), s = svg.querySelector('.s');
    h.setAttribute('transform', 'rotate(' + (hr * 30).toFixed(2) + ' 50 50)');
    m.setAttribute('transform', 'rotate(' + (min * 6).toFixed(2) + ' 50 50)');
    if (s) s.setAttribute('transform', 'rotate(' + sec * 6 + ' 50 50)');
  };
  function cityCode(name) { return String(name || '').replace(/[^A-Za-zÀ-ÿ]/g, '').slice(0, 3).toUpperCase(); }
  function dayLabel(d, local) {
    var diff = Math.round((d - local) / 3600000);
    var day = d.getDate() !== local.getDate() ? (d > local ? 'Amanhã' : 'Ontem') : 'Hoje';
    return day + ', ' + (diff >= 0 ? '+' : '') + diff + 'H';
  }

  KB.def('analog', {
    mount: function (el, w) {
      var tz = KB.opt(w, 'tz', ''), label = KB.opt(w, 'label', ''), secHand = KB.opt(w, 'seconds', true);
      var light = KB.opt(w, 'face', 'dark') === 'light';
      el.innerHTML = '<div class="an">' + KB.clockFace(cityCode(label), light, secHand) + '</div>';
      var face = el.querySelector('svg');
      function secondly() { KB.setFace(face, zoned(tz)); }
      secondly();
      return { secondly: secondly };
    },
  });

  // ---------- world clocks (iPad medium Clock widget: a row of faces) ----------
  KB.def('worldclock', {
    mount: function (el, w) {
      var zones = KB.list(KB.opt(w, 'zones', [])); // template pages come without options: use the catalog's cities
      if (!zones.length) zones = ['São Paulo=America/Sao_Paulo', 'Lisboa=Europe/Lisbon', 'Nova York=America/New_York', 'Tóquio=Asia/Tokyo'];
      zones = zones.map(function (z) {
        var p = z.split('='); return { name: p[0].trim(), tz: (p[1] || p[0]).trim() };
      }).slice(0, 6);
      var light = KB.opt(w, 'face', 'dark') === 'light';
      el.innerHTML = '<div class="wc">' + zones.map(function (z) {
        return '<div class="wc-i">' + KB.clockFace(cityCode(z.name), light, false) + '<div class="wc-n">' + esc(z.name) + '</div><div class="wc-o"></div></div>';
      }).join('') + '</div>';
      var items = el.querySelectorAll('.wc-i');
      function secondly() {
        var local = new Date();
        zones.forEach(function (z, i) {
          var d = zoned(z.tz);
          KB.setFace(items[i].querySelector('svg'), d);
          items[i].querySelector('.wc-o').textContent = dayLabel(d, local);
        });
      }
      secondly();
      return { secondly: secondly };
    },
  });

  // ---------- word clock (pt-BR) ----------
  var HOURS = ['meia-noite', 'uma', 'duas', 'três', 'quatro', 'cinco', 'seis', 'sete', 'oito', 'nove', 'dez', 'onze', 'meio-dia'];
  var MINS = { 5: 'cinco', 10: 'dez', 15: 'quinze', 20: 'vinte', 25: 'vinte e cinco', 30: 'meia' };
  function hourWord(h) { h = h % 24; if (h === 0) return 'meia-noite'; if (h === 12) return 'meio-dia'; return HOURS[h % 12]; }
  function words(d) {
    var h = d.getHours(), m = Math.round(d.getMinutes() / 5) * 5;
    if (m === 60) { m = 0; h++; }
    var lead = function (hh) { var x = hh % 24; return (x === 0 || x === 12) ? 'é' : (x % 12 === 1 ? 'é' : 'são'); };
    if (m === 0) return [lead(h), hourWord(h), (h % 12 === 0 ? '' : 'em ponto')];
    if (m <= 30) return [lead(h), hourWord(h), 'e ' + MINS[m]];
    var next = h + 1;
    var art = next % 24 === 0 ? 'a ' : next % 24 === 12 ? 'o ' : next % 12 === 1 ? 'a ' : 'as ';
    return [MINS[60 - m], 'para', art + hourWord(next)];
  }
  KB.def('wordclock', {
    mount: function (el) {
      el.innerHTML = '<div class="wd"></div>';
      var box = el.firstChild, last = '';
      function secondly() {
        var ws = words(new Date()).filter(Boolean), key = ws.join(' ');
        if (key === last) return;
        last = key;
        box.innerHTML = ws.map(function (x, i) { return '<span style="--i:' + i + '">' + esc(x) + '</span>'; }).join(' ');
      }
      secondly();
      return { secondly: secondly };
    },
  });

  // ---------- calendar (iPad: day view when small, month when square, both when wide) ----------
  var WEEKDAYS = ['domingo', 'segunda-feira', 'terça-feira', 'quarta-feira', 'quinta-feira', 'sexta-feira', 'sábado'];
  KB.def('calendar', {
    mount: function (el, w) {
      var mon = KB.opt(w, 'mondayFirst', false), lastDay = -1;
      function draw() {
        var now = new Date(), y = now.getFullYear(), m = now.getMonth(), today = now.getDate();
        lastDay = today;
        var first = new Date(y, m, 1).getDay(), days = new Date(y, m + 1, 0).getDate();
        var offset = mon ? (first + 6) % 7 : first;
        var names = mon ? ['S', 'T', 'Q', 'Q', 'S', 'S', 'D'] : ['D', 'S', 'T', 'Q', 'Q', 'S', 'S'];
        var cells = names.map(function (n) { return '<i class="hd">' + n + '</i>'; }).join('');
        for (var i = 0; i < offset; i++) cells += '<i></i>';
        for (var d = 1; d <= days; d++) {
          var dow = new Date(y, m, d).getDay();
          cells += '<i class="' + (d === today ? 'today' : d < today ? 'past' : '') + (dow === 0 || dow === 6 ? ' we' : '') + '">' + d + '</i>';
        }
        var month = now.toLocaleDateString('pt-BR', { month: 'long' });
        el.innerHTML =
          '<div class="cl2">' +
          '<div class="cl2-day"><div class="cl2-wd">' + esc(WEEKDAYS[now.getDay()]) + '</div><div class="cl2-n num">' + today + '</div>' +
          '<div class="cl2-ev">Sem eventos hoje</div></div>' +
          '<div class="cl2-month"><div class="cl2-mh">' + esc(month) + '</div><div class="cl2-g">' + cells + '</div></div>' +
          '</div>';
      }
      draw();
      return { secondly: function () { if (new Date().getDate() !== lastDay) draw(); } };
    },
  });

  // ---------- time progress ----------
  KB.def('progress', {
    mount: function (el) {
      var rows = [['dia', 'var(--accent)'], ['semana', 'var(--purple)'], ['mês', 'var(--green)'], ['ano', 'var(--amber)']];
      el.innerHTML = KB.header('progress') + '<div class="w-b tp">' + rows.map(function (r) {
        return '<div class="tp-r"><span class="mono">' + r[0] + '</span><div class="bar" style="--c:' + r[1] + '"><i></i></div><b class="num"></b></div>';
      }).join('') + '</div>';
      var bars = el.querySelectorAll('.tp-r');
      function secondly() {
        var n = new Date(), y = n.getFullYear();
        var day = (n - new Date(y, n.getMonth(), n.getDate())) / 864e5;
        var dow = (n.getDay() + 6) % 7;
        var week = (dow + day) / 7;
        var month = (n.getDate() - 1 + day) / new Date(y, n.getMonth() + 1, 0).getDate();
        var year = (n - new Date(y, 0, 1)) / (new Date(y + 1, 0, 1) - new Date(y, 0, 1));
        [day, week, month, year].forEach(function (v, i) {
          bars[i].querySelector('i').style.width = (v * 100).toFixed(2) + '%';
          bars[i].querySelector('b').textContent = (v * 100).toFixed(v < .1 ? 1 : 0) + '%';
        });
      }
      secondly();
      return { secondly: secondly };
    },
  });

  // ---------- countdown ----------
  KB.def('countdown', {
    mount: function (el, w) {
      var title = KB.opt(w, 'title', ''), raw = String(KB.opt(w, 'date', '')).replace(' ', 'T');
      var target = new Date(raw);
      el.innerHTML = KB.header(title || 'countdown') + '<div class="w-b cd"><div class="cd-v num"></div><div class="cd-s mono dim"></div></div>';
      var v = el.querySelector('.cd-v'), s = el.querySelector('.cd-s');
      function secondly() {
        if (isNaN(target)) { v.textContent = '––'; s.textContent = 'data inválida'; return; }
        var left = (target - Date.now()) / 1000, past = left < 0;
        left = Math.abs(left);
        var d = Math.floor(left / 86400), h = Math.floor(left % 86400 / 3600), m = Math.floor(left % 3600 / 60), sec = Math.floor(left % 60);
        v.innerHTML = d ? d + '<small>d</small> ' + h + '<small>h</small>' : h + '<small>h</small> ' + m + '<small>m</small> ' + sec + '<small>s</small>';
        s.textContent = (past ? 'passou ' : '') + target.toLocaleDateString('pt-BR', { day: 'numeric', month: 'short', year: 'numeric' });
      }
      secondly();
      return { secondly: secondly };
    },
  });

  // ---------- pomodoro ----------
  KB.def('pomodoro', {
    mount: function (el, w) {
      var key = 'pomo:' + w.id;
      var len = { focus: KB.opt(w, 'focus', 25), short: KB.opt(w, 'short', 5), long: KB.opt(w, 'long', 15) };
      var names = { focus: 'foco', short: 'pausa', long: 'pausa longa' };
      var colors = { focus: 'var(--red)', short: 'var(--green)', long: 'var(--cyan)' };
      var st = KB.store.get(key, { mode: 'focus', endsAt: 0, left: len.focus * 60, round: 1 });
      el.innerHTML = '<div class="pm"><div class="pm-ring">' + KB.ring(0, colors[st.mode], 6) + '<div class="pm-c"><div class="pm-t num"></div><div class="pm-m mono"></div></div></div>' +
        '<div class="pm-b"><button class="btn press" data-a="reset">' + KBIcons.html('refresh') + '</button><button class="btn primary press" data-a="go"></button><button class="btn press" data-a="skip">' + KBIcons.html('next') + '</button></div></div>';
      var ring = el.querySelector('svg'), t = el.querySelector('.pm-t'), md = el.querySelector('.pm-m'), go = el.querySelector('[data-a=go]');
      function left() { return st.endsAt ? Math.max(0, (st.endsAt - Date.now()) / 1000) : st.left; }
      function next() {
        if (st.mode === 'focus') { st.mode = st.round % 4 === 0 ? 'long' : 'short'; }
        else { st.mode = 'focus'; st.round++; }
        st.left = len[st.mode] * 60; st.endsAt = 0;
      }
      function draw() {
        var l = left(), total = len[st.mode] * 60;
        if (st.endsAt && l <= 0) {
          KB.buzz('0,200,120,200,120,200');
          KB.toast({ title: st.mode === 'focus' ? 'Foco concluído' : 'Pausa acabou', text: st.mode === 'focus' ? 'Levanta, respira, bebe água.' : 'De volta ao código.', level: 'ok' });
          next(); save(); l = left(); total = len[st.mode] * 60;
        }
        t.textContent = fmt.clock(l);
        md.textContent = names[st.mode] + ' · #' + st.round;
        go.textContent = st.endsAt ? 'Pausar' : 'Iniciar';
        KB.setRing(ring, (1 - l / total) * 100, colors[st.mode]);
      }
      function save() { KB.store.set(key, st); }
      el.querySelector('.pm-b').addEventListener('click', function (e) {
        var b = e.target.closest('button'); if (!b) return;
        KB.haptic();
        var a = b.dataset.a;
        if (a === 'go') { if (st.endsAt) { st.left = left(); st.endsAt = 0; } else st.endsAt = Date.now() + st.left * 1000; }
        if (a === 'reset') { st.endsAt = 0; st.left = len[st.mode] * 60; }
        if (a === 'skip') next();
        save(); draw();
      });
      draw();
      return { secondly: draw };
    },
  });

  // ---------- quick timer ----------
  KB.def('timer', {
    mount: function (el, w) {
      var key = 'timer:' + w.id;
      var presets = KB.list(KB.opt(w, 'presets', '1,5,10,25')).map(Number).filter(function (n) { return n > 0; }).slice(0, 6);
      var st = KB.store.get(key, { endsAt: 0, total: 0 });
      el.innerHTML = KB.header('timer') + '<div class="w-b tm"><div class="tm-t num"></div><div class="bar"><i></i></div><div class="tm-p">' +
        presets.map(function (p) { return '<button class="chip press" data-m="' + p + '">' + p + ' min</button>'; }).join('') +
        '<button class="chip press" data-m="0">' + KBIcons.html('x') + '</button></div></div>';
      var t = el.querySelector('.tm-t'), bar = el.querySelector('.bar i');
      function draw() {
        var l = st.endsAt ? (st.endsAt - Date.now()) / 1000 : 0;
        if (st.endsAt && l <= 0) {
          st.endsAt = 0; KB.store.set(key, st);
          KB.buzz('0,300,150,300,150,300');
          KB.toast({ title: 'Timer', text: 'Acabou o tempo.', level: 'warn', icon: 'timer' });
        }
        t.textContent = st.endsAt ? fmt.clock(l) : '0:00';
        bar.style.width = st.endsAt && st.total ? (100 - l / st.total * 100) + '%' : '0%';
      }
      el.querySelector('.tm-p').addEventListener('click', function (e) {
        var b = e.target.closest('[data-m]'); if (!b) return;
        KB.haptic();
        var m = +b.dataset.m;
        st = m ? { endsAt: Date.now() + m * 60000, total: m * 60 } : { endsAt: 0, total: 0 };
        KB.store.set(key, st); draw();
      });
      draw();
      return { secondly: draw };
    },
  });

  // ---------- stopwatch ----------
  KB.def('stopwatch', {
    mount: function (el, w) {
      var key = 'sw:' + w.id;
      var st = KB.store.get(key, { start: 0, acc: 0, laps: [] });
      el.innerHTML = KB.header('stopwatch') + '<div class="w-b sw"><div class="sw-t num"></div><div class="sw-l mono dim"></div>' +
        '<div class="sw-b"><button class="btn press" data-a="lap">Volta</button><button class="btn primary press" data-a="go"></button></div></div>';
      var t = el.querySelector('.sw-t'), laps = el.querySelector('.sw-l'), go = el.querySelector('[data-a=go]'), raf = 0;
      function elapsed() { return st.acc + (st.start ? Date.now() - st.start : 0); }
      function show(ms) { var s = ms / 1000; return fmt.clock(s) + '.' + Math.floor(ms % 1000 / 100); }
      function frame() { t.textContent = show(elapsed()); if (st.start) raf = requestAnimationFrame(frame); }
      function draw() {
        go.textContent = st.start ? 'Parar' : (st.acc ? 'Continuar' : 'Iniciar');
        el.querySelector('[data-a=lap]').textContent = st.start ? 'Volta' : 'Zerar';
        laps.innerHTML = st.laps.slice(-3).reverse().map(function (l, i) { return '#' + (st.laps.length - i) + ' ' + show(l); }).join('<br>');
        cancelAnimationFrame(raf); frame();
      }
      el.querySelector('.sw-b').addEventListener('click', function (e) {
        var b = e.target.closest('button'); if (!b) return;
        KB.haptic();
        if (b.dataset.a === 'go') { if (st.start) { st.acc = elapsed(); st.start = 0; } else st.start = Date.now(); }
        else if (st.start) st.laps.push(elapsed());
        else st = { start: 0, acc: 0, laps: [] };
        KB.store.set(key, st); draw();
      });
      draw();
      return { destroy: function () { cancelAnimationFrame(raf); } };
    },
  });
})();
