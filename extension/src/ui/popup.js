// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
import qrcode from 'qrcode-generator';
import { parseUnits, formatUnits, isAddress } from 'ethers';
import { api } from '../api.js';
import { h, mount } from './dom.js';
import { t, setLang } from './i18n.js';
import './es.js';
import './es2.js';
import { fmt, short, ago, copy } from './format.js';
import { icon } from './icons.js';
import { tokenAvatar, chainAvatar, identicon, siteAvatar, svgEl } from './avatars.js';
import { validAddress, parseAmount, fmtUnits } from '../utxo.js';
import { payHash } from '../payment.js';
import { newPhrase, validPhrase } from '../wallet.js';

const root = document.getElementById('app');
let S = null;
let showEmpty = false;

async function call(action, payload) {
  const r = await api.runtime.sendMessage({ type: 'ui', action, payload });
  if (!r || !r.ok) throw new Error((r && r.error && r.error.message) || 'Something went wrong');
  return r.result;
}

async function refresh() {
  S = await call('getState');
  setLang(S.settings.lang);
}

const chainOf = (id) => S.chains.find((c) => String(c.chainId) === String(id));
const isUtxo = () => !!(chainOf(S.chainId) || {}).utxo;
const coinAddr = (acc) => (isUtxo() ? acc.addrs[S.chainId] : acc.address);
const route = () => location.hash.replace(/^#/, '') || '/';
const go = (r) => { location.hash = r; };
const closeWindow = () => { try { window.close(); } catch (e) { /* ignore */ } };
const revive = (info) => Object.fromEntries(Object.entries(info || {}).map(([k, v]) => [k, v && typeof v === 'object' && 'big' in v ? BigInt(v.big) : v]));
// A payment opened from a page, a QR code or a right click fills the Send screen once
let prefill = null;
const takePrefill = () => { const p = prefill; prefill = null; return p; };
function prefillBanner(pf) {
  if (!pf) return null;
  let site = '';
  if (pf.from) { try { site = new URL(pf.from).host; } catch (e) { site = ''; } }
  return banner(t('This payment came from {source}. Check the address and the amount before you continue', { source: site || t('a QR code') }) + (pf.label ? ' · ' + pf.label : ''), 'warn');
}
const STABLE = new Set(['USDT', 'USDC', 'DAI', 'FDUSD', 'USDE', 'USDS', 'PYUSD']);

const usd = (n) => {
  if (n == null || Number.isNaN(n)) return '';
  if (n > 0 && n < 0.01) return '<$0.01';
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(n);
};
const toNum = (bal, dec) => Number(formatUnits(BigInt(bal || '0'), dec));
const HIDDEN = '••••';
const nameOf = (addr) => { const c = (S.contacts || []).find((x) => x.address.toLowerCase() === String(addr || '').toLowerCase()); return c ? c.name : null; };
const reduced = () => window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

// Numbers roll up to their value, so a loaded balance feels alive
function countUp(el, target, format, finalText, ms = 700) {
  if (reduced() || !Number.isFinite(target) || target <= 0) { el.textContent = finalText; return; }
  const t0 = performance.now();
  const step = (now) => {
    const p = Math.min(1, (now - t0) / ms);
    el.textContent = p < 1 ? format(target * (1 - Math.pow(1 - p, 3))) : finalText;
    if (p < 1) requestAnimationFrame(step);
  };
  requestAnimationFrame(step);
}

// ---------- building blocks ----------

function brandLogo(size = 28) {
  return h('img', { src: 'icons/icon48.png', width: size, height: size, alt: '', style: 'border-radius:8px' });
}

function toast(text) {
  const app = document.querySelector('.app');
  if (!app) return;
  const el = h('div', { class: 'toast' }, text);
  app.append(el);
  setTimeout(() => el.remove(), 1700);
}

function openSheet(title, content) {
  const app = document.querySelector('.app');
  const close = () => overlay.remove();
  const overlay = h('div', { class: 'overlay', onclick: (e) => { if (e.target === overlay) close(); } },
    h('div', { class: 'sheet' },
      h('div', { class: 'head' }, h('span', {}, title), h('button', { class: 'iconbtn', title: t('Close'), onclick: close }, icon('close', 18))),
      h('div', { class: 'sbody' }, typeof content === 'function' ? content(close) : content)));
  app.append(overlay);
  return close;
}

function frame(top, body, bottom) {
  return h('div', { class: 'app' }, top, h('div', { class: 'scroll' }, body), bottom);
}

function lockButton() {
  return h('button', { class: 'iconbtn', title: t('Lock'), onclick: async () => { await call('lock'); render(); } }, icon('lock', 19));
}

function accountSheet() {
  openSheet(t('Accounts'), (close) => h('div', {},
    S.accounts.map((a, i) => h('button', { class: 'item' + (i === S.selected ? ' on' : ''), onclick: async () => { await call('selectAccount', { index: i }); close(); render(); } },
      identicon(a.address, 38),
      h('div', { class: 'grow' }, h('div', { class: 'name' }, a.name), h('div', { class: 'muted small mono' }, short(a.address))),
      i === S.selected ? icon('check', 18) : null)),
    h('button', { class: 'btn ghost', style: 'margin-top:10px', onclick: async () => { await call('addAccount'); close(); render(); } }, icon('plus', 18), t('Add account'))));
}

function networkSheet() {
  const row = (c, close) => h('button', { class: 'item' + (String(c.chainId) === String(S.chainId) ? ' on' : ''), onclick: async () => { await call('selectChain', { chainId: c.chainId }); close(); render(); } },
    chainAvatar(c.chainId, 34),
    h('div', { class: 'grow' }, h('div', { class: 'name' }, c.name), h('div', { class: 'muted small' }, c.symbol)),
    String(c.chainId) === String(S.chainId) ? icon('check', 18) : null);
  openSheet(t('Network'), (close) => h('div', {},
    h('div', { class: 'sheetsec first' }, t('Bitcoin and more'), h('span', {}, t('Wallet window only'))),
    S.chains.filter((c) => c.utxo).map((c) => row(c, close)),
    h('div', { class: 'sheetsec' }, t('Websites and apps (EVM)'), h('span', {}, t('Ethereum and similar'))),
    S.chains.filter((c) => !c.utxo).map((c) => row(c, close))));
}

function mainShell(active, body) {
  const acc = S.accounts[S.selected];
  const chain = chainOf(S.chainId);
  const top = h('div', { class: 'topbar' },
    h('button', { class: 'pill account-btn', title: t('Accounts'), onclick: accountSheet }, identicon(acc.address, 26), h('span', { class: 't' }, acc.name), icon('down', 14)),
    h('div', { class: 'grow' }),
    h('button', { class: 'pill network-btn', title: t('Network'), onclick: networkSheet }, chainAvatar(S.chainId, 26), h('span', { class: 't' }, chain.name.replace(' Smart Chain', '').replace(' One', '').replace(' C-Chain', '')), icon('down', 14)),
    lockButton());
  const tab = (id, name, label) => h('button', { class: 'tabbtn' + (active === id ? ' on' : ''), onclick: () => go(id) }, icon(name, 22), h('span', {}, label));
  const tabs = h('nav', { class: 'tabbar' }, tab('/', 'wallet', t('Wallet')), tab('/market', 'chart', t('Market')), tab('/activity', 'activity', t('Activity')), tab('/sites', 'globe', t('Sites')), tab('/settings', 'settings', t('Settings')));
  return frame(top, body, tabs);
}

function subShell(title, body, back = '/') {
  const top = h('div', { class: 'topbar' },
    h('button', { class: 'iconbtn', title: t('Back'), onclick: () => go(back) }, icon('back', 22)),
    h('div', { class: 'title' }, title), h('span', { class: 'sp' }));
  return frame(top, body);
}

function plainShell(body, right) {
  const top = h('div', { class: 'topbar' }, brandLogo(26), h('div', { class: 'title', style: 'text-align:left' }, 'Sillet'), right || h('span'));
  return frame(top, body);
}

function field(label, input, hint, extra) {
  return h('label', { class: 'field' }, h('span', { class: 'label' }, h('span', {}, label), extra || null), input, hint ? h('span', { class: 'hint' }, hint) : null);
}

function button(label, onclick, kind = 'primary', extra = {}) {
  return h('button', { class: 'btn ' + kind, onclick, ...extra }, label);
}

function errorBox() {
  const el = h('div', { class: 'error', hidden: true });
  el.show = (m) => { el.textContent = m; el.hidden = false; };
  el.hide = () => { el.hidden = true; };
  return el;
}

function banner(text, kind = 'warn') {
  return h('div', { class: 'banner ' + kind }, icon(kind === 'info' ? 'info' : kind === 'ok' ? 'check' : 'warn', 18), h('div', {}, text));
}
const warning = (text) => banner(text, 'warn');

async function guarded(btn, err, fn) {
  const label = btn.textContent;
  btn.disabled = true;
  err.hide();
  try { await fn(); } catch (e) { err.show(e.message); } finally { btn.disabled = false; btn.textContent = label; }
}

const stick = (...kids) => h('div', { class: 'stickybar row gap' }, ...kids);

function unlockForm(onDone, title) {
  const pass = h('input', { type: 'password', placeholder: t('Password'), autocomplete: 'current-password' });
  const err = errorBox();
  const btn = button(t('Unlock'), () => guarded(btn, err, async () => {
    await call('unlock', { password: pass.value });
    await onDone();
  }));
  pass.addEventListener('keydown', (e) => { if (e.key === 'Enter') btn.click(); });
  setTimeout(() => pass.focus(), 50);
  return h('div', { class: 'center', style: 'padding-top:48px' },
    h('img', { src: 'icons/icon128.png', width: 84, height: 84, alt: '', class: 'logo-big' }),
    h('h2', { style: 'margin-top:10px' }, title || t('Welcome back')),
    h('p', { class: 'muted', style: 'margin-top:0' }, t('Enter your password to unlock Sillet')),
    pass, err, btn);
}

// ---------- no wallet yet ----------

function welcomeView() {
  return plainShell(h('div', { class: 'center', style: 'padding-top:20px' },
    h('img', { src: 'icons/icon128.png', width: 84, height: 84, alt: '', class: 'logo-big' }),
    h('h2', { style: 'margin-top:10px' }, t('Your keys, your wallet')),
    h('p', { class: 'muted' }, t('Sillet keeps your recovery phrase encrypted on this device. We never see it')),
    h('div', { class: 'feat' },
      h('div', {}, icon('shield', 20), t('Open source and private. No account, no tracking')),
      h('div', {}, icon('globe', 20), t('Works with websites and apps on 11 networks')),
      h('div', {}, icon('key', 20), t('Clear warnings before risky approvals'))),
    button(t('Create a wallet'), () => openOnboard()),
    button(t('I already have a wallet'), () => openOnboard('import'), 'ghost')));
}

function openOnboard(mode) {
  const url = api.runtime.getURL('popup.html#/onboard' + (mode ? '/' + mode : ''));
  api.tabs.create({ url });
  closeWindow();
}

function onboardView() {
  const mode = route().split('/')[2];
  const card = h('div', { class: 'card' });
  const wrap = h('div', { class: 'onboard' }, h('div', { class: 'brand' }, brandLogo(36), h('span', {}, 'Sillet')), card);
  const step = (n, ...nodes) => mount(card, n ? h('div', { class: 'steps' }, [1, 2, 3].map((i) => h('i', { class: i <= n ? 'on' : '' }))) : null, nodes);

  function choose() {
    step(0, h('h1', {}, t('Welcome to Sillet')),
      h('p', { class: 'muted' }, t('A wallet for websites and apps on Ethereum, Base, Arbitrum, Polygon, BNB Chain and more')),
      h('div', { class: 'feat' },
        h('div', {}, icon('shield', 20), t('Your keys are encrypted on this device and never leave it')),
        h('div', {}, icon('globe', 20), t('Connect to websites like with other wallets')),
        h('div', {}, icon('warn', 20), t('Clear warnings before unlimited approvals and risky signatures'))),
      warning(t('Sillet is new software and has not been audited yet. Please use only small amounts you can afford to lose')),
      button(t('Create a new wallet'), () => passwordStep(null)),
      button(t('Import with a recovery phrase'), importStep, 'ghost'));
  }

  function importStep() {
    const phrase = h('textarea', { rows: 3, placeholder: t('12 or 24 words separated by spaces'), spellcheck: 'false', autocomplete: 'off' });
    const err = errorBox();
    step(0, h('h1', {}, t('Import a wallet')),
      h('p', { class: 'muted' }, t('Use the same phrase as in the Sillet app, MetaMask or Trust Wallet and you get the same addresses')),
      field(t('Recovery phrase'), phrase), err,
      button(t('Continue'), () => {
        if (!validPhrase(phrase.value)) { err.show(t('That recovery phrase is not valid')); return; }
        passwordStep(phrase.value);
      }),
      button(t('Back'), choose, 'ghost'));
  }

  function passwordStep(existing) {
    const p1 = h('input', { type: 'password', autocomplete: 'new-password' });
    const p2 = h('input', { type: 'password', autocomplete: 'new-password' });
    const err = errorBox();
    const meter = h('div', { class: 'hint' }, t('At least 8 characters. A long phrase is better than a short complicated one'));
    const btn = button(t('Continue'), () => guarded(btn, err, async () => {
      if (p1.value.length < 8) throw new Error(t('Use a password with at least 8 characters'));
      if (p1.value !== p2.value) throw new Error(t('The passwords do not match'));
      if (existing) { await call('setup', { mnemonic: existing, password: p1.value }); doneStep(); } else showPhrase(p1.value);
    }));
    step(1, h('h1', {}, t('Create a password')),
      h('p', { class: 'muted' }, t('It unlocks Sillet on this browser. It cannot be recovered, so keep it safe')),
      field(t('Password'), p1), field(t('Repeat the password'), p2), meter, err, btn,
      button(t('Back'), existing ? importStep : choose, 'ghost'));
    setTimeout(() => p1.focus(), 50);
  }

  function showPhrase(password) {
    const phrase = newPhrase();
    const words = phrase.split(' ');
    const ok = h('input', { type: 'checkbox' });
    const btn = button(t('Continue'), () => { if (ok.checked) confirmStep(password, phrase); }, 'primary', { disabled: true });
    ok.addEventListener('change', () => { btn.disabled = !ok.checked; });
    step(2, h('h1', {}, t('Your recovery phrase')),
      warning(t('Write these words on paper in order. Anyone with them can take your funds, and if you lose them nobody can recover your wallet. Never share them or type them on a website')),
      h('div', { class: 'words' }, words.map((w, i) => h('div', { class: 'word' }, h('span', {}, i + 1), w))),
      h('label', { class: 'check' }, ok, t('I wrote it down and I keep it safe')), btn);
  }

  function confirmStep(password, phrase) {
    const words = phrase.split(' ');
    const picks = [];
    while (picks.length < 3) { const i = crypto.getRandomValues(new Uint32Array(1))[0] % words.length; if (!picks.includes(i)) picks.push(i); }
    picks.sort((a, b) => a - b);
    const inputs = picks.map(() => h('input', { type: 'text', autocomplete: 'off', spellcheck: 'false' }));
    const err = errorBox();
    const btn = button(t('Create wallet'), () => guarded(btn, err, async () => {
      if (!inputs.every((el, k) => el.value.trim().toLowerCase() === words[picks[k]])) throw new Error(t('Those words do not match. Check your paper and try again'));
      await call('setup', { mnemonic: phrase, password });
      doneStep();
    }));
    step(3, h('h1', {}, t('Check your phrase')),
      h('p', { class: 'muted' }, t('Type the words you wrote down to make sure you saved them')),
      picks.map((p, k) => field(t('Word #{n}', { n: p + 1 }), inputs[k])), err, btn,
      button(t('Back'), () => showPhrase(password), 'ghost'));
  }

  function doneStep() {
    step(0, h('div', { class: 'center' },
      h('div', { class: 'okmark' }, icon('check', 32)),
      h('h1', {}, t('Your wallet is ready')),
      h('p', { class: 'muted' }, t('Open Sillet from the puzzle icon in your browser toolbar. You can pin it to keep it close')),
      h('div', { class: 'feat' },
        h('div', {}, icon('globe', 20), t('Open a website and press Connect wallet. Sillet shows up in the list')),
        h('div', {}, icon('lock', 20), t('Sillet locks itself after a while. You choose how long in Settings'))),
      button(t('Close this tab'), closeWindow)));
  }

  if (mode === 'import') importStep(); else choose();
  return wrap;
}

// ---------- home ----------

function skeletonRows(n = 3) {
  return Array.from({ length: n }, () => h('div', { class: 'skelrow' }, h('div', { class: 'dot' }), h('div', { style: 'flex:1;display:grid;gap:7px' }, h('div', { class: 'skel', style: 'width:40%' }), h('div', { class: 'skel', style: 'width:25%;height:11px' })), h('div', { class: 'skel', style: 'width:18%' })));
}

function enablePricesSheet() {
  openSheet(t('Show prices in USD'), (close) => h('div', {},
    h('p', { class: 'muted' }, t('Sillet asks CoinGecko for prices. CoinGecko can see your IP address and the token addresses of the network you use, never your own address. You can turn this off any time in Settings')),
    button(t('Turn on'), async () => { await call('setSettings', { settings: { prices: true } }); close(); render(); }),
    button(t('Not now'), close, 'ghost')));
}

function homeView() {
  if (isUtxo()) return utxoHome();
  const acc = S.accounts[S.selected];
  const chain = chainOf(S.chainId);
  const big = h('div', { class: 'big' }, h('span', { class: 'skel', style: 'display:inline-block;width:120px;height:34px' }));
  const sub = h('div', { class: 'sub' }, chain.name);
  const list = h('div', { class: 'list' }, skeletonRows());
  const toggle = h('button', { class: 'link', onclick: () => { showEmpty = !showEmpty; render(); } }, showEmpty ? t('Hide empty tokens') : t('Show all tokens'));
  const holder = h('div', {});
  const hide = !!S.settings.hideBalances;

  async function load() {
    let items;
    try { items = await call('balances', { chainId: S.chainId, address: acc.address }); } catch (e) { mount(list, h('div', { class: 'empty' }, h('b', {}, t('Could not load balances')), e.message)); big.textContent = '–'; return; }
    window.__balances = items;
    let pr = null;
    if (S.settings.prices) { try { pr = await call('prices', { chainId: S.chainId, tokens: items.filter((i) => !i.native).map((i) => i.address) }); } catch (e) { pr = null; } }
    const priceOf = (i) => {
      if (!pr) return null;
      if (i.native) return pr.coin ? pr.coin.usd : null;
      const p = pr.tokens[(i.address || '').toLowerCase()];
      if (p != null) return p;
      return STABLE.has(String(i.symbol).toUpperCase()) ? 1 : null;
    };
    const rows = items.filter((i) => i.native || showEmpty || (i.balance && i.balance !== '0'));
    let total = 0; let known = false;
    for (const i of items) { const p = priceOf(i); if (p != null && i.balance != null) { total += toNum(i.balance, i.decimals) * p; known = true; } }
    const native = items.find((i) => i.native);
    if (hide) {
      big.textContent = HIDDEN;
    } else if (pr && known) {
      countUp(big, total, usd, usd(total));
      const ch = pr.coin && typeof pr.coin.change === 'number' ? pr.coin.change : null;
      mount(sub, ch == null ? chain.name : [chain.name + ' · ' + chain.symbol + ' ', h('span', { class: ch >= 0 ? 'up' : 'down' }, (ch >= 0 ? '+' : '') + ch.toFixed(2) + '%')]);
    } else {
      if (native && native.balance != null) countUp(big, toNum(native.balance, native.decimals), (n) => n.toFixed(n < 1 ? 4 : 2) + ' ' + native.symbol, fmt(native.balance, native.decimals) + ' ' + native.symbol);
      else big.textContent = '–';
    }
    const onlyNative = items.every((i) => i.native || !i.balance || i.balance === '0');
    holder.replaceChildren(...(!S.settings.prices ? [h('div', { class: 'promo' }, icon('coins', 24), h('div', { class: 'txt' }, h('b', {}, t('See your balance in USD')), t('Optional, uses CoinGecko')), h('button', { class: 'btn primary', onclick: enablePricesSheet }, t('Turn on')))] : []));
    mount(list, rows.map((i) => {
      const p = priceOf(i);
      const val = !hide && p != null && i.balance != null ? usd(toNum(i.balance, i.decimals) * p) : '';
      return h('div', { class: 'item token' },
        tokenAvatar(i.symbol, 40),
        h('div', { class: 'grow' }, h('div', { class: 'name sym' }, i.symbol), h('div', { class: 'muted small' }, i.name || i.symbol)),
        h('div', { class: 'end' }, h('div', { class: 'tbal' }, hide ? HIDDEN : i.balance == null ? t('offline') : fmt(i.balance, i.decimals)), val ? h('div', { class: 'muted small' }, val) : null));
    }));
    if (onlyNative && native && native.balance === '0') {
      holder.append(h('div', { class: 'promo' }, icon('receive', 24), h('div', { class: 'txt' }, h('b', {}, t('Add funds to start')), t('Send some {symbol} to your address', { symbol: chain.symbol })), h('button', { class: 'btn primary', onclick: () => go('/receive') }, t('Receive'))));
    }
  }
  load();

  const act = (name, label, fn, main) => h('button', { class: 'act' + (main ? ' main' : ''), onclick: fn }, h('span', { class: 'c' }, icon(name, 22)), label);
  const eye = h('button', { class: 'iconbtn eye', title: hide ? t('Show balances') : t('Hide balances'), onclick: async () => { await call('setSettings', { settings: { hideBalances: !hide } }); render(); } }, icon(hide ? 'eyeoff' : 'eye', 18));
  const spinner = icon('refresh', 22);
  const body = h('div', {},
    h('div', { class: 'hero' }, eye,
      h('div', { class: 'lbl' }, t('Total balance')), big, sub,
      h('button', { class: 'addr', title: t('Copy address'), onclick: async () => { if (await copy(acc.address)) toast(t('Address copied')); } }, short(acc.address), icon('copy', 13))),
    h('div', { class: 'actions' },
      act('send', t('Send'), () => go('/send'), true), act('receive', t('Receive'), () => go('/receive')),
      act('user', t('Contacts'), () => go('/contacts')),
      h('button', { class: 'act', onclick: () => { spinner.classList.add('spin'); setTimeout(() => render(), 450); } }, h('span', { class: 'c' }, spinner), t('Refresh'))),
    holder,
    h('div', { class: 'sechead' }, h('b', {}, t('Tokens')), h('div', { class: 'row', style: 'gap:14px' }, toggle, h('button', { class: 'link', onclick: () => go('/addtoken') }, '+ ' + t('Add')))),
    list,
    h('div', { class: 'foot' }, t('Public nodes of the network can see your IP address and your public address')));
  return mainShell('/', body);
}

// ---------- receive ----------

function receiveView() {
  const acc = S.accounts[S.selected];
  const chain = chainOf(S.chainId);
  const address = coinAddr(acc);
  const qr = qrcode(0, 'M');
  qr.addData(address);
  qr.make();
  const n = qr.getModuleCount();
  const scale = 6;
  const canvas = h('canvas', { width: (n + 2) * scale, height: (n + 2) * scale, class: 'qr' });
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = '#fff';
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = '#000';
  for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) if (qr.isDark(r, c)) ctx.fillRect((c + 1) * scale, (r + 1) * scale, scale, scale);
  const cp = button(t('Copy address'), async () => { if (await copy(address)) toast(t('Address copied')); });
  cp.prepend(icon('copy', 18));
  return subShell(t('Receive'), h('div', { class: 'center', style: 'padding-top:4px' },
    h('div', { class: 'row', style: 'justify-content:center;gap:8px' }, chainAvatar(S.chainId, 22), h('b', {}, chain.name)),
    h('div', { class: 'qrwrap' }, canvas, h('div', { class: 'mid' }, h('span', {}, h('img', { src: 'icons/icon48.png', width: 30, height: 30, alt: '', style: 'border-radius:7px' })))),
    h('div', { class: 'muted small' }, acc.name),
    h('div', { class: 'addrbox' }, address),
    cp,
    banner(isUtxo() ? t('Only send {name} to this address. Other coins sent here can be lost', { name: chain.name }) : t('This address works on every network of Sillet. Only send assets of the network you are using'), 'info')));
}

