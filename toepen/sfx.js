/*
 * Sound for the whole app (scoreboard and multiplayer): short tones made in the browser, no files.
 * Phones only allow sound after a tap, and pause it when the app goes to the background,
 * so the sound system is woken up on every tap and when you come back to the app.
 */
(function (root) {
  'use strict';
  var ctx = null;
  function ensure() {
    try {
      if (!ctx) { var C = root.AudioContext || root.webkitAudioContext; if (!C) return null; ctx = new C(); }
      if (ctx.state !== 'running' && ctx.resume) ctx.resume();
    } catch (e) { return null; }
    return ctx;
  }
  ['pointerdown', 'touchstart', 'keydown'].forEach(function (ev) { try { root.addEventListener(ev, ensure, { passive: true }); } catch (e) {} });
  try { document.addEventListener('visibilitychange', function () { if (document.visibilityState === 'visible') ensure(); }); } catch (e) {}

  var SOUNDS = {
    tick: [[440, 0.18]],                               // a card is played
    toep: [[523, 0.2], [784, 0.2]],                    // someone toept
    turn: [[660, 0.18], [880, 0.2]],                   // it is your turn
    hit: [[330, 0.2], [247, 0.22]],                    // penalty points
    round: [[392, 0.18], [523, 0.2]],                  // round is over
    win: [[523, 0.18], [659, 0.18], [784, 0.18], [1047, 0.22]]
  };
  root.ToepenSfx = {
    unlock: ensure,
    play: function (kind) {
      try { if (root.ToepenPrefs && !root.ToepenPrefs.get().sound) return; } catch (e) {}
      var c = ensure(); if (!c) return;
      (SOUNDS[kind] || SOUNDS.tick).forEach(function (n, i) {
        var o = c.createOscillator(), g = c.createGain(), t = c.currentTime + i * 0.09;
        o.type = 'triangle'; o.frequency.value = n[0];
        g.gain.setValueAtTime(0.0001, t);
        g.gain.exponentialRampToValueAtTime(n[1], t + 0.02);
        g.gain.exponentialRampToValueAtTime(0.0001, t + 0.24);
        o.connect(g); g.connect(c.destination);
        o.start(t); o.stop(t + 0.27);
      });
    }
  };
})(this);
