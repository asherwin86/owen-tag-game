// Tag Game multiplayer server.
// Rooms with short codes, clients report their own position, the server decides
// every tag (range + facing check, first press wins) and keeps the scores.
const http = require('http');
const { WebSocketServer } = require('ws');

const PORT = process.env.PORT || 8080;
const TICK_HZ = 15;
const FAST = !!process.env.TAG_FAST;   // tests only: shorter waits
// Maps: 0 = Arena (the original small one), 1 = School (~150 m), 2 = City (~250 m).
const MAP_COUNT = 3;
const MAP_ROUND_SECONDS = [60, 90, 120];   // bigger map, longer round
const MAP_SPAWN_RADIUS = [9, 20, 24];      // players start on a ring around the centre plaza
const MAP_LIMIT = [40, 90, 140];           // clamp for reported positions (half size + a margin)
const NO_TAG_BACK_MS = 3500;               // the player who just handed over "it" can't be tagged straight back by them
const MAX_PLAYERS = 8;
const TAG_RANGE = 3.0;          // metres, a little generous for latency
const TAG_MIN_DOT = 0.5;        // target must be within ~60 degrees of where the tagger looks
const TAG_COOLDOWN_MS = 600;    // tagger can't spam
const STUN_MS = 1500;           // a tagged player can't tag back for a moment
const CODE_LETTERS = 'ABCDEFGHJKLMNPQRSTUVWXYZ'; // no I or O

const PUBLIC_CODE = 'PUBLIC';
const PUBLIC_COUNTDOWN_MS = FAST ? 1500 : 15000;   // public game: wait this long once 2+ players are in
const PUBLIC_BREAK_MS = FAST ? 300 : 8000;       // public game: pause between rounds
const rooms = new Map(); // code -> room
const practice = new Map(); // player id -> live Practice session someone is streaming for spectators
const watchers = new Set(); // connections currently spectating something
const VIEW_HZ = 10;
const MAX_PRACTICE_ENTITIES = 24;
let nextId = 1;

const server = http.createServer((req, res) => {
  res.writeHead(200, { 'Content-Type': 'text/plain' });
  res.end('Tag Game server is running. Rooms: ' + rooms.size + '\n');
});
const wss = new WebSocketServer({ server });

function makeCode() {
  for (let attempt = 0; attempt < 100; attempt++) {
    let code = '';
    for (let i = 0; i < 4; i++) code += CODE_LETTERS[Math.floor(Math.random() * CODE_LETTERS.length)];
    if (!rooms.has(code)) return code;
  }
  return null;
}

function cleanName(name) {
  const n = String(name || '').replace(/[^\w \-]/g, '').trim().slice(0, 12);
  return n || 'Player' + Math.floor(100 + Math.random() * 900);
}

function send(ws, obj) {
  if (ws.readyState === 1) ws.send(JSON.stringify(obj));
}

function broadcast(room, obj) {
  const data = JSON.stringify(obj);
  for (const p of room.players.values()) if (p.ws.readyState === 1) p.ws.send(data);
}

function roomInfo(room) {
  return {
    t: 'room',
    code: room.code,
    hostId: room.hostId,
    phase: room.phase,
    isPublic: room.isPublic,
    players: [...room.players.values()].map(p => ({ id: p.id, name: p.name, score: Math.floor(p.score), wins: p.wins, color: p.color, vote: p.vote, bot: !!p.bot })),
    bots: room.bots || 'auto',
    map: room.map,
    it: room.itId || 0,
  };
}

function scoresOf(room) {
  return [...room.players.values()].map(p => ({ id: p.id, name: p.name, score: Math.floor(p.score) }));
}

function createRoom(host, isPublic) {
  const code = isPublic ? PUBLIC_CODE : makeCode();
  if (!code) return null;
  const room = { code, isPublic: !!isPublic, hostId: isPublic ? 0 : host.id, phase: 'lobby', map: 0, itId: 0, bots: 'auto', lastTick: Date.now(), players: new Map(), endsAt: 0, nextStart: 0, timer: null, tick: null };
  rooms.set(code, room);
  room.tick = setInterval(() => tickRoom(room), 1000 / TICK_HZ);
  return room;
}

