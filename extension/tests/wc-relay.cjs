// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// A tiny fake WalletConnect relay for tests: keeps messages per topic and hands them to whoever subscribes
const { WebSocketServer } = require('ws');

function startRelay(port) {
  const wss = new WebSocketServer({ port, host: '127.0.0.1' });
  const subs = new Map(); // topic -> Set of {ws, subId}
  const mailbox = new Map(); // topic -> messages not yet delivered
  let n = 0;
  const send = (ws, o) => { try { ws.send(JSON.stringify(o)); } catch (e) { /* closed */ } };
  const deliver = (topic, msg) => {
    const list = subs.get(topic);
    const others = list ? [...list].filter((x) => x.ws !== msg.from) : [];
    if (!others.length) { (mailbox.get(topic) || mailbox.set(topic, []).get(topic)).push(msg); return; }
    for (const s of others) send(s.ws, { id: Date.now() * 1000 + (n++ % 1000), jsonrpc: '2.0', method: 'irn_subscription', params: { id: s.subId, data: { topic, message: msg.message, publishedAt: Date.now(), tag: msg.tag } } });
  };
  const subscribe = (ws, topic) => {
    const subId = 'sub' + (++n).toString(16).padStart(8, '0');
    if (!subs.has(topic)) subs.set(topic, new Set());
    subs.get(topic).add({ ws, subId });
    const waiting = mailbox.get(topic) || [];
    mailbox.delete(topic);
    setTimeout(() => waiting.forEach((m) => deliver(topic, m)), 20);
    return subId;
  };
  wss.on('connection', (ws) => {
    ws.on('message', (raw) => {
      let m; try { m = JSON.parse(String(raw)); } catch (e) { return; }
      if (!m.method) return; // a reply to something we sent
      const p = m.params || {};
      if (process.env.WC_DEBUG) console.log('   [relay]', m.method, (p.topic || p.pairingTopic || '').slice(0, 6), 'tag', p.tag, 'port', ws._socket ? ws._socket.remotePort : '');
      const ok = (result) => send(ws, { id: m.id, jsonrpc: '2.0', result });
      if (m.method === 'irn_subscribe') return ok(subscribe(ws, p.topic));
      if (m.method === 'irn_batchSubscribe') return ok((p.topics || []).map((t) => subscribe(ws, t)));
      if (m.method === 'irn_publish') { ok(true); deliver(p.topic, { message: p.message, tag: p.tag, from: ws }); return; }
      // Newer SDKs send the session proposal and approval as their own relay calls
      if (m.method === 'wc_proposeSession') { ok(true); subscribe(ws, p.pairingTopic); deliver(p.pairingTopic, { message: p.sessionProposal, tag: 1100, from: ws }); return; }
      if (m.method === 'wc_approveSession') { ok(true); subscribe(ws, p.sessionTopic); deliver(p.pairingTopic, { message: p.sessionProposalResponse, tag: 1101, from: ws }); deliver(p.sessionTopic, { message: p.sessionSettlementRequest, tag: 1102, from: ws }); return; }
      if (m.method === 'irn_unsubscribe') { const l = subs.get(p.topic); if (l) for (const s of [...l]) if (s.ws === ws) l.delete(s); return ok(true); }
      if (m.method === 'irn_batchUnsubscribe') return ok(true);
      if (m.method === 'irn_batchFetchMessages') return ok({ messages: [], hasMore: false });
      return ok(true);
    });
    ws.on('close', () => { for (const l of subs.values()) for (const s of [...l]) if (s.ws === ws) l.delete(s); });
  });
  return new Promise((resolve) => wss.on('listening', () => resolve(wss)));
}
module.exports = { startRelay };
