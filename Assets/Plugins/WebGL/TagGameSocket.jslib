// Minimal browser WebSocket bridge. C# polls it every frame (no callbacks into C#).
mergeInto(LibraryManager.library, {
  TG_Connect: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    if (window.__tg && window.__tg.ws) { try { window.__tg.ws.close(); } catch (e) {} }
    var w = window.__tg = { ws: null, queue: [], state: 0 };
    try {
      var ws = new WebSocket(url);
      w.ws = ws;
      ws.onopen = function () { if (window.__tg === w) w.state = 1; };
      ws.onmessage = function (e) { if (window.__tg === w) w.queue.push(String(e.data)); };
      ws.onclose = function () { if (window.__tg === w) w.state = 3; };
      ws.onerror = function () { if (window.__tg === w) w.state = 3; };
    } catch (e) { w.state = 3; }
  },
  TG_State: function () {
    return window.__tg ? window.__tg.state : 3;
  },
  TG_Send: function (msgPtr) {
    var w = window.__tg;
    if (w && w.state === 1) { try { w.ws.send(UTF8ToString(msgPtr)); } catch (e) {} }
  },
  TG_Recv: function () {
    var w = window.__tg;
    if (!w || w.queue.length === 0) return 0;
    var s = w.queue.shift();
    var len = lengthBytesUTF8(s) + 1;
    var buf = _malloc(len);
    stringToUTF8(s, buf, len);
    return buf;
  },
  TG_Close: function () {
    var w = window.__tg;
    if (w && w.ws) { try { w.ws.close(); } catch (e) {} }
    window.__tg = null;
  }
});