function addPlayer(room, p) {
  room.players.set(p.id, p);
  p.room = room;
  p.score = 0;
  p.wins = 0;
  p.vote = -1;
  p.noTagId = 0; p.noTagUntil = 0;
  const used = new Set([...room.players.values()].filter(o => o !== p).map(o => o.color));
  p.color = 0; while (used.has(p.color)) p.color++;
  p.stunUntil = 0;
  p.lastTagAt = 0;
  if (p.spectate === undefined) p.spectate = true;   // others may watch you (you can switch this off)
  p.x = 0; p.z = 0; p.ry = 0;
}

// Count the lobby votes; most votes wins, ties are broken at random, no votes = random map.
function pickMap(room) {
  const counts = new Array(MAP_COUNT).fill(0);
  for (const p of room.players.values()) if (p.vote >= 0 && p.vote < MAP_COUNT) counts[p.vote]++;
  const top = Math.max(...counts);
  const best = [];
  counts.forEach((c, i) => { if (c === top) best.push(i); });
  return best[Math.floor(Math.random() * best.length)];
}

function spawnFor(room, i, n) {
  const r = MAP_SPAWN_RADIUS[room.map];
  const angle = (i / Math.max(1, n)) * Math.PI * 2;
  return { x: Math.cos(angle) * r, z: Math.sin(angle) * r };
}

function removePlayer(p) {
  const room = p.room;
  if (!room) return;
  room.players.delete(p.id);
  p.room = null;
  if (humanCount(room) === 0) {
    clearInterval(room.tick);
    clearTimeout(room.timer);
    rooms.delete(room.code);
    return;
  }
  if (!room.isPublic && room.hostId === p.id) room.hostId = [...room.players.values()].find(o => !o.bot).id; // host leaves -> next player hosts
  if (room.phase === 'playing' && room.itId === p.id) pickNewIt(room);
  broadcast(room, roomInfo(room));
  if (room.phase === 'playing' && room.players.size < 2) endRound(room);
}

function humanCount(room) { let n = 0; for (const p of room.players.values()) if (!p.bot) n++; return n; }

function pickNewIt(room) {
  const ids = [...room.players.keys()];
  room.itId = ids.length ? ids[Math.floor(Math.random() * ids.length)] : 0;
  const it = room.players.get(room.itId);
  if (it) it.stunUntil = Date.now() + 1000;
}

function startRound(room) {
  room.map = pickMap(room);
  addBots(room);
  if (room.map > 0 && !grids.has(room.map)) {
    const h = [...room.players.values()].find(o => !o.bot);
    if (h) send(h.ws, { t: 'needgrid', map: room.map });
  }
  const seconds = MAP_ROUND_SECONDS[room.map];
  room.phase = 'playing';
  room.roundSeconds = seconds;
  room.endsAt = Date.now() + seconds * 1000;
  room.lastTick = Date.now();
  let i = 0;
  const n = room.players.size;
  for (const p of room.players.values()) {
    p.score = 0;
    p.stunUntil = 0;
    p.noTagId = 0; p.noTagUntil = 0;
    p.spawn = spawnFor(room, i, n);
    p.x = p.spawn.x; p.z = p.spawn.z;
    p.vote = -1;
    i++;
  }
  pickNewIt(room);
  room.nextStart = 0;
  broadcast(room, { t: 'start', map: room.map, it: room.itId, duration: seconds, left: seconds, spawns: [...room.players.values()].map(p => ({ id: p.id, x: p.spawn.x, z: p.spawn.z })) });
  broadcast(room, roomInfo(room));
  room.timer = setTimeout(() => endRound(room), seconds * 1000);
}

