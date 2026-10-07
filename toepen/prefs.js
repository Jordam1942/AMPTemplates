/*
 * Settings that apply to the whole app (main menu, scoreboard and multiplayer):
 * theme, sound, vibration and keeping the screen on. Kept on this phone.
 */
(function (root) {
  'use strict';
  var KEY = 'toepen-prefs-v1', DEFAULTS = { theme: 'system', sound: true, vibrate: true, wake: true, hand: 'zichtbaar', takeover: 45, confirmToep: true, speed: 'rustig', gfx: true, fxPlay: true, fxTrick: true, fxToep: true, fxScore: true, fxWin: true };  // gfx: animations on/off, fx*: each animation on its own  // speed: rustig | normaal | snel (pace of computer players and rounds)  // takeover: seconds before the computer plays for someone who is offline (0 = never)  // hand: zichtbaar | verborgen | auto (plat op tafel)
  function read() {
    try { var o = JSON.parse(localStorage.getItem(KEY)); return o && typeof o === 'object' ? o : null; } catch (e) { return null; }
  }
  var api = {
    get: function () { var o = read() || {}, r = {}; Object.keys(DEFAULTS).forEach(function (k) { r[k] = o[k] === undefined ? DEFAULTS[k] : o[k]; }); return r; },
    // Is this animation on? (master switch, its own switch, and the phone's 'reduce motion' setting)
    fx: function (k) {
      var o = api.get(); if (!o.gfx || !o[k]) return false;
      try { if (root.matchMedia && root.matchMedia('(prefers-reduced-motion: reduce)').matches) return false; } catch (e) {}
      return true;
    },
    has: function () { return !!read(); },
    set: function (k, v) { var o = api.get(); if (o[k] === v) return; o[k] = v; try { localStorage.setItem(KEY, JSON.stringify(o)); } catch (e) {} },
    setMany: function (obj) { Object.keys(DEFAULTS).forEach(function (k) { if (obj && obj[k] !== undefined) api.set(k, obj[k]); }); },
    apply: function () {
      var t = api.get().theme, r = document.documentElement;
      if (t === 'light' || t === 'dark') r.setAttribute('data-theme', t); else r.removeAttribute('data-theme');
    }
  };
  api.apply();
  root.ToepenPrefs = api;
})(this);
