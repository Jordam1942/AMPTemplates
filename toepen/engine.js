/*
 * Toepen game rules. Pure functions on a plain state object, so the same
 * code runs on the host phone and in tests (Node).
 *
 * Cards are strings: rank + suit, e.g. "10h", "Bs".
 * Suits: h harten, r ruiten, k klaveren, s schoppen.
 * Ranks low -> high: B V H A 7 8 9 10.
 */
(function (root) {
  'use strict';

  var SUITS = ['h', 'r', 'k', 's'];
  var RANKS = ['B', 'V', 'H', 'A', '7', '8', '9', '10'];
  var FACES = ['B', 'V', 'H', 'A'];

  function suitOf(c) { return c.slice(-1); }
  function rankOf(c) { return c.slice(0, -1); }
  function power(c) { return RANKS.indexOf(rankOf(c)); }

  function newDeck() {
    var d = [];
    SUITS.forEach(function (s) { RANKS.forEach(function (r) { d.push(r + s); }); });
    return d;
  }

  function randomInt(n) {
    var c = (typeof crypto !== 'undefined' && crypto.getRandomValues) ? crypto : null;
    if (c) {
      // Rejection sampling avoids modulo bias.
      var max = Math.floor(0x100000000 / n) * n, buf = new Uint32Array(1);
      do { c.getRandomValues(buf); } while (buf[0] >= max);
      return buf[0] % n;
    }
    return Math.floor(Math.random() * n);
  }

  function shuffle(arr) {
    for (var i = arr.length - 1; i > 0; i--) {
      var j = randomInt(i + 1), t = arr[i]; arr[i] = arr[j]; arr[j] = t;
    }
    return arr;
  }

  var DEFAULT_RULES = { dubbelToep: false, scherpPassen: true, wittewas: true, wasStraf: 1, maxInzet: 4, hoogsteDeelt: false };   // maxInzet: highest stake (0 = no limit)

  /* ---------- Game setup ---------- */

  // seats: array of { id, name, bot }
  function newGame(opts) {
    var G = {
      limit: opts.limit || 10,
      rules: Object.assign({}, DEFAULT_RULES, opts.rules || {}),
      seats: opts.seats.map(function (s) { return s.id; }),
      players: {},
      dealer: opts.dealer || 0,
      roundNo: 0,
      round: null,
      phase: 'play',
      winner: null,
      results: [],
      seq: 0
    };
    opts.seats.forEach(function (s) {
      G.players[s.id] = { name: s.name, score: 0, out: false, bot: !!s.bot, toeps: 0, roundsWon: 0, passes: 0 };
    });
    startRound(G);
    return G;
  }

  function activeSeats(G) { return G.seats.filter(function (id) { return !G.players[id].out; }); }

  function inRound(G, id) {
    var p = G.players[id];
    return !!p && !p.out && G.round && !(id in G.round.passed);
  }
  function inRoundIds(G) { return G.seats.filter(function (id) { return inRound(G, id); }); }

  function nextFrom(G, id, pred) {
    var n = G.seats.length, i = G.seats.indexOf(id);
    for (var k = 1; k <= n; k++) {
      var cand = G.seats[(i + k) % n];
      if (pred(cand)) return cand;
    }
    return null;
  }

  function startRound(G) {
    var dealerId = G.seats[G.dealer];
    if (G.players[dealerId].out) {
      dealerId = nextFrom(G, dealerId, function (id) { return !G.players[id].out; });
      G.dealer = G.seats.indexOf(dealerId);
    }
    var deck = shuffle(newDeck()), hands = {};
    var order = [], cur = dealerId;
    activeSeats(G).forEach(function () {
      cur = nextFrom(G, cur, function (id) { return !G.players[id].out; });
      order.push(cur);
    });
    order.forEach(function (id) { hands[id] = []; });
    for (var k = 0; k < 4; k++) order.forEach(function (id) { hands[id].push(deck.pop()); });
    G.roundNo++;
    G.round = {
      n: G.roundNo, stake: 1, toeps: [], passed: {}, hands: hands, stock: deck,
      trick: [], trickNo: 1, leader: order[0], turn: order[0], pending: null,
      lastTrick: null, wasDone: {}, was: null, wasResult: null, dealer: dealerId
    };
    G.phase = 'play';
    G.seq++;
  }

  /* ---------- Queries ---------- */

  function legalCards(G, id) {
    var r = G.round, hand = r.hands[id] || [];
    var lead = leadSuit(G);
    if (!lead) return hand.slice();
    var same = hand.filter(function (c) { return suitOf(c) === lead; });
    return same.length ? same : hand.slice();
  }

  function leadSuit(G) {
    var t = G.round.trick.filter(function (p) { return inRound(G, p.id); });
    return t.length ? suitOf(t[0].card) : null;
  }

  function lastToeper(G) {
    var t = G.round.toeps;
    return t.length ? t[t.length - 1].by : null;
  }

  function isScherp(G, id) { return G.players[id].score === G.limit - 1; }

  // Games saved before this rule existed have no maxInzet: they get the default, not 'no limit'.
  function maxStake(G) { return G.rules.maxInzet === undefined ? DEFAULT_RULES.maxInzet : G.rules.maxInzet; }
  function canToep(G, id) {
    var r = G.round;
    return G.phase === 'play' && !r.pending && !r.was && r.turn === id && inRound(G, id) &&
      (!maxStake(G) || r.stake < maxStake(G)) && inRoundIds(G).length > 1 && (G.rules.dubbelToep || lastToeper(G) !== id);
  }

  function isWitteWas(hand) {
    return hand.length === 4 && hand.every(function (c) { return FACES.indexOf(rankOf(c)) >= 0; });
  }

  // Anyone may claim a dirty-laundry hand before the first card is played, also when bluffing.
  // The others may then check: the game looks at the real hand and decides.
  function canWas(G, id) {
    var r = G.round;
    return G.phase === 'play' && G.rules.wittewas && !r.pending && !r.was && inRound(G, id) &&
      r.trickNo === 1 && r.trick.length === 0 && r.toeps.length === 0 &&
      !r.wasDone[id] && r.stock.length >= 4 && (r.hands[id] || []).length === 4;
  }

  function mustPlay(G, id) { return !G.rules.scherpPassen && isScherp(G, id); }

  /* ---------- Actions ---------- */

  function apply(G, id, a) {
    if (G.phase !== 'play') return fail('Het spel staat stil.');
    if (!G.players[id]) return fail('Onbekende speler.');
    var r = G.round, ev = [];
    switch (a.type) {
      case 'toep':
        if (!canToep(G, id)) return fail('Je kunt nu niet toepen.');
        var to = r.stake + 1;
        r.toeps.push({ by: id, to: to });
        G.players[id].toeps++;
        var awaiting = inRoundIds(G).filter(function (o) { return o !== id; });
        var answers = {};
        awaiting.forEach(function (o) { if (mustPlay(G, o)) answers[o] = 'mee'; });
        r.pending = { by: id, to: to, awaiting: awaiting, answers: answers };
        ev.push({ t: 'toep', by: id, to: to });
        resolvePending(G, ev);
        break;

      case 'answer':
        if (!r.pending || r.pending.awaiting.indexOf(id) < 0 || r.pending.answers[id]) return fail('Er wordt nu niets gevraagd.');
        if (!a.mee && mustPlay(G, id)) return fail('Op scherp moet je mee.');
        r.pending.answers[id] = a.mee ? 'mee' : 'pas';
        resolvePending(G, ev);
        break;

      case 'was':
        if (!canWas(G, id)) return fail('Vuile was kan nu niet.');
        r.wasDone[id] = true;
        r.wasResult = null;
        r.was = { by: id, awaiting: inRoundIds(G).filter(function (o) { return o !== id; }), answers: {} };
        ev.push({ t: 'wasClaim', by: id });
        if (!r.was.awaiting.length) resolveWas(G, null, ev);
        break;

      case 'wascheck':
        if (!r.was || r.was.awaiting.indexOf(id) < 0 || r.was.answers[id]) return fail('Er wordt nu niets gevraagd.');
        if (a.check) { resolveWas(G, id, ev); break; }
        r.was.answers[id] = 'geloof';
        if (r.was.awaiting.every(function (o) { return r.was.answers[o]; })) resolveWas(G, null, ev);
        break;

      case 'play':
        if (r.pending || r.was) return fail('Wacht tot iedereen gekozen heeft.');
        if (r.turn !== id) return fail('Je bent niet aan de beurt.');
        if (legalCards(G, id).indexOf(a.card) < 0) return fail('Die kaart mag je nu niet spelen.');
        r.hands[id].splice(r.hands[id].indexOf(a.card), 1);
        r.wasResult = null;
        r.trick.push({ id: id, card: a.card });
        ev.push({ t: 'play', by: id, card: a.card });
        afterPlay(G, id, ev);
        break;

      default:
        return fail('Onbekende actie.');
    }
    G.seq++;
    return { ok: true, events: ev };
  }

  function fail(msg) { return { ok: false, error: msg }; }

  function swapHand(G, id) {
    var r = G.round, old = r.hands[id];
    r.hands[id] = r.stock.splice(r.stock.length - 4, 4);
    r.stock = shuffle(r.stock.concat(old));
  }
  // Nobody checked: the claimer swaps four cards (even when it was a bluff).
  // Someone checked: the real hand decides. Right claim: the checker gets the penalty. Bluff: the claimer does.
  function resolveWas(G, checkerId, ev) {
    var r = G.round, w = r.was, id = w.by, pts = G.rules.wasStraf || 1;
    r.was = null;
    if (!checkerId) {
      swapHand(G, id);
      r.wasResult = { by: id, checker: null };
      ev.push({ t: 'was', by: id, checker: null });
      return;
    }
    var honest = isWitteWas(r.hands[id] || []), victim = honest ? checkerId : id;
    r.wasResult = { by: id, checker: checkerId, ok: honest, hand: r.hands[id].slice(), pts: pts, victim: victim };
    G.players[victim].score += pts;
    if (G.players[victim].score >= G.limit) G.players[victim].out = true;
    if (honest) swapHand(G, id);
    ev.push({ t: 'was', by: id, checker: checkerId, ok: honest, pts: pts, victim: victim });
    var left = inRoundIds(G);
    if (left.length <= 1) { endRound(G, left.length ? left[0] : id, ev); return; }
    if (!inRound(G, r.turn)) r.turn = nextFrom(G, r.turn, function (o) { return inRound(G, o); });
    if (!inRound(G, r.leader)) r.leader = r.turn;
  }

  function resolvePending(G, ev) {
    var r = G.round, p = r.pending;
    if (p.awaiting.some(function (o) { return !p.answers[o]; })) return;
    var before = p.to - 1;
    r.stake = p.to;
    r.pending = null;
    p.awaiting.forEach(function (o) {
      if (p.answers[o] === 'pas') {
        r.passed[o] = before;
        G.players[o].score += before;
        G.players[o].passes++;
        if (G.players[o].score >= G.limit) G.players[o].out = true;
        ev.push({ t: 'pass', by: o, pts: before });
      }
    });
    // Cards of players who folded no longer count in the current trick.
    r.trick = r.trick.filter(function (pl) { return !(pl.id in r.passed); });
    var left = inRoundIds(G);
    if (left.length === 1) { endRound(G, left[0], ev); return; }
    if (!inRound(G, r.turn)) r.turn = nextFrom(G, r.turn, function (o) { return inRound(G, o); });
    maybeFinishTrick(G, ev);
  }

  function afterPlay(G, id, ev) {
    if (maybeFinishTrick(G, ev)) return;
    G.round.turn = nextFrom(G, id, function (o) { return inRound(G, o) && !playedThisTrick(G, o); });
  }

  function playedThisTrick(G, id) {
    return G.round.trick.some(function (p) { return p.id === id; });
  }

  function maybeFinishTrick(G, ev) {
    var r = G.round, players = inRoundIds(G);
    if (!r.trick.length || !players.every(function (o) { return playedThisTrick(G, o); })) return false;
    var lead = leadSuit(G), best = null;
    r.trick.forEach(function (pl) {
      if (suitOf(pl.card) === lead && (!best || power(pl.card) > power(best.card))) best = pl;
    });
    r.lastTrick = { cards: r.trick.slice(), winner: best.id, n: r.trickNo };
    ev.push({ t: 'trick', winner: best.id, n: r.trickNo });
    r.trick = [];
    r.trickNo++;
    if (r.trickNo > 4) { endRound(G, best.id, ev); return true; }
    r.leader = r.turn = best.id;
    return true;
  }

  function endRound(G, winnerId, ev) {
    var r = G.round, losers = [];
    inRoundIds(G).forEach(function (o) {
      if (o === winnerId) return;
      G.players[o].score += r.stake;
      losers.push(o);
      if (G.players[o].score >= G.limit) G.players[o].out = true;
    });
    G.players[winnerId].roundsWon++;
    var result = { n: r.n, winner: winnerId, stake: r.stake, losers: losers, passed: Object.assign({}, r.passed), toeps: r.toeps.slice() };
    G.results.push(result);
    ev.push({ t: 'round', result: result });
    r.turn = null;
    var act = activeSeats(G);
    if (act.length <= 1) {
      G.phase = 'done';
      G.winner = act.length ? act[0] : winnerId;
      ev.push({ t: 'done', winner: G.winner });
    } else {
      G.phase = 'roundEnd';
    }
  }

  function nextRound(G) {
    if (G.phase !== 'roundEnd') return false;
    var cur = G.seats[G.dealer];
    // House rule: the player with the most points deals (ties: next at the table).
    var top = G.rules.hoogsteDeelt ? Math.max.apply(null, activeSeats(G).map(function (id) { return G.players[id].score; })) : null;
    var nd = nextFrom(G, cur, function (id) { return !G.players[id].out && (top === null || G.players[id].score === top); });
    G.dealer = G.seats.indexOf(nd);
    startRound(G);
    return true;
  }

  /* ---------- What each phone may see ---------- */

  function publicState(G) {
    var r = G.round, counts = {};
    Object.keys(r.hands).forEach(function (id) { counts[id] = r.hands[id].length; });
    return {
      seq: G.seq, limit: G.limit, rules: G.rules, seats: G.seats, players: G.players,
      dealer: G.dealer, phase: G.phase, winner: G.winner,
      lastResult: G.results.length ? G.results[G.results.length - 1] : null,
      round: {
        n: r.n, stake: r.stake, toeps: r.toeps, passed: r.passed, trick: r.trick, trickNo: r.trickNo,
        leader: r.leader, turn: r.turn, pending: r.pending, lastTrick: r.lastTrick, wasDone: r.wasDone, was: r.was, wasResult: r.wasResult, undone: G.undone || 0,
        dealer: r.dealer, handCounts: counts, stockCount: r.stock.length
      }
    };
  }

  /* ---------- Computer player ---------- */

  function handStrength(hand) {
    return hand.reduce(function (s, c) { return s + power(c); }, 0) / Math.max(1, hand.length);
  }

  function botAction(G, id) {
    var r = G.round;
    if (G.phase !== 'play' || !inRound(G, id)) return null;
    if (r.was) {
      if (r.was.awaiting.indexOf(id) < 0 || r.was.answers[id]) return null;
      return { type: 'wascheck', check: Math.random() < 0.3 };   // a computer player doubts now and then
    }
    if (r.pending) {
      if (r.pending.awaiting.indexOf(id) < 0 || r.pending.answers[id]) return null;
      var hand = r.hands[id], cost = r.pending.to - 1, p = G.players[id];
      var strong = handStrength(hand) >= 4.2 || hand.some(function (c) { return power(c) === 7; });
      var wouldBeOut = p.score + cost >= G.limit;
      var mee = strong || wouldBeOut || (r.pending.to <= 2 && handStrength(hand) >= 3) || mustPlay(G, id);
      return { type: 'answer', mee: mee };
    }
    if (r.turn !== id) return null;
    if (canWas(G, id) && isWitteWas(r.hands[id])) return { type: 'was' };
    var legal = legalCards(G, id).sort(function (a, b) { return power(a) - power(b); });
    var hi = legal[legal.length - 1];
    if (canToep(G, id) && r.stake < 4 && power(hi) >= 6 && r.hands[id].length <= 2 && Math.random() < 0.6) return { type: 'toep' };
    var lead = leadSuit(G);
    if (r.trickNo === 4) return { type: 'play', card: hi };
    if (!lead) return { type: 'play', card: legal[0] };
    // Follow suit as cheaply as possible; keep the best card for the last trick.
    return { type: 'play', card: legal[0] };
  }

  var api = {
    SUITS: SUITS, RANKS: RANKS, suitOf: suitOf, rankOf: rankOf, power: power, newDeck: newDeck, shuffle: shuffle,
    newGame: newGame, startRound: startRound, nextRound: nextRound, apply: apply,
    legalCards: legalCards, canToep: canToep, maxStake: maxStake, canWas: canWas, mustPlay: mustPlay, isScherp: isScherp,
    inRound: inRound, inRoundIds: inRoundIds, activeSeats: activeSeats, leadSuit: leadSuit,
    publicState: publicState, botAction: botAction, isWitteWas: isWitteWas
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.ToepenEngine = api;
})(this);
