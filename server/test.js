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
    b.send({ t: 'start' }); await sleep(100);
    assert(!a.msgs.find(m => m.t === 'start'), 'non-host must not start');
    a.send({ t: 'start' });
    const sa = await a.wait('start'); await b.wait('start');
    console.log('start msg', JSON.stringify(sa).slice(0, 200));
    // place players: A at 0,0 facing +z (ry=0); B at 0,2
    a.send({ t: 'state', x: 0, z: 0, ry: 0 });
    b.send({ t: 'state', x: 0, z: 2, ry: Math.PI }); // B faces A too
    await sleep(150);
    // both press tag at once; first one wins, other is stunned
    a.send({ t: 'tag' }); b.send({ t: 'tag' });
    const t = await a.wait('tagged');
    assert.strictEqual(t.by, ja.id); assert.strictEqual(t.target, jb.id);
    await sleep(150);
    const sc = a.msgs.filter(m => m.t === 'tagged');
    assert.strictEqual(sc.length, 1, 'second press during stun must not tag');
    // out of range / behind
    a.send({ t: 'state', x: 0, z: 0, ry: Math.PI }); await sleep(150);
    await sleep(700);
    a.send({ t: 'tag' });
    await a.wait('miss');
    // public game: two players join, round auto-starts
    const c = client(port), d = client(port);
    await c.open(); await d.open();
    c.send({ t: 'joinpublic', name: 'Cy' }); await c.wait('joined');
    d.send({ t: 'joinpublic', name: 'Di' }); await d.wait('joined');
    const st = await c.wait('start', 12000);
    assert(st.left > 50);
    console.log('public auto-start ok');
    console.log('ALL TESTS PASSED');
    process.exit(0);
  } catch (e) { console.error('FAIL', e); process.exit(1); }
});
