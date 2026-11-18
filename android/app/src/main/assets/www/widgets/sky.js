/* Weather & sky: weather, forecast, sun & moon, moon, air quality. Data: Open-Meteo via the agent. */
(function () {
  'use strict';
  var esc = KB.esc, fmt = KB.fmt;

  // ---------- icons (24×24 line art) ----------
  var SUN = '<circle cx="12" cy="12" r="4.2" fill="#ffd60a" stroke="none"/><g stroke="#ffd60a">' +
    [0, 45, 90, 135, 180, 225, 270, 315].map(function (a) { return '<line x1="12" y1="2.6" x2="12" y2="4.6" transform="rotate(' + a + ' 12 12)"/>'; }).join('') + '</g>';
  var MOON = '<path d="M15.5 3.5a8.5 8.5 0 1 0 5 13.6A7 7 0 0 1 15.5 3.5z" fill="#e5e5ea" stroke="none"/>';
  var CLOUD = '<path d="M7 18.5h10a4 4 0 0 0 .5-7.97A5.5 5.5 0 0 0 6.9 11 3.75 3.75 0 0 0 7 18.5z" fill="#d1d1d6" stroke="none"/>';
  var CLOUD_SM = '<path d="M9 19h8.5a3.5 3.5 0 0 0 .4-6.98A4.8 4.8 0 0 0 8.9 12.6 3.2 3.2 0 0 0 9 19z" fill="#d1d1d6" stroke="none"/>';
  function drops(color, n) {
    var s = '<g stroke="' + color + '">';
    for (var i = 0; i < n; i++) s += '<line x1="' + (8 + i * 4) + '" y1="20" x2="' + (7 + i * 4) + '" y2="22.5"/>';
    return s + '</g>';
  }
  function svg(inner) { return '<svg class="wx" viewBox="0 0 24 24" fill="none" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">' + inner + '</svg>'; }
  function cloudUp(c) { return '<g transform="translate(0,-3)">' + c + '</g>'; }

  KB.wxIcon = function (code, isDay) {
    var day = isDay !== 0;
    var orb = day ? '<g transform="translate(-3,-3) scale(.8)">' + SUN + '</g>' : '<g transform="translate(-2,-3) scale(.75)">' + MOON + '</g>';
    if (code === 0) return svg(day ? SUN : MOON);
    if (code === 1 || code === 2) return svg(orb + CLOUD_SM);
    if (code === 3) return svg(CLOUD);
    if (code === 45 || code === 48) return svg('<g stroke="#aeaeb2"><line x1="4" y1="9" x2="20" y2="9"/><line x1="3" y1="13" x2="21" y2="13"/><line x1="5" y1="17" x2="19" y2="17"/></g>');
    if (code >= 51 && code <= 57) return svg(cloudUp(CLOUD) + drops('#64d2ff', 2));
    if ((code >= 61 && code <= 67) || (code >= 80 && code <= 82)) return svg(cloudUp(CLOUD) + drops('#0a84ff', 3));
    if ((code >= 71 && code <= 77) || code === 85 || code === 86) return svg(cloudUp(CLOUD) + '<g fill="#fff" stroke="none"><circle cx="8" cy="21" r="1"/><circle cx="12" cy="22" r="1"/><circle cx="16" cy="21" r="1"/></g>');
    if (code >= 95) return svg(cloudUp(CLOUD) + '<path d="M12.5 15.5 10 20h3l-1.5 3.5" stroke="#ffd60a"/>');
    return svg(CLOUD);
  };

  var TEXT = { 0: 'Céu limpo', 1: 'Quase limpo', 2: 'Parcialmente nublado', 3: 'Nublado', 45: 'Neblina', 48: 'Neblina', 51: 'Garoa fraca', 53: 'Garoa', 55: 'Garoa forte', 56: 'Garoa gelada', 57: 'Garoa gelada', 61: 'Chuva fraca', 63: 'Chuva', 65: 'Chuva forte', 66: 'Chuva gelada', 67: 'Chuva gelada', 71: 'Neve fraca', 73: 'Neve', 75: 'Neve forte', 77: 'Grãos de neve', 80: 'Pancadas', 81: 'Pancadas', 82: 'Temporal', 85: 'Neve', 86: 'Neve forte', 95: 'Trovoada', 96: 'Trovoada com granizo', 99: 'Trovoada com granizo' };
  KB.wxText = function (code) { return TEXT[code] || '—'; };

  function city() { return (KB.cfg && KB.cfg.weather && KB.cfg.weather.city) || ''; }
  // ---------- iPad weather look: the card's sky follows the condition and the time of day ----------
  /** Opaque gradients (cheap to composite), picked like the iPad Weather widget does. */
  KB.wxSky = function (code, isDay) {
    var night = isDay === 0;
    if (code >= 95) return night ? ['#1c2130', '#3a4152'] : ['#3f4756', '#6b7384'];
    if ((code >= 61 && code <= 67) || (code >= 80 && code <= 82) || (code >= 51 && code <= 57)) return night ? ['#16202f', '#33445a'] : ['#4d6178', '#7a8ea4'];
    if ((code >= 71 && code <= 77) || code === 85 || code === 86) return night ? ['#2a3446', '#4c5b72'] : ['#8ea2b8', '#c3d1df'];
    if (code === 3 || code === 45 || code === 48) return night ? ['#1d2532', '#3a4658'] : ['#61788f', '#93a8bd'];
    if (code === 2) return night ? ['#0f1d3d', '#2f4473'] : ['#3a7bc8', '#86b4e3'];
    return night ? ['#081633', '#22397a'] : ['#2f7fd8', '#78b6ef'];
  };
  function paintSky(el, code, isDay) {
    var s = KB.wxSky(code, isDay);
    el.style.background = 'linear-gradient(180deg,' + s[0] + ',' + s[1] + ')';
    el.classList.add('sky');
  }
  function hourly(d, count) {
    var h = d.hourly, now = Date.now(), start = 0, out = [];
    for (var j = 0; j < h.time.length; j++) if (new Date(h.time[j]).getTime() > now - 3600000) { start = j; break; }
    for (var k = start; k < Math.min(h.time.length, start + count); k++)
      out.push({ k: k === start ? 'Agora' : new Date(h.time[k]).getHours() + 'h', code: h.weather_code[k], day: h.is_day[k], t: Math.round(h.temperature_2m[k]) + '°', p: h.precipitation_probability[k] });
    return out;
  }
  function daily(d) {
    var dd = d.daily, out = [];
    for (var i = 0; i < dd.time.length; i++) {
      var day = new Date(dd.time[i] + 'T12:00');
      out.push({ k: i === 0 ? 'Hoje' : day.toLocaleDateString('pt-BR', { weekday: 'short' }).replace('.', ''), code: dd.weather_code[i], max: Math.round(dd.temperature_2m_max[i]), min: Math.round(dd.temperature_2m_min[i]), p: dd.precipitation_probability_max[i] });
    }
    return out;
  }
  function hourRow(items) {
    return '<div class="wx-h">' + items.map(function (x) {
      return '<div><span>' + esc(x.k) + '</span>' + KB.wxIcon(x.code, x.day) + '<b class="num">' + x.t + '</b></div>';
    }).join('') + '</div>';
  }
  function dayList(items) {
    var lo = Math.min.apply(null, items.map(function (x) { return x.min; })), hi = Math.max.apply(null, items.map(function (x) { return x.max; }));
    var span = Math.max(1, hi - lo);
    return '<div class="wx-dl">' + items.map(function (x) {
      return '<div><span class="wdn">' + esc(x.k) + '</span>' + KB.wxIcon(x.code, 1) + '<span class="pp num">' + (x.p >= 30 ? x.p + '%' : '') + '</span>' +
        '<span class="num lo">' + x.min + '°</span><span class="rg"><i style="left:' + ((x.min - lo) / span * 100).toFixed(1) + '%;right:' + ((hi - x.max) / span * 100).toFixed(1) + '%"></i></span><span class="num">' + x.max + '°</span></div>';
    }).join('') + '</div>';
  }
  function fail(el) { el.innerHTML = '<div class="empty">sem dados do clima</div>'; el.classList.remove('sky'); el.style.background = ''; }

  // ---------- weather: small = now; wide adds the next hours; tall adds the week ----------
  KB.def('weather', {
    mount: function (el) {
      function load() {
        KB.weather().then(function (d) {
          var c = d.current, t = d.daily;
          paintSky(el, c.weather_code, c.is_day);
          el.innerHTML =
            '<div class="wxc">' +
            '<div class="wx-top"><div><div class="wx-city">' + esc(city() || 'Clima') + ' ' + KBIcons.html('arrow') + '</div><div class="wx-t num">' + Math.round(c.temperature_2m) + '°</div></div>' +
            '<div class="wx-now">' + KB.wxIcon(c.weather_code, c.is_day) + '<div>' + esc(KB.wxText(c.weather_code)) + '</div><div class="num">Máx.: ' + Math.round(t.temperature_2m_max[0]) + '° Mín.: ' + Math.round(t.temperature_2m_min[0]) + '°</div></div></div>' +
            hourRow(hourly(d, 6)) + dayList(daily(d).slice(0, 6)) +
            '</div>';
        }).catch(function () { fail(el); });
      }
      load();
      var timer = setInterval(load, 600000);
      return { destroy: function () { clearInterval(timer); } };
    },
  });

  // ---------- forecast: the same sky, hourly or the week ----------
  KB.def('forecast', {
    mount: function (el, w) {
      var mode = KB.opt(w, 'mode', 'hourly');
      function load() {
        KB.weather().then(function (d) {
          var c = d.current;
          paintSky(el, c.weather_code, c.is_day);
          el.innerHTML = '<div class="fx"><div class="fx-h"><span>' + esc(city() || 'Previsão') + '</span><span class="num">' + Math.round(c.temperature_2m) + '° ' + esc(KB.wxText(c.weather_code)) + '</span></div>' +
            (mode === 'daily' ? dayList(daily(d)) : hourRow(hourly(d, 8))) + '</div>';
        }).catch(function () { fail(el); });
      }
      load();
      var timer = setInterval(load, 600000);
      return { destroy: function () { clearInterval(timer); } };
    },
  });

  function failBody(el) { var b = el.querySelector('.w-b'); if (b) b.innerHTML = '<div class="empty">sem dados do clima</div>'; }

  // ---------- moon ----------
  var PHASES = ['Lua nova', 'Crescente', 'Quarto crescente', 'Crescente gibosa', 'Lua cheia', 'Minguante gibosa', 'Quarto minguante', 'Minguante'];
  KB.moon = function (date) {
    var syn = 29.530588853, ref = Date.UTC(2000, 0, 6, 18, 14);
    var age = (((date.getTime() - ref) / 864e5) % syn + syn) % syn;
    var frac = age / syn;
    var illum = (1 - Math.cos(frac * 2 * Math.PI)) / 2;
    return { age: age, frac: frac, illum: illum, name: PHASES[Math.floor((frac * 8 + .5)) % 8], nextFull: (14.765 - age + syn) % syn };
  };
  /** A lit disc drawn with two arcs: the terminator is an ellipse whose width follows the phase. */
  KB.moonSvg = function (frac) {
    var r = 45, waxing = frac < .5, k = Math.cos(frac * 2 * Math.PI); // 1 new … -1 full
    var rx = Math.abs(k) * r;
    var lit = waxing ? 1 : 0;
    var d = 'M50 5 A45 45 0 0 ' + lit + ' 50 95 A' + rx.toFixed(2) + ' 45 0 0 ' + (k > 0 ? (waxing ? 0 : 1) : (waxing ? 1 : 0)) + ' 50 5Z';
    return '<svg class="moon" viewBox="0 0 100 100"><circle cx="50" cy="50" r="45" fill="#1c1c1e"/><path d="' + d + '" fill="#e5e5ea"/></svg>';
  };
  KB.def('moon', {
    mount: function (el) {
      el.innerHTML = KB.header('moon') + '<div class="w-b mn"></div>';
      var b = el.querySelector('.w-b');
      function draw() {
        var m = KB.moon(new Date());
        b.innerHTML = KB.moonSvg(m.frac) + '<div class="mn-t"><b>' + m.name + '</b><span class="mono dim">' + Math.round(m.illum * 100) + '% · cheia em ' + Math.round(m.nextFull) + ' d</span></div>';
      }
      draw();
      var t = setInterval(draw, 3600000);
      return { destroy: function () { clearInterval(t); } };
    },
  });

  // ---------- sun & moon ----------
  KB.def('sun', {
    mount: function (el) {
      el.innerHTML = KB.header('sun & moon') + '<div class="w-b sn"></div>';
      var b = el.querySelector('.w-b'), data = null;
      function draw() {
        if (!data) return;
        var d = data.daily, rise = new Date(d.sunrise[0]), set = new Date(d.sunset[0]), now = new Date();
        var p = KB.clamp((now - rise) / (set - rise), 0, 1), up = now > rise && now < set;
        var x = 10 + p * 80, y = 46 - Math.sin(p * Math.PI) * 36;
        var m = KB.moon(now);
        b.innerHTML =
          '<svg class="sn-arc" viewBox="0 0 100 50"><path d="M10 46 Q50 -26 90 46" fill="none" stroke="rgba(255,255,255,.15)" stroke-dasharray="2 2"/>' +
          '<line x1="4" y1="46" x2="96" y2="46" stroke="rgba(255,255,255,.12)"/>' +
          (up ? '<circle cx="' + x.toFixed(1) + '" cy="' + y.toFixed(1) + '" r="4.5" fill="#ffd60a"/>' : '') + '</svg>' +
          '<div class="sn-r mono"><span>↑ ' + KB.fmt.time(rise) + '</span><span>' + (up ? fmt.dur((set - now) / 1000) + ' de luz' : 'noite') + '</span><span>↓ ' + KB.fmt.time(set) + '</span></div>' +
          '<div class="sn-r mono dim"><span>UV ' + Math.round(d.uv_index_max[0]) + '</span><span>' + m.name.toLowerCase() + ' · ' + Math.round(m.illum * 100) + '%</span></div>';
      }
      function load() { KB.weather().then(function (d) { data = d; draw(); }).catch(function () { failBody(el); }); }
      load();
      var t1 = setInterval(load, 600000), t2 = setInterval(draw, 60000);
      return { destroy: function () { clearInterval(t1); clearInterval(t2); } };
    },
  });

  // ---------- air quality ----------
  function aqiLevel(v) {
    if (v <= 20) return ['Bom', 'var(--green)'];
    if (v <= 40) return ['Razoável', '#a3e635'];
    if (v <= 60) return ['Moderado', 'var(--amber)'];
    if (v <= 80) return ['Ruim', '#ff6b3d'];
    if (v <= 100) return ['Muito ruim', 'var(--red)'];
    return ['Péssimo', 'var(--purple)'];
  }
  KB.def('air', {
    mount: function (el) {
      el.innerHTML = KB.header('air quality') + '<div class="w-b aq"></div>';
      var b = el.querySelector('.w-b');
      function load() {
        var w = (KB.cfg && KB.cfg.weather) || {};
        KB.feed('https://air-quality-api.open-meteo.com/v1/air-quality?latitude=' + (w.lat || 0) + '&longitude=' + (w.lon || 0) +
          '&current=european_aqi,pm2_5,pm10,ozone,nitrogen_dioxide&timezone=auto', 1200000).then(function (d) {
          var c = d.current, lv = aqiLevel(c.european_aqi);
          b.innerHTML = '<div class="aq-top"><div class="aq-v num" style="color:' + lv[1] + '">' + Math.round(c.european_aqi) + '</div><div><b>' + lv[0] + '</b><span class="mono faint">AQI europeu</span></div></div>' +
            '<div class="aq-g mono">' + [['PM2.5', c.pm2_5], ['PM10', c.pm10], ['O₃', c.ozone], ['NO₂', c.nitrogen_dioxide]].map(function (x) {
              return '<span><i class="faint">' + x[0] + '</i> ' + Math.round(x[1]) + '</span>';
            }).join('') + '</div>';
        }).catch(function () { failBody(el); });
      }
      load();
      var t = setInterval(load, 1200000);
      return { destroy: function () { clearInterval(t); } };
    },
  });
})();
