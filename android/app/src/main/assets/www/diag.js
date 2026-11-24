/* Loaded first: tells the PC which engine this screen runs and any script error, so layout problems on a
   tablet or an old phone show up in the agent log (the user never has to open dev tools). ES3 on purpose. */
(function () {
  var sent = 0;
  function send(kind, data) {
    if (sent++ > 20) return;
    try {
      var x = new XMLHttpRequest();
      x.open('POST', '/api/clientlog', true);
      x.setRequestHeader('Content-Type', 'application/json');
      x.setRequestHeader('X-Kong', '1');
      data.kind = kind;
      data.ua = navigator.userAgent;
      data.screen = (window.innerWidth || 0) + 'x' + (window.innerHeight || 0) + '@' + (window.devicePixelRatio || 1);
      x.send(JSON.stringify(data));
    } catch (e) { }
  }
  var cq = false;
  try { cq = !!(window.CSS && CSS.supports && CSS.supports('container-type', 'size')); } catch (e) { }
  if (/[?&]nocq=1/.test(location.search)) cq = false; // test the fallback on a modern browser
  if (!cq) document.documentElement.className += ' no-cq';
  window.onerror = function (msg, src, line, col) {
    if (/ResizeObserver loop/.test(String(msg))) return; // benign: a resize callback changed a size, the browser retries next frame
    send('error', { msg: String(msg), src: String(src || '').split('/').pop(), line: line || 0, col: col || 0 });
  };
  send('hello', { cq: cq, resizeObserver: !!window.ResizeObserver, pointer: !!window.PointerEvent });
})();
