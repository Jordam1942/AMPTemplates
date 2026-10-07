/*
 * "QR-ring": play without internet or a server.
 *
 * Phones on the same wifi (or the same hotspot, with or without mobile data)
 * connect directly with WebRTC. There is no signalling server: the two phones
 * swap their connection details by scanning each other's QR code.
 *
 * Every phone keeps a copy of the table's data and floods each change to the
 * phones it is linked to, so the same database API as net.js works on top of
 * it (see ToepenNet.local). The host stays in charge of
 * the game. Phones can be linked as a ring (A-B-C-D-A): when one link drops,
 * messages still arrive the other way round.
 */
(function (root) {
  'use strict';

  var STORE_DEFAULT = 'toepen-ring-data', UIDKEY = 'toepen-ring-uid';
  function clean(v) { return v === undefined ? null : JSON.parse(JSON.stringify(v)); }
  function rnd() { return Math.random().toString(36).slice(2, 10); }
  function parts(p) { return p.split('/').filter(Boolean); }

  /* ---------- Connection details <-> short code ----------
   * Only what a data channel needs is sent (ICE login, certificate print and
   * the phone's addresses), so the QR code stays small and easy to scan. */
  function b64u(bytes) {
    var s = ''; for (var i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
    return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }
  function unb64u(t) {
    t = t.replace(/-/g, '+').replace(/_/g, '/'); while (t.length % 4) t += '=';
    var s = atob(t), out = []; for (var i = 0; i < s.length; i++) out.push(s.charCodeAt(i)); return out;
  }
  function packSdp(sdp, kind) {
    var g = function (re) { var m = sdp.match(re); return m ? m[1].trim() : ''; };
    var ufrag = g(/a=ice-ufrag:(.+)/), pwd = g(/a=ice-pwd:(.+)/), fp = g(/a=fingerprint:sha-256 (.+)/);
    var cands = [];
    sdp.split(/\r?\n/).forEach(function (l) {
      var m = l.match(/^a=candidate:\S+ \d+ udp \d+ (\S+) (\d+) typ host/i);
      if (m) cands.push(m[1] + '/' + m[2]);
    });
    cands.sort(function (a, b) { return (a.indexOf(':') >= 0) - (b.indexOf(':') >= 0); });  // IPv4 first
    if (!ufrag || !pwd || !fp || !cands.length) throw new Error('Geen netwerk gevonden. Staat wifi of de hotspot aan?');
    var fpBytes = fp.split(':').map(function (h) { return parseInt(h, 16); });
    return 'TP1' + (kind === 'o' ? 'o' : 'a') + '.' + [ufrag, pwd, b64u(fpBytes), cands.slice(0, 3).join(',')].join('.');
  }
  function unpackSdp(code) {
    var m = /^TP1([oa])\.([A-Za-z0-9+\/]+)\.([A-Za-z0-9+\/]+)\.([A-Za-z0-9_-]+)\.([0-9A-Za-z:.,\/_-]+)$/.exec((code || '').trim());
    if (!m) throw new Error('Dit is geen geldige koppelcode.');
    var fp = unb64u(m[4]).map(function (b) { return ('0' + b.toString(16)).slice(-2).toUpperCase(); }).join(':');
    var lines = [
      'v=0', 'o=- 4611731400430051336 2 IN IP4 127.0.0.1', 's=-', 't=0 0', 'a=group:BUNDLE 0', 'a=msid-semantic: WMS',
      'm=application 9 UDP/DTLS/SCTP webrtc-datachannel', 'c=IN IP4 0.0.0.0',
      'a=ice-ufrag:' + m[2], 'a=ice-pwd:' + m[3], 'a=ice-options:trickle', 'a=fingerprint:sha-256 ' + fp,
      'a=setup:' + (m[1] === 'o' ? 'actpass' : 'active'), 'a=mid:0', 'a=sctp-port:5000', 'a=max-message-size:262144'
    ];
    m[5].split(',').forEach(function (c, i) {
      var k = c.lastIndexOf('/');
      lines.push('a=candidate:' + (i + 1) + ' 1 udp ' + (2122260223 - i) + ' ' + c.slice(0, k) + ' ' + c.slice(k + 1) + ' typ host generation 0');
    });
    return { kind: m[1], sdp: lines.join('\r\n') + '\r\n' };
  }

  function ringBackend(opts) {
    opts = opts || {};
    var STORE = opts.storeKey || STORE_DEFAULT;
    var store = opts.session ? root.sessionStorage : root.localStorage;
    var uid = null;
    try { uid = store.getItem(UIDKEY); } catch (e) {}
    if (!uid) { uid = 'r' + rnd() + rnd(); try { store.setItem(UIDKEY, uid); } catch (e) {} }

    var W = {};            // path -> { s, f, v }: every write, with its Lamport stamp
    var tree = {};
    var clock = 0, counter = 0;
    var links = {};        // id -> { pc, dc, open, peer }
    var seen = {}, seenOrder = [];
    var listeners = [], linkListeners = [];
    var pending = null;    // offer waiting for its answer
    var everLinked = false;

    try {
      var saved = JSON.parse(store.getItem(STORE) || 'null');
      if (saved && saved.W) { W = saved.W; clock = saved.clock || 0; Object.keys(W).forEach(function (p) { setTree(p, W[p].v); }); }
    } catch (e) {}
    var saveTimer;
    function save() {
      clearTimeout(saveTimer);
      saveTimer = setTimeout(function () {
        // 'removed' markers only need to outlive late copies of the write; drop old ones so the table does not grow all evening
        var old = Date.now() - 30 * 60 * 1000;
        Object.keys(W).forEach(function (k) { if (W[k].v === null && (W[k].t || 0) < old) delete W[k]; });
        try { store.setItem(STORE, JSON.stringify({ W: W, clock: clock })); } catch (e) {}
      }, 300);
    }

    /* ----- data ----- */
    function getAt(t, p) {
      var cur = t;
      for (var i = 0, ps = parts(p); i < ps.length; i++) { if (cur == null || typeof cur !== 'object') return null; cur = cur[ps[i]]; }
      return cur === undefined ? null : cur;
    }
    function setTree(p, v) {
      var ps = parts(p), cur = tree;
      for (var i = 0; i < ps.length - 1; i++) {
        if (cur[ps[i]] == null || typeof cur[ps[i]] !== 'object') cur[ps[i]] = {};
        cur = cur[ps[i]];
      }
      if (v === null || v === undefined) delete cur[ps[ps.length - 1]]; else cur[ps[ps.length - 1]] = v;
    }
    function newer(s, f, o) { return !o || s > o.s || (s === o.s && f > o.f); }
    // Apply one write unless something newer already covers this path.
    function applyWrite(p, v, s, f) {
      var ps = parts(p), i, anc = '';
      if (!newer(s, f, W[ps.join('/')])) return false;
      for (i = 0; i < ps.length - 1; i++) { anc += (i ? '/' : '') + ps[i]; if (W[anc] && !newer(s, f, W[anc])) return false; }
      var key = ps.join('/');
      Object.keys(W).forEach(function (k) { if (k.indexOf(key + '/') === 0 && !newer(W[k].s, W[k].f, { s: s, f: f })) delete W[k]; });
      W[key] = { s: s, f: f, v: v, t: Date.now() };
      setTree(key, v);
      return true;
    }
    function notify() {
      listeners.slice().forEach(function (l) { l(tree); });
    }
    function remember(id) {
      seen[id] = 1; seenOrder.push(id);
      if (seenOrder.length > 3000) delete seen[seenOrder.shift()];
    }
    function sendTo(link, msg) {
      if (link.open && link.dc.readyState === 'open') { try { link.dc.send(JSON.stringify(msg)); } catch (e) {} }
    }
    // With privateHands (star topology via the host) a player's hand only goes to that player.
    function visible(link, path) {
      if (!opts.privateHands) return true;
      var m = /(?:^|\/)hands\/([^/]+)/.exec(path);
      return !m || link.peer === m[1];
    }
    function flood(msg, exceptId) {
      Object.keys(links).forEach(function (id) {
        if (id === exceptId) return;
        var link = links[id], out = msg;
        if (opts.privateHands && msg.t === 'w') {
          var w = msg.w.filter(function (it) { return visible(link, it[0]); });
          if (!w.length) return;
          out = { t: 'w', id: msg.id, s: msg.s, f: msg.f, w: w };
        }
        sendTo(link, out);
      });
    }
    function write(items) {
      var s = ++clock, id = uid + ':' + (++counter) + rnd();
      remember(id);
      var changed = false;
      items.forEach(function (it) { if (applyWrite(it[0], it[1], s, uid)) changed = true; });
      flood({ t: 'w', id: id, s: s, f: uid, w: items });
      save();
      if (changed) setTimeout(notify, 0);
      return Promise.resolve();
    }

    function onMessage(link, text) {
      link.seen = Date.now();
      var m; try { m = JSON.parse(text); } catch (e) { return; }
      if (m.t === 'ping') return;
      if (m.t === 'hi') { link.peer = m.uid; if (opts.privateHands) syncTo(link); fireLinks(); return; }
      if (m.t === 'sync') {
        var ch = false;
        (m.w || []).sort(function (a, b) { return a[2] - b[2] || (a[3] < b[3] ? -1 : 1); }).forEach(function (e) {
          clock = Math.max(clock, e[2]);
          if (applyWrite(e[0], e[1], e[2], e[3])) ch = true;
        });
        if (ch) { save(); notify(); }
        return;
      }
      if (m.t === 'w') {
        if (seen[m.id]) return;
        remember(m.id);
        clock = Math.max(clock, m.s);
        var changed = false;
        m.w.forEach(function (it) { if (applyWrite(it[0], it[1], m.s, m.f)) changed = true; });
        flood(m, link.id);
        if (changed) { save(); notify(); }
      }
    }
    function syncTo(link) {
      var w = Object.keys(W).filter(function (k) { return visible(link, k); }).map(function (k) { return [k, W[k].v, W[k].s, W[k].f]; });
      sendTo(link, { t: 'sync', w: w });
    }

    /* ----- links ----- */
    function fireLinks() {
      var open = Object.keys(links).filter(function (i) { return links[i].open; }).length;
      var info = Object.keys(links).map(function (i) { return { id: i, open: links[i].open, peer: links[i].peer }; });
      linkListeners.slice().forEach(function (l) { l(open, info); });
    }
    function wait(pc) {
      return new Promise(function (res) {
        if (pc.iceGatheringState === 'complete') return res();
        var done = function () { pc.removeEventListener('icegatheringstatechange', ch); res(); };
        var ch = function () { if (pc.iceGatheringState === 'complete') done(); };
        pc.addEventListener('icegatheringstatechange', ch);
        setTimeout(done, 2500);
      });
    }
    function attach(link, dc) {
      link.dc = dc;
      dc.onopen = function () {
        link.open = true; everLinked = true; link.seen = Date.now();
        sendTo(link, { t: 'hi', uid: uid });
        syncTo(link);
        fireLinks();
      };
      dc.onclose = function () { link.open = false; if (link.auto) delete links[link.id]; fireLinks(); };
      dc.onmessage = function (e) { onMessage(link, e.data); };
    }
    function newLink(noPc) {
      var id = rnd();
      var pc = noPc ? null : new root.RTCPeerConnection({ iceServers: [] });
      var link = links[id] = { id: id, pc: pc, dc: null, open: false, peer: null };
      if (noPc) return link;
      pc.onconnectionstatechange = function () {
        if (pc.connectionState === 'failed' || pc.connectionState === 'closed') { link.open = false; fireLinks(); }
      };
      return link;
    }

    // Heartbeat: a phone that locks or loses signal does not close its connection politely, it just goes quiet.
    // Both sides send a ping every few seconds; 8 seconds of silence counts as gone.
    var lastTick = Date.now();
    setInterval(function () {
      var now = Date.now(), frozen = now - lastTick > 5000;   // this phone itself was asleep: do not blame the others
      lastTick = now;
      Object.keys(links).forEach(function (id) {
        var l = links[id]; if (!l || !l.open) return;
        if (frozen) { l.seen = now; return; }
        if (now - (l.seen || now) > 8000) {
          l.open = false; try { if (l.dc && l.dc.close) l.dc.close(); } catch (e) {}
          if (l.auto) delete links[id];
          fireLinks(); return;
        }
        sendTo(l, { t: 'ping' });
      });
    }, 2500);

    var api = {
      kind: opts.kind || 'ring',
      uid: uid,
      ready: function () { return Promise.resolve(uid); },
      get: function (p) { return Promise.resolve(clean(getAt(tree, p))); },
      set: function (p, v) { return write([[parts(p).join('/'), clean(v)]]); },
      update: function (p, v) {
        v = clean(v);
        return write(Object.keys(v).map(function (k) { return [parts(p + '/' + k).join('/'), v[k]]; }));
      },
      remove: function (p) { return write([[parts(p).join('/'), null]]); },
      push: function (p, v) {
        var key = Date.now().toString(36) + '-' + (++counter).toString(36) + uid.slice(1, 5) + rnd().slice(0, 3);
        return write([[parts(p + '/' + key).join('/'), clean(v)]]).then(function () { return { key: key }; });
      },
      on: function (p, cb) {
        var last;
        var l = function (t) { var v = getAt(t, p), s = JSON.stringify(v); if (s !== last) { last = s; cb(clean(v)); } };
        listeners.push(l);
        setTimeout(function () { l(tree); }, 0);
        return function () { listeners = listeners.filter(function (x) { return x !== l; }); };
      },
      onChildAdded: function (p, cb) {
        var seenKeys = {};
        var l = function (t) {
          var v = getAt(t, p) || {};
          Object.keys(v).sort().forEach(function (k) { if (!seenKeys[k]) { seenKeys[k] = 1; cb(k, clean(v[k])); } });
        };
        listeners.push(l);
        setTimeout(function () { l(tree); }, 0);
        return function () { listeners = listeners.filter(function (x) { return x !== l; }); };
      },
      onDisconnect: function (p, v) {
        root.addEventListener('pagehide', function () { write([[parts(p).join('/'), clean(v)]]); });
        return Promise.resolve();
      },
      onConnection: function (cb) {
        var f = function (open) { cb(open > 0 || !everLinked); };
        linkListeners.push(f); setTimeout(function () { f(Object.keys(links).filter(function (i) { return links[i].open; }).length); }, 0);
        return function () { linkListeners = linkListeners.filter(function (x) { return x !== f; }); };
      },

      /* ----- linking two phones ----- */
      onLinks: function (cb) {
        linkListeners.push(cb);
        return function () { linkListeners = linkListeners.filter(function (x) { return x !== cb; }); };
      },
      linkCount: function () { return Object.keys(links).filter(function (i) { return links[i].open; }).length; },
      // Phone A: make a code for the next phone to scan.
      makeOffer: function () {
        if (pending) { try { pending.pc.close(); } catch (e) {} delete links[pending.id]; pending = null; }
        var link = newLink(); pending = link;
        attach(link, link.pc.createDataChannel('toepen'));
        return link.pc.createOffer().then(function (o) { return link.pc.setLocalDescription(o); })
          .then(function () { return wait(link.pc); })
          .then(function () { return packSdp(link.pc.localDescription.sdp, 'o'); });
      },
      // Phone B: take A's code and make the answer for A to scan.
      acceptOffer: function (code) {
        var o = unpackSdp(code);
        if (o.kind !== 'o') return Promise.reject(new Error('Dit is een antwoordcode. Scan eerst de QR van je buurman.'));
        var link = newLink();
        link.pc.ondatachannel = function (e) { attach(link, e.channel); };
        return link.pc.setRemoteDescription({ type: 'offer', sdp: o.sdp })
          .then(function () { return link.pc.createAnswer(); })
          .then(function (a) { return link.pc.setLocalDescription(a); })
          .then(function () { return wait(link.pc); })
          .then(function () { return packSdp(link.pc.localDescription.sdp, 'a'); });
      },
      // Phone A: take B's answer; the channel opens a moment later.
      finishOffer: function (code) {
        var o = unpackSdp(code);
        if (o.kind !== 'a') return Promise.reject(new Error('Dit is geen antwoordcode van je buurman.'));
        if (!pending) return Promise.reject(new Error('Maak eerst een QR voor je buurman.'));
        var link = pending; pending = null;
        return link.pc.setRemoteDescription({ type: 'answer', sdp: o.sdp });
      },
      // Any other transport (PeerJS) hands in a channel: { send, readyState, onopen, onclose, onmessage }.
      addChannel: function (chan) {
        var link = newLink(true); link.auto = true;
        attach(link, chan);
        return link;
      },
      // Leave the table: close every link (the data stays, so a host can come back).
      stop: function () {
        Object.keys(links).forEach(function (id) {
          var l = links[id]; l.open = false;
          try { if (l.dc && l.dc.close) l.dc.close(); } catch (e) {}
          try { if (l.pc) l.pc.close(); } catch (e) {}
          delete links[id];
        });
        pending = null; fireLinks();
      },
      reset: function () { W = {}; tree = {}; try { store.removeItem(STORE); } catch (e) {} }
    };
    return api;
  }

  root.ToepenNet = root.ToepenNet || {};
  root.ToepenNet.ring = ringBackend;
  root.ToepenNet._ring = { packSdp: packSdp, unpackSdp: unpackSdp };
})(this);
