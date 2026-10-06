// Plays thousands of random games and checks the rules after every move.
// Run with: node toepen/tests/engine.test.js
const E = require('../engine.js');
const assert = require('assert');
function pick(a) { return a[Math.floor(Math.random() * a.length)]; }
const stats = { games: 0, rounds: 0, toeps: 0, passes: 0, was: 0, actions: 0, maxRounds: 0 };

function checkDeal(G) {
  const r = G.round, all = [];
  Object.values(r.hands).forEach(h => all.push(...h));
  r.trick.forEach(t => all.push(t.card));
  all.push(...r.stock);
  // cards already played this round are gone from hands; count only at round start
  return all;
}

function runGame(n, rules, botsOnly) {
  const seats = Array.from({ length: n }, (_, i) => ({ id: 'p' + i, name: 'P' + i }));
  const limit = pick([10, 15, 21]);
  const G = E.newGame({ seats, limit, rules, dealer: Math.floor(Math.random() * n) });
  let guard = 0, roundStartCheck = true;
  while (G.phase !== 'done') {
    assert(++guard < 20000, 'game does not end');
    if (G.phase === 'roundEnd') {
      E.nextRound(G); roundStartCheck = true;
      const d = G.seats[G.dealer], act = E.activeSeats(G);
      assert(!G.players[d].out, 'dealer must still be in');
      if (G.rules.hoogsteDeelt) assert.strictEqual(G.players[d].score, Math.max(...act.map(id => G.players[id].score)), 'highest score deals');
      stats.dealerChecks = (stats.dealerChecks || 0) + 1;
      continue;
    }
    if (roundStartCheck) {
      const cards = checkDeal(G);
      assert.strictEqual(cards.length, 32, 'deck size ' + cards.length);
      assert.strictEqual(new Set(cards).size, 32, 'duplicate cards');
      E.activeSeats(G).forEach(id => assert.strictEqual(G.round.hands[id].length, 4, 'four cards each'));
      assert(!G.players[G.round.dealer].out, 'dealer is out');
      roundStartCheck = false;
    }
    const before = {}; G.seats.forEach(id => before[id] = G.players[id].score);
    const r = G.round;
    let actor, action;
    if (r.was) {
      actor = r.was.awaiting.find(id => !r.was.answers[id]);
      action = botsOnly ? E.botAction(G, actor) : { type: 'wascheck', check: Math.random() < 0.4 };
    } else if (r.pending) {
      actor = r.pending.awaiting.find(id => !r.pending.answers[id]);
      action = botsOnly ? E.botAction(G, actor) : { type: 'answer', mee: E.mustPlay(G, actor) ? true : Math.random() < 0.6 };
    } else {
      actor = r.turn;
      if (botsOnly) action = E.botAction(G, actor);
      else {
        const opts = [];
        if (E.canToep(G, actor) && Math.random() < 0.25) opts.push({ type: 'toep' });
        if (E.canWas(G, actor) && Math.random() < 0.5) opts.push({ type: 'was' });  // also with a normal hand: a bluff
        if (!opts.length) opts.push({ type: 'play', card: pick(E.legalCards(G, actor)) });
        action = pick(opts);
      }
    }
    assert(actor, 'nobody can act');
    // Illegal play must be refused
    if (action.type === 'play') {
      const illegal = (G.round.hands[actor] || []).find(c => E.legalCards(G, actor).indexOf(c) < 0);
      if (illegal) assert(!E.apply(G, actor, { type: 'play', card: illegal }).ok, 'illegal card accepted');
      const other = G.seats.find(id => id !== actor && E.inRound(G, id));
      if (other && G.round.hands[other] && G.round.hands[other].length) assert(!E.apply(G, other, { type: 'play', card: G.round.hands[other][0] }).ok, 'out of turn accepted');
    }
    const res = E.apply(G, actor, action);
    assert(res.ok, 'legal action refused: ' + JSON.stringify(action) + ' ' + res.error);
    stats.actions++;
    res.events.forEach(e => {
      if (e.t === 'toep') stats.toeps++;
      if (e.t === 'pass') stats.passes++;
      if (e.t === 'wasClaim') stats.was++;
      if (e.t === 'was' && e.checker) stats.wasChecked = (stats.wasChecked || 0) + 1;
      if (e.t === 'round') {
        stats.rounds++;
        const rr = e.result;
        assert(!rr.losers.includes(rr.winner), 'winner also loser');
      }
    });
    // score only ever goes up, by the right amounts
    const deltas = G.seats.map(id => G.players[id].score - before[id]);
    deltas.forEach(d => assert(d >= 0, 'score went down'));
    const roundEv = res.events.find(e => e.t === 'round');
    const passEv = res.events.filter(e => e.t === 'pass');
    const wasPts = res.events.filter(e => e.t === 'was' && e.checker).reduce((s, e) => s + e.pts, 0);
    const expected = passEv.reduce((s, e) => s + e.pts, 0) + wasPts + (roundEv ? roundEv.result.stake * roundEv.result.losers.length : 0);
    assert.strictEqual(deltas.reduce((a, b) => a + b, 0), expected, 'score bookkeeping');
    // a player who is out never holds the turn
    if (G.phase === 'play' && G.round.turn) assert(!G.players[G.round.turn].out && E.inRound(G, G.round.turn), 'turn held by player not in round');
  }
  assert(G.winner && !G.players[G.winner].out, 'winner must still be in');
  assert.strictEqual(E.activeSeats(G).length, 1, 'exactly one left');
  stats.games++; stats.maxRounds = Math.max(stats.maxRounds, G.roundNo);
  // public state must not leak hands or stock
  const pub = JSON.stringify(E.publicState(G));
  assert(!/"hands"|"stock"/.test(pub), 'public state leaks cards');
}

