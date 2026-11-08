// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Actions the Sillet window can ask the background to do
import { Interface, getAddress, isAddress, parseUnits, formatUnits } from 'ethers';
import * as store from './state.js';
import * as core from './core.js';
import * as approvals from './approvals.js';
import { CHAINS, chainById } from './chains.js';
import { rpc } from './rpc.js';
import { describeTx, tokenInfo, erc20 } from './decode.js';
import { prepare, signAndSend, feeCost, normalizeRequest } from './tx.js';
import { serializeInfo } from './handler.js';
import { addHistory } from './history.js';
import { pricesFor, marketList } from './prices.js';
import * as utxo from './utxo.js';
import { scanForUi } from './scan.js';
import * as wc from './wc.js';

function utxoAddr(st, p) {
  if (!utxo.isNet(p.net)) throw new Error('Unknown network');
  const acc = st.accounts.find((a) => a.index === p.index);
  const addr = acc && acc.addrs && acc.addrs[p.net];
  if (!addr) throw new Error('Unlock Sillet first');
  return addr;
}

const BAL = new Interface(['function balanceOf(address owner) view returns (uint256)']);

function tokenList(st, chainId) {
  const chain = chainById(chainId);
  const custom = (st.tokens[chainId] || []).map((t) => ({ ...t, custom: true }));
  const known = (chain.tokens || []).map((t) => ({ ...t }));
  const seen = new Set(known.map((t) => t.address.toLowerCase()));
  return [...known, ...custom.filter((t) => !seen.has(t.address.toLowerCase()))];
}