function endRound(room) {
  if (room.phase !== 'playing') return;
  clearTimeout(room.timer);
  room.phase = 'lobby';
  if (room.isPublic) room.nextStart = Date.now() + PUBLIC_BREAK_MS;
  const scores = scoresOf(room).sort((a, b) => b.score - a.score);
  room.itId = 0;
  const top = scores.length ? scores[0].score : 0;
  const winnerList = scores.filter(s => s.score === top && top > 0);
  for (const w of winnerList) { const wp = room.players.get(w.id); if (wp) wp.wins++; }
  const winners = winnerList.map(s => s.name);
  broadcast(room, { t: 'end', scores, winners });
  removeBots(room);
  broadcast(room, roomInfo(room));
}

function tickRoom(room) {
  const players = [...room.players.values()].map(p => ({ id: p.id, x: p.x, z: p.z, ry: p.ry }));
  const msg = { t: 'snap', players, it: room.itId || 0 };
  const nowT = Date.now();
  const dt = Math.min(0.5, (nowT - room.lastTick) / 1000);
  room.lastTick = nowT;
  if (room.phase === 'playing') {
    updateBots(room, dt);
    msg.players = [...room.players.values()].map(p => ({ id: p.id, x: +p.x.toFixed(2), z: +p.z.toFixed(2), ry: +(p.ry || 0).toFixed(3) }));
    msg.left = Math.max(0, (room.endsAt - nowT) / 1000);
    // Points are seconds spent NOT being "it".
    for (const p of room.players.values()) if (p.id !== room.itId) p.score += dt;
  }
  if (room.isPublic && room.phase === 'lobby') {
    if (humanCount(room) >= (room.bots === 'off' ? 2 : 1)) {
      if (!room.nextStart) room.nextStart = Date.now() + PUBLIC_COUNTDOWN_MS;
      msg.next = Math.max(0, (room.nextStart - Date.now()) / 1000);
      if (Date.now() >= room.nextStart) { startRound(room); return; }
    } else {
      room.nextStart = 0;
    }
  }
  broadcast(room, msg);
}

// The heart of the game: only the "it" player can tag. Whoever they tag becomes the new "it".
function handleTag(p) {
  const room = p.room;
  if (!room || room.phase !== 'playing') return;
  if (room.itId !== p.id) return;                // only the tagger can tag
  const now = Date.now();
  if (now < p.stunUntil) return;                 // a freshly tagged player is frozen for a moment
  if (now - p.lastTagAt < TAG_COOLDOWN_MS) return;
  p.lastTagAt = now;

  const fx = Math.sin(p.ry), fz = Math.cos(p.ry); // facing direction (Unity yaw, radians)
  let best = null, bestDist = Infinity;
  for (const o of room.players.values()) {
    if (o.id === p.id) continue;
    if (p.noTagId === o.id && now < p.noTagUntil) continue; // can't hand it straight back
    const dx = o.x - p.x, dz = o.z - p.z;
    const dist = Math.hypot(dx, dz);
    if (dist > TAG_RANGE || dist < 0.0001) continue;
    const dot = (dx * fx + dz * fz) / dist;
    if (dot < TAG_MIN_DOT) continue;
    if (dist < bestDist) { best = o; bestDist = dist; }
  }
  if (!best) { send(p.ws, { t: 'miss' }); return; }
  doTag(room, p, best, now);
}

function doTag(room, p, best, now) {
  room.itId = best.id;
  best.stunUntil = now + STUN_MS;
  best.noTagId = p.id; best.noTagUntil = now + NO_TAG_BACK_MS;
  if (best.bot) best.path = null;
  broadcast(room, { t: 'tagged', by: p.id, target: best.id, it: best.id, byName: p.name, targetName: best.name, stunMs: STUN_MS, scores: scoresOf(room) });
}