const t0 = Date.now();
for (let i = 0; i < 6000; i++) {
  const n = 2 + (i % 7);
  const rules = { dubbelToep: Math.random() < 0.5, scherpPassen: Math.random() < 0.5, wittewas: Math.random() < 0.7, wasStraf: Math.random() < 0.5 ? 1 : 2, maxInzet: pick([0, 3, 4, 6]), hoogsteDeelt: Math.random() < 0.5 };
  runGame(n, rules, i % 3 === 0);
}
// ---- vuile was: claim, check, bluff ----
function wasGame(handA) {
  const g = E.newGame({ seats: [{ id: 'a', name: 'A' }, { id: 'b', name: 'B' }, { id: 'c', name: 'C' }], rules: { wittewas: true, wasStraf: 2 }, dealer: 0 });
  g.round.hands.a = handA.slice(); g.round.turn = 'a'; g.round.leader = 'a';
  return g;
}
const CLEAN = ['Bh', 'Vh', 'Hs', 'Ak'], NORMAL = ['7h', '8s', '9k', '10r'];
{ // 1. honest claim, nobody checks -> swaps four cards, no penalty
  const g = wasGame(CLEAN); assert(E.canWas(g, 'a'));
  assert(E.apply(g, 'a', { type: 'was' }).ok);
  assert(!E.apply(g, 'a', { type: 'play', card: 'Bh' }).ok, 'no playing while others decide');
  assert(!E.canToep(g, 'a'), 'no toep during claim');
  assert(E.apply(g, 'b', { type: 'wascheck', check: false }).ok); assert(g.round.was, 'still waiting for c');
  assert(E.apply(g, 'c', { type: 'wascheck', check: false }).ok);
  assert(!g.round.was && g.round.hands.a.join() !== CLEAN.join() && g.round.hands.a.length === 4, 'hand swapped');
  assert.strictEqual(g.players.a.score + g.players.b.score + g.players.c.score, 0);
}
{ // 2. honest claim, b checks -> b is wrong and gets 2 points; a swaps
  const g = wasGame(CLEAN); E.apply(g, 'a', { type: 'was' });
  assert(E.apply(g, 'b', { type: 'wascheck', check: true }).ok);
  assert.strictEqual(g.players.b.score, 2); assert.strictEqual(g.players.a.score, 0);
  assert(g.round.wasResult.ok && g.round.wasResult.victim === 'b' && g.round.wasResult.hand.join() === CLEAN.join());
  assert(!g.round.was && g.round.hands.a.join() !== CLEAN.join(), 'honest claimer swaps');
  assert(E.apply(g, 'a', { type: 'play', card: g.round.hands.a[0] }).ok, 'play continues');
  assert.strictEqual(g.round.wasResult, null, 'result cleared after the next card');
}
{ // 3. bluff, c checks -> a gets 2 points and keeps the hand
  const g = wasGame(NORMAL); assert(E.canWas(g, 'a'), 'bluffing is allowed');
  E.apply(g, 'a', { type: 'was' }); assert(E.apply(g, 'c', { type: 'wascheck', check: true }).ok);
  assert.strictEqual(g.players.a.score, 2); assert.strictEqual(g.players.c.score, 0);
  assert(!g.round.wasResult.ok && g.round.hands.a.join() === NORMAL.join(), 'bluffer keeps cards');
}
{ // 4. bluff nobody checks -> the bluff works: swap, no penalty
  const g = wasGame(NORMAL); E.apply(g, 'a', { type: 'was' });
  E.apply(g, 'b', { type: 'wascheck', check: false }); E.apply(g, 'c', { type: 'wascheck', check: false });
  assert.strictEqual(g.players.a.score, 0); assert(g.round.hands.a.join() !== NORMAL.join());
}
{ // 5. only once per round, only before the first card, not twice at once; public state hides the hand
  const g = wasGame(NORMAL); E.apply(g, 'a', { type: 'was' });
  assert(!E.apply(g, 'b', { type: 'was' }).ok, 'second claim while open'); 
  E.apply(g, 'b', { type: 'wascheck', check: false }); E.apply(g, 'c', { type: 'wascheck', check: false });
  assert(!E.canWas(g, 'a'), 'only once per round');
  assert(!/"hands"/.test(JSON.stringify(E.publicState(g))));
  const g2 = wasGame(NORMAL); assert(E.apply(g2, 'a', { type: 'play', card: '7h' }).ok); assert(!E.canWas(g2, 'b'), 'not after a card was played');
  assert(!E.apply(g2, 'b', { type: 'wascheck', check: true }).ok, 'nothing to check');
}
{ // 6. a penalty that reaches the limit puts the player out and the round ends cleanly
  const g = E.newGame({ seats: [{ id: 'a', name: 'A' }, { id: 'b', name: 'B' }], rules: { wittewas: true, wasStraf: 2 }, limit: 10, dealer: 0 });
  g.round.hands.a = NORMAL.slice(); g.players.a.score = 9; g.round.turn = 'a';
  E.apply(g, 'a', { type: 'was' }); assert(E.apply(g, 'b', { type: 'wascheck', check: true }).ok);
  assert(g.players.a.out && g.phase === 'done' && g.winner === 'b', 'bluffer who hits the limit loses');
}
// ---- highest stake ----
{ const g = E.newGame({ seats: [{ id: 'a', name: 'A' }, { id: 'b', name: 'B' }, { id: 'c', name: 'C' }], rules: { maxInzet: 3, dubbelToep: true }, dealer: 0 });
  g.round.turn = 'a';
  assert(E.canToep(g, 'a')); E.apply(g, 'a', { type: 'toep' }); E.apply(g, 'b', { type: 'answer', mee: true }); E.apply(g, 'c', { type: 'answer', mee: true });
  assert.strictEqual(g.round.stake, 2); assert(E.canToep(g, 'a'), 'second toep allowed (dubbelToep, stake 2 < 3)');
  E.apply(g, 'a', { type: 'toep' }); E.apply(g, 'b', { type: 'answer', mee: true }); E.apply(g, 'c', { type: 'answer', mee: true });
  assert.strictEqual(g.round.stake, 3); assert(!E.canToep(g, 'a'), 'no toep above the highest stake'); assert(!E.apply(g, 'a', { type: 'toep' }).ok);
  const g0 = E.newGame({ seats: [{ id: 'a', name: 'A' }, { id: 'b', name: 'B' }], rules: { maxInzet: 0, dubbelToep: true }, dealer: 0 }); g0.round.turn = 'a';
  for (let i = 0; i < 8; i++) { assert(E.apply(g0, 'a', { type: 'toep' }).ok); E.apply(g0, 'b', { type: 'answer', mee: true }); }
  assert.strictEqual(g0.round.stake, 9, 'no limit when maxInzet is 0'); }
