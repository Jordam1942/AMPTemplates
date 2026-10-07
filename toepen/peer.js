/*
 * Play over the internet with PeerJS: no account and nothing to set up.
 * The free public PeerJS server only introduces the phones to each other;
 * after that they talk directly (host in the middle, guests around it).
 * The data layer is the same as the QR-ring (ring.js); only the way phones
 * find each other differs. Each player only receives their own hand.
 */
(function (root) {
  'use strict';
  var loading = null;
  function loadLib() {
    if (root.Peer) return Promise.resolve();
    if (!loading) loading = new Promise(function (res, rej) {
      var s = document.createElement('script'); s.src = 'vendor/peerjs.min.js'; s.onload = res;
      s.onerror = function () { loading = null; rej(new Error('PeerJS laden mislukt. Is er internet?')); };
      document.head.appendChild(s);
    });
    return loading;
  }
  var NL = {
    'unavailable-id': 'Deze tafelcode is al in gebruik.',
    'peer-unavailable': 'Tafel niet gevonden. Controleer de code, en of de host de tafel open heeft.',
    'network': 'Geen verbinding met de PeerJS-server. Is er internet?',
    'server-error': 'Geen verbinding met de PeerJS-server. Probeer het zo nog eens.',
    'socket-error': 'Geen verbinding met de PeerJS-server. Is er internet?',
    'socket-closed': 'Geen verbinding met de PeerJS-server. Is er internet?',
    'browser-incompatible': 'Deze browser kan niet rechtstreeks verbinden. Gebruik Chrome.'
  };
  function nl(e) {
    var err = new Error(NL[e && e.type] || ('PeerJS-fout: ' + ((e && (e.message || e.type)) || 'onbekend')));
    err.type = e && e.type; return err;
  }

  function peerBackend(options) {
    options = options || {};          // host/port/path/secure/iceServers: only for tests with an own server
    var api = root.ToepenNet.ring({ kind: 'peer', storeKey: 'toepen-peer-data', privateHands: true });
    var peer = null, role = null, stopped = false, redial = null, conns = [];

    function cfg() {
      var c = { debug: 0 };
      if (options.host) { c.host = options.host; c.port = options.port; c.path = options.path || '/'; c.secure = !!options.secure; }
      if (options.iceServers) c.config = { iceServers: options.iceServers };
      return c;
    }
    function adapt(conn) {
      conns.push(conn);
      conn.on('close', function () { conns = conns.filter(function (c) { return c !== conn; }); });
      var ch = { readyState: 'connecting', send: function (d) { conn.send(d); }, onopen: null, onclose: null, onmessage: null };
      conn.on('open', function () { ch.readyState = 'open'; if (ch.onopen) ch.onopen(); });
      conn.on('data', function (d) { if (ch.onmessage) ch.onmessage({ data: d }); });
      conn.on('close', function () { ch.readyState = 'closed'; if (ch.onclose) ch.onclose(); });
      conn.on('error', function () {});
      api.addChannel(ch);
      if (conn.open) { ch.readyState = 'open'; if (ch.onopen) ch.onopen(); }
      return ch;
    }
    function destroy() { if (peer) { try { peer.destroy(); } catch (e) {} peer = null; } }

    // The table code is the host's address. After an accident the public server can keep the old
    // registration of a dead phone for a minute or more, so a resumed host may take a spare address
    // and guests simply try all of them.
    function ids(code) { return ['toepen-' + code, 'toepen-' + code + '-b', 'toepen-' + code + '-c']; }
    api.openHost = function (code, opts) {
      var list = ids(code), tries = opts && opts.resume ? list.length : 1;
      function attempt(i) {
        return openOne(code, list[i]).catch(function (e) {
          if (e && e.type === 'unavailable-id' && i + 1 < tries) return attempt(i + 1);
          throw e;
        });
      }
      return loadLib().then(function () { return attempt(0); });
    };
    function openOne(code, id) {
      return Promise.resolve().then(function () {
        return new Promise(function (res, rej) {
          destroy(); role = 'host'; stopped = false;
          var done = false;
          peer = new root.Peer(id, cfg());
          peer.on('open', function () { done = true; res(code); });
          peer.on('error', function (e) { if (!done) { done = true; rej(nl(e)); } });
          peer.on('connection', function (conn) { adapt(conn); });
          peer.on('disconnected', function () { if (!stopped && peer) { try { peer.reconnect(); } catch (e) {} } });
          setTimeout(function () { if (!done) { done = true; rej(nl({ type: 'network' })); } }, 12000);
        });
      });
    }
    // Guest: call the host; keeps trying again when the link drops.
    api.joinHost = function (code) {
      return loadLib().then(function () {
        return new Promise(function (res, rej) {
          destroy(); role = 'client'; stopped = false;
          var first = true, hostIds = ids(code), idx = 0, unavail = 0;
          function dial() {
            if (stopped || !peer || peer.destroyed || api.linkCount() > 0) return;
            if (peer.disconnected) { try { peer.reconnect(); } catch (e) {} setTimeout(dial, 1500); return; }
            var conn = peer.connect(hostIds[idx % hostIds.length], { reliable: true }), settled = false;
            // a dead phone that is still registered never answers: after a while try the next address
            var timer = setTimeout(function () { if (!settled && !stopped) { settled = true; try { conn.close(); } catch (e) {} idx++; dial(); } }, 7000);
            conn.on('open', function () { settled = true; clearTimeout(timer); unavail = 0; if (first) { first = false; res(); } });
            conn.on('close', function () {
              clearTimeout(timer);
              if (stopped) return;
              if (!settled) { settled = true; idx++; setTimeout(dial, 500); } else setTimeout(dial, 2000);
            });
            adapt(conn);
          }
          redial = dial;
          peer = new root.Peer(cfg());
          peer.on('open', dial);
          peer.on('error', function (e) {
            if (e && e.type === 'peer-unavailable' && !stopped) {
              idx++; unavail++;
              if (first && unavail >= hostIds.length) { first = false; rej(nl(e)); }
              else setTimeout(dial, first ? 200 : 2500);
            } else if (first) { first = false; rej(nl(e)); }
          });
          setTimeout(function () { if (first) { first = false; rej(nl({ type: 'peer-unavailable' })); } }, 22000);
        });
      });
    };
    api.stop = function () { stopped = true; destroy(); redial = null; };
    // The phone woke up again (screen unlocked): connect right away instead of waiting for the next try.
    api.nudge = function () {
      if (stopped || !peer || peer.destroyed) return;
      if (peer.disconnected) { try { peer.reconnect(); } catch (e) {} }
      if (role === 'client' && redial && api.linkCount() === 0) setTimeout(redial, 300);
    };
    api._dropLinks = function () { conns.slice().forEach(function (c) { try { c.close(); } catch (e) {} }); };
    var baseOn = api.onConnection;
    api.onConnection = function (cb) { return baseOn(function (ok) { cb(role === 'host' ? true : ok); }); };
    return api;
  }

  root.ToepenNet = root.ToepenNet || {};
  root.ToepenNet.peer = peerBackend;
})(this);