// ---------------------------------------------------------------- bots
// Bots: off, auto (only when there aren't many people: fill up to 4 players, the default) or on (fill up to 6, always at least 2).
// They play by the same rules: chase when IT, run from IT otherwise. On the big maps they find their way around
// walls with a walk grid that the first player's game sends up once per map (same on every machine).
const BOT_NAMES = ['Ava', 'Ben', 'Cy', 'Dot', 'Eli', 'Fin', 'Gus', 'Ivy'];
const BOT_AUTO_FILL = 4;
const BOT_LIMIT = [14, 72, 120];
const BOT_MODES = ['off', 'auto', 'on'];
const BOT_ON_FILL = 6;     // 'on': fill the room up to this many, always at least 2 bots
const NOOP_WS = { readyState: 3, send() {}, ping() {}, terminate() {} };
const grids = new Map(); // map id -> { n, half, cell, b: Uint8Array }

function botWanted(room) {
  const humans = humanCount(room);
  const mode = room.bots || 'auto';
  let want = mode === 'off' ? 0 : mode === 'auto' ? Math.max(0, BOT_AUTO_FILL - humans) : Math.max(2, BOT_ON_FILL - humans);
  return Math.max(0, Math.min(want, MAX_PLAYERS - humans));
}

function addBots(room) {
  removeBots(room);
  const n = botWanted(room);
  for (let i = 0; i < n; i++) {
    const b = { id: nextId++, ws: NOOP_WS, name: 'Bot ' + BOT_NAMES[i % BOT_NAMES.length], room: null, bot: true, spectate: true };
    addPlayer(room, b);
    b.speed = 5.3 + Math.random() * 0.9;
    b.path = null; b.pi = 0; b.pathAt = 0; b.wx = 0; b.wz = 0; b.wt = 0; b.dashUntil = 0; b.dashReady = 0;
  }
}

function removeBots(room) { for (const p of [...room.players.values()]) if (p.bot) { room.players.delete(p.id); p.room = null; } }

function gBlocked(g, x, z) {
  const cx = Math.floor((x + g.half) / g.cell), cz = Math.floor((z + g.half) / g.cell);
  if (cx < 0 || cz < 0 || cx >= g.n || cz >= g.n) return true;
  return g.b[cz * g.n + cx] === 1;
}

function gLos(g, ax, az, bx, bz) {
  const len = Math.hypot(bx - ax, bz - az), steps = Math.max(1, Math.ceil(len / (g.cell * 0.5)));
  for (let i = 1; i < steps; i++) { const t = i / steps; if (gBlocked(g, ax + (bx - ax) * t, az + (bz - az) * t)) return false; }
  return true;
}

function gPath(g, ax, az, bx, bz) {
  const n = g.n, cell = g.cell, half = g.half;
  const toC = w => Math.max(0, Math.min(n - 1, Math.floor((w + half) / cell)));
  const s = toC(az) * n + toC(ax), goal = toC(bz) * n + toC(bx);
  const gScore = new Float32Array(n * n).fill(Infinity), parent = new Int32Array(n * n).fill(-1), closed = new Uint8Array(n * n);
  const heap = []; // [f, idx]
  const push = (f, i) => { heap.push([f, i]); let k = heap.length - 1; while (k > 0) { const p = (k - 1) >> 1; if (heap[p][0] <= heap[k][0]) break; [heap[p], heap[k]] = [heap[k], heap[p]]; k = p; } };
  const pop = () => { const top = heap[0], last = heap.pop(); if (heap.length) { heap[0] = last; let k = 0; for (;;) { const l = 2 * k + 1, r = l + 1; let m = k; if (l < heap.length && heap[l][0] < heap[m][0]) m = l; if (r < heap.length && heap[r][0] < heap[m][0]) m = r; if (m === k) break; [heap[m], heap[k]] = [heap[k], heap[m]]; k = m; } } return top; };
  const h = i => { const dx = Math.abs((i % n) - (goal % n)), dz = Math.abs(Math.floor(i / n) - Math.floor(goal / n)); return Math.max(dx, dz) + 0.414 * Math.min(dx, dz); };
  gScore[s] = 0; push(h(s), s);
  let iter = 0;
  while (heap.length && iter++ < 30000) {
    const [, cur] = pop();
    if (cur === goal) break;
    if (closed[cur]) continue;
    closed[cur] = 1;
    const cx = cur % n, cz = Math.floor(cur / n);
    for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) {
      if (!dx && !dz) continue;
      const nx = cx + dx, nz = cz + dz;
      if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
      const ni = nz * n + nx;
      if (g.b[ni] && ni !== goal) continue;
      if (dx && dz && (g.b[cz * n + nx] || g.b[nz * n + cx])) continue; // no corner cutting
      const ng = gScore[cur] + (dx && dz ? 1.414 : 1);
      if (ng < gScore[ni]) { gScore[ni] = ng; parent[ni] = cur; push(ng + h(ni), ni); }
    }
  }
  if (parent[goal] === -1 && goal !== s) return null;
  const pts = [];
  for (let i = goal; i !== -1 && i !== s; i = parent[i]) pts.push([-half + (i % n + 0.5) * cell, -half + (Math.floor(i / n) + 0.5) * cell]);
  pts.reverse();
  return pts.length ? pts : null;
}

