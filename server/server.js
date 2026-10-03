// Tag Game multiplayer server.
// Rooms with short codes, clients report their own position, the server decides
// every tag (range + facing check, first press wins) and keeps the scores.
const http = require('http');
const { WebSocketServer } = require('ws');

const PORT = process.env.PORT || 8080;
const TICK_HZ = 15;
const ROUND_SECONDS = 60;
const MAX_PLAYERS = 8;
const TAG_RANGE = 3.0;          // metres, a little generous for latency
const TAG_MIN_DOT = 0.5;        // target must be within ~60 degrees of where the tagger looks
const TAG_COOLDOWN_MS = 600;    // tagger can't spam
const STUN_MS = 1500;           // a tagged player can't tag back for a moment
const CODE_LETTERS = 'ABCDEFGHJKLMNPQRSTUVWXYZ'; // no I or O

const rooms = new Map(); // code -> room
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
    players: [...room.players.values()].map(p => ({ id: p.id, name: p.name, score: p.score })),
  };
}

function scoresOf(room) {
  return [...room.players.values()].map(p => ({ id: p.id, name: p.name, score: p.score }));
}

function createRoom(host) {
  const code = makeCode();
  if (!code) return null;
  const room = { code, hostId: host.id, phase: 'lobby', players: new Map(), endsAt: 0, timer: null, tick: null };
  rooms.set(code, room);
  room.tick = setInterval(() => tickRoom(room), 1000 / TICK_HZ);
  return room;
}

function addPlayer(room, p) {
  room.players.set(p.id, p);
  p.room = room;
  p.score = 0;
  p.stunUntil = 0;
  p.lastTagAt = 0;
  p.x = 0; p.z = 0; p.ry = 0;
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
  if (room.hostId === p.id) room.hostId = room.players.keys().next().value; // host leaves -> next player hosts
  broadcast(room, roomInfo(room));
  if (room.phase === 'playing' && room.players.size < 2) endRound(room);
}

function startRound(room) {
  room.phase = 'playing';
  room.endsAt = Date.now() + ROUND_SECONDS * 1000;
  let i = 0;
  const n = room.players.size;
  for (const p of room.players.values()) {
    p.score = 0;
    p.stunUntil = 0;
    const angle = (i / n) * Math.PI * 2;
    p.spawn = { x: Math.cos(angle) * 9, z: Math.sin(angle) * 9 };
    i++;
  }
  broadcast(room, { t: 'start', duration: ROUND_SECONDS, spawns: [...room.players.values()].map(p => ({ id: p.id, x: p.spawn.x, z: p.spawn.z })) });
  broadcast(room, roomInfo(room));
  room.timer = setTimeout(() => endRound(room), ROUND_SECONDS * 1000);
}

function endRound(room) {
  if (room.phase !== 'playing') return;
  clearTimeout(room.timer);
  room.phase = 'lobby';
  const scores = scoresOf(room).sort((a, b) => b.score - a.score);
  const top = scores.length ? scores[0].score : 0;
  const winners = scores.filter(s => s.score === top).map(s => s.name);
  broadcast(room, { t: 'end', scores, winners });
  broadcast(room, roomInfo(room));
}

function tickRoom(room) {
  const players = [...room.players.values()].map(p => ({ id: p.id, x: p.x, z: p.z, ry: p.ry }));
  const msg = { t: 'snap', players };
  if (room.phase === 'playing') msg.left = Math.max(0, (room.endsAt - Date.now()) / 1000);
  broadcast(room, msg);
}

// The heart of the game: who gets tagged when someone presses TAG.
function handleTag(p) {
  const room = p.room;
  if (!room || room.phase !== 'playing') return;
  const now = Date.now();
  if (now < p.stunUntil) return;                 // stunned players can't tag
  if (now - p.lastTagAt < TAG_COOLDOWN_MS) return;
  p.lastTagAt = now;

  const fx = Math.sin(p.ry), fz = Math.cos(p.ry); // facing direction (Unity yaw, radians)
  let best = null, bestDist = Infinity;
  for (const o of room.players.values()) {
    if (o.id === p.id) continue;
    if (now < o.stunUntil) continue;             // can't be tagged again while stunned
    const dx = o.x - p.x, dz = o.z - p.z;
    const dist = Math.hypot(dx, dz);
    if (dist > TAG_RANGE || dist < 0.0001) continue;
    const dot = (dx * fx + dz * fz) / dist;
    if (dot < TAG_MIN_DOT) continue;
    if (dist < bestDist) { best = o; bestDist = dist; }
  }
  if (!best) { send(p.ws, { t: 'miss' }); return; }

  best.stunUntil = now + STUN_MS;
  p.score += 1;
  broadcast(room, { t: 'tagged', by: p.id, target: best.id, byName: p.name, targetName: best.name, stunMs: STUN_MS, scores: scoresOf(room) });
}

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
      case 'join': {
        if (p.room) return;
        const code = String(m.code || '').toUpperCase().trim();
        const room = rooms.get(code);
        if (!room) { send(ws, { t: 'err', msg: 'No game with that code.' }); return; }
        if (room.players.size >= MAX_PLAYERS) { send(ws, { t: 'err', msg: 'That game is full.' }); return; }
        if (room.phase === 'playing') { send(ws, { t: 'err', msg: 'That game already started, try again soon.' }); return; }
        p.name = cleanName(m.name);
        addPlayer(room, p);
        send(ws, { t: 'joined', id: p.id });
        broadcast(room, roomInfo(room));
        break;
      }
      case 'state': {
        if (!p.room) return;
        if (Number.isFinite(m.x) && Number.isFinite(m.z) && Number.isFinite(m.ry)) {
          p.x = Math.max(-200, Math.min(200, m.x));
          p.z = Math.max(-200, Math.min(200, m.z));
          p.ry = m.ry;
        }
        break;
      }
      case 'tag': handleTag(p); break;
      case 'start': {
        const room = p.room;
        if (!room || room.hostId !== p.id || room.phase !== 'lobby') return;
        if (room.players.size < 2) { send(ws, { t: 'err', msg: 'Need at least 2 players to start.' }); return; }
        startRound(room);
        break;
      }
      case 'leave': removePlayer(p); break;
    }
  });

  ws.on('close', () => removePlayer(p));
  ws.on('error', () => {});
});

// Drop dead connections (phones that went to sleep, etc.).
setInterval(() => {
  for (const ws of wss.clients) {
    if (!ws.isAlive) { ws.terminate(); continue; }
    ws.isAlive = false;
    ws.ping();
  }
}, 20000);

if (require.main === module) server.listen(PORT, () => console.log('Tag Game server listening on ' + PORT));

module.exports = { server, wss, rooms };