// ---------- send ----------

function txSummary(rec) {
  const info = revive(rec.info);
  const sym = rec.nativeSymbol;
  const dec = rec.nativeDecimals;
  const L = [];
  const row = (k, v) => L.push(h('div', { class: 'kv' }, h('span', { class: 'k' }, k), h('span', { class: 'v' }, v)));
  const amt = (v, d, s) => fmt(v.toString(), d) + ' ' + (s || '');
  switch (info.kind) {
    case 'native': row(t('You send'), amt(info.value, dec, sym)); row(t('To'), info.to); break;
    case 'transfer': row(t('You send'), amt(info.amount, info.decimals, info.symbol || t('tokens'))); row(t('To'), info.recipient); row(t('Token contract'), info.token); break;
    case 'transferFrom': row(t('Moves'), amt(info.amount, info.decimals, info.symbol || t('tokens'))); row(t('From'), info.sender); row(t('To'), info.recipient); break;
    case 'approve': case 'increaseAllowance': case 'permit':
      row(t('Permission to spend'), info.amount >= (1n << 255n) ? t('ALL your {symbol}', { symbol: info.symbol || t('tokens') }) : amt(info.amount, info.decimals, info.symbol || t('tokens')));
      row(t('Given to'), info.spender); row(t('Token contract'), info.token); break;
    case 'setApprovalForAll': row(info.approved ? t('Permission to move ALL your NFTs') : t('Removes permission'), info.spender); row(t('Collection'), info.token); break;
    case 'create': row(t('Action'), t('Creates a new contract')); break;
    default: row(t('Contract'), info.to); if (info.value) row(t('Sends'), amt(info.value, dec, sym)); row(t('Data'), (info.selector || '') + ' · ' + info.dataBytes + ' bytes');
  }
  return { rows: L, info };
}

