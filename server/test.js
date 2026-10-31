const WebSocket = require('ws');
const assert = require('assert');
const { server, wss } = require('./server');

function client(port) {
  const ws = new WebSocket('ws://localhost:' + port);
  const msgs = [];
  const waiters = [];
  ws.on('message', d => {
    const m = JSON.parse(d.toString());
    msgs.push(m);
    for (let i = waiters.length - 1; i >= 0; i--) if (waiters[i].t === m.t) { waiters[i].res(m); waiters.splice(i, 1); }
  });
  return {
    ws, msgs,
    open: () => new Promise(r => ws.on('open', r)),
    send: o => ws.send(JSON.stringify(o)),
    wait: (t, ms = 3000) => {
      const found = msgs.find(m => m.t === t); if (found) { msgs.splice(msgs.indexOf(found), 1); return Promise.resolve(found); }
      return new Promise((res, rej) => { waiters.push({ t, res }); setTimeout(() => rej(new Error('timeout ' + t)), ms); });
    },
  };
}
const sleep = ms => new Promise(r => setTimeout(r, ms));

server.listen(0, async () => {
  const port = server.address().port;
  try {
    const a = client(port), b = client(port);
    await a.open(); await b.open();
    a.send({ t: 'create', name: 'Ann' });
    const ja = await a.wait('joined');
    const room = await a.wait('room');
    assert.strictEqual(room.code.length, 4);
    b.send({ t: 'join', code: room.code.toLowerCase(), name: 'Bob' });
    const jb = await b.wait('joined');
    await b.wait('room');
    // votes: both vote City, it must win
    a.send({ t: 'vote', map: 2 }); b.send({ t: 'vote', map: 2 });
    await sleep(100);
    b.send({ t: 'start' }); await sleep(100);
    assert(!a.msgs.find(m => m.t === 'start'), 'non-host must not start');
    a.send({ t: 'start' });
    const sa = await a.wait('start'); await b.wait('start');
    assert.strictEqual(sa.map, 2, 'City should win the vote');
    assert.strictEqual(sa.duration, 120);
    assert([ja.id, jb.id].includes(sa.it), 'someone must be it');
    const it = sa.it === ja.id ? a : b, other = sa.it === ja.id ? b : a;
    const itId = sa.it, otherId = itId === ja.id ? jb.id : ja.id;
    console.log('start msg', JSON.stringify(sa).slice(0, 200));
    // place players: "it" at 0,0 facing +z, the other 2 m ahead
    it.send({ t: 'state', x: 0, z: 0, ry: 0 });
    other.send({ t: 'state', x: 0, z: 2, ry: Math.PI });
    await sleep(1100); // the new "it" is frozen for 1 s at the start
    // the player who is NOT it can't tag
    other.send({ t: 'tag' }); await sleep(150);
    assert(!it.msgs.find(m => m.t === 'tagged'), 'only "it" can tag');
    // it tags -> other becomes it
    it.send({ t: 'tag' });
    const t = await it.wait('tagged');
    assert.strictEqual(t.by, itId); assert.strictEqual(t.target, otherId); assert.strictEqual(t.it, otherId);
    // frozen new "it" can't tag at once, and can't hand it straight back
    other.send({ t: 'tag' }); await sleep(200);
    assert.strictEqual(it.msgs.filter(m => m.t === 'tagged').length, 1, 'new it is frozen');
    await sleep(1600);
    other.send({ t: 'tag' }); await other.wait('miss');   // within the no-tag-back window
    console.log('pass-the-tag ok');
    // out of range / behind
    it.send({ t: 'state', x: 50, z: 50, ry: 0 }); await sleep(150);
    await sleep(700);
    other.send({ t: 'tag' });
    await other.wait('miss');
    const info = [...(await a.wait('room').catch(() => ({ players: [] }))).players];
    // colours, wins, kick
    assert.notStrictEqual(room.players[0].color, undefined);
    b.send({ t: 'kick', id: ja.id }); await sleep(100);
    a.send({ t: 'kick', id: jb.id });
    await b.wait('kicked');
    console.log('kick ok');
    // public game: two players join, round auto-starts
    const c = client(port), d = client(port);
    await c.open(); await d.open();
    c.send({ t: 'joinpublic', name: 'Cy' }); await c.wait('joined');
    d.send({ t: 'joinpublic', name: 'Di' }); await d.wait('joined');
    const st = await c.wait('start', 12000);
    assert(st.left > 50); assert(st.map >= 0 && st.map <= 2);
    console.log('public auto-start ok');
    console.log('ALL TESTS PASSED');
    process.exit(0);
  } catch (e) { console.error('FAIL', e); process.exit(1); }
});
