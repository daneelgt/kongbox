/* Smart Stack: up to 12 widgets in one slot. Swipe vertically or tap the dots; optional auto-rotation. */
(function () {
  'use strict';
  KB.def('stack', {
    mount: function (el, w, ctx) {
      var cards = ((w.options && w.options.cards) || []).filter(function (c) { return c && c.type && c.type !== 'stack' && c.enabled !== false; }).slice(0, 12);
      var rotate = +KB.opt(w, 'rotate', 0) || 0;
      el.classList.add('bare', 'sk');
      if (!cards.length) { el.innerHTML = '<div class="w"><div class="empty">Smart Stack vazio</div></div>'; return {}; }
      el.innerHTML = '<div class="sk-t"></div><div class="sk-d"></div>';
      var track = el.querySelector('.sk-t'), dots = el.querySelector('.sk-d'), insts = [], idx = 0, lastMove = Date.now();
      cards.forEach(function (c, i) {
        var host = KB.h('div', 'sk-c');
        track.appendChild(host);
        insts.push(KB.mount(host, { id: w.id + '-' + i, type: c.type, options: c.options || {} }, ctx));
        dots.appendChild(KB.h('i'));
      });
      function go(i) {
        idx = (i + cards.length) % cards.length;
        track.style.transform = 'translate3d(0,' + (-idx * 100) + '%,0)';
        Array.prototype.forEach.call(dots.children, function (d, k) { d.classList.toggle('on', k === idx); });
        var cur = insts[idx];
        if (cur) cur.update(KB.full && KB.full.t ? mergeState() : KB.state, true);
      }
      function mergeState() { var m = {}, k; for (k in KB.full) m[k] = KB.full[k]; for (k in KB.state) m[k] = KB.state[k]; return m; }
      dots.addEventListener('click', function (e) { e.stopPropagation(); lastMove = Date.now(); go(idx + 1); KB.haptic(); });
      // vertical swipe inside the stack; horizontal swipes still change pages
      var y0 = null, x0 = 0;
      el.addEventListener('pointerdown', function (e) { y0 = e.clientY; x0 = e.clientX; lastMove = Date.now(); });
      el.addEventListener('pointerup', function (e) {
        if (y0 == null) return;
        var dy = e.clientY - y0, dx = e.clientX - x0; y0 = null;
        if (Math.abs(dy) > 30 && Math.abs(dy) > Math.abs(dx) * 1.3) { lastMove = Date.now(); go(idx + (dy < 0 ? 1 : -1)); KB.haptic(); KB.swallowClick = true; setTimeout(function () { KB.swallowClick = false; }, 60); }
      });
      go(0);
      return {
        update: function (s, full) { var cur = insts[idx]; if (cur) cur.update(s, full); },
        secondly: function () {
          var cur = insts[idx];
          if (cur && cur.secondly) cur.secondly();
          // touching the stack (lastMove) pauses rotation for one interval
          if (rotate > 0 && Date.now() - lastMove > rotate * 1000 && !KB.sheetOpen()) { lastMove = Date.now(); go(idx + 1); }
        },
        destroy: function () { insts.forEach(function (i) { if (i) i.destroy(); }); },
      };
    },
  });
})();