const WARN = {
  unlimitedApproval: 'This lets the address below spend ALL of this token from your wallet, now and in the future. Only continue if you fully trust this site',
  approvalForAll: 'This lets the address below move ALL your NFTs of this collection. Only continue if you fully trust this site',
  spenderIsWallet: 'The address receiving this permission is a normal wallet, not a contract. That is a common scam',
  unknownToken: 'This token is not in the Sillet list. Check the contract before continuing',
  unknownCall: 'Sillet cannot read what this transaction does. Only continue if you trust this site',
  contractCreation: 'This transaction creates a new contract',
  permitSignature: 'This signature can let someone spend your tokens without a transaction. Only sign it if you fully trust this site',
  orderSignature: 'Signing this can sell or move your assets. Only sign it if you fully trust this site',
};

function txConfirm(rec, onConfirm, confirmLabel, onCancel) {
  let level = 'market';
  const { rows, info } = txSummary(rec);
  const feeBox = h('div', { class: 'kv' });
  const levels = h('div', { class: 'seg' });
  function costOf() {
    const fee = rec.prep.fees[level];
    return BigInt(rec.prep.gasLimit) * BigInt(fee.type === 2 ? fee.maxFeePerGas : fee.gasPrice);
  }
  function upd() {
    mount(levels, ['low', 'market', 'fast'].map((l) => h('button', { class: l === level ? 'on' : '', onclick: () => { level = l; upd(); } }, t(l === 'low' ? 'Slow' : l === 'market' ? 'Normal' : 'Fast'))));
    mount(feeBox, h('span', { class: 'k' }, t('Network fee (max)')), h('span', { class: 'v' }, fmt(costOf().toString(), rec.nativeDecimals, 6) + ' ' + rec.nativeSymbol));
  }
  upd();
  const warns = [...(info.warnings || []).map((w) => [w, WARN[w]]).filter((x) => x[1])];
  const err = errorBox();
  const ok = button(confirmLabel, () => guarded(ok, err, async () => onConfirm(level)));
  const cancel = button(t('Cancel'), onCancel, 'ghost');
  let hero = null;
  if (info.kind === 'native' || info.kind === 'transfer') {
    const sym = info.kind === 'native' ? rec.nativeSymbol : (info.symbol || t('tokens'));
    const dec = info.kind === 'native' ? rec.nativeDecimals : info.decimals;
    const val = info.kind === 'native' ? info.value : info.amount;
    hero = h('div', { class: 'sendhero' }, h('div', { class: 'coin' }, tokenAvatar(sym, 52)), h('div', { class: 'amt' }, fmt(val.toString(), dec) + ' ' + sym), h('div', { class: 'to' }, t('To') + ' ' + short(info.kind === 'native' ? info.to : info.recipient)));
  }
  return h('div', { class: 'tx' },
    hero,
    h('div', { class: 'panel' },
      h('div', { class: 'kv' }, h('span', { class: 'k' }, t('Network')), h('span', { class: 'v' }, rec.chainName)),
      h('div', { class: 'kv' }, h('span', { class: 'k' }, t('From')), h('span', { class: 'v mono' }, rec.account || rec.req?.from || '')),
      rows),
    rec.tx?.data || rec.req?.data ? h('details', {}, h('summary', {}, t('Raw data')), h('pre', {}, rec.tx?.data || rec.req?.data)) : null,
    warns.map(([k, w]) => banner(t(w), RISKY.has(k) ? 'risk' : 'warn')),
    rec.prep.willFail ? banner(t('The network says this transaction is likely to fail. You would still pay the fee'), 'risk') : null,
    levels, h('div', { class: 'panel' }, feeBox), err, stick(cancel, ok));
}