function steerTo(b, tx, tz, g, now) {
  if (!g || gLos(g, b.x, b.z, tx, tz)) { b.path = null; return [tx, tz]; }
  if (!b.path || now >= b.pathAt) { b.path = gPath(g, b.x, b.z, tx, tz); b.pi = 0; b.pathAt = now + 600 + Math.random() * 300; }
  if (!b.path) return [tx, tz];
  while (b.pi < b.path.length - 1 && Math.hypot(b.path[b.pi][0] - b.x, b.path[b.pi][1] - b.z) < 0.9) b.pi++;
  return b.path[b.pi];
}

function freeAt(g, lim, x, z) { return Math.abs(x) < lim && Math.abs(z) < lim && (!g || !gBlocked(g, x, z)); }

function updateBots(room, dt) {
  const now = Date.now();
  const g = room.map > 0 ? grids.get(room.map) : null;
  const lim = BOT_LIMIT[room.map] || 14;
  const all = [...room.players.values()];
  const it = room.players.get(room.itId);
  for (const b of all) {
    if (!b.bot || now < b.stunUntil) continue;
    let target, speed;
    if (b.id === room.itId) {
      let best = null, bd = Infinity;
      for (const o of all) {
        if (o === b || (b.noTagId === o.id && now < b.noTagUntil)) continue;
        const d = Math.hypot(o.x - b.x, o.z - b.z);
        if (d < bd) { bd = d; best = o; }
      }
      if (!best) continue;
      if (bd < 1.3) { doTag(room, b, best, now); continue; }
      target = steerTo(b, best.x, best.z, g, now);
      speed = b.speed * 1.08;
      if (bd < 5 && now > b.dashReady && Math.random() < 0.04) { b.dashUntil = now + 220; b.dashReady = now + 3000 + Math.random() * 3000; }
    } else if (it && Math.hypot(b.x - it.x, b.z - it.z) < 18) {
      const ax = b.x - it.x, az = b.z - it.z, l = Math.hypot(ax, az) || 1;
      let dir = [-az / l, ax / l];
      for (const deg of [0, 40, -40, 80, -80, 120, -120, 160]) {
        const r = deg * Math.PI / 180, dx = (ax / l) * Math.cos(r) - (az / l) * Math.sin(r), dz = (ax / l) * Math.sin(r) + (az / l) * Math.cos(r);
        if (freeAt(g, lim - 1, b.x + dx * 1.5, b.z + dz * 1.5) && freeAt(g, lim - 1, b.x + dx * 3, b.z + dz * 3)) { dir = [dx, dz]; break; }
      }
      target = [b.x + dir[0] * 3, b.z + dir[1] * 3];
      speed = b.speed * 0.92;
      if (l < 4 && now > b.dashReady && Math.random() < 0.04) { b.dashUntil = now + 220; b.dashReady = now + 3000 + Math.random() * 3000; }
    } else {
      if (now > b.wt || Math.hypot(b.wx - b.x, b.wz - b.z) < 1.5) {
        for (let k = 0; k < 20; k++) { const x = (Math.random() * 2 - 1) * (lim - 2), z = (Math.random() * 2 - 1) * (lim - 2); if (freeAt(g, lim, x, z)) { b.wx = x; b.wz = z; break; } }
        b.wt = now + 3000 + Math.random() * 3000;
      }
      target = steerTo(b, b.wx, b.wz, g, now);
      speed = 2.6;
    }
    if (now < b.dashUntil) speed *= 2.2;
    let dx = target[0] - b.x, dz = target[1] - b.z;
    const l = Math.hypot(dx, dz);
    if (l < 0.01) continue;
    dx /= l; dz /= l;
    const step = Math.min(l, speed * dt);
    let nx = b.x + dx * step, nz = b.z + dz * step;
    if (g && gBlocked(g, nx, nz)) {
      if (!gBlocked(g, nx, b.z)) nz = b.z; else if (!gBlocked(g, b.x, nz)) nx = b.x; else { nx = b.x; nz = b.z; b.path = null; }
    }
    b.x = Math.max(-lim, Math.min(lim, nx)); b.z = Math.max(-lim, Math.min(lim, nz)); b.ry = Math.atan2(dx, dz);
  }
}