// specific: witte was must be impossible with 8 players (no stock)
const G8 = E.newGame({ seats: Array.from({ length: 8 }, (_, i) => ({ id: 'p' + i, name: 'P' + i })), rules: { wittewas: true } });
G8.round.hands.p1 = ['Bh', 'Vh', 'Hh', 'Ah'];
assert(!E.canWas(G8, 'p1'), 'witte was with empty stock');
// follow suit example
const G2 = E.newGame({ seats: [{ id: 'a', name: 'A' }, { id: 'b', name: 'B' }] });
G2.round.turn = 'a'; G2.round.hands.a = ['7h', 'Bs', 'Vk', '9r']; G2.round.hands.b = ['10h', 'Bh', 'Ar', '8s'];
assert(E.apply(G2, 'a', { type: 'play', card: '7h' }).ok);
assert.deepStrictEqual(E.legalCards(G2, 'b').sort(), ['10h', 'Bh'].sort());
assert(E.apply(G2, 'b', { type: 'play', card: '10h' }).ok);
assert.strictEqual(G2.round.lastTrick.winner, 'b', '10 beats 7');
assert.strictEqual(G2.round.turn, 'b', 'trick winner leads');
console.log('OK', stats, (Date.now() - t0) + 'ms');

// A game saved before 'maxInzet' existed must not allow unlimited toepen.
(function () {
  const E = require('../engine.js');
  const G = E.newGame({ seats: [{ id: 'a', name: 'A' }, { id: 'b', name: 'B' }], limit: 15, rules: { dubbelToep: true }, dealer: 0 });
  delete G.rules.maxInzet;
  G.round.stake = 4;
  if (E.canToep(G, G.round.turn)) { console.error('FAIL: old game allows toep above 4'); process.exit(1); }
  G.round.stake = 3;
  if (!E.canToep(G, G.round.turn)) { console.error('FAIL: toep at 3 should be allowed'); process.exit(1); }
})();