function sendView() {
  if (isUtxo()) return utxoSendView();
  const acc = S.accounts[S.selected];
  const chain = chainOf(S.chainId);
  const items = [{ native: true, symbol: chain.symbol, decimals: chain.decimals, name: chain.symbol }, ...chain.tokens];
  let balances = window.__balances || [];
  let idx = 0;
  let price = null;
  const to = h('input', { type: 'text', placeholder: '0x…', spellcheck: 'false', autocomplete: 'off' });
  const amount = h('input', { type: 'text', inputmode: 'decimal', placeholder: '0.0', autocomplete: 'off' });
  const err = errorBox();
  const wrap = h('div', {});
  const balInfo = h('span', { class: 'hint' });
  const usdInfo = h('span', { class: 'hint' });
  const assetBox = h('div', {});
  const cur = () => items[idx];
  const bal = () => balances.find((b) => (cur().native ? b.native : b.address && b.address.toLowerCase() === cur().address.toLowerCase()));
  function showBal() {
    const b = bal();
    balInfo.textContent = b && b.balance != null ? t('Balance: {amount}', { amount: fmt(b.balance, b.decimals) + ' ' + b.symbol }) : '';
    const n = Number(amount.value.replace(',', '.'));
    usdInfo.textContent = price != null && n > 0 ? '≈ ' + usd(n * price) : '';
  }
  async function loadPrice() {
    price = null;
    if (!S.settings.prices) return;
    try {
      const c = cur();
      const pr = await call('prices', { chainId: S.chainId, tokens: c.native ? [] : [c.address] });
      price = c.native ? (pr.coin ? pr.coin.usd : null) : (pr.tokens[c.address.toLowerCase()] ?? (STABLE.has(c.symbol.toUpperCase()) ? 1 : null));
    } catch (e) { price = null; }
    showBal();
  }
  function drawAsset() {
    const c = cur();
    const b = bal();
    mount(assetBox, h('button', { class: 'assetbtn', onclick: pickAsset }, tokenAvatar(c.symbol, 36),
      h('div', { class: 'grow' }, h('div', { class: 'name' }, c.symbol), h('div', { class: 'muted small' }, c.name || c.symbol)),
      b && b.balance != null ? h('div', { class: 'muted small' }, fmt(b.balance, b.decimals)) : null, icon('down', 16)));
    showBal();
  }
  function pickAsset() {
    openSheet(t('Asset'), (close) => h('div', {}, items.map((i, k) => {
      const b = balances.find((x) => (i.native ? x.native : x.address && x.address.toLowerCase() === i.address.toLowerCase()));
      return h('button', { class: 'item' + (k === idx ? ' on' : ''), onclick: () => { idx = k; close(); drawAsset(); loadPrice(); } },
        tokenAvatar(i.symbol, 38), h('div', { class: 'grow' }, h('div', { class: 'name' }, i.symbol), h('div', { class: 'muted small' }, i.name || i.symbol)),
        b && b.balance != null ? h('div', { class: 'end' }, fmt(b.balance, b.decimals)) : null);
    })));
  }
  if (!balances.length) call('balances', { chainId: S.chainId, address: acc.address }).then((b) => { balances = b; window.__balances = b; drawAsset(); }).catch(() => {});
  drawAsset();
  loadPrice();
  amount.addEventListener('input', showBal);
  const max = h('button', { class: 'link', onclick: async () => {
    try {
      if (cur().native) amount.value = await call('maxNative', { chainId: S.chainId, from: acc.address, to: isAddress(to.value) ? to.value : undefined });
      else { const b = bal(); amount.value = b ? formatUnits(BigInt(b.balance || '0'), b.decimals) : ''; }
      showBal();
    } catch (e) { err.show(e.message); }
  } }, 'Max');
  const paste = h('span', { class: 'row', style: 'gap:12px' }, scanButton(), h('button', { class: 'link', onclick: async () => { try { to.value = (await navigator.clipboard.readText()).trim(); showWho(); } catch (e) { toast(t('Cannot read the clipboard')); } } }, t('Paste')));
  const pre = route().split('/')[2];
  if (pre && isAddress(pre)) to.value = pre;
  const pf = takePrefill();
  let pfNote = prefillBanner(pf);
  if (pf) {
    if (pf.to) to.value = pf.to;
    const k = pf.token ? items.findIndex((i) => !i.native && i.address && i.address.toLowerCase() === pf.token.toLowerCase()) : 0;
    if (pf.token && k < 0) pfNote = [pfNote, banner(t('This link asks for a token that is not in your list. Add the token first, then try again'), 'risk')];
    else {
      idx = k;
      drawAsset();
      loadPrice();
      const dec = items[k].decimals;
      if (pf.raw) { try { amount.value = formatUnits(BigInt(pf.raw), dec); } catch (e) { /* ignore */ } } else if (pf.amount) amount.value = pf.amount;
      showBal();
    }
  }
  const who = h('span', { class: 'hint' });
  const showWho = () => { const n = nameOf(to.value.trim()); who.textContent = n ? '✓ ' + n : ''; };
  to.addEventListener('input', showWho);
  showWho();
  const people = (S.contacts || []).slice(0, 4).map((c) => [c.name, c.address]);
  const recents = [...new Set(S.history.filter((x) => x.to && isAddress(x.to)).map((x) => x.to))].filter((a) => !nameOf(a)).slice(0, 3).map((a) => [short(a), a]);
  const review = button(t('Review'), () => guarded(review, err, async () => {
    if (!isAddress(to.value.trim())) throw new Error(t('That is not a valid address'));
    const c = cur();
    let amt;
    try { amt = parseUnits(amount.value.trim(), c.decimals); } catch (e) { throw new Error(t('Enter a valid amount')); }
    if (amt <= 0n) throw new Error(t('Enter a valid amount'));
    const b = bal();
    if (b && b.balance != null && BigInt(b.balance) < amt) throw new Error(t('You do not have enough {symbol}', { symbol: c.symbol }));
    const built = await call('buildSend', { chainId: S.chainId, from: acc.address, to: to.value.trim(), token: c.native ? undefined : c.address, amount: amount.value.trim() });
    const rec = { ...built, chainId: S.chainId, chainName: chain.name, nativeSymbol: chain.symbol, nativeDecimals: chain.decimals, account: acc.address };
    mount(wrap, txConfirm(rec, async (level) => {
      const hash = await call('sendNow', { chainId: S.chainId, req: built.req, level, gasLimit: built.prep.gasLimit, token: c.native ? undefined : c.address, recipient: to.value.trim(), displayValue: '0x' + amt.toString(16), symbol: c.symbol, decimals: c.decimals });
      mount(wrap, sentView(hash, chain, to.value.trim()));
    }, t('Confirm and send'), () => render()));
  }));
  mount(wrap,
    pfNote,
    field(t('Asset'), assetBox),
    field(t('To'), to, null, paste),
    who,
    people.length + recents.length ? h('div', { class: 'chips', style: 'margin-top:4px;margin-bottom:6px' }, [...people, ...recents].map(([label, a]) => h('button', { class: 'chipbtn', onclick: () => { to.value = a; showWho(); } }, label))) : null,
    h('label', { class: 'field' }, h('span', { class: 'label' }, h('span', {}, t('Amount')), max), amount, h('span', { class: 'row between' }, balInfo, usdInfo)),
    err, stick(review));
  return subShell(t('Send'), wrap);
}

// Reads the QR code that is on the website tab you are looking at
function scanButton() {
  const b = h('button', { class: 'link', title: t('Scan the QR code on the page you have open') }, t('Scan'));
  b.addEventListener('click', async () => {
    b.textContent = '…';
    try {
      const r = await call('scanTab');
      if (!r.pay) { toast(t('This QR code is not a crypto address or payment link')); b.textContent = t('Scan'); return; }
      go(payHash(r.pay, r.origin));
    } catch (e) { toast(e.message); b.textContent = t('Scan'); }
  });
  return b;
}

function sentView(hash, chain, to) {
  const save = to && !nameOf(to) ? h('button', { class: 'link', onclick: () => go('/contacts') }, t('Save this address as a contact')) : null;
  return h('div', { class: 'center', style: 'padding-top:40px' },
    h('div', { class: 'okmark' }, icon('check', 34)),
    h('h2', {}, t('Transaction sent')),
    h('p', { class: 'muted' }, t('It can take a minute to confirm. You can follow it in Activity')),
    h('a', { class: 'link', href: chain.explorer + (chain.txPath || '/tx/') + hash, target: '_blank', rel: 'noopener noreferrer' }, t('View on the explorer')),
    save,
    button(t('Done'), () => go('/')));
}

// ---------- activity ----------

function activityView() {
  if (isUtxo()) return utxoActivity();
  const list = h('div', { class: 'list' });
  const rows = S.history;
  const body = h('div', {});
  if (!rows.length) {
    mount(body, h('div', { class: 'empty', style: 'padding-top:70px' }, h('div', { class: 'ico' }, icon('activity', 26)), h('b', {}, t('Nothing here yet')), t('Payments and approvals you make with Sillet show up here')));
  } else {
    mount(list, rows.map((r) => {
      const chain = chainOf(r.chainId);
      const status = h('span', { class: 'chip ' + r.status }, t(r.status === 'confirmed' ? 'Confirmed' : r.status === 'failed' ? 'Failed' : 'Pending'));
      if (r.status === 'pending') {
        call('txStatus', { chainId: r.chainId, hash: r.hash }).then(async (s) => {
          if (s !== 'pending') { await call('updateHistory', { hash: r.hash, status: s }); status.textContent = t(s === 'confirmed' ? 'Confirmed' : 'Failed'); status.className = 'chip ' + s; }
        }).catch(() => {});
      }
      const pay = r.kind === 'native' || r.kind === 'transfer';
      const amount = pay ? fmt(r.value, r.decimals) + ' ' + r.symbol : r.origin;
      return h('a', { class: 'item', href: chain.explorer + (chain.txPath || '/tx/') + r.hash, target: '_blank', rel: 'noopener noreferrer' },
        pay ? h('span', { class: 'coin' }, tokenAvatar(r.symbol, 40), h('span', { class: 'badge arrow' }, icon('up', 11))) : h('div', { class: 'txic out' }, icon('link', 20)),
        h('div', { class: 'grow' }, h('div', { class: 'name' }, (pay ? t('Sent') + ' ' : '') + amount), h('div', { class: 'muted small' }, (r.to ? t('To') + ' ' + (nameOf(r.to) || short(r.to)) : t('Contract')) + ' · ' + chain.name + ' · ' + ago(r.time))),
        status);
    }));
    mount(body, list, h('div', { class: 'row gap', style: 'margin-top:12px' }, button(t('Export CSV'), exportCsv, 'ghost'), button(t('Clear list'), async () => { await call('clearHistory'); render(); }, 'ghost')));
  }
  return mainShell('/activity', body);
}

// A spreadsheet file with your activity, for taxes or your own records
function exportCsv() {
  const head = ['Date (UTC)', 'Network', 'Type', 'Amount', 'Asset', 'To', 'Contact', 'Site', 'Status', 'Transaction'];
  saveCsv(head, S.history.map((r) => {
    const pay = r.kind === 'native' || r.kind === 'transfer';
    return [new Date(r.time).toISOString(), (chainOf(r.chainId) || {}).name || r.chainId, pay ? 'Sent' : 'Contract call', pay ? formatUnits(BigInt(r.value || '0'), r.decimals) : '', pay ? r.symbol : '', r.to || '', nameOf(r.to) || '', r.origin || '', r.status, r.hash];
  }));
}

function saveCsv(head, rows) {
  const cell = (v) => {
    let x = String(v == null ? '' : v);
    if (/^[=+\-@\t\r]/.test(x)) x = "'" + x; // stops spreadsheets from running text as a formula
    return /[",\n]/.test(x) ? '"' + x.replace(/"/g, '""') + '"' : x;
  };
  const blob = new Blob(['\ufeff' + [head.map(cell).join(','), ...rows.map((r) => r.map(cell).join(','))].join('\n')], { type: 'text/csv;charset=utf-8' });
  const a = h('a', { href: URL.createObjectURL(blob), download: 'sillet-activity.csv' });
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 4000);
  toast(t('File saved'));
}

// ---------- Bitcoin, Litecoin and Dogecoin ----------

