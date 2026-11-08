// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// QR scanning, the right click menu, the keyboard shortcut and payment page detection.
// Everything runs on this device. Pictures are decoded here and never sent anywhere
import jsQR from 'jsqr';
import { api } from './api.js';
import * as store from './state.js';
import { parsePayment, findAddresses, payHash } from './payment.js';
import { NETS } from './utxo.js';

const ES = String((api && api.i18n && api.i18n.getUILanguage ? api.i18n.getUILanguage() : 'en')).toLowerCase().startsWith('es');
const L = (en, es) => (ES ? es : en);

// ---------- reading a QR from a picture ----------

export function decodePixels(data, width, height) {
  const r = jsQR(data, width, height, { inversionAttempts: 'attemptBoth' });
  return r && typeof r.data === 'string' ? r.data : null;
}

async function decodeBlob(blob) {
  const bmp = await createImageBitmap(blob);
  try {
    const big = Math.max(bmp.width, bmp.height);
    // Huge screenshots are shrunk so the scan stays fast. Tiny pictures are enlarged so the code is easier to find
    const tries = [big > 2400 ? 2400 / big : big < 400 ? 800 / big : 1];
    if (big >= 400) tries.push(0.5);
    for (const k of tries) {
      const w = Math.max(1, Math.round(bmp.width * k)); const h = Math.max(1, Math.round(bmp.height * k));
      const canvas = new OffscreenCanvas(w, h);
      const ctx = canvas.getContext('2d', { willReadFrequently: true });
      ctx.imageSmoothingEnabled = k >= 1 ? false : true;
      ctx.fillStyle = '#fff';
      ctx.fillRect(0, 0, w, h);
      ctx.drawImage(bmp, 0, 0, w, h);
      const img = ctx.getImageData(0, 0, w, h);
      const text = decodePixels(img.data, w, h);
      if (text) return text;
    }
    return null;
  } finally { if (bmp.close) bmp.close(); }
}

async function fromImageUrl(url) {
  if (!/^(https?:|data:image\/)/i.test(url || '')) return null;
  try {
    const r = await fetch(url, { credentials: 'omit', referrerPolicy: 'no-referrer' });
    if (!r.ok) return null;
    const blob = await r.blob();
    if (blob.size > 12 * 1024 * 1024 || /svg/i.test(blob.type)) return null;
    return await decodeBlob(blob);
  } catch (e) { return null; }
}

async function fromScreen(windowId) {
  const dataUrl = await api.tabs.captureVisibleTab(windowId, { format: 'png' });
  const blob = await (await fetch(dataUrl)).blob();
  return decodeBlob(blob);
}

// ---------- opening the Sillet window ----------

let resultWindow = null;
async function openWindow(hash) {
  if (resultWindow != null) { try { await api.windows.remove(resultWindow); } catch (e) { /* already closed */ } }
  try {
    const win = await api.windows.create({ url: api.runtime.getURL('popup.html#' + hash), type: 'popup', width: 400, height: 680, focused: true });
    resultWindow = win && win.id;
  } catch (e) { resultWindow = null; }
}

const originOf = (url) => { try { const o = new URL(url).origin; return /^https?:/.test(o) ? o : ''; } catch (e) { return ''; } };

async function showResult(text, origin) {
  if (!text) return openWindow('/scan?none=1');
  const p = parsePayment(text);
  if (p) return openWindow(payHash(p, origin));
  return openWindow('/scan?text=' + encodeURIComponent(text.slice(0, 500)));
}

export async function scanTab(tab, srcUrl) {
  let text = null;
  if (srcUrl) text = await fromImageUrl(srcUrl);
  if (!text) { try { text = await fromScreen(tab.windowId); } catch (e) { text = null; } }
  return showResult(text, originOf(tab.url));
}

