/*
 * Small realtime-database layer (in one browser):
 *  - local:    same API inside one browser, synced between tabs with
 *              BroadcastChannel (practice mode and automated tests)
 * Paths look like "rooms/KLAV/state".
 */
(function (root) {
  'use strict';

  function clean(v) { return v === undefined ? null : JSON.parse(JSON.stringify(v)); }

  /* ---------- Local (one browser) ---------- */
  function localBackend(ns) {
    var KEY = 'toepen-localdb-' + (ns || 'test');
    var chan = typeof BroadcastChannel !== 'undefined' ? new BroadcastChannel(KEY) : null;
    var listeners = [];
    var uidKey = KEY + '-uid', uid;
    try { uid = sessionStorage.getItem(uidKey); } catch (e) {}
    if (!uid) {
      uid = 'u' + Math.random().toString(36).slice(2, 10);
      try { sessionStorage.setItem(uidKey, uid); } catch (e) {}
    }

    function readTree() { try { return JSON.parse(localStorage.getItem(KEY)) || {}; } catch (e) { return {}; } }
    function writeTree(t) { try { localStorage.setItem(KEY, JSON.stringify(t)); } catch (e) {} }
    function parts(p) { return p.split('/').filter(Boolean); }
    function getAt(t, p) {
      var cur = t;
      for (var i = 0, ps = parts(p); i < ps.length; i++) { if (cur == null || typeof cur !== 'object') return null; cur = cur[ps[i]]; }
      return cur === undefined ? null : cur;
    }
    function setAt(t, p, v) {
      var ps = parts(p), cur = t;
      for (var i = 0; i < ps.length - 1; i++) {
        if (cur[ps[i]] == null || typeof cur[ps[i]] !== 'object') cur[ps[i]] = {};
        cur = cur[ps[i]];
      }
      if (v === null) delete cur[ps[ps.length - 1]]; else cur[ps[ps.length - 1]] = v;
    }
    function changed() {
      notify();
      if (chan) chan.postMessage('x');
    }
    function notify() {
      var t = readTree();
      listeners.slice().forEach(function (l) { l(t); });
    }
    if (chan) chan.onmessage = function () { setTimeout(notify, 0); };

    var pushCount = 0;
    function write(fn) { var t = readTree(); fn(t); writeTree(t); setTimeout(changed, 0); return Promise.resolve(); }

    var api = {
      kind: 'local',
      ready: function () { return Promise.resolve(uid); },
      get: function (p) { return Promise.resolve(clean(getAt(readTree(), p))); },
      set: function (p, v) { return write(function (t) { setAt(t, p, clean(v)); }); },
      update: function (p, v) {
        v = clean(v);
        return write(function (t) { Object.keys(v).forEach(function (k) { setAt(t, p + '/' + k, v[k]); }); });
      },
      remove: function (p) { return write(function (t) { setAt(t, p, null); }); },
      push: function (p, v) {
        var key = Date.now().toString(36) + '-' + (++pushCount).toString(36) + Math.random().toString(36).slice(2, 6);
        return write(function (t) { setAt(t, p + '/' + key, clean(v)); }).then(function () { return { key: key }; });
      },
      on: function (p, cb) {
        var last;
        var l = function (t) { var v = getAt(t, p), s = JSON.stringify(v); if (s !== last) { last = s; cb(clean(v)); } };
        listeners.push(l);
        setTimeout(function () { l(readTree()); }, 0);
        return function () { listeners = listeners.filter(function (x) { return x !== l; }); };
      },
      onChildAdded: function (p, cb) {
        var seen = {};
        var l = function (t) {
          var v = getAt(t, p) || {};
          Object.keys(v).sort().forEach(function (k) { if (!seen[k]) { seen[k] = 1; cb(k, clean(v[k])); } });
        };
        listeners.push(l);
        setTimeout(function () { l(readTree()); }, 0);
        return function () { listeners = listeners.filter(function (x) { return x !== l; }); };
      },
      onDisconnect: function (p, v) {
        root.addEventListener('pagehide', function () {
          var t = readTree(); setAt(t, p, clean(v)); writeTree(t);
          if (chan) chan.postMessage('x');
        });
        return Promise.resolve();
      },
      onConnection: function (cb) { setTimeout(function () { cb(true); }, 0); return function () {}; },
      reset: function () { try { localStorage.removeItem(KEY); } catch (e) {} }
    };
    return api;
  }

  root.ToepenNet = { local: localBackend };
})(this);