function utxoHome() {
  const acc = S.accounts[S.selected];
  const net = chainOf(S.chainId);
  const address = acc.addrs[net.chainId];
  const hide = !!S.settings.hideBalances;
  const big = h('div', { class: 'big' }, h('span', { class: 'skel', style: 'display:inline-block;width:120px;height:34px' }));
  const sub = h('div', { class: 'sub' }, net.name);
  const list = h('div', { class: 'list' }, skeletonRows(1));
  const holder = h('div', {});
  (async () => {
    let bal;
    try { bal = await call('utxoBalance', { net: net.chainId, index: acc.index }); } catch (e) { mount(list, h('div', { class: 'empty' }, h('b', {}, t('Could not load balances')), e.message)); big.textContent = '–'; return; }
    let price = null; let change = null;
    if (S.settings.prices) { try { const pr = await call('prices', { chainId: net.chainId, tokens: [] }); if (pr && pr.coin) { price = pr.coin.usd; change = pr.coin.change; } } catch (e) { /* no price */ } }
    const amount = Number(fmtUnits(BigInt(bal.balance), net.decimals));
    if (hide) big.textContent = HIDDEN;
    else if (price != null) { countUp(big, amount * price, usd, usd(amount * price)); if (typeof change === 'number') mount(sub, [net.name + ' · ' + net.symbol + ' ', h('span', { class: change >= 0 ? 'up' : 'down' }, (change >= 0 ? '+' : '') + change.toFixed(2) + '%')]); }
    else countUp(big, amount, (n) => n.toFixed(n < 1 ? 4 : 2) + ' ' + net.symbol, fmtUnits(BigInt(bal.balance), net.decimals) + ' ' + net.symbol);
    const pend = BigInt(bal.pending);
    mount(list, h('div', { class: 'item token' }, tokenAvatar(net.symbol, 40),
      h('div', { class: 'grow' }, h('div', { class: 'name sym' }, net.symbol), h('div', { class: 'muted small' }, pend !== 0n ? t('{amount} waiting for confirmation', { amount: (pend > 0n ? '+' : '') + fmtUnits(pend, net.decimals) }) : net.name)),
      h('div', { class: 'end' }, h('div', { class: 'tbal' }, hide ? HIDDEN : fmtUnits(BigInt(bal.balance), net.decimals)), price != null && !hide ? h('div', { class: 'muted small' }, usd(amount * price)) : null)));
    if (BigInt(bal.balance) === 0n) holder.append(h('div', { class: 'promo' }, icon('receive', 24), h('div', { class: 'txt' }, h('b', {}, t('Add funds to start')), t('Send some {symbol} to your address', { symbol: net.symbol })), h('button', { class: 'btn primary', onclick: () => go('/receive') }, t('Receive'))));
    if (!S.settings.prices) holder.append(h('div', { class: 'promo' }, icon('coins', 24), h('div', { class: 'txt' }, h('b', {}, t('See your balance in USD')), t('Optional, uses CoinGecko')), h('button', { class: 'btn primary', onclick: enablePricesSheet }, t('Turn on'))));
  })();
  const act = (name, label, fn, main) => h('button', { class: 'act' + (main ? ' main' : ''), onclick: fn }, h('span', { class: 'c' }, icon(name, 22)), label);
  const eye = h('button', { class: 'iconbtn eye', title: hide ? t('Show balances') : t('Hide balances'), onclick: async () => { await call('setSettings', { settings: { hideBalances: !hide } }); render(); } }, icon(hide ? 'eyeoff' : 'eye', 18));
  const spinner = icon('refresh', 22);
  return mainShell('/', h('div', {},
    h('div', { class: 'hero' }, eye, h('div', { class: 'lbl' }, t('Total balance')), big, sub,
      h('button', { class: 'addr', title: t('Copy address'), onclick: async () => { if (await copy(address)) toast(t('Address copied')); } }, short(address), icon('copy', 13))),
    h('div', { class: 'actions' }, act('send', t('Send'), () => go('/send'), true), act('receive', t('Receive'), () => go('/receive')), act('chart', t('Market'), () => go('/market')),
      h('button', { class: 'act', onclick: () => { spinner.classList.add('spin'); setTimeout(() => render(), 450); } }, h('span', { class: 'c' }, spinner), t('Refresh'))),
    holder,
    h('div', { class: 'sechead' }, h('b', {}, t('Coins'))), list,
    h('div', { class: 'foot' }, t('The public explorer of this network can see your IP address and your address. Websites cannot use this coin, only the Sillet window'))));
}

function utxoSendView() {
  const acc = S.accounts[S.selected];
  const net = chainOf(S.chainId);
  const to = h('input', { type: 'text', placeholder: net.symbol === 'BTC' ? 'bc1…' : net.symbol === 'LTC' ? 'ltc1…' : net.symbol === 'BCH' ? 'bitcoincash:q…' : 'D…', spellcheck: 'false', autocomplete: 'off' });
  const amount = h('input', { type: 'text', inputmode: 'decimal', placeholder: '0.0', autocomplete: 'off' });
  const err = errorBox();
  const wrap = h('div', {});
  const balInfo = h('span', { class: 'hint' });
  const usdInfo = h('span', { class: 'hint' });
  let sweep = false; let price = null; let balance = null;
  call('utxoBalance', { net: net.chainId, index: acc.index }).then((b) => { balance = BigInt(b.balance); balInfo.textContent = t('Balance: {amount}', { amount: fmtUnits(balance, net.decimals) + ' ' + net.symbol }); }).catch(() => {});
  if (S.settings.prices) call('prices', { chainId: net.chainId, tokens: [] }).then((pr) => { price = pr && pr.coin ? pr.coin.usd : null; showUsd(); }).catch(() => {});
  function showUsd() { const n = Number(amount.value.replace(',', '.')); usdInfo.textContent = price != null && n > 0 ? '≈ ' + usd(n * price) : ''; }
  amount.addEventListener('input', () => { sweep = false; showUsd(); });
  const max = h('button', { class: 'link', onclick: async () => {
    try {
      if (!validAddress(net.chainId, to.value.trim())) throw new Error(t('Write the address first, so Sillet can work out the fee'));
      const r = await call('utxoPrepare', { net: net.chainId, index: acc.index, to: to.value.trim(), amount: 'max' });
      const lvl = r.levels.market || Object.values(r.levels)[0];
      amount.value = fmtUnits(BigInt(lvl.amount), net.decimals); sweep = true; showUsd();
    } catch (e) { err.show(e.message); }
  } }, 'Max');
  const paste = h('span', { class: 'row', style: 'gap:12px' }, scanButton(), h('button', { class: 'link', onclick: async () => { try { to.value = (await navigator.clipboard.readText()).trim(); } catch (e) { toast(t('Cannot read the clipboard')); } } }, t('Paste')));
  const pf = takePrefill();
  if (pf) { if (pf.to) to.value = pf.to; if (pf.amount) amount.value = pf.amount; showUsd(); }
  const review = button(t('Review'), () => guarded(review, err, async () => {
    const dest = to.value.trim();
    if (!validAddress(net.chainId, dest)) throw new Error(t('That is not a valid {name} address', { name: net.name }));
    let raw;
    try { raw = parseAmount(amount.value, net.decimals); } catch (e) { throw new Error(t('Enter a valid amount')); }
    if (raw <= 0n) throw new Error(t('Enter a valid amount'));
    const amt = sweep ? 'max' : amount.value.trim();
    const r = await call('utxoPrepare', { net: net.chainId, index: acc.index, to: dest, amount: amt });
    mount(wrap, utxoConfirm(net, acc, dest, r.levels, async (level) => {
      const hash = await call('utxoSend', { net: net.chainId, index: acc.index, to: dest, amount: amt, level });
      mount(wrap, sentView(hash, net, null));
    }, () => render()));
  }));
  mount(wrap,
    prefillBanner(pf),
    field(t('Asset'), h('div', { class: 'assetbtn static' }, tokenAvatar(net.symbol, 36), h('div', { class: 'grow' }, h('div', { class: 'name' }, net.symbol), h('div', { class: 'muted small' }, net.name)))),
    field(t('To'), to, null, paste),
    h('label', { class: 'field' }, h('span', { class: 'label' }, h('span', {}, t('Amount')), max), amount, h('span', { class: 'row between' }, balInfo, usdInfo)),
    err, stick(review));
  return subShell(t('Send'), wrap);
}

function utxoConfirm(net, acc, dest, levels, onConfirm, onCancel) {
  let level = levels.market ? 'market' : Object.keys(levels)[0];
  const feeBox = h('div', { class: 'kv' });
  const amtBox = h('div', { class: 'amt' });
  const rateBox = h('div', { class: 'kv' });
  const levelsBox = h('div', { class: 'seg' });
  const err = errorBox();
  function upd() {
    const L = levels[level];
    mount(levelsBox, ['low', 'market', 'fast'].map((l) => h('button', { class: l === level ? 'on' : '', disabled: !levels[l], onclick: () => { level = l; upd(); } }, t(l === 'low' ? 'Slow' : l === 'market' ? 'Normal' : 'Fast'))));
    amtBox.textContent = fmtUnits(BigInt(L.amount), net.decimals) + ' ' + net.symbol;
    mount(feeBox, h('span', { class: 'k' }, t('Network fee')), h('span', { class: 'v' }, fmtUnits(BigInt(L.fee), net.decimals) + ' ' + net.symbol));
    mount(rateBox, h('span', { class: 'k' }, t('Total')), h('span', { class: 'v' }, fmtUnits(BigInt(L.amount) + BigInt(L.fee), net.decimals) + ' ' + net.symbol));
  }
  upd();
  const ok = button(t('Confirm and send'), () => guarded(ok, err, async () => onConfirm(level)));
  return h('div', { class: 'tx' },
    h('div', { class: 'sendhero' }, h('div', { class: 'coin' }, tokenAvatar(net.symbol, 52)), amtBox, h('div', { class: 'to' }, t('To') + ' ' + short(dest))),
    h('div', { class: 'panel' },
      h('div', { class: 'kv' }, h('span', { class: 'k' }, t('Network')), h('span', { class: 'v' }, net.name)),
      h('div', { class: 'kv' }, h('span', { class: 'k' }, t('From')), h('span', { class: 'v mono' }, acc.addrs[net.chainId])),
      h('div', { class: 'kv' }, h('span', { class: 'k' }, t('To')), h('span', { class: 'v mono' }, dest))),
    banner(t('Coins sent on this network cannot be taken back. Check the address'), 'warn'),
    levelsBox, h('div', { class: 'panel' }, feeBox, rateBox), err, stick(button(t('Cancel'), onCancel, 'ghost'), ok));
}

