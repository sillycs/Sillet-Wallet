// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
import { api } from './api.js';
import { handleRpc } from './handler.js';
import { handleUi } from './ui.js';
import * as core from './core.js';
import * as approvals from './approvals.js';
import { serializeError } from './errors.js';
import * as scan from './scan.js';
import * as wc from './wc.js';

const extensionUrl = api.runtime.getURL('');

function originOf(sender) {
  try {
    const o = sender.origin && sender.origin !== 'null' ? sender.origin : new URL(sender.url).origin;
    return /^https?:\/\//.test(o) ? o : null;
  } catch (e) {
    return null;
  }
}

async function route(msg, sender) {
  if (!msg || typeof msg !== 'object') throw new Error('Bad message');
  if (msg.type === 'ping') return 'pong';
  if (msg.type === 'rpc') {
    const origin = originOf(sender);
    if (!origin) throw Object.assign(new Error('This page cannot use Sillet'), { code: 4100 });
    return handleRpc({ origin, tabId: sender.tab && sender.tab.id }, String(msg.method), msg.params);
  }
  if (msg.type === 'ui') {
    // Only Sillet's own pages may drive the wallet, never a website
    if (!sender.url || !sender.url.startsWith(extensionUrl)) throw new Error('Not allowed');
    return handleUi(String(msg.action), msg.payload || {});
  }
  // eslint-disable-next-line no-undef
  if (typeof __SILLET_TEST_BASE__ === 'string' && __SILLET_TEST_BASE__ && msg.type === 'test-scan-image' && sender.url && sender.url.startsWith(extensionUrl)) return scan.testImage(String(msg.url));
  if (msg.type === 'detect' || msg.type === 'pay-open' || msg.type === 'scanner-hide') return scan.handlePage(msg, sender);
  throw new Error('Unknown message');
}

api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  route(msg, sender).then((result) => sendResponse({ ok: true, result })).catch((e) => sendResponse({ ok: false, error: serializeError(e) }));
  return true;
});

api.windows.onRemoved.addListener((id) => approvals.onWindowClosed(id));
api.alarms.onAlarm.addListener((a) => { if (a.name === core.ALARM_NAME) core.lock(); else wc.onAlarm(a.name); });

// Reconnect to WalletConnect if there are sessions, so requests from dapps can arrive
wc.resume().catch(() => {});

core.onStateChanged(async () => {
  try {
    const tabs = await api.tabs.query({});
    for (const t of tabs) api.tabs.sendMessage(t.id, { type: 'sillet-state' }).catch(() => {});
  } catch (e) { /* no tabs */ }
});

// Pages that were already open when Sillet was installed get the wallet too, without a reload
async function injectIntoOpenTabs() {
  if (!api.scripting) return;
  let tabs = [];
  try { tabs = await api.tabs.query({ url: ['http://*/*', 'https://*/*'] }); } catch (e) { return; }
  for (const tab of tabs) {
    const allFrames = false;
    const target = { tabId: tab.id, allFrames: true };
    try {
      await api.scripting.executeScript({ target, files: ['inpage.js'], world: 'MAIN' });
      await api.scripting.executeScript({ target, files: ['content.js'] });
      if (!allFrames) await api.scripting.executeScript({ target: { tabId: tab.id }, files: ['scanner.js'] });
    } catch (e) { /* page does not allow extensions (store pages, PDF viewer) */ }
  }
}

scan.listen();

api.runtime.onInstalled.addListener((d) => {
  scan.setupMenus();
  if (d.reason === 'install') api.tabs.create({ url: api.runtime.getURL('popup.html#/onboard') });
  if (d.reason === 'install' || d.reason === 'update') injectIntoOpenTabs();
});
