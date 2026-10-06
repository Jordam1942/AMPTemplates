/*
 * Saved games: the scoreboard (game, players, ranking) can be kept under a name
 * and opened again later. Shared by the main menu and the scoreboard.
 * Everything stays on this phone (localStorage).
 */
(function (root) {
  'use strict';
  var KEY = 'toepen-saves-v1', SB = 'toepen-scorebord-v2', MAX_AUTO = 2;

  function read() {
    try { var a = JSON.parse(localStorage.getItem(KEY)); return Array.isArray(a) ? a : []; } catch (e) { return []; }
  }
  function write(a) {
    try { localStorage.setItem(KEY, JSON.stringify(a)); return true; } catch (e) { return false; }
  }
  function sbState() {
    try { var d = JSON.parse(localStorage.getItem(SB)); return d && d.v === 2 ? d : null; } catch (e) { return null; }
  }
  function clone(o) { return JSON.parse(JSON.stringify(o)); }

  // What a save looks like in a list: who plays, who leads, who won how often.
  function summarize(st) {
    var g = st.game, players = [], round = 0, done = false;
    if (g) {
      players = g.players.filter(function (p) { return !p.left; }).map(function (p) { return { name: p.name, score: p.score, out: !!p.out }; });
      round = g.round ? g.round.n : 0;
      done = players.filter(function (p) { return !p.out; }).length <= 1;
    }
    var wins = Object.keys(st.wins || {}).map(function (n) { return { name: n, wins: st.wins[n] }; })
      .sort(function (a, b) { return b.wins - a.wins; });
    var games = Math.max((st.archive || []).length, wins.reduce(function (n, w) { return n + w.wins; }, 0));
    return { players: players, round: round, done: done, wins: wins.slice(0, 3), games: games };
  }
  function hasContent(st) {
    return !!(st && (st.game || (st.archive && st.archive.length) || Object.keys(st.wins || {}).length));
  }
  function uid() { return 's' + Date.now().toString(36) + Math.random().toString(36).slice(2, 6); }

  var api = {
    list: function () { return read().sort(function (a, b) { return b.at - a.at; }); },
    count: function () { return read().length; },
    // The scoreboard that is on this phone right now (for 'continue').
    current: function () { var st = sbState(); return st && hasContent(st) ? { state: st, sum: summarize(st) } : null; },
    summarize: summarize,
    hasContent: hasContent,
    // Keep a scoreboard state under a name. Returns the record, or null if the phone's storage is full.
    saveState: function (name, st, opts) {
      opts = opts || {};
      var s = {};
      Object.keys(st).forEach(function (k) { if (k !== 'undo' && k !== 'redo' && k !== 'settings') s[k] = st[k]; });
      s = clone(s);
      var rec = { id: uid(), name: String(name || '').trim().slice(0, 40) || 'Zonder naam', at: Date.now(), auto: !!opts.auto, state: s, sum: summarize(s) };
      var all = read();
      if (opts.auto) {
        var autos = all.filter(function (x) { return x.auto; }).sort(function (a, b) { return b.at - a.at; });
        autos.slice(MAX_AUTO - 1).forEach(function (x) { all.splice(all.indexOf(x), 1); });
      }
      all.push(rec);
      return write(all) ? rec : null;
    },
    rename: function (id, name) {
      var all = read(), r = all.filter(function (x) { return x.id === id; })[0];
      if (!r) return false;
      r.name = String(name || '').trim().slice(0, 40) || r.name; r.auto = false;
      return write(all);
    },
    remove: function (id) { return write(read().filter(function (x) { return x.id !== id; })); },
    // Put a save back as the scoreboard. The scoreboard that was there is kept as an automatic save first.
    load: function (id) {
      var rec = read().filter(function (x) { return x.id === id; })[0];
      if (!rec) return false;
      var cur = sbState();
      if (cur && hasContent(cur) && !api.saveState('Automatisch bewaard', cur, { auto: true })) return false;  // storage full: keep what is there
      var next = clone(rec.state);
      next.v = 2; next.undo = []; next.redo = [];
      next.settings = cur && cur.settings ? cur.settings : { theme: 'system', sound: true, vibrate: true, wake: true };
      if (!next.screen) next.screen = next.game ? 'game' : 'setup';
      try { localStorage.setItem(SB, JSON.stringify(next)); return true; } catch (e) { return false; }
    }
  };
  root.ToepenSaves = api;
})(this);