// ---------------------------------------------------------------- spectating
// Watchers connect with the same WebSocket and ask for a list of live games, then pick one.
// Public room players and Practice runs are listed while "allow spectators" is on (the default).
// A private room is never listed; you need its code to watch it.
function liveList() {
  const items = [];
  for (const room of rooms.values()) {
    if (!room.isPublic) continue;
    const shown = [...room.players.values()].filter(p => p.spectate !== false);
    if (!shown.length) continue;
    items.push({ key: 'room:' + room.code, kind: 'room', label: 'Public game', map: room.map, players: shown.length, phase: room.phase });
  }
  for (const [id, s] of practice) {
    items.push({ key: 'practice:' + id, kind: 'practice', label: s.name + "'s Practice", map: s.map, players: s.e.length, phase: 'playing' });
  }
  return items;
}

function viewOf(key) {
  if (key.startsWith('room:')) {
    const room = rooms.get(key.slice(5));
    if (!room) return null;
    const all = [...room.players.values()];
    const shown = all.filter(p => p.spectate !== false);
    return {
      t: 'view', key, kind: 'room', label: room.isPublic ? 'Public game' : 'Room ' + room.code,
      map: room.map, phase: room.phase, it: room.itId || 0, left: room.phase === 'playing' ? Math.max(0, (room.endsAt - Date.now()) / 1000) : 0,
      hidden: all.length - shown.length,
      players: shown.map(p => ({ id: p.id, n: p.name, x: +p.x.toFixed(1), z: +p.z.toFixed(1), ry: +(p.ry || 0).toFixed(2), score: Math.floor(p.score), c: p.color })),
    };
  }
  if (key.startsWith('practice:')) {
    const sess = practice.get(Number(key.slice(9)));
    if (!sess) return null;
    return { t: 'view', key, kind: 'practice', label: sess.name + "'s Practice", map: sess.map, phase: 'playing', it: sess.it, left: sess.left, hidden: 0, players: sess.e };
  }
  return null;
}

setInterval(() => {
  const cache = new Map();
  for (const w of watchers) {
    if (!w.watch || w.ws.readyState !== 1) continue;
    if (!cache.has(w.watch)) cache.set(w.watch, viewOf(w.watch));
    const v = cache.get(w.watch);
    if (v) send(w.ws, v); else { send(w.ws, { t: 'viewend', key: w.watch }); w.watch = null; }
  }
}, 1000 / VIEW_HZ);

function num(v, lim) { return Number.isFinite(v) ? Math.max(-lim, Math.min(lim, v)) : 0; }
function endPractice(p) { practice.delete(p.id); }