function utxoActivity() {
  const net = chainOf(S.chainId);
  const acc = S.accounts[S.selected];
  const body = h('div', {}, h('div', { class: 'list' }, skeletonRows(4)));
  let rows = [];
  call('utxoHistory', { net: net.chainId, index: acc.index }).then((list) => {
    rows = list;
    if (!list.length) { mount(body, h('div', { class: 'empty', style: 'padding-top:70px' }, h('div', { class: 'ico' }, icon('activity', 26)), h('b', {}, t('Nothing here yet')), t('Payments to and from this address show up here'))); return; }
    mount(body, h('div', { class: 'list' }, list.map((r) => {
      const d = BigInt(r.delta);
      const incoming = d >= 0n;
      return h('a', { class: 'item', href: net.explorer + net.txPath + r.hash, target: '_blank', rel: 'noopener noreferrer' },
        h('span', { class: 'coin' }, tokenAvatar(net.symbol, 40), h('span', { class: 'badge arrow' }, icon(incoming ? 'dn' : 'up', 11))),
        h('div', { class: 'grow' }, h('div', { class: 'name' }, (incoming ? t('Received') : t('Sent')) + ' ' + fmtUnits(incoming ? d : -d, net.decimals) + ' ' + net.symbol), h('div', { class: 'muted small' }, (r.counterparty ? (incoming ? t('From') : t('To')) + ' ' + short(r.counterparty) + ' · ' : '') + net.name + (r.time ? ' · ' + ago(r.time) : ''))),
        h('span', { class: 'chip ' + (r.confirmed ? 'confirmed' : 'pending') }, t(r.confirmed ? 'Confirmed' : 'Pending')));
    })), h('div', { class: 'row gap', style: 'margin-top:12px' }, button(t('Export CSV'), () => saveCsv(['Date (UTC)', 'Network', 'Type', 'Amount', 'Asset', 'Other address', 'Status', 'Transaction'],
      rows.map((r) => { const d = BigInt(r.delta); return [r.time ? new Date(r.time).toISOString() : '', net.name, d >= 0n ? 'Received' : 'Sent', fmtUnits(d < 0n ? -d : d, net.decimals), net.symbol, r.counterparty || '', r.confirmed ? 'confirmed' : 'pending', r.hash]; })), 'ghost')));
  }).catch((e) => mount(body, h('div', { class: 'empty' }, h('b', {}, t('Could not load activity')), e.message)));
  return mainShell('/activity', body);
}

// ---------- market ----------

const SORTS = [['cap', 'Top'], ['up', 'Gainers'], ['down', 'Losers'], ['fav', 'Starred']];
const OPEN_IN_WALLET = { bitcoin: 'bitcoin', litecoin: 'litecoin', dogecoin: 'dogecoin', 'bitcoin-cash': 'bitcoincash', ethereum: 1, binancecoin: 56, 'avalanche-2': 43114 };
let mkt = { q: '', sort: 'cap' };

const priceText = (n) => {
  if (n == null) return '–';
  if (n >= 1) return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(n);
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumSignificantDigits: 4 }).format(n);
};
const compact = (n) => (n == null ? '–' : '$' + new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 2 }).format(n));
const pct = (n) => (typeof n === 'number' ? (n >= 0 ? '+' : '') + n.toFixed(2) + '%' : '–');

function spark(line, w, hgt, up, big) {
  const svg = svgEl('svg', { viewBox: `0 0 ${w} ${hgt}`, width: big ? '100%' : w, height: hgt, class: 'spark ' + (up ? 'up' : 'down'), preserveAspectRatio: 'none' });
  if (!line || line.length < 2) return svg;
  const min = Math.min(...line); const max = Math.max(...line); const span = max - min || 1;
  const pts = line.map((v, i) => [(i / (line.length - 1)) * w, 2 + (hgt - 4) * (1 - (v - min) / span)]);
  const d = pts.map((p, i) => (i ? 'L' : 'M') + p[0].toFixed(1) + ' ' + p[1].toFixed(1)).join('');
  if (big) svg.append(svgEl('path', { d: d + `L${w} ${hgt}L0 ${hgt}Z`, class: 'area' }));
  svg.append(svgEl('path', { d, class: 'line', pathLength: 1, 'vector-effect': 'non-scaling-stroke' }));
  return svg;
}

function coinSheet(c) {
  const fav = (S.favorites || []).includes(c.id);
  const target = OPEN_IN_WALLET[c.id];
  openSheet(c.name, (close) => {
    const star = h('button', { class: 'iconbtn' + (fav ? ' starred' : ''), title: t('Star'), onclick: async () => { await call('toggleFavorite', { id: c.id }); await refresh(); close(); render(); } }, icon('star', 20));
    const stat = (k, v) => h('div', { class: 'kv' }, h('span', { class: 'k' }, k), h('span', { class: 'v' }, v));
    return h('div', {},
      h('div', { class: 'coinhead' }, tokenAvatar(c.symbol, 44), h('div', { class: 'grow' }, h('div', { class: 'name' }, c.symbol), h('div', { class: 'muted small' }, c.rank ? '#' + c.rank + ' · ' + c.name : c.name)), star),
      h('div', { class: 'coinprice' }, priceText(c.price), h('span', { class: c.c24 >= 0 ? 'up' : 'down' }, pct(c.c24))),
      h('div', { class: 'chartbox' }, spark(c.line, 300, 110, (c.c7 ?? c.c24 ?? 0) >= 0, true), h('div', { class: 'muted small' }, t('Last 7 days') + ' · ' + pct(c.c7))),
      h('div', { class: 'panel' }, stat(t('Market cap'), compact(c.cap)), stat(t('Volume (24h)'), compact(c.volume)), stat(t('24h high'), priceText(c.high)), stat(t('24h low'), priceText(c.low)), stat(t('All-time high'), priceText(c.ath))),
      target != null ? button(t('Open in wallet'), async () => { await call('selectChain', { chainId: target }); close(); go('/'); render(); }) : null,
      h('p', { class: 'muted small', style: 'text-align:center' }, t('Prices by CoinGecko')));
  });
}

function marketView() {
  const body = h('div', {});
  if (!S.settings.prices) {
    mount(body, h('div', { class: 'empty', style: 'padding-top:50px' }, h('div', { class: 'ico' }, icon('chart', 26)), h('b', {}, t('Market')), t('Prices, 24 hour changes and charts of the top coins. Uses CoinGecko, so it is off until you turn it on')),
      button(t('Turn on'), enablePricesSheet));
    return mainShell('/market', body);
  }
  const list = h('div', { class: 'list' }, skeletonRows(6));
  const search = h('input', { type: 'text', placeholder: t('Search coins'), value: mkt.q, spellcheck: 'false', autocomplete: 'off', class: 'searchin' });
  const seg = h('div', { class: 'seg four' });
  let data = [];
  function draw() {
    mount(seg, SORTS.map(([k, label]) => h('button', { class: mkt.sort === k ? 'on' : '', onclick: () => { mkt.sort = k; draw(); } }, k === 'fav' ? [icon('star', 13), ' ' + t(label)] : t(label))));
    const q = mkt.q.trim().toLowerCase();
    let rows = data.filter((c) => !q || c.name.toLowerCase().includes(q) || c.symbol.toLowerCase().includes(q));
    if (mkt.sort === 'fav') rows = rows.filter((c) => (S.favorites || []).includes(c.id));
    if (mkt.sort === 'up') rows = [...rows].filter((c) => c.c24 != null).sort((a, b) => b.c24 - a.c24);
    if (mkt.sort === 'down') rows = [...rows].filter((c) => c.c24 != null).sort((a, b) => a.c24 - b.c24);
    if (!rows.length) { mount(list, h('div', { class: 'empty' }, h('b', {}, mkt.sort === 'fav' ? t('No starred coins') : t('Nothing found')), mkt.sort === 'fav' ? t('Open a coin and tap the star to keep it here') : '')); return; }
    mount(list, rows.slice(0, 100).map((c) => h('button', { class: 'item mk', onclick: () => coinSheet(c) },
      tokenAvatar(c.symbol, 38),
      h('div', { class: 'grow' }, h('div', { class: 'name sym' }, c.symbol, (S.favorites || []).includes(c.id) ? h('span', { class: 'favdot' }, icon('star', 11)) : null), h('div', { class: 'muted small' }, c.name)),
      spark(c.line, 56, 24, (c.c7 ?? c.c24 ?? 0) >= 0),
      h('div', { class: 'end' }, h('div', { class: 'tbal' }, priceText(c.price)), h('div', { class: 'small ' + (c.c24 >= 0 ? 'up' : 'down') }, pct(c.c24))))));
  }
  search.addEventListener('input', () => { mkt.q = search.value; draw(); });
  call('market').then((rows) => { data = rows || []; draw(); }).catch((e) => mount(list, h('div', { class: 'empty' }, h('b', {}, t('Could not load the market')), e.message)));
  mount(body, h('div', { class: 'searchwrap' }, icon('search', 16), search), seg, list, h('div', { class: 'foot' }, t('Prices by CoinGecko. They can be a little behind')));
  return mainShell('/market', body);
}

// ---------- payments from pages, QR codes and the right click menu ----------

async function payRoute(r) {
  const q = new URLSearchParams(r.slice(r.indexOf('?') + 1));
  const raw = q.get('net') || 'evm';
  const net = /^\d+$/.test(raw) ? Number(raw) : raw;
  const target = net === 'evm' ? (S.chains.some((c) => !c.utxo && c.chainId === S.chainId) ? S.chainId : S.evmChainId) : net;
  if (!chainOf(target)) throw new Error(t('Sillet does not support this network'));
  await call('selectChain', { chainId: target });
  prefill = { to: q.get('to') || '', amount: q.get('amount') || '', raw: q.get('raw') || '', token: q.get('token') || '', label: q.get('label') || '', from: q.get('from') || '' };
  location.replace('#/send');
}

function scanView(r) {
  const q = new URLSearchParams(r.slice(r.indexOf('?') + 1));
  const text = q.get('text');
  if (!text) {
    return subShell(t('Scan QR'), h('div', { class: 'empty', style: 'padding-top:60px' }, h('div', { class: 'ico' }, icon('search', 26)), h('b', {}, t('No QR code found')),
      t('Make the code bigger on the page and try again. Right click it and choose Scan QR with Sillet'), h('div', { style: 'margin-top:18px;width:100%' }, button(t('Done'), closeWindow, 'ghost'))), '/');
  }
  return subShell(t('Scan QR'), h('div', {},
    banner(t('This QR code is not a crypto address or payment link'), 'info'),
    h('div', { class: 'addrbox' }, text),
    button(t('Copy text'), async () => { if (await copy(text)) toast(t('Copied')); }),
    button(t('Done'), closeWindow, 'ghost')), '/');
}

// ---------- WalletConnect ----------

function wcView(r) {
  const q = new URLSearchParams(r.includes('?') ? r.slice(r.indexOf('?') + 1) : '');
  const uri = h('input', { type: 'text', placeholder: 'wc:…', spellcheck: 'false', autocomplete: 'off', value: q.get('uri') || '' });
  const err = errorBox();
  const done = h('div', {});
  const connect = button(t('Connect'), () => guarded(connect, err, async () => {
    await call('wcPair', { uri: uri.value.trim() });
    mount(done, banner(t('Linking… A window will open to ask you to approve the connection'), 'info'));
    setTimeout(() => go('/sites'), 2500);
  }));
  const paste = h('button', { class: 'link', onclick: async () => { try { uri.value = (await navigator.clipboard.readText()).trim(); } catch (e) { toast(t('Cannot read the clipboard')); } } }, t('Paste'));
  const body = !S.wcReady
    ? h('div', { class: 'empty', style: 'padding-top:50px' }, h('div', { class: 'ico' }, icon('link', 26)), h('b', {}, t('WalletConnect is not ready in this version')), t('The Sillet team still has to add its WalletConnect project id'))
    : h('div', {},
      h('p', { class: 'muted' }, t('On a website, choose WalletConnect and copy the link (or show the QR code and press Scan here). Then paste it below')),
      field(t('WalletConnect link'), uri, null, h('span', { class: 'row', style: 'gap:12px' }, scanButton(), paste)),
      err, done, stick(connect));
  return subShell(t('WalletConnect'), body, '/sites');
}