// Used by the Send screen: reads the tab the person is looking at and returns what it found
async function activeTab() {
  let tabs = await api.tabs.query({ active: true, currentWindow: true });
  if (!tabs.length || !/^https?:/.test(tabs[0].url || '')) {
    const wins = await api.windows.getAll({ windowTypes: ['normal'] });
    const w = wins.find((x) => x.focused) || wins[0];
    tabs = w ? await api.tabs.query({ active: true, windowId: w.id }) : [];
  }
  return tabs[0] || null;
}
export async function scanForUi() {
  const tab = await activeTab();
  if (!tab) throw new Error(L('Open a website with the QR code first', 'Abre primero una página con el código QR'));
  const text = await fromScreen(tab.windowId).catch(() => { throw new Error(L('Sillet cannot see this page', 'Sillet no puede ver esta página')); });
  if (!text) throw new Error(L('No QR code found. Make the code bigger on the page and try again', 'No se encontró ningún QR. Haz el código más grande en la página y prueba otra vez'));
  return { pay: parsePayment(text), text: text.slice(0, 500), origin: originOf(tab.url) };
}

// ---------- right click menu and shortcut ----------

export function setupMenus() {
  if (!api.contextMenus) return;
  api.contextMenus.removeAll(() => {
    const ctx = (id, title, contexts) => api.contextMenus.create({ id, title, contexts });
    ctx('sillet-scan-image', L('Scan QR with Sillet', 'Escanear QR con Sillet'), ['image']);
    ctx('sillet-scan-page', L('Scan QR on this page with Sillet', 'Escanear QR de esta página con Sillet'), ['page', 'frame']);
    ctx('sillet-pay-selection', L('Pay "%s" with Sillet', 'Pagar "%s" con Sillet'), ['selection']);
  });
}

export function listen() {
  if (api.contextMenus && api.contextMenus.onClicked) {
    api.contextMenus.onClicked.addListener(async (info, tab) => {
      const origin = originOf((tab && tab.url) || info.pageUrl);
      if (info.menuItemId === 'sillet-pay-selection') {
        const text = String(info.selectionText || '').trim();
        const p = parsePayment(text) || findAddresses(text, 1)[0];
        if (p) return openWindow(payHash(p, origin));
        return openWindow('/scan?text=' + encodeURIComponent(text.slice(0, 500)));
      }
      if (info.menuItemId === 'sillet-scan-image') return scanTab(tab, info.srcUrl);
      if (info.menuItemId === 'sillet-scan-page') return scanTab(tab, null);
    });
  }
  if (api.commands && api.commands.onCommand) {
    api.commands.onCommand.addListener(async (cmd) => {
      if (cmd !== 'scan_qr') return;
      const tab = await activeTab();
      if (tab) scanTab(tab, null);
    });
  }
}

// ---------- messages from the page scanner ----------

let lastOpen = 0;
export async function handlePage(msg, sender) {
  const origin = sender.tab ? originOf(sender.tab.url || sender.url) : '';
  if (!origin) throw new Error('Not allowed');
  const st = await store.load();
  if (msg.type === 'scanner-hide') {
    if (!st.scannerOff.includes(origin)) await store.save({ scannerOff: [...st.scannerOff, origin].slice(-200) });
    return true;
  }
  if (msg.type === 'detect') {
    if (!st.settings.detectPages || st.scannerOff.includes(origin)) return { enabled: false, links: [], addresses: [] };
    const links = (Array.isArray(msg.links) ? msg.links : []).slice(0, 20).map((l) => parsePayment(String(l).slice(0, 600)));
    const text = (Array.isArray(msg.candidates) ? msg.candidates : []).slice(0, 40).map((c) => String(c).slice(0, 120)).join(' ');
    const addresses = findAddresses(text, 6).map((a) => ({ ...a, name: NETS[a.net] ? NETS[a.net].name : 'EVM' }));
    return { enabled: true, links, addresses };
  }
  if (msg.type === 'pay-open') {
    // Only a real click by the person counts. Pages cannot open the Sillet window on their own
    if (msg.trusted !== true || Date.now() - lastOpen < 800) return false;
    lastOpen = Date.now();
    const p = parsePayment(String(msg.text || '').slice(0, 600));
    if (!p) return false;
    await openWindow(payHash(p, origin));
    return true;
  }
  throw new Error('Unknown message');
}

// Only the automated browser test calls this, to check the picture decoder inside a real browser
export const testImage = (url) => fromImageUrl(url);
