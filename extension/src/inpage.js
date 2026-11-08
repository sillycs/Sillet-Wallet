// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Runs inside the web page and offers window.ethereum (EIP-1193) and EIP-6963 discovery
(() => {
  if (window.__silletInjected) return;
  Object.defineProperty(window, '__silletInjected', { value: true });

  class ProviderRpcError extends Error {
    constructor(code, message, data) {
      super(message);
      this.code = code;
      this.data = data;
    }
  }

  class SilletProvider {
    constructor() {
      this._events = {};
      this._pending = new Map();
      this._id = 0;
      this._ready = false;
      this._queue = [];
      this.chainId = null;
      this.networkVersion = null;
      this.selectedAddress = null;
      this._connected = false;
      this.isSillet = true;
      // Many sites only look for MetaMask. Sites that support EIP-6963 find Sillet by its own name
      this.isMetaMask = true;
      for (const k of ['request', 'on', 'once', 'off', 'addListener', 'removeListener', 'removeAllListeners', 'emit', 'listenerCount', 'enable', 'send', 'sendAsync', 'isConnected']) this[k] = this[k].bind(this);

      window.addEventListener('message', (ev) => {
        if (ev.source !== window) return;
        const d = ev.data;
        if (!d || d.target !== 'sillet-inpage') return;
        if (d.type === 'ready') { this._onReady(); return; }
        if (d.type === 'state') { this._update(d.accounts, d.chainId); return; }
        if (d.type === 'response') {
          const p = this._pending.get(d.id);
          if (!p) return;
          this._pending.delete(d.id);
          if (d.ok) p.resolve(d.result);
          else p.reject(new ProviderRpcError(d.error.code, d.error.message, d.error.data));
        }
      });
      window.postMessage({ target: 'sillet-content', type: 'hello' }, '*');
    }

    _onReady() {
      if (this._ready) return;
      this._ready = true;
      const q = this._queue;
      this._queue = [];
      q.forEach((fn) => fn());
      Promise.all([this._send('eth_chainId', []), this._send('eth_accounts', [])]).then(([c, a]) => this._update(a, c), () => {});
    }

    _send(method, params) {
      return new Promise((resolve, reject) => {
        const go = () => {
          const id = ++this._id;
          this._pending.set(id, { resolve, reject });
          window.postMessage({ target: 'sillet-content', type: 'request', id, method, params }, '*');
        };
        if (this._ready) go();
        else this._queue.push(go);
      });
    }

    _update(accounts, chainId) {
      if (chainId && chainId !== this.chainId) {
        const first = this.chainId === null;
        this.chainId = chainId;
        this.networkVersion = String(parseInt(chainId, 16));
        if (first) { this._connected = true; this.emit('connect', { chainId }); } else this.emit('chainChanged', chainId);
      }
      const next = Array.isArray(accounts) ? accounts : [];
      const prev = this.selectedAddress ? [this.selectedAddress] : [];
      if (next.join() !== prev.join()) {
        this.selectedAddress = next[0] || null;
        this.emit('accountsChanged', next);
      }
    }

    async request(args) {
      if (!args || typeof args !== 'object' || Array.isArray(args)) throw new ProviderRpcError(-32600, 'Expected a single request object');
      const { method, params } = args;
      if (typeof method !== 'string' || !method) throw new ProviderRpcError(-32600, 'Invalid method');
      if (params !== undefined && typeof params !== 'object') throw new ProviderRpcError(-32602, 'Invalid params');
      return this._send(method, params);
    }

    isConnected() { return this._connected; }
    enable() { return this.request({ method: 'eth_requestAccounts' }); }
    send(a, b) {
      if (typeof a === 'string') return this.request({ method: a, params: b });
      return this.sendAsync(a, b);
    }
    sendAsync(payload, cb) {
      const one = (p) => this.request({ method: p.method, params: p.params }).then((result) => ({ jsonrpc: '2.0', id: p.id, result }), (e) => ({ jsonrpc: '2.0', id: p.id, error: { code: e.code, message: e.message } }));
      const run = Array.isArray(payload) ? Promise.all(payload.map(one)) : one(payload);
      if (typeof cb === 'function') { run.then((r) => cb(null, r)); return undefined; }
      return run;
    }

    on(ev, fn) { (this._events[ev] = this._events[ev] || []).push(fn); return this; }
    addListener(ev, fn) { return this.on(ev, fn); }
    once(ev, fn) { const w = (...a) => { this.off(ev, w); fn(...a); }; w.fn = fn; return this.on(ev, w); }
    off(ev, fn) { this._events[ev] = (this._events[ev] || []).filter((f) => f !== fn && f.fn !== fn); return this; }
    removeListener(ev, fn) { return this.off(ev, fn); }
    removeAllListeners(ev) { if (ev) delete this._events[ev]; else this._events = {}; return this; }
    listenerCount(ev) { return (this._events[ev] || []).length; }
    emit(ev, ...args) {
      (this._events[ev] || []).slice().forEach((fn) => { try { fn(...args); } catch (e) { setTimeout(() => { throw e; }); } });
      return (this._events[ev] || []).length > 0;
    }
  }

  const provider = new SilletProvider();
  Object.freeze(Object.getPrototypeOf(provider));

  const info = Object.freeze({ uuid: crypto.randomUUID(), name: 'Sillet', icon: __SILLET_ICON__, rdns: 'fun.raton.sillet' });
  const detail = Object.freeze({ info, provider });
  const announce = () => window.dispatchEvent(new CustomEvent('eip6963:announceProvider', { detail }));
  window.addEventListener('eip6963:requestProvider', announce);
  announce();

  // Do not take over a wallet the person already uses
  if (!window.ethereum) {
    try { Object.defineProperty(window, 'ethereum', { value: provider, configurable: true, writable: true }); } catch (e) { /* page locked it */ }
  }
  try { Object.defineProperty(window, 'sillet', { value: provider, configurable: true }); } catch (e) { /* ignore */ }
})();