// ---------- sites ----------

function sitesView() {
  const body = h('div', {});
  if (!S.sites.length) {
    mount(body, h('div', { class: 'empty', style: 'padding-top:60px' }, h('div', { class: 'ico' }, icon('globe', 26)), h('b', {}, t('No sites are connected')),
      t('Open a website and press Connect wallet. Sillet shows up in the list'),
      h('div', { class: 'feat', style: 'margin-top:18px' }, h('div', {}, icon('shield', 20), t('A site sees nothing until you approve it')), h('div', {}, icon('key', 20), t('It can never move funds without your approval')))));
  } else {
    mount(body, h('div', { class: 'list' }, S.sites.map((s) => h('div', { class: 'site' },
      h('div', { class: 'top' }, siteAvatar(s.label || s.origin, 38), h('div', { class: 'grow', style: 'flex:1;min-width:0' }, h('div', { class: 'name', style: 'font-weight:650;word-break:break-all' }, s.label || s.origin), h('div', { class: 'muted small' }, ((chainOf(s.chainId) || {}).name || '') + (s.wc ? ' · WalletConnect' : '')))),
      h('div', { class: 'row2' },
        s.wc ? h('div', { class: 'muted small', style: 'flex:1' }, (S.accounts.find((a) => a.address === s.account) || {}).name || short(s.account)) : h('select', { onchange: async (e) => { await call('setSiteAccount', { origin: s.origin, address: e.target.value }); toast(t('Saved')); } },
          S.accounts.map((a) => h('option', { value: a.address, selected: a.address === s.account }, a.name))),
        h('button', { class: 'btn ghost sm', onclick: async () => { await call('revokeSite', { origin: s.origin }); render(); } }, t('Disconnect')))))));
  }
  return mainShell('/sites', h('div', {}, h('button', { class: 'promo wcbtn', onclick: () => go('/wc') }, icon('link', 24), h('div', { class: 'txt' }, h('b', {}, t('Connect with WalletConnect')), t('For sites that show a QR code or a wc: link')), icon('right', 18)), body));
}

// ---------- contacts ----------

function contactsView() {
  const name = h('input', { type: 'text', placeholder: t('Name'), maxlength: 24, autocomplete: 'off' });
  const addr = h('input', { type: 'text', placeholder: '0x…', spellcheck: 'false', autocomplete: 'off' });
  const err = errorBox();
  const add = button(t('Save contact'), () => guarded(add, err, async () => { await call('addContact', { name: name.value, address: addr.value.trim() }); toast(t('Saved')); render(); }));
  const rows = S.contacts || [];
  const list = rows.length
    ? h('div', { class: 'list' }, rows.map((c) => h('div', { class: 'item' },
      identicon(c.address, 40),
      h('div', { class: 'grow' }, h('div', { class: 'name' }, c.name), h('div', { class: 'muted small mono' }, short(c.address))),
      h('button', { class: 'iconbtn', title: t('Send'), onclick: () => go('/send/' + c.address) }, icon('send', 18)),
      h('button', { class: 'iconbtn', title: t('Copy address'), onclick: async () => { if (await copy(c.address)) toast(t('Address copied')); } }, icon('copy', 18)),
      h('button', { class: 'iconbtn', title: t('Remove'), onclick: async () => { await call('removeContact', { address: c.address }); render(); } }, icon('trash', 18)))))
    : h('div', { class: 'empty' }, h('div', { class: 'ico' }, icon('user', 26)), h('b', {}, t('No contacts yet')), t('Save the addresses you use often and send to them with one tap'));
  return subShell(t('Contacts'), h('div', {},
    h('div', { class: 'card', style: 'padding:14px;margin-bottom:14px' }, field(t('Name'), name), field(t('Address'), addr), err, add),
    h('div', { class: 'sechead' }, h('b', {}, t('Saved contacts') + (rows.length ? ' · ' + rows.length : ''))),
    list));
}

// ---------- add token ----------

function addTokenView() {
  const addr = h('input', { type: 'text', placeholder: '0x…', spellcheck: 'false' });
  const err = errorBox();
  const btn = button(t('Add'), () => guarded(btn, err, async () => { await call('addToken', { chainId: S.chainId, address: addr.value.trim() }); go('/'); }));
  return subShell(t('Add token'), h('div', {},
    h('div', { class: 'row', style: 'gap:8px;margin:6px 0' }, chainAvatar(S.chainId, 22), h('b', {}, chainOf(S.chainId).name)),
    h('p', { class: 'muted' }, t('Paste the token contract address on {network}. Sillet reads the name and decimals from the network', { network: chainOf(S.chainId).name })),
    field(t('Token contract'), addr), err, btn));
}

// ---------- settings ----------

function settingsView() {
  const out = h('div', { class: 'settings' });
  const sec = (title, ...kids) => h('section', {}, h('h3', {}, title), h('div', { class: 'list' }, ...kids));
  const rowOf = (ic, title, desc, control) => h('div', { class: 'setrow' }, h('div', { class: 'si' }, icon(ic, 18)), h('div', { class: 'grow' }, h('div', {}, title), desc ? h('div', { class: 'd' }, desc) : null), control || null);

  function renameRow(a, i) {
    const row = h('div', { class: 'setrow' });
    const show = () => mount(row, identicon(a.address, 34), h('div', { class: 'grow' }, h('div', {}, a.name), h('div', { class: 'd mono' }, short(a.address))),
      i === S.selected ? h('span', { class: 'chip confirmed' }, t('Active')) : null,
      h('button', { class: 'iconbtn', title: t('Rename'), onclick: edit }, icon('tag', 17)));
    const edit = () => {
      const inp = h('input', { type: 'text', value: a.name, maxlength: 24 });
      const save = async () => { await call('renameAccount', { index: i, name: inp.value }); render(); };
      inp.addEventListener('keydown', (e) => { if (e.key === 'Enter') save(); });
      mount(row, identicon(a.address, 34), h('div', { class: 'grow' }, inp), h('button', { class: 'btn primary sm', onclick: save }, t('Save')));
      inp.focus();
      inp.select();
    };
    show();
    return row;
  }
  const accounts = sec(t('Accounts'),
    S.accounts.map(renameRow),
    h('div', { style: 'padding:10px 14px' }, button(t('Add account'), async () => { await call('addAccount'); render(); }, 'ghost')));
  const people = sec(t('Contacts'), h('button', { class: 'setrow item', onclick: () => go('/contacts') },
    h('div', { class: 'si' }, icon('user', 18)), h('div', { class: 'grow' }, h('div', {}, t('Address book')), h('div', { class: 'd' }, (S.contacts || []).length ? t('{n} saved', { n: S.contacts.length }) : t('Save the addresses you use often'))), icon('right', 18)));

  const lock = h('select', { onchange: async (e) => { await call('setSettings', { settings: { autolockMin: Number(e.target.value) } }); toast(t('Saved')); } },
    [[5, '5 min'], [15, '15 min'], [60, '1 h'], [0, t('Until the browser closes')]].map(([v, l]) => h('option', { value: v, selected: Number(S.settings.autolockMin) === v }, l)));
  const lang = h('select', { onchange: async (e) => { await call('setSettings', { settings: { lang: e.target.value } }); render(); } },
    [['auto', t('Automatic')], ['en', 'English'], ['es', 'Español']].map(([v, l]) => h('option', { value: v, selected: S.settings.lang === v }, l)));
  const detectSwitch = h('label', { class: 'switch' }, h('input', { type: 'checkbox', checked: S.settings.detectPages !== false, onchange: async (e) => { await call('setSettings', { settings: { detectPages: e.target.checked } }); toast(t('Saved')); } }));
  const priceSwitch = h('label', { class: 'switch' }, h('input', { type: 'checkbox', checked: !!S.settings.prices, onchange: async (e) => { await call('setSettings', { settings: { prices: e.target.checked } }); toast(e.target.checked ? t('Prices on') : t('Prices off')); } }));

  const phraseBox = h('div', { style: 'padding:0 14px' });
  const reveal = rowOf('eye', t('Show recovery phrase'), t('Needs your password'), h('button', { class: 'btn ghost sm', onclick: () => {
    const pw = h('input', { type: 'password', placeholder: t('Password'), autocomplete: 'current-password' });
    const err = errorBox();
    const go1 = button(t('Show'), () => guarded(go1, err, async () => {
      const phrase = await call('revealPhrase', { password: pw.value });
      mount(phraseBox, banner(t('Never share these words. Anyone who sees them can take your funds'), 'risk'), h('div', { class: 'words' }, phrase.split(' ').map((w, i) => h('div', { class: 'word' }, h('span', {}, i + 1), w))), button(t('Hide'), () => mount(phraseBox), 'ghost'));
    }));
    mount(phraseBox, h('div', { style: 'padding-bottom:12px' }, pw, err, go1));
    pw.focus();
  } }, t('Show')));

  const pwBox = h('div', { style: 'padding:0 14px' });
  const chg = rowOf('key', t('Change password'), null, h('button', { class: 'btn ghost sm', onclick: () => {
    const a = h('input', { type: 'password', placeholder: t('Current password'), autocomplete: 'current-password' });
    const b = h('input', { type: 'password', placeholder: t('New password'), autocomplete: 'new-password', style: 'margin-top:8px' });
    const err = errorBox();
    const ok = button(t('Save'), () => guarded(ok, err, async () => { await call('changePassword', { oldPassword: a.value, newPassword: b.value }); mount(pwBox, h('div', { class: 'okline' }, t('Password changed'))); }));
    mount(pwBox, h('div', { style: 'padding-bottom:12px' }, a, b, err, ok));
  } }, t('Change')));

  const security = sec(t('Security'), rowOf('lock', t('Lock automatically'), null, lock), reveal, phraseBox, chg, pwBox);
  const hideSwitch = h('label', { class: 'switch' }, h('input', { type: 'checkbox', checked: !!S.settings.hideBalances, onchange: async (e) => { await call('setSettings', { settings: { hideBalances: e.target.checked } }); toast(e.target.checked ? t('Balances hidden') : t('Balances visible')); } }));
  const prefs = sec(t('Preferences'), rowOf('globe', t('Language'), null, lang), rowOf('eyeoff', t('Private mode'), t('Hides balances on the main screen'), hideSwitch), rowOf('coins', t('Show prices in USD'), t('Uses CoinGecko. Off by default'), priceSwitch),
    rowOf('search', t('Find payments on pages'), t('Shows Pay with Sillet on payment pages and opens bitcoin: and ethereum: links. Checked on this device'), detectSwitch),
    (S.scannerOff || []).length ? rowOf('eyeoff', t('Hidden sites'), t('{n} sites'  , { n: S.scannerOff.length }), h('button', { class: 'btn ghost sm', onclick: async () => { await call('scannerReset'); toast(t('Saved')); render(); } }, t('Show again'))) : null);
  const data = sec(t('Your data'), rowOf('activity', t('Activity'), t('Download it as a CSV file'), h('button', { class: 'btn ghost sm', onclick: exportCsv }, t('Export'))));

  const customs = [];
  for (const c of S.chains) {
    for (const tk of c.tokens.filter((x) => x.custom)) {
      customs.push(h('div', { class: 'setrow' }, tokenAvatar(tk.symbol, 34), h('div', { class: 'grow' }, h('div', {}, tk.symbol), h('div', { class: 'd' }, c.name)), h('button', { class: 'iconbtn', title: t('Remove'), onclick: async () => { await call('removeToken', { chainId: c.chainId, address: tk.address }); render(); } }, icon('trash', 18))));
    }
  }

  const resetBox = h('div', { style: 'padding:0 14px' });
  const reset = rowOf('trash', t('Reset Sillet'), t('Deletes the wallet from this browser'), h('button', { class: 'btn ghost sm', onclick: () => {
    const word = h('input', { type: 'text', placeholder: 'RESET' });
    const ok = button(t('Erase this wallet from the browser'), async () => { if (word.value === 'RESET') { await call('reset'); go('/'); render(); } }, 'danger');
    mount(resetBox, h('div', { style: 'padding-bottom:12px' }, banner(t('This deletes the wallet from this browser. You can only get it back with your recovery phrase. Type RESET to confirm'), 'risk'), word, ok));
  } }, t('Reset')));

  const about = h('section', {}, h('h3', {}, t('About')),
    h('div', { class: 'card', style: 'padding:16px' },
      h('div', { class: 'row', style: 'gap:12px' }, brandLogo(40), h('div', {}, h('b', {}, 'Sillet ' + api.runtime.getManifest().version), h('div', { class: 'muted small' }, t('Open source · MIT license · by the Raton team')))),
      banner(t('Sillet is new software and has not been audited yet. Please use only small amounts you can afford to lose'), 'warn'),
      h('p', { class: 'muted small' }, t('Sillet talks only to public nodes of the network you use. There are no accounts and no tracking')),
      h('div', { class: 'row', style: 'gap:16px' }, h('a', { class: 'link', href: 'https://raton.fun/wallet', target: '_blank', rel: 'noopener noreferrer' }, t('Page')), h('a', { class: 'link', href: 'https://raton.fun/wallet/secure', target: '_blank', rel: 'noopener noreferrer' }, t('Secure')), h('a', { class: 'link', href: 'https://github.com/sillycs/Sillet-Wallet', target: '_blank', rel: 'noopener noreferrer' }, 'GitHub'))));

  mount(out, accounts, people, security, prefs, data, customs.length ? sec(t('Your tokens'), customs) : null, about, sec(t('Danger zone'), reset, resetBox));
  return mainShell('/settings', out);
}

