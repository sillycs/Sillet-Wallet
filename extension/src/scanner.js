// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Runs on web pages. Looks for crypto payment links and payment addresses and offers "Pay with Sillet".
// The page text is checked on this device. Only strings that look like crypto addresses go to the Sillet background, which checks their checksum
(() => {
  if (window.top !== window || window.__silletScanner) return;
  window.__silletScanner = true;
  const api = globalThis.browser || globalThis.chrome;
  // eslint-disable-next-line no-undef
  const TESTING = typeof __SILLET_TEST_BASE__ === 'string' && __SILLET_TEST_BASE__ !== '';
  const ES = String(navigator.language || '').toLowerCase().startsWith('es');
  const T = (en, es) => (ES ? es : en);

  const PAY_LINK = /^(bitcoin|litecoin|dogecoin|bitcoincash|ethereum):/i;
  const CANDIDATE = /(?:bitcoincash:)?[qp][0-9a-z]{41}\b|\b0x[0-9a-fA-F]{40}\b|\b(?:bc1|ltc1)[0-9a-z]{20,70}\b|\b[13LMD][1-9A-HJ-NP-Za-km-z]{25,34}\b/gi;
  const WORDS = /(pay(ment)?|deposit|invoice|checkout|donat|send (exactly|only)|amount due|wallet address|crypto|pago|dep[oó]sito|donaci|enviar|factura|paiement|zahlung)/i;

  let enabled = false;
  let shown = '';
  let timer = 0;
  let host = null;

  function sendMsg(msg) {
    try { return Promise.resolve(api.runtime.sendMessage(msg)).catch(() => null); } catch (e) { return Promise.resolve(null); }
  }
  const unwrap = (r) => (r && r.ok ? r.result : null);

  function collect() {
    const links = [...document.querySelectorAll('a[href]')].map((a) => a.getAttribute('href') || '').filter((h) => PAY_LINK.test(h)).slice(0, 20);
    let text = '';
    try { text = (document.body && document.body.innerText) || ''; } catch (e) { text = ''; }
    if (text.length > 300000) text = text.slice(0, 300000);
    const vals = [...document.querySelectorAll('input[type=text], input:not([type]), textarea, code')].slice(0, 80).map((el) => el.value || el.textContent || '').join(' ');
    const cands = [...new Set(((text + ' ' + vals).match(CANDIDATE) || []))].slice(0, 40);
    return { links, cands, words: WORDS.test(text.slice(0, 20000)) };
  }

  async function scan() {
    const { links, cands, words } = collect();
    if (!links.length && !(cands.length && words)) { removeBadge(); return; }
    const res = unwrap(await sendMsg({ type: 'detect', links, candidates: cands }));
    if (!res || !res.enabled) { enabled = false; removeBadge(); return; }
    enabled = true;
    const items = [];
    res.links.forEach((p, i) => { if (p) items.push({ ...p, text: links[i] }); });
    // Pages full of addresses are usually block explorers or trading screens, so only small sets count as a payment page
    if (!items.length && res.addresses.length >= 1 && res.addresses.length <= 3 && words) res.addresses.forEach((a) => items.push({ ...a, text: a.to }));
    const key = items.map((i) => i.text).join('|');
    if (!items.length) { removeBadge(); return; }
    if (key !== shown) { shown = key; drawBadge(items.slice(0, 3)); }
  }

  const schedule = () => { clearTimeout(timer); timer = setTimeout(scan, 1500); };

  function removeBadge() { if (host) { host.remove(); host = null; shown = ''; } }

  const NETNAME = { bitcoin: 'Bitcoin', litecoin: 'Litecoin', dogecoin: 'Dogecoin', bitcoincash: 'Bitcoin Cash', evm: 'EVM' };
  const short = (a) => (a.length > 18 ? a.replace(/^bitcoincash:/, '').slice(0, 8) + '…' + a.slice(-6) : a);

  const CSS = `
:host{all:initial}
*{box-sizing:border-box;font-family:system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
.wrap{position:fixed;right:16px;bottom:16px;z-index:2147483647;display:flex;flex-direction:column;align-items:flex-end;gap:8px;color:#F4F4F5}
.pill{display:flex;align-items:center;gap:8px;background:#111113;color:#F4F4F5;border:0;border-radius:999px;padding:9px 14px 9px 10px;font-size:13px;font-weight:650;cursor:pointer;box-shadow:0 6px 24px rgba(0,0,0,.45);animation:in .28s ease-out}
.pill:hover{background:#18181B}
.logo{width:22px;height:22px;border-radius:7px;background:#F4F4F5;color:#09090B;display:grid;place-items:center;font-weight:800;font-size:13px}
.card{width:296px;background:#111113;border-radius:16px;padding:12px;box-shadow:0 10px 36px rgba(0,0,0,.55);animation:in .22s ease-out}
.card h4{margin:2px 4px 10px;font-size:13px;font-weight:700}
.row{background:#18181B;border-radius:12px;padding:10px;margin-bottom:8px}
.net{font-size:12px;color:#8C8C96;margin-bottom:2px}
.addr{font-family:ui-monospace,Menlo,Consolas,monospace;font-size:12.5px;word-break:break-all}
.amt{font-size:13px;font-weight:700;margin-top:4px}
.btns{display:flex;gap:6px;margin-top:8px}
button.b{flex:1;border:0;border-radius:10px;padding:8px 10px;font-size:13px;font-weight:650;cursor:pointer;background:#222226;color:#F4F4F5}
button.b.main{background:#F4F4F5;color:#09090B}
button.b:hover{filter:brightness(1.12)}
.foot{display:flex;justify-content:space-between;margin:4px 2px 0}
button.l{background:none;border:0;color:#8C8C96;font-size:12px;cursor:pointer;padding:4px}
button.l:hover{color:#F4F4F5}
@keyframes in{from{opacity:0;transform:translateY(8px)}to{opacity:1;transform:none}}
@media (prefers-reduced-motion:reduce){*{animation:none!important}}`;

  function el(tag, cls, text) { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }

  function drawBadge(items) {
    removeBadge();
    host = document.createElement('sillet-pay');
    host.setAttribute('data-sillet-scanner', '1');
    const root = host.attachShadow({ mode: TESTING ? 'open' : 'closed' });
    const style = document.createElement('style');
    style.textContent = CSS;
    const wrap = el('div', 'wrap');
    const pill = el('button', 'pill');
    pill.type = 'button';
    pill.append(el('span', 'logo', 'S'), el('span', '', items.length === 1 && items[0].amount ? T('Pay with Sillet', 'Pagar con Sillet') : T('Pay with Sillet', 'Pagar con Sillet')));
    let card = null;
    function openCard() {
      if (card) { card.remove(); card = null; return; }
      card = el('div', 'card');
      card.append(el('h4', '', T('Crypto payment found on this page', 'Pago cripto encontrado en esta página')));
      for (const it of items) {
        const row = el('div', 'row');
        row.append(el('div', 'net', NETNAME[it.net] || (typeof it.net === 'number' ? 'EVM' : String(it.net))), el('div', 'addr', short(it.to)));
        if (it.amount) row.append(el('div', 'amt', it.amount + ' ' + ({ bitcoin: 'BTC', litecoin: 'LTC', dogecoin: 'DOGE', bitcoincash: 'BCH' }[it.net] || '')));
        const btns = el('div', 'btns');
        const pay = el('button', 'b main', T('Pay with Sillet', 'Pagar con Sillet'));
        pay.type = 'button';
        pay.addEventListener('click', (e) => { sendMsg({ type: 'pay-open', text: it.text, trusted: e.isTrusted === true }); });
        const cp = el('button', 'b', T('Copy', 'Copiar'));
        cp.type = 'button';
        cp.addEventListener('click', async () => { try { await navigator.clipboard.writeText(it.to); cp.textContent = T('Copied', 'Copiado'); } catch (err) { cp.textContent = '–'; } });
        btns.append(pay, cp);
        row.append(btns);
        card.append(row);
      }
      const foot = el('div', 'foot');
      const hide = el('button', 'l', T('Hide on this site', 'Ocultar en este sitio'));
      hide.type = 'button';
      hide.addEventListener('click', () => { sendMsg({ type: 'scanner-hide' }); enabled = false; removeBadge(); });
      const close = el('button', 'l', T('Close', 'Cerrar'));
      close.type = 'button';
      close.addEventListener('click', () => { card.remove(); card = null; });
      foot.append(hide, close);
      card.append(foot);
      wrap.insertBefore(card, pill);
    }
    pill.addEventListener('click', openCard);
    wrap.append(pill);
    root.append(style, wrap);
    (document.documentElement || document.body).append(host);
  }

  // A bitcoin: or ethereum: link opens the Sillet window instead of failing with "no app for this link"
  document.addEventListener('click', (e) => {
    if (!enabled || !e.isTrusted || e.defaultPrevented) return;
    const a = e.target && e.target.closest ? e.target.closest('a[href]') : null;
    const href = a && a.getAttribute('href');
    if (!href || !PAY_LINK.test(href)) return;
    e.preventDefault();
    sendMsg({ type: 'pay-open', text: href, trusted: true });
  }, true);

  const start = () => {
    scan();
    try { new MutationObserver(schedule).observe(document.documentElement, { childList: true, subtree: true, characterData: true }); } catch (e) { /* no observer */ }
  };
  if (document.readyState === 'complete') setTimeout(start, 600); else window.addEventListener('load', () => setTimeout(start, 600), { once: true });
})();
