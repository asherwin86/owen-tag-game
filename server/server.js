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
    players: [...room.players.values()].map(p => ({ id: p.id, name: p.name, score: Math.floor(p.score), wins: p.wins, color: p.color, vote: p.vote })),
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
  const room = { code, isPublic: !!isPublic, hostId: isPublic ? 0 : host.id, phase: 'lobby', map: 0, itId: 0, lastTick: Date.now(), players: new Map(), endsAt: 0, nextStart: 0, timer: null, tick: null };
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
  if (room.players.size === 0) {
    clearInterval(room.tick);
    clearTimeout(room.timer);
    rooms.delete(room.code);
    return;
  }
  if (!room.isPublic && room.hostId === p.id) room.hostId = room.players.keys().next().value; // host leaves -> next player hosts
  if (room.phase === 'playing' && room.itId === p.id) pickNewIt(room);
  broadcast(room, roomInfo(room));
  if (room.phase === 'playing' && room.players.size < 2) endRound(room);
}

function pickNewIt(room) {
  const ids = [...room.players.keys()];
  room.itId = ids.length ? ids[Math.floor(Math.random() * ids.length)] : 0;
  const it = room.players.get(room.itId);
  if (it) it.stunUntil = Date.now() + 1000;
}

function startRound(room) {
  room.map = pickMap(room);
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
  broadcast(room, roomInfo(room));
}

function tickRoom(room) {
  const players = [...room.players.values()].map(p => ({ id: p.id, x: p.x, z: p.z, ry: p.ry }));
  const msg = { t: 'snap', players, it: room.itId || 0 };
  const nowT = Date.now();
  const dt = Math.min(0.5, (nowT - room.lastTick) / 1000);
  room.lastTick = nowT;
  if (room.phase === 'playing') {
    msg.left = Math.max(0, (room.endsAt - nowT) / 1000);
    // Points are seconds spent NOT being "it".
    for (const p of room.players.values()) if (p.id !== room.itId) p.score += dt;
  }
  if (room.isPublic && room.phase === 'lobby') {
    if (room.players.size >= 2) {
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

  room.itId = best.id;
  best.stunUntil = now + STUN_MS;
  best.noTagId = p.id; best.noTagUntil = now + NO_TAG_BACK_MS;
  broadcast(room, { t: 'tagged', by: p.id, target: best.id, it: best.id, byName: p.name, targetName: best.name, stunMs: STUN_MS, scores: scoresOf(room) });
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
        if (room.players.size < 2) { send(ws, { t: 'err', msg: 'Need at least 2 players to start.' }); return; }
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