// ---------- requests from websites ----------

const RISKY = new Set(['unlimitedApproval', 'approvalForAll', 'spenderIsWallet', 'permitSignature', 'orderSignature', 'contractCreation']);

async function approveView() {
  const id = route().split('/')[2];
  const rec = await call('approval:get', { id });
  if (!rec) {
    return plainShell(h('div', { class: 'center', style: 'padding-top:40px' }, h('div', { class: 'txic' }, icon('info', 22)), h('p', { class: 'muted' }, t('This request has expired. Try it again from the website')), button(t('Close'), closeWindow)));
  }
  if (!S.unlocked) return plainShell(unlockForm(() => render(), t('Unlock to continue')));

  window.__beat = window.__beat || setInterval(() => api.runtime.sendMessage({ type: 'ping' }).catch(() => {}), 10000);
  const reject = async () => { try { await call('approval:reject', { id }); } catch (e) { /* gone */ } closeWindow(); };
  const accept = async (decision) => { await call('approval:resolve', { id, decision }); closeWindow(); };
  const insecure = rec.origin.startsWith('http://') && !/^http:\/\/(localhost|127\.0\.0\.1)(:|$)/.test(rec.origin);
  const head = (text) => h('div', {},
    h('div', { class: 'sitehead' }, siteAvatar(rec.label || rec.origin, 44), h('div', { style: 'min-width:0' }, h('div', { class: 'origin' }, rec.label || rec.origin), h('div', { class: 'sm' }, insecure ? t('Not secure') : rec.viaWc ? t('WalletConnect request') : t('Website request')))),
    insecure ? banner(t('This site is not using a secure connection (https)'), 'risk') : null,
    rec.viaWc && rec.verified === 'INVALID' ? banner(t('WalletConnect says this app is not who it claims to be. Do not continue'), 'risk') : null,
    rec.viaWc && rec.verified === 'UNKNOWN' ? banner(t('WalletConnect could not verify this app. Only connect if you started this from a site you trust'), 'warn') : null,
    h('h2', {}, text));
  const err = errorBox();
  const confirmBtn = (label, fn) => { const b = button(label, () => guarded(b, err, fn)); return b; };
  let body;

  switch (rec.kind) {
    case 'connect': {
      let pick = S.accounts[S.selected].address;
      body = h('div', {}, head(t('Connect to this site?')),
        h('p', { class: 'muted' }, t('It will see the address you choose and your balances, and can ask you to sign. It can never move funds without your approval')),
        rec.chains && rec.chains.length ? h('p', { class: 'muted small' }, t('Networks: {list}', { list: rec.chains.join(', ') })) : null,
        h('div', { class: 'accounts' }, S.accounts.map((a) => h('label', { class: 'pickrow' },
          h('input', { type: 'radio', name: 'acc', checked: a.address === pick, onchange: () => { pick = a.address; } }),
          identicon(a.address, 34),
          h('div', {}, h('div', {}, a.name), h('div', { class: 'muted small mono' }, short(a.address)))))),
        err, stick(button(t('Cancel'), reject, 'ghost'), confirmBtn(t('Connect'), () => accept({ account: pick }))));
      break;
    }
    case 'sign': {
      body = h('div', {}, head(t('Sign this message?')),
        h('div', { class: 'panel' }, h('div', { class: 'kv' }, h('span', { class: 'k' }, t('Account')), h('span', { class: 'v mono' }, short(rec.account)))),
        h('pre', { class: 'msg' }, rec.text != null ? rec.text : rec.hex),
        rec.text == null ? banner(t('This message is not readable text. Only sign it if you trust this site'), 'risk') : null,
        banner(t('Signing proves you own this address. Never sign a message you do not understand'), 'warn'),
        err, stick(button(t('Cancel'), reject, 'ghost'), confirmBtn(t('Sign'), () => accept({}))));
      break;
    }
    case 'signTyped': {
      body = h('div', {}, head(t('Sign this request?')),
        h('div', { class: 'panel' }, h('div', { class: 'kv' }, h('span', { class: 'k' }, t('Network')), h('span', { class: 'v' }, rec.chainName))),
        (rec.warnings || []).map((w) => banner(t(WARN[w]), RISKY.has(w) ? 'risk' : 'warn')),
        h('pre', { class: 'msg' }, JSON.stringify({ domain: rec.typed.domain, [rec.typed.primaryType]: rec.typed.message }, null, 2)),
        err, stick(button(t('Cancel'), reject, 'ghost'), confirmBtn(t('Sign'), () => accept({}))));
      break;
    }
    case 'tx': {
      const full = { ...rec, nativeSymbol: rec.nativeSymbol, nativeDecimals: rec.nativeDecimals };
      body = h('div', {}, head(t('Confirm this transaction?')), txConfirm(full, (level) => accept({ level }), t('Confirm'), reject));
      break;
    }
    case 'switchChain': {
      body = h('div', {}, head(t('Switch network?')),
        h('div', { class: 'panel' }, h('div', { class: 'kv' }, h('span', { class: 'k' }, t('From')), h('span', { class: 'v' }, rec.fromName)), h('div', { class: 'kv' }, h('span', { class: 'k' }, t('To')), h('span', { class: 'v' }, rec.toName))),
        h('p', { class: 'muted' }, t('{origin} wants to change from {a} to {b}', { origin: rec.origin, a: rec.fromName, b: rec.toName })),
        err, stick(button(t('Cancel'), reject, 'ghost'), confirmBtn(t('Switch'), () => accept({}))));
      break;
    }
    case 'watchAsset': {
      body = h('div', {}, head(t('Add this token?')),
        h('div', { class: 'panel' },
          h('div', { class: 'kv' }, h('span', { class: 'k' }, t('Token')), h('span', { class: 'v' }, rec.token.symbol)),
          h('div', { class: 'kv' }, h('span', { class: 'k' }, t('Contract')), h('span', { class: 'v mono' }, rec.token.address)),
          h('div', { class: 'kv' }, h('span', { class: 'k' }, t('Network')), h('span', { class: 'v' }, rec.chainName))),
        err, stick(button(t('Cancel'), reject, 'ghost'), confirmBtn(t('Add token'), () => accept({}))));
      break;
    }
    default: body = h('p', {}, 'Unknown request');
  }
  return plainShell(body);
}

// ---------- router ----------

async function render() {
  try {
    await refresh();
    const r = route();
    if (r.startsWith('/onboard')) { mount(root, onboardView()); document.body.className = 'wide'; return; }
    document.body.className = r.startsWith('/approve/') ? 'approve' : '';
    if (r.startsWith('/approve/')) { mount(root, await approveView()); return; }
    if (!S.hasVault) { mount(root, welcomeView()); return; }
    if (!S.unlocked) { mount(root, plainShell(unlockForm(() => render()))); return; }
    if (r.startsWith('/pay')) { await payRoute(r); return; }
    if (r.startsWith('/wc')) { mount(root, wcView(r)); return; }
    if (r.startsWith('/scan')) { document.body.className = 'approve'; mount(root, scanView(r)); return; }
    const views = { '/': homeView, '/send': sendView, '/receive': receiveView, '/activity': activityView, '/sites': sitesView, '/settings': settingsView, '/addtoken': addTokenView, '/contacts': contactsView, '/market': marketView };
    mount(root, (views['/' + r.split('/')[1]] || homeView)());
  } catch (e) {
    mount(root, h('div', { class: 'center' }, h('p', { class: 'error' }, e.message), h('button', { class: 'btn ghost', onclick: render }, 'Retry')));
  }
}

window.addEventListener('hashchange', render);
api.runtime.onMessage.addListener(() => {});
render();