export async function handleUi(action, p = {}) {
  await core.touch();
  let st = await store.load();
  switch (action) {
    case 'getState': {
      if (st.vault && (await core.isUnlocked())) { await core.ensureAddrs(); st = await store.load(); }
      const sites = Object.entries(st.permissions).map(([origin, v]) => ({ origin, account: v.accounts[0], chainId: store.siteChain(st, origin), wc: wc.isWcOrigin(origin), label: v.wc ? v.wc.name : undefined, url: v.wc ? v.wc.url : undefined }));
      return {
        hasVault: !!st.vault, unlocked: await core.isUnlocked(), accounts: st.accounts, selected: st.selected, chainId: st.coin && utxo.isNet(st.coin) ? st.coin : st.chainId,
        wcReady: wc.configured(), favorites: st.favorites, scannerOff: st.scannerOff, evmChainId: st.chainId,
        settings: st.settings, history: st.history, contacts: st.contacts, sites,
        chains: [...CHAINS.map((c) => ({ chainId: c.chainId, name: c.name, symbol: c.native.symbol, decimals: c.native.decimals, explorer: c.explorer, tokens: tokenList(st, c.chainId) })), ...Object.values(utxo.NETS).map(utxo.publicNet)],
      };
    }
    case 'setup': await core.setup(p.mnemonic, p.password); return true;
    case 'unlock': await core.unlock(p.password); return true;
    case 'lock': await core.lock(); return true;
    case 'reset': await core.wipe(); return true;
    case 'addAccount': return core.addAccount();
    case 'selectAccount': if (!st.accounts[p.index]) throw new Error('No such account'); await store.save({ selected: p.index }); return true;
    case 'renameAccount': {
      const accounts = st.accounts.map((a, i) => (i === p.index ? { ...a, name: String(p.name || a.name).slice(0, 24) } : a));
      await store.save({ accounts });
      return true;
    }
    case 'selectChain':
      // Bitcoin, Litecoin and Dogecoin are only for the Sillet window. Websites keep using the EVM network
      if (utxo.isNet(p.chainId)) { await store.save({ coin: p.chainId }); return true; }
      if (!chainById(p.chainId)) throw new Error('Unknown network');
      await store.save({ chainId: p.chainId, coin: null });
      return true;
    case 'revealPhrase': return core.revealPhrase(p.password);
    case 'changePassword': await core.changePassword(p.oldPassword, p.newPassword); return true;
    case 'setSettings': await store.save({ settings: { ...st.settings, ...p.settings } }); await core.touch(); return true;

    case 'balances': {
      const chain = chainById(p.chainId);
      if (!chain) throw new Error('Unknown network');
      const address = getAddress(p.address);
      const jobs = [
        rpc(chain.chainId, 'eth_getBalance', [address, 'latest']).then((v) => ({ symbol: chain.native.symbol, name: chain.native.name, decimals: chain.native.decimals, native: true, balance: BigInt(v).toString() })).catch(() => ({ symbol: chain.native.symbol, name: chain.native.name, decimals: chain.native.decimals, native: true, balance: null })),
        ...tokenList(st, chain.chainId).map((t) => rpc(chain.chainId, 'eth_call', [{ to: t.address, data: BAL.encodeFunctionData('balanceOf', [address]) }, 'latest'])
          .then((v) => ({ ...t, balance: BigInt(v === '0x' ? '0x0' : v).toString() })).catch(() => ({ ...t, balance: null }))),
      ];
      return Promise.all(jobs);
    }

    case 'addContact': {
      if (!isAddress(p.address)) throw new Error('That is not a valid address');
      const name = String(p.name || '').trim().slice(0, 24);
      if (!name) throw new Error('Write a name for this contact');
      const address = getAddress(p.address);
      const rest = st.contacts.filter((c) => c.address.toLowerCase() !== address.toLowerCase());
      if (rest.length >= 200) throw new Error('Too many contacts');
      await store.save({ contacts: [...rest, { name, address }].sort((a, b) => a.name.localeCompare(b.name)) });
      return true;
    }
    case 'removeContact': {
      await store.save({ contacts: st.contacts.filter((c) => c.address.toLowerCase() !== String(p.address).toLowerCase()) });
      return true;
    }

    case 'utxoBalance': return utxo.getBalance(p.net, utxoAddr(st, p));
    case 'utxoHistory': return utxo.getHistory(p.net, utxoAddr(st, p));
    case 'utxoPrepare': {
      const r = await utxo.prepareSend({ netId: p.net, address: utxoAddr(st, p), to: String(p.to || '').trim(), amount: p.amount });
      const levels = {};
      for (const [k, v] of Object.entries(r.plans)) levels[k] = utxo.summarize(v, r.rates[k]);
      if (!Object.keys(levels).length) throw r.err;
      return { levels, symbol: r.net.symbol, decimals: r.net.decimals };
    }
    case 'utxoSend': {
      const sess = await store.getSession();
      if (!sess) throw new Error('Unlock Sillet first');
      const address = utxoAddr(st, p);
      const to = String(p.to || '').trim();
      const r = await utxo.prepareSend({ netId: p.net, address, to, amount: p.amount });
      const chosen = r.plans[p.level] || r.plans.market;
      if (!chosen) throw r.err;
      const hex = utxo.signPlan(p.net, sess.mnemonic, p.index, chosen, r.own);
      const hash = await utxo.broadcast(p.net, hex);
      await addHistory({ hash, chainId: p.net, from: address, to, value: '0x' + chosen.amount.toString(16), symbol: r.net.symbol, decimals: r.net.decimals, origin: 'Sillet', kind: 'native', status: 'pending' });
      return hash;
    }

    case 'scanTab': return scanForUi();
    case 'scannerReset': await store.save({ scannerOff: [] }); return true;

    case 'market': {
      if (!st.settings.prices) return null;
      return marketList();
    }
    case 'toggleFavorite': {
      const id = String(p.id || '').slice(0, 80);
      const has = st.favorites.includes(id);
      await store.save({ favorites: has ? st.favorites.filter((x) => x !== id) : [...st.favorites, id].slice(-100) });
      return !has;
    }

    case 'prices': {
      if (!st.settings.prices) return null;
      return pricesFor(p.chainId, (p.tokens || []).filter((a) => isAddress(a)));
    }

    case 'addToken': {
      if (!isAddress(p.address)) throw new Error('That is not a valid address');
      const address = getAddress(p.address);
      const info = await tokenInfo(p.chainId, address, st.tokens[p.chainId] || []);
      if (!info.symbol) throw new Error('That address is not a token on this network');
      const list = st.tokens[p.chainId] || [];
      if (!list.some((t) => t.address.toLowerCase() === address.toLowerCase())) {
        await store.save({ tokens: { ...st.tokens, [p.chainId]: [...list, { address, symbol: info.symbol, name: info.symbol, decimals: info.decimals }] } });
      }
      return true;
    }
    case 'removeToken': {
      const list = (st.tokens[p.chainId] || []).filter((t) => t.address.toLowerCase() !== String(p.address).toLowerCase());
      await store.save({ tokens: { ...st.tokens, [p.chainId]: list } });
      return true;
    }

    case 'buildSend': {
      const chain = chainById(p.chainId);
      if (!isAddress(p.to)) throw new Error('That is not a valid address');
      const from = getAddress(p.from);
      const to = getAddress(p.to);
      let req;
      if (p.token) {
        const info = await tokenInfo(p.chainId, p.token, st.tokens[p.chainId] || []);
        const amount = parseUnits(String(p.amount), info.decimals);
        req = { from, to: getAddress(p.token), value: '0x0', data: erc20.encodeFunctionData('transfer', [to, amount]) };
      } else {
        const amount = parseUnits(String(p.amount), chain.native.decimals);
        req = { from, to, value: '0x' + amount.toString(16) };
      }
      req = normalizeRequest(req);
      const [prep, info] = await Promise.all([prepare(p.chainId, req), describeTx(p.chainId, req, st.tokens[p.chainId] || [])]);
      return { req, prep, info: serializeInfo(info) };
    }
    case 'maxNative': {
      const chain = chainById(p.chainId);
      const from = getAddress(p.from);
      const bal = BigInt(await rpc(p.chainId, 'eth_getBalance', [from, 'latest']));
      const prep = await prepare(p.chainId, { from, to: getAddress(p.to || from), value: '0x0' });
      const cost = (feeCost(prep.fees.market, '0x5208') * 12n) / 10n;
      const max = bal > cost ? bal - cost : 0n;
      return formatUnits(max, chain.native.decimals);
    }
    case 'sendNow': {
      const w = await core.walletForAddress(p.req.from);
      if (!w) throw new Error('Unlock Sillet first');
      const req = normalizeRequest(p.req);
      const prep = await prepare(p.chainId, req);
      const fee = prep.fees[p.level] || prep.fees.market;
      const sent = await signAndSend(p.chainId, w, req, p.gasLimit || prep.gasLimit, fee);
      const chain = chainById(p.chainId);
      await addHistory({ hash: sent.hash, chainId: p.chainId, from: req.from, to: p.recipient || req.to, value: p.displayValue || req.value, symbol: p.symbol || chain.native.symbol, decimals: p.decimals ?? chain.native.decimals, origin: 'Sillet', kind: p.token ? 'transfer' : 'native', status: 'pending' });
      return sent.hash;
    }
    case 'clearHistory': await store.save({ history: [] }); return true;
    case 'txStatus': {
      if (utxo.isNet(p.chainId)) return utxo.getTxStatus(p.chainId, p.hash);
      const r = await rpc(p.chainId, 'eth_getTransactionReceipt', [p.hash]);
      return r ? (r.status === '0x1' ? 'confirmed' : 'failed') : 'pending';
    }
    case 'updateHistory': {
      const history = st.history.map((h) => (h.hash === p.hash ? { ...h, status: p.status } : h));
      await store.save({ history });
      return true;
    }

    case 'wcPair': return wc.pair(p.uri);
    case 'revokeSite': {
      if (wc.isWcOrigin(p.origin)) { await wc.disconnect(p.origin); core.stateChanged(); return true; }
      const permissions = { ...st.permissions };
      delete permissions[p.origin];
      await store.save({ permissions });
      core.stateChanged();
      return true;
    }
    case 'setSiteAccount': {
      if (!st.accounts.some((a) => a.address === p.address) || !st.permissions[p.origin]) throw new Error('Unknown site or account');
      await store.save({ permissions: { ...st.permissions, [p.origin]: { ...st.permissions[p.origin], accounts: [p.address] } } });
      core.stateChanged();
      return true;
    }

    case 'approval:get': {
      const r = approvals.get(p.id);
      if (!r) return null;
      const chain = chainById(r.chainId || r.to || st.chainId);
      return { ...r, chainName: chainById(r.chainId)?.name, toName: r.to ? chainById(r.to)?.name : undefined, fromName: r.from ? chainById(r.from)?.name : undefined, nativeSymbol: chain?.native.symbol, nativeDecimals: chain?.native.decimals };
    }
    case 'approval:resolve': {
      const r = approvals.get(p.id);
      if (!r) throw new Error('This request has expired. Try it again from the website');
      if (r.kind === 'connect' && !st.accounts.some((a) => a.address === p.decision?.account)) throw new Error('Choose an account');
      return approvals.resolve(p.id, p.decision || {});
    }
    case 'approval:reject': return approvals.reject(p.id);
    default: throw new Error('Unknown action ' + action);
  }
}