wss.on('connection', (ws) => {
  const p = { id: nextId++, ws, name: '', room: null };
  ws.isAlive = true;
  ws.on('pong', () => { ws.isAlive = true; });

  ws.on('message', (raw) => {
    let m;
    try { m = JSON.parse(raw.toString()); } catch { return; }
    if (!m || typeof m.t !== 'string') return;

    switch (m.t) {
      case 'create': {
        if (p.room) return;
        p.name = cleanName(m.name);
        const room = createRoom(p);
        if (!room) { send(ws, { t: 'err', msg: 'Could not create a room, try again.' }); return; }
        addPlayer(room, p);
        send(ws, { t: 'joined', id: p.id });
        broadcast(room, roomInfo(room));
        break;
      }
      case 'joinpublic': {
        if (p.room) return;
        let room = rooms.get(PUBLIC_CODE) || createRoom(p, true);
        if (room.players.size >= MAX_PLAYERS) { send(ws, { t: 'err', msg: 'The public game is full, try again soon.' }); return; }
        p.name = cleanName(m.name);
        addPlayer(room, p);
        send(ws, { t: 'joined', id: p.id });
        if (room.phase === 'playing') {
          const a = Math.random() * Math.PI * 2;
          const r = MAP_SPAWN_RADIUS[room.map];
          p.spawn = { x: Math.cos(a) * r, z: Math.sin(a) * r };
          p.x = p.spawn.x; p.z = p.spawn.z;
          send(ws, { t: 'start', map: room.map, it: room.itId, duration: room.roundSeconds, left: Math.max(0, (room.endsAt - Date.now()) / 1000), spawns: [{ id: p.id, x: p.spawn.x, z: p.spawn.z }] });
        }
        broadcast(room, roomInfo(room));
        break;
      }
      case 'join': {
        if (p.room) return;
        const code = String(m.code || '').toUpperCase().trim();
        if (code === PUBLIC_CODE) { send(ws, { t: 'err', msg: 'Use the public game button for that one.' }); return; }
        const room = rooms.get(code);
        if (!room) { send(ws, { t: 'err', msg: 'No game with that code.' }); return; }
        if (room.players.size >= MAX_PLAYERS) { send(ws, { t: 'err', msg: 'That game is full.' }); return; }
        if (room.phase === 'playing' && !room.isPublic) { send(ws, { t: 'err', msg: 'That game already started, try again soon.' }); return; }
        p.name = cleanName(m.name);
        addPlayer(room, p);
        send(ws, { t: 'joined', id: p.id });
        broadcast(room, roomInfo(room));
        break;
      }
      case 'state': {
        if (!p.room) return;
        if (Number.isFinite(m.x) && Number.isFinite(m.z) && Number.isFinite(m.ry)) {
          const lim = MAP_LIMIT[p.room.map] || 200;
          p.x = Math.max(-lim, Math.min(lim, m.x));
          p.z = Math.max(-lim, Math.min(lim, m.z));
          p.ry = m.ry;
        }
        break;
      }
      case 'spectate': { p.spectate = m.on !== false; break; }
      case 'pstate': {
        // A Practice game streams itself here so people can watch. Same checks as everything else: clamp and cap.
        if (p.room || m.on === false) { endPractice(p); break; }
        if (!Array.isArray(m.e)) break;
        const map = Number.isInteger(m.map) && m.map >= 0 && m.map < MAP_COUNT ? m.map : 0;
        const lim = MAP_LIMIT[map];
        const e = m.e.slice(0, MAX_PRACTICE_ENTITIES).map((q, i) => ({ id: i, n: cleanName(q.n), x: +num(q.x, lim).toFixed(1), z: +num(q.z, lim).toFixed(1), ry: +num(q.ry, 7).toFixed(2), score: Math.max(0, Math.min(9999, Math.floor(Number(q.s) || 0))), c: q.p ? 0 : 1 }));
        if (!p.name) p.name = cleanName(m.name);
        practice.set(p.id, { name: p.name || cleanName(m.name), map, it: Math.max(-1, Math.min(MAX_PRACTICE_ENTITIES, Math.floor(Number(m.it)))), left: Math.max(0, Math.min(600, Number(m.left) || 0)), e, at: Date.now() });
        break;
      }
      case 'pend': endPractice(p); break;
      case 'list': watchers.add(p); send(ws, { t: 'list', items: liveList() }); break;
      case 'watch': {
        const key = m.code ? 'room:' + String(m.code).toUpperCase().trim() : String(m.key || '');
        if (!viewOf(key)) { send(ws, { t: 'err', msg: 'Nothing to watch there.' }); break; }
        watchers.add(p); p.watch = key;
        break;
      }
      case 'unwatch': p.watch = null; break;
      case 'tag': handleTag(p); break;
      case 'bots': {
        const room = p.room;
        if (!room || room.isPublic || room.hostId !== p.id || room.phase !== 'lobby') return;
        const mode = String(m.mode || '');
        if (!BOT_MODES.includes(mode)) return;
        room.bots = mode;
        broadcast(room, roomInfo(room));
        break;
      }
      case 'grid': {
        // walk grid for a big map, sent once by a player's game (identical on every machine)
        const map = Number(m.map), n = Number(m.n), half = Number(m.half), cell = Number(m.cell);
        if (!(map >= 1 && map < MAP_COUNT) || grids.has(map)) break;
        if (!Number.isInteger(n) || n < 10 || n > 400 || !(half > 5 && half < 300) || !(cell > 0.2 && cell < 5)) break;
        let bytes;
        try { bytes = Buffer.from(String(m.data || ''), 'base64'); } catch { break; }
        if (bytes.length !== Math.ceil(n * n / 8)) break;
        const b = new Uint8Array(n * n);
        for (let i = 0; i < n * n; i++) b[i] = (bytes[i >> 3] >> (i & 7)) & 1;
        grids.set(map, { n, half, cell, b });
        break;
      }
      case 'vote': {
        const room = p.room;
        if (!room || room.phase !== 'lobby') return;
        const v = Number(m.map);
        p.vote = (Number.isInteger(v) && v >= 0 && v < MAP_COUNT) ? v : -1;
        broadcast(room, roomInfo(room));
        break;
      }
      case 'start': {
        const room = p.room;
        if (!room || room.isPublic || room.hostId !== p.id || room.phase !== 'lobby') return;
        if (room.players.size + botWanted(room) < 2) { send(ws, { t: 'err', msg: 'Need at least 2 players to start (or turn bots on).' }); return; }
        startRound(room);
        break;
      }
      case 'kick': {
        const room = p.room;
        if (!room || room.isPublic || room.hostId !== p.id) return;
        const target = room.players.get(Number(m.id));
        if (!target || target.id === p.id) return;
        send(target.ws, { t: 'kicked' });
        removePlayer(target);
        break;
      }
      case 'leave': removePlayer(p); break;
    }
  });

  ws.on('close', () => { removePlayer(p); endPractice(p); watchers.delete(p); });
  ws.on('error', () => {});
});

// Practice runs that stopped sending drop off the list.
setInterval(() => { const t = Date.now(); for (const [id, s] of practice) if (t - s.at > 6000) practice.delete(id); }, 2000);

// Drop dead connections (phones that went to sleep, etc.).
setInterval(() => {
  for (const ws of wss.clients) {
    if (!ws.isAlive) { ws.terminate(); continue; }
    ws.isAlive = false;
    ws.ping();
  }
}, 20000);

if (require.main === module) {
  server.listen(PORT, () => {
    console.log('Tag Game server listening on port ' + PORT);
    console.log('  On this computer, use the address:  ws://localhost:' + PORT);
    const nets = require('os').networkInterfaces();
    for (const name of Object.keys(nets)) for (const n of nets[name]) {
      if (n.family === 'IPv4' && !n.internal) console.log('  Friends on your Wi-Fi/network use:  ws://' + n.address + ':' + PORT);
    }
  });
}

module.exports = { server, wss, rooms };
